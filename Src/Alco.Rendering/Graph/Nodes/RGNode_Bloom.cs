
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Chain transform node that adds a bloom glow: the input is first copied into the
/// output, then the bloom pyramid is composited additively on top.
/// </summary>
public sealed class RGNode_Bloom : RGNode_ChainTransform
{
    private readonly Bloom _bloom;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _blitMaterial;

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
    /// Creates the node wrapping the bloom effect. The node takes ownership of
    /// <paramref name="bloom"/>; <paramref name="blitShader"/> stays owned by the caller.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain the node reads and advances.</param>
    /// <param name="outputLayout">The attachment layout of the node's output transient
    /// (color-only, in the chain's content format).</param>
    /// <param name="bloom">The bloom effect implementation.</param>
    /// <param name="blitShader">The shader used for the plain copy of the input.</param>
    public RGNode_Bloom(RenderingSystem rendering, RenderGraph graph, RenderChain chain,
        GPUAttachmentLayout outputLayout, Bloom bloom, Shader blitShader)
        : base(graph, chain, outputLayout, name: "bloom")
    {
        _bloom = bloom;
        _fullScreenMesh = rendering.MeshFullScreen;
        _blitMaterial = rendering.CreateMaterial(blitShader);
    }

    /// <inheritdoc />
    protected override void OnProcess(RenderTexture input, RenderTexture output, in RenderGraphContext context)
    {
        // Setting the same texture is a no-op; an in-place resize of the input is
        // picked up by the material system's version check.
        _blitMaterial.SetRenderTexture(ShaderResourceId.Texture, input);

        // The bloom blit is additive: the output must already hold the scene image.
        using (RenderPassScope pass = context.RenderContext.BeginPass(output.FrameBuffer))
        {
            pass.Draw(_fullScreenMesh, _blitMaterial);
        }

        _bloom.Blit(input, output.FrameBuffer);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bloom.Dispose();
            _blitMaterial.Dispose();
        }
        base.Dispose(disposing);
    }
}
