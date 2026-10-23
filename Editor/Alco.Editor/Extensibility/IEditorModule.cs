namespace Alco.Editor.Extensibility;

/// <summary>
/// A pluggable editor module that registers its document factories, services, menus, dockable windows, panels, asset templates and meta types into the shared <see cref="EditorRegistry"/>.
/// Modules register at startup after the built-in defaults; later modules can replace document factories and services, and window IDs must be unique.
/// </summary>
public interface IEditorModule
{
    /// <summary>Registers the module's extensions into the editor registry.</summary>
    /// <param name="registry">The composition root receiving the registrations.</param>
    void Register(EditorRegistry registry);
}
