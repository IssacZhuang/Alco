using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A graph node that opens a single render pass on a target resource — clearing its
/// attachments — and invokes every enabled <see cref="IRenderPassContent"/> in
/// <see cref="Content"/> inside it. Use it for passes that many content providers
/// share (a G-buffer pass, a custom depth prepass, ...), so all draws land in one
/// render pass and render bundles are replayed without re-opening.
/// </summary>
public sealed class GeometryPassNode : AutoDisposable, IRenderGraphNode
{
    private readonly RenderGraphTexture _target;
    private readonly ClearColorData[] _clearColors;
    private readonly float? _clearDepth;
    private readonly RenderContext _context;

    /// <summary>
    /// Creates the pass node.
    /// </summary>
    /// <param name="rendering">The rendering system, for the pass's render context.</param>
    /// <param name="target">The resource the pass renders into (a full write: the pass
    /// clears the attachments first).</param>
    /// <param name="clearColors">The per-attachment clear colors, or null to not clear
    /// any color attachment.</param>
    /// <param name="clearDepth">The depth clear value, or null to not clear depth.</param>
    /// <param name="name">A diagnostic name for the pass's render context.</param>
    public GeometryPassNode(RenderingSystem rendering, RenderGraphTexture target,
        ClearColorData[]? clearColors = null, float? clearDepth = null, string name = "geometry_pass")
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _clearColors = clearColors ?? [];
        _clearDepth = clearDepth;
        _context = rendering.CreateRenderContext(name);
    }

    /// <summary>The content drawn inside the pass, in list order. Register content
    /// providers here — the pipeline the pass belongs to is not involved.</summary>
    public List<IRenderPassContent> Content { get; } = new();

    /// <summary>Optional CPU/GPU stage instrumentation.</summary>
    public PassInstrumentation? Instrumentation { get; set; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <summary>The target resource the pass renders into.</summary>
    public RenderGraphTexture Target => _target;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Write(_target);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        long startTicks = Instrumentation?.BeginCpuTiming() ?? 0;
        if (Instrumentation != null)
        {
            Instrumentation.BeginPass(_context, _target.Texture.FrameBuffer, _clearColors, _clearDepth);
        }
        else
        {
            _context.Begin(_target.Texture.FrameBuffer, _clearColors, _clearDepth);
        }

        List<IRenderPassContent> content = Content;
        for (int i = 0; i < content.Count; i++)
        {
            if (content[i].IsEnabled)
            {
                content[i].OnRender(_context, _target.Texture.AttachmentLayout);
            }
        }

        if (Instrumentation != null)
        {
            Instrumentation.EndPass(_context);
            Instrumentation.PushCpuTiming(startTicks);
        }
        else
        {
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
