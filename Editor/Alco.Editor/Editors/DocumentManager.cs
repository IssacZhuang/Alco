using System.Numerics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Particles;

namespace Alco.Editor;

/// <summary>
/// Owns the open asset documents: opens assets by path, dispatches to the document
/// type registered for the file extension (mirroring how <see cref="AssetSystem"/>
/// routes loaders by extension), selects already-open tabs, and closes/disposes
/// documents whose tab was closed.
/// </summary>
public sealed class DocumentManager
{
    private const string CloseConfirmPopupName = "Unsaved Changes";

    private readonly EditorContext _context;
    private readonly List<AssetDocument> _documents = new();
    private readonly Dictionary<string, Func<string, AssetDocument>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private AssetDocument? _documentToSelect;
    private AssetDocument? _activeDocument;
    private AssetDocument? _pendingClose;
    private bool _openCloseConfirm;

    /// <summary>Creates the manager and registers the built-in document factories.</summary>
    public DocumentManager(EditorContext context)
    {
        _context = context;

        RegisterFactory(FileExt.Material, path => new MaterialDocument(context, path));
        RegisterFactory(ParticleAssetPipeline.EffectExtension, path => new ParticleEffectDocument(context, path));
        RegisterFactory(FileExt.ImagePNG, path => new TextureDocument(context, path));
        RegisterFactory(FileExt.ImageJPG, path => new TextureDocument(context, path));
        RegisterFactory(FileExt.ImageBMP, path => new TextureDocument(context, path));
        RegisterFactory(FileExt.ImageTGA, path => new TextureDocument(context, path));
        RegisterFactory(FileExt.ImageGIF, path => new TextureDocument(context, path));
        RegisterFactory(FileExt.ImageHDR, path => new TextureDocument(context, path));
        RegisterFactory(FileExt.ImageDDS, path => new TextureDocument(context, path));
    }

    /// <summary>The currently open documents.</summary>
    public IReadOnlyList<AssetDocument> Documents => _documents;

    /// <summary>The document whose tab is currently selected, null when none is open.</summary>
    public AssetDocument? ActiveDocument => _activeDocument;

    /// <summary>Raised after a document was opened (not when an existing tab was focused).</summary>
    public event Action<AssetDocument>? DocumentOpened;

    /// <summary>Raised after a document was closed and disposed.</summary>
    public event Action<AssetDocument>? DocumentClosed;

    /// <summary>Registers the document factory for a file extension (e.g. <c>.amat</c>).</summary>
    public void RegisterFactory(string extension, Func<string, AssetDocument> factory)
    {
        _factories[extension] = factory;
    }

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
            _documentToSelect = existing;
            return;
        }

        AssetDocument document;
        try
        {
            string extension = Path.GetExtension(assetPath);
            document = _factories.TryGetValue(extension, out Func<string, AssetDocument>? factory)
                ? factory(assetPath)
                : new InfoDocument(_context, assetPath);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to open {assetPath}:", e);
            document = new InfoDocument(_context, assetPath);
        }

        _documents.Add(document);
        _documentToSelect = document;
        DocumentOpened?.Invoke(document);
    }

    /// <summary>
    /// Draws the document-area tab bar with one tab per open document and disposes the
    /// ones that were closed. Ctrl+S saves the selected document; closing a dirty tab
    /// asks for confirmation first.
    /// </summary>
    public void DrawDocuments()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S, false))
        {
            SaveActive();
        }

        _activeDocument = null;
        if (_documents.Count == 0)
        {
            ImGui.TextDisabled("Double-click an asset in the browser to open it.");
        }
        else if (ImGui.BeginTabBar("##document_tabs", ImGuiTabBarFlags.Reorderable))
        {
            for (int i = _documents.Count - 1; i >= 0; i--)
            {
                AssetDocument document = _documents[i];
                if (document.DrawTabItem(document == _documentToSelect))
                {
                    _activeDocument = document;
                }
                if (!document.IsOpen)
                {
                    if (document.IsDirty)
                    {
                        // Veto the close and ask first (popup drawn below).
                        document.Reopen();
                        _pendingClose = document;
                        _openCloseConfirm = true;
                    }
                    else
                    {
                        CloseDocument(document);
                    }
                }
            }
            ImGui.EndTabBar();
        }

        _documentToSelect = null;
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
        _documents.Remove(document);
        if (_documentToSelect == document)
        {
            _documentToSelect = null;
        }
        if (_pendingClose == document)
        {
            _pendingClose = null;
        }
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
