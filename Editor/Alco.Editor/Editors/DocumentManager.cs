using Alco.ImGUI;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// Owns the open asset documents: opens assets by path, dispatches to the document
/// type registered for the file extension (mirroring how <see cref="AssetSystem"/>
/// routes loaders by extension), selects already-open tabs, and closes/disposes
/// documents whose tab was closed.
/// </summary>
public sealed class DocumentManager
{
    private readonly EditorContext _context;
    private readonly List<AssetDocument> _documents = new();
    private readonly Dictionary<string, Func<string, AssetDocument>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private AssetDocument? _documentToSelect;

    /// <summary>Creates the manager and registers the built-in document factories.</summary>
    public DocumentManager(EditorContext context)
    {
        _context = context;

        RegisterFactory(FileExt.Material, path => new MaterialDocument(context, path));
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

        _documents.Remove(document);
        document.Dispose();
        DocumentClosed?.Invoke(document);
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
    /// ones that were closed.
    /// </summary>
    public void DrawDocuments()
    {
        if (_documents.Count == 0)
        {
            ImGui.TextDisabled("Double-click an asset in the browser to open it.");
            return;
        }

        if (ImGui.BeginTabBar("##document_tabs", ImGuiTabBarFlags.Reorderable))
        {
            for (int i = _documents.Count - 1; i >= 0; i--)
            {
                AssetDocument document = _documents[i];
                document.DrawTabItem(document == _documentToSelect);
                if (!document.IsOpen)
                {
                    _documents.RemoveAt(i);
                    if (_documentToSelect == document)
                    {
                        _documentToSelect = null;
                    }
                    document.Dispose();
                    DocumentClosed?.Invoke(document);
                }
            }
            ImGui.EndTabBar();
        }

        _documentToSelect = null;
    }
}
