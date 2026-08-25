
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
    private readonly GraphicsMaterial _blitMaterial;

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
    /// The node's construction data: the bloom effect's four shaders, the chain
    /// node's scene-copy shader and the effect's tunables. Service-type
    /// dependencies (the rendering system, graph, chain, output layout) are
    /// explicit constructor parameters instead — a descriptor is pure data.
    /// </summary>
    public readonly struct Descriptor
    {
        /// <summary>The bloom pyramid's plain-copy shader.</summary>
        public required Shader BlitShader { get; init; }
        /// <summary>The threshold pre-pass shader.</summary>
        public required Shader ClampShader { get; init; }
        /// <summary>The pyramid downsample shader.</summary>
        public required Shader DownsampleShader { get; init; }
        /// <summary>The pyramid upsample shader.</summary>
        public required Shader UpsampleShader { get; init; }
        /// <summary>The chain node's scene-copy shader.</summary>
        public required Shader SceneCopyShader { get; init; }

        /// <summary>The pyramid's target downsample height in pixels.</summary>
        public uint TargetDownsampleHeight { get; init; } = 11;
        /// <summary>Only pixels above this brightness contribute to the bloom effect.</summary>
        public float Threshold { get; init; } = 1f;
        /// <summary>The final output strength of the bloom effect.</summary>
        public float Intensity { get; init; } = 0.35f;
        /// <summary>How far the bloom spreads across the pyramid.</summary>
        public float Spread { get; init; } = 1f;
        /// <summary>The gamma correction value for bloom blending.</summary>
        public float Gamma { get; init; } = 2.2f;

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
            descriptor.ClampShader,
            descriptor.DownsampleShader,
            descriptor.UpsampleShader,
            descriptor.TargetDownsampleHeight)
        {
            Threshold = descriptor.Threshold,
            Intensity = descriptor.Intensity,
            Spread = descriptor.Spread,
            Gamma = descriptor.Gamma,
        };
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
