using System.Numerics;
using Alco.Editor.Extensibility;
using Alco.ImGUI;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// Owns the open asset documents: opens assets by path, dispatches to the document
/// type registered for the file extension (mirroring how <see cref="AssetSystem"/>
/// routes loaders by extension), selects already-open tabs, and closes/disposes
/// documents whose tab was closed. The factories come from the
/// <see cref="DocumentRegistry"/> filled by the editor modules.
/// </summary>
public sealed class DocumentManager
{
    private const string CloseConfirmPopupName = "Unsaved Changes";

    private readonly EditorContext _context;
    private readonly DocumentRegistry _registry;
    private readonly List<AssetDocument> _documents = new();
    private AssetDocument? _documentToSelect;
    private AssetDocument? _activeDocument;
    private AssetDocument? _pendingClose;
    private bool _openCloseConfirm;
    private int _lastTabBarFrame = -1;
    private uint _lastTabBarId;

    /// <summary>Creates the manager over the document factories in <paramref name="registry"/>.</summary>
    public DocumentManager(EditorContext context, DocumentRegistry registry)
    {
        _context = context;
        _registry = registry;
    }

    /// <summary>The currently open documents.</summary>
    public IReadOnlyList<AssetDocument> Documents => _documents;

    /// <summary>
    /// The selected document, retained while the document window is hidden; null when
    /// no document is open. Opening an asset selects it immediately.
    /// </summary>
    public AssetDocument? ActiveDocument => _activeDocument;

    /// <summary>Raised after a new document was opened, with <see cref="ActiveDocument"/> already assigned.</summary>
    public event Action<AssetDocument>? DocumentOpened;

    /// <summary>
    /// Raised by <see cref="Open"/> for both new and already-open documents after
    /// assigning <see cref="ActiveDocument"/> and requesting tab selection. The host
    /// can use this notification to reveal the shared asset-document window.
    /// </summary>
    public event Action<AssetDocument>? DocumentActivated;

    /// <summary>
    /// Raised after a document was closed and disposed. <see cref="ActiveDocument"/>
    /// already refers to a surviving document or null before disposal begins.
    /// </summary>
    public event Action<AssetDocument>? DocumentClosed;

