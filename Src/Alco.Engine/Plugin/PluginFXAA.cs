namespace Alco.Engine;

/// <summary>
/// Plugin that enables FXAA (Fast Approximate Anti-Aliasing) post-processing.
/// Provides screen-space anti-aliasing with minimal performance impact.
/// </summary>
public class PluginFXAA : BaseEnginePlugin
{
    private FXAASystem? _fxaaSystem;

    /// <summary>
    /// Execution order for the FXAA plugin. Should be after main rendering setup.
    /// </summary>
    public override int Order => 950;

    /// <summary>
    /// Gets or sets the quality setting for FXAA.
    /// Higher values provide better quality at the cost of performance.
    /// Valid range: 0.5 - 2.0, Default: 1.0
    /// </summary>
    public float Quality { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the edge detection threshold.
    /// Lower values detect more edges but may introduce artifacts.
    /// Valid range: 0.063 - 0.333, Default: 0.125
    /// </summary>
    public float Threshold { get; set; } = 0.125f;

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
        Quality = Math.Clamp(quality, 0.5f, 2.0f);
        Threshold = Math.Clamp(threshold, 0.063f, 0.333f);
    }

    /// <summary>
    /// Called after engine initialization. Sets up the FXAA system.
    /// </summary>
    /// <param name="engine">The game engine instance</param>
    public override void OnPostInitialize(GameEngine engine)
    {
        _fxaaSystem = new FXAASystem(engine, engine.MainRenderTarget);

        // Apply settings
        _fxaaSystem.Quality = Quality;
        _fxaaSystem.Threshold = Threshold;

        engine.AddSystem(_fxaaSystem);
    }

    /// <summary>
    /// Gets the FXAA system instance for runtime configuration.
    /// </summary>
    /// <returns>The FXAA system instance, or null if not initialized</returns>
    public FXAASystem? GetFXAASystem()
    {
        return _fxaaSystem;
    }

    /// <summary>
    /// Disposes of resources used by the plugin.
    /// </summary>
    public override void Dispose()
    {
        _fxaaSystem?.Dispose();
    }
}