using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Plugin that registers an <see cref="FXAAStage"/> (Fast Approximate Anti-Aliasing) on the
/// main render pipeline.
/// </summary>
public class PluginFXAA : BaseEnginePlugin
{
    private FXAAStage? _stage;
    private float _threshold = 0.125f;

    /// <summary>
    /// Execution order for the FXAA plugin. Should be after main rendering setup.
    /// </summary>
    public override int Order => 950;

    /// <summary>
    /// Gets or sets the edge detection threshold.
    /// Lower values detect more edges but may introduce artifacts.
    /// Valid range: 0.063 - 0.333, Default: 0.125
    /// </summary>
    public float Threshold
    {
        get => _threshold;
        set
        {
            _threshold = value;
            if (_stage != null)
            {
                _stage.Threshold = value;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the PluginFXAA with default settings.
    /// </summary>
    public PluginFXAA()
    {
    }

    /// <summary>
    /// Initializes a new instance of the PluginFXAA with custom settings.
    /// </summary>
    /// <param name="quality">Quality setting (0.5-2.0)</param>
    /// <param name="threshold">Edge detection threshold (0.063-0.333)</param>
    public PluginFXAA(float quality, float threshold)
    {
        Threshold = Math.Clamp(threshold, 0.063f, 0.333f);
    }

    /// <summary>
    /// Called after engine initialization. Registers the FXAA stage on the main pipeline.
    /// </summary>
    /// <param name="engine">The game engine instance</param>
    public override void OnPostInitialize(GameEngine engine)
    {
        _stage = new FXAAStage(engine.RenderingSystem.CreateFXAA(
            engine.BuiltInAssets.Shader_FXAA,
            engine.BuiltInAssets.Shader_Blit));
        _stage.Threshold = _threshold;
        engine.MainPipeline.PostProcess.Add(_stage);
    }
}
