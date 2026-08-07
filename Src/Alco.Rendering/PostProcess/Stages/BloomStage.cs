
namespace Alco.Rendering;

/// <summary>
/// Post-process stage that adds a bloom glow to the scene: the source is first copied into
/// the destination, then the bloom pyramid is composited additively on top.
/// </summary>
public sealed class BloomStage : PostProcessStage
{
    private readonly Bloom _bloom;
    private RenderTexture? _lastInput;

    /// <inheritdoc />
    public override int Order => 500;

    /// <summary>
    /// Only pixels above this brightness contribute to the bloom effect.
    /// </summary>
    public float Threshold
    {
        get => _bloom.Threshold;
        set => _bloom.Threshold = value;
    }

    /// <summary>
    /// How far the bloom spreads across the pyramid.
    /// </summary>
    public float Spread
    {
        get => _bloom.Spread;
        set => _bloom.Spread = value;
    }

    /// <summary>
    /// The final output strength of the bloom effect.
    /// </summary>
    public float Intensity
    {
        get => _bloom.Intensity;
        set => _bloom.Intensity = value;
    }

    /// <summary>
    /// The gamma correction value for bloom blending. Default is 2.2.
    /// </summary>
    public float Gamma
    {
        get => _bloom.Gamma;
        set => _bloom.Gamma = value;
    }

    /// <summary>
    /// Creates the stage wrapping the bloom effect. The stage takes ownership.
    /// </summary>
    public BloomStage(Bloom bloom)
    {
        _bloom = bloom;
    }

    /// <inheritdoc />
    public override void Apply(PostProcessContext context)
    {
        if (!ReferenceEquals(_lastInput, context.Source))
        {
            _bloom.SetInput(context.Source);
            _lastInput = context.Source;
        }

        // The bloom blit is additive: the destination must already hold the scene image.
        context.Chain.Blit(context.Source, context.Destination);
        _bloom.Blit(context.Destination);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bloom.Dispose();
        }
    }
}
