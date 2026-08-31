using Alco.Engine;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// Services shared by editor panels and asset documents: the engine and the open
/// project. The project is replaceable at runtime through <see cref="SetProject"/>
/// (project switching); panels read <see cref="Project"/> per frame, so they pick up
/// the new project automatically.
/// </summary>
public sealed class EditorContext
{
    private AlcoProject _project;

    /// <summary>Creates the context for the given engine and project.</summary>
    public EditorContext(GameEngine engine, AlcoProject project)
    {
        Engine = engine;
        _project = project;
    }

    /// <summary>The running editor engine.</summary>
    public GameEngine Engine { get; }

    /// <summary>The project open in the editor.</summary>
    public AlcoProject Project => _project;

    /// <summary>Raised after <see cref="SetProject"/> published a new project.</summary>
    public event Action<AlcoProject>? ProjectChanged;

    /// <summary>
    /// Replaces the project open in the editor and raises <see cref="ProjectChanged"/>.
    /// This only publishes the new project — swapping the mounted asset sources is the
    /// caller's job (<see cref="ProjectOpener"/> does it around this call).
    /// </summary>
    /// <param name="project">The project to open.</param>
    public void SetProject(AlcoProject project)
    {
        _project = project;
        ProjectChanged?.Invoke(project);
    }

    /// <summary>Shortcut for <see cref="GameEngine.AssetSystem"/>.</summary>
    public AssetSystem AssetSystem => Engine.AssetSystem;

    /// <summary>Shortcut for <see cref="GameEngine.RenderingSystem"/>.</summary>
    public RenderingSystem RenderingSystem => Engine.RenderingSystem;
}
