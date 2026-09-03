namespace Alco.Editor.Extensibility;

/// <summary>
/// The composition root of the editor: one registry per extension point, handed to
/// every <see cref="IEditorModule"/> at startup. Created by <see cref="EditorSystem"/>
/// before the editor shell is assembled, so the shell itself (documents, menus,
/// templates, meta types) is built from whatever the modules registered.
/// </summary>
public sealed class EditorRegistry
{
    /// <summary>Creates the registry over the given editor context.</summary>
    public EditorRegistry(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        Documents = new DocumentRegistry(context);
        Menus = new MenuRegistry();
        Panels = new PanelRegistry();
        AssetTemplates = new AssetTemplateRegistry();
        MetaTypes = new MetaTypeRegistry();
    }

    /// <summary>The shared editor services (project, engine).</summary>
    public EditorContext Context { get; }

    /// <summary>The document factories, keyed by file extension.</summary>
    public DocumentRegistry Documents { get; }

    /// <summary>The interface-keyed service bag shared by modules.</summary>
    public EditorServices Services => Context.Extensions;

    /// <summary>The main-menu-bar entries.</summary>
    public MenuRegistry Menus { get; }

    /// <summary>The toggleable floating panels.</summary>
    public PanelRegistry Panels { get; }

    /// <summary>The templates offered by the asset browser's "new asset" menu.</summary>
    public AssetTemplateRegistry AssetTemplates { get; }

    /// <summary>The meta sidecar types the hot reloader swallows.</summary>
    public MetaTypeRegistry MetaTypes { get; }
}
