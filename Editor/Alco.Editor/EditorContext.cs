using Alco.Engine;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// Services shared by editor panels and asset documents: the engine and the open
/// project.
/// </summary>
public sealed class EditorContext
{
    /// <summary>Creates the context for the given engine and project.</summary>
    public EditorContext(GameEngine engine, AlcoProject project)
    {
        Engine = engine;
        Project = project;
    }

    /// <summary>The running editor engine.</summary>
    public GameEngine Engine { get; }

    /// <summary>The project open in the editor.</summary>
    public AlcoProject Project { get; }

    /// <summary>Shortcut for <see cref="GameEngine.AssetSystem"/>.</summary>
    public AssetSystem AssetSystem => Engine.AssetSystem;

    /// <summary>Shortcut for <see cref="GameEngine.RenderingSystem"/>.</summary>
    public RenderingSystem RenderingSystem => Engine.RenderingSystem;
}
