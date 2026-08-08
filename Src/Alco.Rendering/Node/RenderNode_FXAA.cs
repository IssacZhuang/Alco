
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Content processor node that applies FXAA (Fast Approximate Anti-Aliasing) to the input.
/// </summary>
public sealed class RenderNode_FXAA : AutoDisposable, IContentProcessorNode
{
    private readonly FXAA _fxaa;
    private RenderTexture? _input;

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

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
    /// Creates the node wrapping the FXAA effect. The node takes ownership.
    /// </summary>
    public RenderNode_FXAA(FXAA fxaa)
    {
        _fxaa = fxaa;
    }

    /// <inheritdoc />
    public void OnRenderForward(RenderTexture input, RenderTexture target)
    {
        if (!ReferenceEquals(_input, input))
        {
            _input = input;
            _fxaa.SetInput(input);
        }
        _fxaa.Blit(target.FrameBuffer);
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
