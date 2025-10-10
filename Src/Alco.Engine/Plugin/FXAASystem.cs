using Alco.Graphics;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// System that manages FXAA (Fast Approximate Anti-Aliasing) post-processing.
/// Applies FXAA to the main render target to reduce aliasing artifacts.
/// </summary>
public class FXAASystem : BaseEngineSystem
{
    private readonly ViewRenderTarget _renderTarget;
    private readonly GameEngine _engine;
    private readonly RenderingSystem _rendering;
    private readonly Shader _fxaaShader;
    private readonly Shader _blitShader;
    private readonly FXAA _fxaa;

    /// <summary>
    /// Execution order for the FXAA system. Should run after main rendering but before UI.
    /// </summary>
    public override int Order => 1100;

    /// <summary>
    /// Gets or sets whether FXAA is enabled.
    /// When disabled, the render target is not processed with FXAA.
    /// Default: true
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the edge detection threshold.
    /// Lower values detect more edges but may introduce artifacts.
    /// Valid range: 0.063 - 0.333, Default: 0.125
    /// </summary>
    public float Threshold
    {
        get => _fxaa.Threshold;
        set => _fxaa.Threshold = value;
    }

    /// <summary>
    /// Initializes a new instance of the FXAASystem.
    /// </summary>
    /// <param name="engine">The game engine instance</param>
    /// <param name="renderTarget">The render target to apply FXAA to</param>
    public FXAASystem(GameEngine engine, ViewRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;
        _engine = engine;
        _rendering = engine.RenderingSystem;

        // Load the FXAA shader and blit shader
        _fxaaShader = engine.BuiltInAssets.Shader_FXAA;
        _blitShader = engine.BuiltInAssets.Shader_Blit;

        // Create the FXAA post-processing effect
        _fxaa = _rendering.CreateFXAA(_fxaaShader, _blitShader);
        _fxaa.SetInput(renderTarget.RenderTexture);

        // Subscribe to render target resize events
        renderTarget.OnResize += OnRenderTargetResize;
    }

    /// <summary>
    /// Called after the main update loop. Applies FXAA to the render target if enabled.
    /// </summary>
    /// <param name="delta">Time since last frame in seconds</param>
    public override void OnPostUpdate(float delta)
    {
        // Only apply FXAA if enabled
        if (IsEnabled)
        {
            _fxaa.Blit(_renderTarget.RenderTexture);
        }
    }

    /// <summary>
    /// Handles render target resize events by updating the FXAA input.
    /// </summary>
    /// <param name="size">New size of the render target</param>
    private void OnRenderTargetResize(uint2 size)
    {
        _fxaa.SetInput(_renderTarget.RenderTexture);
    }

    /// <summary>
    /// Disposes of resources used by the FXAA system.
    /// </summary>
    public override void Dispose()
    {
        _renderTarget.OnResize -= OnRenderTargetResize;
        _fxaa.Dispose();
        GC.SuppressFinalize(this);
    }
}