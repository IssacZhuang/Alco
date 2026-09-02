using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Base class for chain transform nodes: post-process effects that read the chain's
/// current content and write the transformed result into their own private transient
/// output, then advance the chain to that output. On headless frames (null
/// destination) the node is culled automatically — its output is never consumed.
/// <br/>Derive and implement <see cref="OnProcess"/>; both textures are backed by
/// real GPU textures for the duration of the call. The node owns its output
/// transient — destroyed via <see cref="RenderGraph.DestroyTransient"/> with the
/// node — and the graph's transient pool aliases the historical ping-pong
/// temporaries.
/// <br/>Timing: per-node CPU time is measured automatically by the graph (see
/// <see cref="RenderGraph.Profiler"/>). For GPU time, when no external
/// <see cref="Instrumentation"/> is set and the graph has a profiler, the node
/// lazily self-instruments — when the device supports timestamp queries it
/// creates a private GPU sampler whose slot pair wraps the pass opened through
/// <see cref="BeginProcessPass"/> (or a span the derivative brackets itself via
/// <see cref="PassInstrumentation.BeginSpanPass"/>/
/// <see cref="PassInstrumentation.EndSpanPass"/>).
/// </summary>
public abstract class RGNode_ChainTransform : AutoDisposable, IRenderGraphNode
{
    private readonly RenderGraph _graph;
    private readonly string _name;

    // The resource read this frame, captured during Setup (the chain continues to
    // advance for later nodes before Execute runs).
    private RenderGraphTexture? _input;

    // Auto instrumentation state, created lazily on the first Execute (see class
    // remarks). Null when external Instrumentation is wired or the graph has no
    // profiler.
    private GpuTimestampSampler? _autoGpuSampler;
    private RenderProfiler? _autoProfiler;
    private RenderProfileCounterId _autoGpuCounter;
    private double _autoGpuMilliseconds;

    /// <summary>
    /// Creates the node, including its private output transient.
    /// </summary>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain the node reads and advances.</param>
    /// <param name="outputLayout">The attachment layout of the output transient
    /// (typically color-only, in the chain's content format).</param>
    /// <param name="resolutionScale">The output's resolution scale relative to the graph viewport.</param>
    /// <param name="name">A diagnostic name for the output transient and the
    /// auto-registered profiler counters.</param>
    /// <param name="depthSource">Optional transient whose depth attachment the output
    /// shares (both layouts must declare a depth attachment with the same format) — a
    /// transform feeding later content nodes that depth-test against the scene depth.</param>
    protected RGNode_ChainTransform(RenderGraph graph, RenderChain chain, GPUAttachmentLayout outputLayout,
        float resolutionScale = 1.0f, string name = "chain_transform", RenderGraphTexture? depthSource = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(outputLayout);
        _graph = graph;
        _name = name;
        Chain = chain;
        Output = graph.CreateTransient(new RenderGraphTextureDescriptor(
            outputLayout, resolutionScale: resolutionScale, depthSource: depthSource, name: name + "_output"));
    }

    /// <summary>The content chain the node reads and advances.</summary>
    protected RenderChain Chain { get; }

    /// <summary>The resource read this frame (valid during <see cref="OnProcess"/>).</summary>
    protected RenderGraphTexture Input => _input!;

    /// <summary>The node's private output transient, destroyed with the node.</summary>
    public RenderGraphTexture Output { get; }

    /// <summary>The diagnostic name of the node (from construction); also labels
    /// its auto-registered profiler counters.</summary>
    public string Name => _name;

