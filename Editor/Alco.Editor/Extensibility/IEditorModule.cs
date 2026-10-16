namespace Alco.Editor.Extensibility;

/// <summary>
/// A pluggable editor module: registers its document factories, services, menus,
/// dockable windows, panels, asset templates and meta types into the shared
/// <see cref="EditorRegistry"/>. Modules are registered at startup after the built-in
/// defaults. Later modules can replace document factories and services; window IDs
/// must be unique. Register <see cref="IEditorWindow"/> implementations through
/// <see cref="EditorRegistry.Windows"/> to join the workspace and Window menu.
/// </summary>
public interface IEditorModule
{
    /// <summary>Registers the module's extensions into the editor registry.</summary>
    /// <param name="registry">The composition root receiving the registrations.</param>
    void Register(EditorRegistry registry);
}
