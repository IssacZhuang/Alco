using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Plugin that registers the procedural color grading system.
/// Color grading applies between scene and UI rendering via the <see cref="ColorGradingSystem"/>.
/// </summary>
public class PluginColorGrading : BaseEnginePlugin
{
    private ColorGradingSystem? _system;

    /// <summary>
    /// The execution order of the plugin. Runs before bloom initialization.
    /// </summary>
    public override int Order => 850;

    /// <summary>
    /// Gets the color grading system. Available after engine initialization.
    /// </summary>
    public ColorGradingSystem System => _system!;

    public override void OnPostInitialize(GameEngine engine)
    {
        Shader gradingShader = engine.AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_ColorGrading);
        _system = new ColorGradingSystem(engine, engine.MainRenderTarget, gradingShader);
        engine.AddSystem(_system);
    }

    public override void Dispose()
    {
        _system?.Dispose();
    }
}
