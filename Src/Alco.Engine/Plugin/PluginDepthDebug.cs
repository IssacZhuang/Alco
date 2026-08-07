using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Plugin that registers a <see cref="DepthDebugStage"/> on the main render pipeline.
/// Debug tooling: visualizes the scene depth buffer as grayscale when the stage is enabled.
/// </summary>
public class PluginDepthDebug : BaseEnginePlugin
{
    private DepthDebugStage? _stage;

    public override int Order => 2000;

    /// <summary>
    /// The depth debug stage registered on the main pipeline. Available after engine initialization.
    /// </summary>
    public DepthDebugStage Stage => _stage!;

    public override void OnPostInitialize(GameEngine engine)
    {
        _stage = new DepthDebugStage(
            engine.RenderingSystem,
            engine.AssetSystem.Load<string>(BuiltInAssetsPath.Shader_BlitDepth),
            BuiltInAssetsPath.Shader_BlitDepth,
            engine.BuiltInAssets.Shader_Blit,
            engine.MainView.Size.X,
            engine.MainView.Size.Y);
        engine.MainPipeline.PostProcess.Add(_stage);
    }
}
