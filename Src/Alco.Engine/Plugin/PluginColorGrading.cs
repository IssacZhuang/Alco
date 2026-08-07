using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Plugin that registers a <see cref="ColorGradingStage"/> on the main render pipeline.
/// Configure the grading parameters via <see cref="Stage"/>.
/// </summary>
public class PluginColorGrading : BaseEnginePlugin
{
    private ColorGradingStage? _stage;

    /// <summary>
    /// The execution order of the plugin. Runs before bloom initialization.
    /// </summary>
    public override int Order => 850;

    /// <summary>
    /// The color grading stage registered on the main pipeline. Available after engine initialization.
    /// </summary>
    public ColorGradingStage Stage => _stage!;

    public override void OnPostInitialize(GameEngine engine)
    {
        _stage = new ColorGradingStage(engine.RenderingSystem, engine.BuiltInAssets.Shader_ColorGrading);
        engine.MainPipeline.PostProcess.Add(_stage);
    }
}