    /// <summary>Optional GPU stage instrumentation. When left null and the
    /// graph has a profiler, the node self-instruments on its first execute (see
    /// the class remarks); setting it later replaces that auto instrumentation.</summary>
    public PassInstrumentation? Instrumentation { get; set; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public virtual void Resize(uint width, uint height) { }

    /// <inheritdoc />
    public virtual void Setup(RenderGraphBuilder builder)
    {
        _input = Chain.Current!;
        builder.Read(_input);
        builder.Write(Output);
        Chain.Advance(Output);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        EnsureAutoInstrumentation(context);
        PassInstrumentation? instrumentation = Instrumentation;

        // On sample frames the previous sample is read back after OnProcess: the
        // recorded resolves have not executed yet (submission happens at frame
        // end), so the buffer still holds the previous sample.
        bool gpuSample = instrumentation is { ShouldRecordGpu: true };

        OnProcess(_input!.Texture, Output.Texture, context);

        if (instrumentation != null)
        {
            if (gpuSample && _autoGpuSampler != null)
            {
                ulong[]? timestamps = _autoGpuSampler.TryReadback();
                if (timestamps != null)
                {
                    _autoGpuMilliseconds = _autoGpuSampler.DeltaMilliseconds(timestamps, 0, 1);
                }
                _autoGpuSampler.EndSample();
            }
            if (_autoGpuSampler != null)
            {
                _autoProfiler!.PushValue(_autoGpuCounter, _autoGpuMilliseconds);
            }
        }
    }

    /// <summary>
    /// Renders the processed content of <paramref name="input"/> into
    /// <paramref name="output"/>. The two textures are always distinct.
    /// </summary>
    /// <param name="input">The texture holding the content produced so far.</param>
    /// <param name="output">The texture to write the processed content into.</param>
    /// <param name="context">The per-frame execution context.</param>
    protected abstract void OnProcess(RenderTexture input, RenderTexture output, in RenderGraphContext context);

    /// <summary>
    /// Opens the node's main process pass on the frame's render context, wrapping
    /// it with the instrumentation's GPU timestamp pair (begin + end) and
    /// scheduling the resolve at pass close. Derivatives drawing a single pass
    /// into <paramref name="output"/> should open it through this helper so the
    /// auto instrumentation records them.
    /// </summary>
    /// <param name="output">The node's output texture (from <see cref="OnProcess"/>).</param>
    /// <param name="context">The per-frame execution context.</param>
    /// <param name="clearStencil">Optional stencil clear value for the process pass.</param>
    /// <returns>The pass scope; dispose it (or use <c>using</c>) to close the pass.</returns>
    protected RenderPassScope BeginProcessPass(
        RenderTexture output,
        in RenderGraphContext context,
        uint? clearStencil = null)
    {
        RenderPassScope pass = Instrumentation != null
            ? Instrumentation.BeginPass(
                context.RenderContext,
                output.FrameBuffer,
                ReadOnlySpan<ClearColorData>.Empty,
                clearStencil: clearStencil)
            : context.RenderContext.BeginPass(output.FrameBuffer, clearStencil: clearStencil);
        Instrumentation?.ScheduleResolve(pass);
        return pass;
    }

    /// <summary>
    /// Opens the first pass of a GPU-timed span covering several consecutive
    /// passes (only the begin timestamp is written); close the span with
    /// <see cref="EndProcessSpanPass"/>.
    /// </summary>
    /// <param name="target">The framebuffer the span's first pass renders to.</param>
    /// <param name="context">The per-frame execution context.</param>
    /// <returns>The pass scope; dispose it (or use <c>using</c>) to close the pass.</returns>
    protected RenderPassScope BeginProcessSpanPass(GPUFrameBuffer target, in RenderGraphContext context)
    {
        return Instrumentation != null
            ? Instrumentation.BeginSpanPass(context.RenderContext, target, ReadOnlySpan<ClearColorData>.Empty)
            : context.RenderContext.BeginPass(target);
    }

    /// <summary>
    /// Opens the last pass of a span opened with <see cref="BeginProcessSpanPass"/>
    /// (only the end timestamp is written) and schedules the span's timestamp
    /// resolve at pass close.
    /// </summary>
    /// <param name="target">The framebuffer the span's last pass renders to.</param>
    /// <param name="context">The per-frame execution context.</param>
    /// <returns>The pass scope; dispose it (or use <c>using</c>) to close the pass.</returns>
    protected RenderPassScope EndProcessSpanPass(GPUFrameBuffer target, in RenderGraphContext context)
    {
        RenderPassScope pass = Instrumentation != null
            ? Instrumentation.EndSpanPass(context.RenderContext, target, ReadOnlySpan<ClearColorData>.Empty)
            : context.RenderContext.BeginPass(target);
        Instrumentation?.ScheduleResolve(pass);
        return pass;
    }

    /// <summary>Creates the auto instrumentation when no external one is set and
    /// the graph has a profiler (see the class remarks).</summary>
    private void EnsureAutoInstrumentation(in RenderGraphContext context)
    {
        if (Instrumentation != null)
        {
            return;
        }
        RenderProfiler? profiler = _graph.Profiler;
        if (profiler == null)
        {
            return;
        }

        string displayName = ToDisplayName(_name);
        GPUDevice device = context.Rendering.GraphicsDevice;
        _autoProfiler = profiler;
        _autoGpuSampler = device.IsFeatureSupported(GPUFeatures.TimestampQuery)
            ? new GpuTimestampSampler(device, 2, "chain_" + _name)
            : null;
        if (_autoGpuSampler != null)
        {
            _autoGpuCounter = profiler.RegisterCounter("PostProcess", displayName + " (GPU)");
        }
        Instrumentation = new PassInstrumentation
        {
            GpuTimestamps = _autoGpuSampler,
            GpuQueryBase = 0,
        };
    }

    /// <summary>Turns a snake-case node name into a counter display name
    /// ("color_grading" → "Color Grading").</summary>
    private static string ToDisplayName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "Chain Transform";
        }
        string spaced = name.Replace('_', ' ');
        return char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_graph.IsDisposed)
            {
                _graph.DestroyTransient(Output);
            }
            _autoGpuSampler?.Dispose();
        }
    }
}
