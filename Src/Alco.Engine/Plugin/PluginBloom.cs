using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Plugin that registers a <see cref="BloomStage"/> on the main render pipeline.
/// Configure the effect at runtime via <c>engine.MainPipeline.PostProcess.Get&lt;BloomStage&gt;()</c>.
/// </summary>
public class PluginBloom : BaseEnginePlugin
{
    public override int Order => 900;

    public override void OnPostInitialize(GameEngine engine)
    {
        BuiltInAssets assets = engine.BuiltInAssets;
        Bloom bloom = engine.RenderingSystem.CreateBloom(
            assets.Shader_BloomBlit,
            assets.Shader_BloomClamp,
            assets.Shader_BloomDownSample,
            assets.Shader_BloomUpSample,
            11);
        engine.MainPipeline.PostProcess.Add(new BloomStage(bloom));
    }
}
