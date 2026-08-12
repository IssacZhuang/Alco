
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Chain transform node that applies FXAA (Fast Approximate Anti-Aliasing) to the input.
/// </summary>
public sealed class FxaaNode : ChainTransformNode
{
    private readonly FXAA _fxaa;

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
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain the node reads and advances.</param>
    /// <param name="outputLayout">The attachment layout of the node's output transient
    /// (color-only, in the chain's content format).</param>
    /// <param name="fxaa">The FXAA effect implementation.</param>
    public FxaaNode(RenderGraph graph, RenderChain chain, GPUAttachmentLayout outputLayout, FXAA fxaa)
        : base(graph, chain, outputLayout, name: "fxaa")
    {
        _fxaa = fxaa;
    }

    /// <inheritdoc />
    protected override void OnProcess(RenderTexture input, RenderTexture output, in RenderGraphContext context)
    {
        _fxaa.Blit(input, output.FrameBuffer);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fxaa.Dispose();
        }
        base.Dispose(disposing);
    }
}
