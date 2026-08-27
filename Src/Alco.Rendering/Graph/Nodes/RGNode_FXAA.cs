
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Chain transform node that applies FXAA (Fast Approximate Anti-Aliasing) to the input.
/// </summary>
public sealed class RGNode_FXAA : RGNode_ChainTransform
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
    /// The subpixel aliasing removal amount. Higher values remove more subpixel
    /// aliasing but blur more detail. Valid range: 0 - 1, default: 0.75.
    /// </summary>
    public float Subpix
    {
        get => _fxaa.Subpix;
        set => _fxaa.Subpix = value;
    }

    /// <summary>
    /// The node's construction data: the scene-copy shader, the fxaa shader and
    /// the effect's tunables. The quality axis is a generic value specialization
    /// of the module's MainPS&lt;let Quality : int&gt; entry — the node requests each
    /// preset's specialized pipeline on demand. Service-type dependencies
    /// (the rendering system, graph, chain, output layout) are explicit
    /// constructor parameters instead — a descriptor is pure data.
    /// </summary>
    public readonly struct Descriptor
    {
        /// <summary>The scene-copy shader used for the final blit.</summary>
        public required Shader SceneCopyShader { get; init; }

        /// <summary>The fxaa shader (each quality preset is its own specialization).</summary>
        public required Shader FxaaShader { get; init; }

        /// <summary>The quality preset; changing it selects a different specialized shader.</summary>
        public FXAAQuality Quality { get; init; } = FXAAQuality.Medium;
        /// <summary>The edge detection threshold (0.063 - 0.333).</summary>
        public float Threshold { get; init; } = 0.125f;
        /// <summary>The subpixel aliasing removal amount (0 - 1).</summary>
        public float Subpix { get; init; } = 0.75f;

        /// <summary>Required so the property initializers run (C# struct rule).</summary>
        public Descriptor() { }
    }

    /// <summary>
    /// Creates the node wrapping a FXAA effect built from the descriptor's
    /// scene-copy shader and fxaa module; the node takes ownership of the effect.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain the node reads and advances.</param>
    /// <param name="outputLayout">The attachment layout of the node's output transient
    /// (color-only, in the chain's content format).</param>
    /// <param name="descriptor">The node's construction data.</param>
    public RGNode_FXAA(RenderingSystem rendering, RenderGraph graph, RenderChain chain,
        GPUAttachmentLayout outputLayout, in Descriptor descriptor)
        : base(graph, chain, outputLayout, name: "FXAA")
    {
        _fxaa = new FXAA(rendering, descriptor.SceneCopyShader, descriptor.FxaaShader)
        {
            Quality = descriptor.Quality,
            Threshold = descriptor.Threshold,
            Subpix = descriptor.Subpix,
        };
    }

    /// <inheritdoc />
    protected override void OnProcess(RenderTexture input, RenderTexture output, in RenderGraphContext context)
    {
        // The effect records onto the frame-shared context so the node executes in
        // graph order inside the graph's single submission; hand it this frame's
        // timestamp span so both passes are bracketed by one pair.
        if (Instrumentation is { ShouldRecordGpu: true } instrumentation)
        {
            _fxaa.TimestampSampler = instrumentation.GpuTimestamps;
            _fxaa.TimestampBaseSlot = instrumentation.GpuQueryBase;
        }
        else
        {
            _fxaa.TimestampSampler = null;
        }
        _fxaa.Blit(context.RenderContext, input, output.FrameBuffer);
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
