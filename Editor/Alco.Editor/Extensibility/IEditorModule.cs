namespace Alco.Editor.Extensibility;

/// <summary>
/// A pluggable editor module: registers its document factories, services, menus,
/// panels, asset templates and meta types into the shared <see cref="EditorRegistry"/>.
/// Modules are registered in order at editor startup (built-in defaults first), so a
/// later module can override an earlier registration.
/// </summary>
public interface IEditorModule
{
    /// <summary>Registers the module's extensions into the editor registry.</summary>
    /// <param name="registry">The composition root receiving the registrations.</param>
    void Register(EditorRegistry registry);
}
