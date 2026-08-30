using Alco.ImGUI;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// Owns the open asset documents: opens assets by path, dispatches to the document
/// type registered for the file extension (mirroring how <see cref="AssetSystem"/>
/// routes loaders by extension), focuses already-open tabs, and closes/disposes
/// documents whose window was closed.
/// </summary>
public sealed class DocumentManager
{
    private readonly EditorContext _context;
    private readonly List<AssetDocument> _documents = new();
    private readonly Dictionary<string, Func<string, AssetDocument>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private string? _windowToFocus;

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

    /// <summary>Registers the document factory for a file extension (e.g. <c>.amat</c>).</summary>
    public void RegisterFactory(string extension, Func<string, AssetDocument> factory)
    {
        _factories[extension] = factory;
    }

    /// <summary>
    /// Opens an asset as a document tab, or focuses the existing tab when the asset is
    /// already open. Extensions without a registered editor fall back to
    /// <see cref="InfoDocument"/>; load failures fall back to it as well (logged).
    /// </summary>
    public void Open(string assetPath)
    {
        AssetDocument? existing = null;
        for (int i = 0; i < _documents.Count; i++)
        {
            if (string.Equals(_documents[i].AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                existing = _documents[i];
                break;
            }
        }
        if (existing != null)
        {
            _windowToFocus = existing.WindowName;
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
        _windowToFocus = document.WindowName;
    }

    /// <summary>Draws all open document windows and disposes the ones that were closed.</summary>
    public void DrawDocuments()
    {
        for (int i = _documents.Count - 1; i >= 0; i--)
        {
            AssetDocument document = _documents[i];
            document.DrawWindow();
            if (!document.IsOpen)
            {
                _documents.RemoveAt(i);
                document.Dispose();
            }
        }

        if (_windowToFocus != null)
        {
            ImGui.SetWindowFocus(_windowToFocus);
            _windowToFocus = null;
        }
    }

    /// <summary>Docks every open document into the given node (used by the default layout).</summary>
    public void DockAllDocuments(uint dockId)
    {
        foreach (AssetDocument document in _documents)
        {
            ImGuiDockBuilder.DockWindow(document.WindowName, dockId);
        }
    }
}
