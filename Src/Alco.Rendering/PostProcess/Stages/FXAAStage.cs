
namespace Alco.Rendering;

/// <summary>
/// Post-process stage that applies FXAA (Fast Approximate Anti-Aliasing) to the scene.
/// </summary>
public sealed class FXAAStage : PostProcessStage
{
    private readonly FXAA _fxaa;
    private RenderTexture? _lastInput;

    /// <inheritdoc />
    public override int Order => 900;

    /// <summary>
    /// The FXAA quality preset. Changing it recompiles the shader variant.
    /// </summary>
    public FXAAQuality Quality
    {
        get => _fxaa.Quality;
        set => _fxaa.Quality = value;
    }

    /// <summary>
    /// The edge detection threshold. Lower values detect more edges but may introduce
    /// artifacts. Valid range: 0.063 - 0.333, default: 0.125.
    /// </summary>
    public float Threshold
    {
        get => _fxaa.Threshold;
        set => _fxaa.Threshold = value;
    }

    /// <summary>
    /// Creates the stage wrapping the FXAA effect. The stage takes ownership.
    /// </summary>
    public FXAAStage(FXAA fxaa)
    {
        _fxaa = fxaa;
    }

    /// <inheritdoc />
    public override void Apply(PostProcessContext context)
    {
        if (!ReferenceEquals(_lastInput, context.Source))
        {
            _fxaa.SetInput(context.Source);
            _lastInput = context.Source;
        }

        _fxaa.Blit(context.Destination);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fxaa.Dispose();
        }
    }
}