    /// <summary>Returns the open document for <paramref name="assetPath"/>, or null.</summary>
    public AssetDocument? FindOpen(string assetPath)
    {
        for (int i = 0; i < _documents.Count; i++)
        {
            if (string.Equals(_documents[i].AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                return _documents[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Saves the document open for <paramref name="assetPath"/>. Returns false when no
    /// such document is open or it edits a referenced (read-only) asset.
    /// </summary>
    public bool Save(string assetPath)
    {
        AssetDocument? document = FindOpen(assetPath);
        if (document == null || document.IsReadOnly)
        {
            return false;
        }

        document.Save();
        return true;
    }

    /// <summary>
    /// Closes and disposes the document open for <paramref name="assetPath"/>.
    /// Returns false when no such document is open.
    /// </summary>
    public bool Close(string assetPath)
    {
        AssetDocument? document = FindOpen(assetPath);
        if (document == null)
        {
            return false;
        }

        CloseDocument(document);
        return true;
    }

    /// <summary>
    /// Saves the document of the currently selected tab (the Ctrl+S target).
    /// Returns false when nothing is active, it is read-only, or it has no changes.
    /// </summary>
    public bool SaveActive()
    {
        if (_activeDocument is not { IsDirty: true, IsReadOnly: false } document)
        {
            return false;
        }

        document.Save();
        return true;
    }

    /// <summary>
    /// Opens an asset as a document tab, or selects the existing tab when the asset is
    /// already open. Extensions without a registered editor fall back to
    /// <see cref="InfoDocument"/>; load failures fall back to it as well (logged).
    /// </summary>
    public void Open(string assetPath)
    {
        AssetDocument? existing = FindOpen(assetPath);
        if (existing != null)
        {
            _activeDocument = existing;
            _documentToSelect = existing;
            DocumentActivated?.Invoke(existing);
            return;
        }

        AssetDocument document;
        try
        {
            string extension = Path.GetExtension(assetPath);
            document = _registry.TryCreate(extension, assetPath)
                ?? new InfoDocument(_context, assetPath);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to open {assetPath}:", e);
            document = new InfoDocument(_context, assetPath);
        }

        _documents.Add(document);
        _activeDocument = document;
        _documentToSelect = document;
        DocumentOpened?.Invoke(document);
        DocumentActivated?.Invoke(document);
    }

    /// <summary>
    /// Draws the shared document window's internal tab bar and closes clean tabs.
    /// Hidden tabs retain the active document. Dirty close requests are confirmed by
    /// <see cref="Update"/> independently of the document window's visibility.
    /// </summary>
    public void DrawDocuments()
    {
        if (_documents.Count == 0)
        {
            ImGui.TextDisabled("Double-click an asset in the browser to open it.");
            return;
        }
        uint tabBarId = ImGui.GetID("##document_tabs");
        if (!ImGui.BeginTabBar("##document_tabs", ImGuiTabBarFlags.Reorderable))
        {
            return;
        }

        int frame = ImGui.GetFrameCount();
        if (_lastTabBarFrame < frame - 1 || _lastTabBarId != tabBarId)
        {
            // The host can hide or relocate the shared document window. Restore its
            // remembered selection after either a visibility gap or a parent ID change.
            _documentToSelect ??= _activeDocument;
        }
        _lastTabBarFrame = frame;
        _lastTabBarId = tabBarId;

        for (int i = _documents.Count - 1; i >= 0; i--)
        {
            AssetDocument document = _documents[i];
            bool selected = document.DrawTabItem(document == _documentToSelect);
            if (selected && (_documentToSelect == null || _documentToSelect == document))
            {
                // SetSelected can take effect on the following frame. Do not let
                // the old visible tab overwrite an explicit activation in between.
                _activeDocument = document;
                if (_documentToSelect == document)
                {
                    _documentToSelect = null;
                }
            }
            if (document.IsOpen)
            {
                continue;
            }
            if (document.IsDirty)
            {
                document.Reopen();
                _pendingClose = document;
                _openCloseConfirm = true;
                continue;
            }
            CloseDocument(document);
        }
        ImGui.EndTabBar();
    }

    /// <summary>
    /// Updates every open document, handles Ctrl+S once and draws dirty-close
    /// confirmation. The host must call this once per frame after all editor windows,
    /// inside the same stable ImGui window and ID scope even when documents are hidden.
    /// </summary>
    public void Update()
    {
        for (int i = 0; i < _documents.Count; i++)
        {
            _documents[i].Update();
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S, false))
        {
            SaveActive();
        }
        DrawCloseConfirmPopup();
    }

    /// <summary>
    /// Closes every open document. With <paramref name="force"/> false, documents with
    /// unsaved changes stay open and false is returned (clean documents are closed
    /// anyway); with true, unsaved changes are discarded and everything closes.
    /// </summary>
    /// <param name="force">Whether to discard unsaved changes.</param>
    /// <returns>True when no document remains open.</returns>
    public bool CloseAll(bool force = false)
    {
        for (int i = _documents.Count - 1; i >= 0; i--)
        {
            AssetDocument document = _documents[i];
            if (!force && document.IsDirty)
            {
                continue;
            }
            CloseDocument(document);
        }
        return _documents.Count == 0;
    }

    /// <summary>Removes, disposes and notifies about a closed document.</summary>
    private void CloseDocument(AssetDocument document)
    {
        int index = _documents.IndexOf(document);
        _documents.RemoveAt(index);
        if (_activeDocument == document)
        {
            _activeDocument = _documents.Count == 0 ? null : _documents[Math.Min(index, _documents.Count - 1)];
            _documentToSelect = _activeDocument;
        }
        else if (_documentToSelect == document)
        {
            _documentToSelect = _activeDocument;
        }
        if (_pendingClose == document)
        {
            _pendingClose = null;
            _openCloseConfirm = false;
        }

        // Inspector and lifecycle subscribers must never observe a disposed active
        // document, including callbacks run by the document's own Dispose override.
        document.Dispose();
        DocumentClosed?.Invoke(document);
    }

    /// <summary>Draws the unsaved-changes confirmation modal for a tab close request.</summary>
    private void DrawCloseConfirmPopup()
    {
        if (_openCloseConfirm)
        {
            _openCloseConfirm = false;
            ImGui.OpenPopup(CloseConfirmPopupName);
        }

        bool popupOpen = true;
        bool visible = ImGui.BeginPopupModal(CloseConfirmPopupName, ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popupOpen)
        {
            // The modal's own close button means Cancel.
            _pendingClose = null;
        }
        if (!visible)
        {
            return;
        }

        AssetDocument? document = _pendingClose;
        if (document == null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        ImGui.TextUnformatted($"'{document.AssetPath}' has unsaved changes.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (!document.IsReadOnly)
        {
            if (ImGui.Button("Save", new Vector2(110f, 0f)))
            {
                document.Save();
                if (!document.IsDirty)
                {
                    // Saved cleanly; a failed save keeps the document dirty (its
                    // toolbar shows the error) and the popup open.
                    CloseDocument(document);
                    _pendingClose = null;
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine();
        }
        if (ImGui.Button("Don't Save", new Vector2(110f, 0f)))
        {
            CloseDocument(document);
            _pendingClose = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(110f, 0f)))
        {
            _pendingClose = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
