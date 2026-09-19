
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Chain transform node that adds a sum-of-gaussians bloom: the input
/// is first copied into the output, then the bloom pyramid is composited
/// additively on top, before tone mapping.
/// </summary>
public sealed class RGNode_Bloom : RGNode_ChainTransform
{
    private readonly Bloom _bloom;
    private readonly Mesh _fullScreenMesh;
    private readonly GraphicsMaterial _blitMaterial;

    /// <summary>
    /// The brightness cutoff of the bloom setup pass; negative values disable
    /// the threshold so the whole image blooms.
    /// </summary>
    public float Threshold
    {
        get => _bloom.Threshold;
        set => _bloom.Threshold = value;
    }

    /// <summary>
    /// The overall bloom strength.
    /// </summary>
    public float Intensity
    {
        get => _bloom.Intensity;
        set => _bloom.Intensity = value;
    }

    /// <summary>
    /// A multiplier on every gaussian radius.
    /// </summary>
    public float SizeScale
    {
        get => _bloom.SizeScale;
        set => _bloom.SizeScale = value;
    }

    /// <summary>
    /// The per-stage gaussian sizes in Bloom1..6 order, as percents of the screen
    /// width; a stage with a size of zero is skipped.
    /// </summary>
    public float[] Sizes => _bloom.Sizes;

    /// <summary>The per-stage gaussian tints in Bloom1..6 order.</summary>
    public Vector3[] Tints => _bloom.Tints;

    /// <summary>
    /// The node's construction data: the bloom effect's four shaders, the chain
    /// node's scene-copy shader and the effect's tunables. Service-type
    /// dependencies (the rendering system, graph, chain, output layout) are
    /// explicit constructor parameters instead — a descriptor is pure data.
    /// </summary>
    public readonly struct Descriptor
    {
        /// <summary>The additive composite shader.</summary>
        public required Shader BlitShader { get; init; }
        /// <summary>The threshold setup shader.</summary>
        public required Shader SetupShader { get; init; }
        /// <summary>The downsample chain shader.</summary>
        public required Shader DownsampleShader { get; init; }
        /// <summary>The separable gaussian stage shader.</summary>
        public required Shader GaussianShader { get; init; }
        /// <summary>The chain node's scene-copy shader.</summary>
        public required Shader SceneCopyShader { get; init; }

        /// <summary>The brightness cutoff (linear ramp, full at cutoff+2); negative disables the threshold (whole image blooms). Defaults to 0.</summary>
        public float Threshold { get; init; } = 0f;
        /// <summary>The overall bloom strength. Defaults to 1.</summary>
        public float Intensity { get; init; } = 1f;
        /// <summary>A multiplier on every gaussian radius.</summary>
        public float SizeScale { get; init; } = 1f;
        /// <summary>The per-stage gaussian sizes in Bloom1..6 order, as percents of the screen width.</summary>
        public float[] Sizes { get; init; } = [0.3f, 1f, 2f, 10f, 30f, 64f];
        /// <summary>The per-stage gaussian tints in Bloom1..6 order.</summary>
        public Vector3[] Tints { get; init; } = [Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One];

        /// <summary>Required so the property initializers run (C# struct rule).</summary>
        public Descriptor() { }
    }

    /// <summary>
    /// Creates the node wrapping a bloom effect built from the descriptor's
    /// shaders; the node takes ownership of the effect.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain the node reads and advances.</param>
    /// <param name="outputLayout">The attachment layout of the node's output transient
    /// (color-only, in the chain's content format).</param>
    /// <param name="descriptor">The node's construction data.</param>
    public RGNode_Bloom(RenderingSystem rendering, RenderGraph graph, RenderChain chain,
        GPUAttachmentLayout outputLayout, in Descriptor descriptor)
        : base(graph, chain, outputLayout, name: "bloom")
    {
        _bloom = new Bloom(
            rendering,
            descriptor.BlitShader,
            descriptor.SetupShader,
            descriptor.DownsampleShader,
            descriptor.GaussianShader)
        {
            Threshold = descriptor.Threshold,
            Intensity = descriptor.Intensity,
            SizeScale = descriptor.SizeScale,
        };
        for (int i = 0; i < descriptor.Sizes.Length && i < 6; i++)
        {
            _bloom.Sizes[i] = descriptor.Sizes[i];
        }
        for (int i = 0; i < descriptor.Tints.Length && i < 6; i++)
        {
            _bloom.Tints[i] = descriptor.Tints[i];
        }
        _fullScreenMesh = rendering.MeshFullScreen;
        _blitMaterial = rendering.CreateGraphicsMaterial(descriptor.SceneCopyShader);
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

        // The pyramid records onto the frame-shared context after the copy pass, so
        // the whole node executes in graph order inside the graph's single submission.
        if (Instrumentation is { ShouldRecordGpu: true } instrumentation)
        {
            _bloom.TimestampSampler = instrumentation.GpuTimestamps;
            _bloom.TimestampBaseSlot = instrumentation.GpuQueryBase;
        }
        else
        {
            _bloom.TimestampSampler = null;
        }
        _bloom.Blit(context.RenderContext, input, output.FrameBuffer);
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
