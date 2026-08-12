using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A graph node that draws a full-screen material in place into the chain's current
/// content, reading and writing it — the shape of additive overlays such as
/// volumetric light. The node never advances the chain; on headless frames (null
/// destination) it roots itself so it still runs.
/// <br/>The node owns its render context; it does not own the material.
/// </summary>
public sealed class FullscreenOverlayNode : AutoDisposable, IRenderGraphNode
{
    private readonly RenderGraph _graph;
    private readonly RenderChain _chain;
    private readonly Material _material;
    private readonly RenderContext _context;
    private readonly Mesh _fullScreenMesh;

    // The resource drawn into this frame, captured during Setup (the chain continues
    // to advance for later nodes before Execute runs).
    private RenderGraphTexture? _target;

    /// <summary>
    /// Creates the overlay node.
    /// </summary>
    /// <param name="rendering">The rendering system, for the render context and the
    /// full-screen mesh.</param>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain whose current content the node draws into.</param>
    /// <param name="material">The full-screen material (e.g. additive blend). Not
    /// owned by the node.</param>
    /// <param name="name">A diagnostic name.</param>
    public FullscreenOverlayNode(RenderingSystem rendering, RenderGraph graph, RenderChain chain,
        Material material, string name = "fullscreen_overlay")
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(material);
        _graph = graph;
        _chain = chain;
        _material = material;
        _context = rendering.CreateRenderContext(name);
        _fullScreenMesh = rendering.MeshFullScreen;
    }

    /// <summary>Optional CPU/GPU stage instrumentation.</summary>
    public PassInstrumentation? Instrumentation { get; set; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        _target = _chain.Current!;
        builder.ReadWrite(_target);
        if (!_graph.HasDestinationThisFrame)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        long startTicks = Instrumentation?.BeginCpuTiming() ?? 0;
        if (Instrumentation != null)
        {
            Instrumentation.BeginPass(_context, _target!.Texture.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty);
            _context.Draw(_fullScreenMesh, _material);
            Instrumentation.EndPass(_context);
            Instrumentation.PushCpuTiming(startTicks);
        }
        else
        {
            _context.Begin(_target!.Texture.FrameBuffer);
            _context.Draw(_fullScreenMesh, _material);
            _context.End();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _context.Dispose();
        }
    }
}
