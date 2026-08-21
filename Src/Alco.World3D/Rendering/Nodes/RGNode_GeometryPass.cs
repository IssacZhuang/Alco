using Alco.Graphics;

using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// A graph node that opens a single render pass on a target resource — clearing its
/// attachments — and invokes every enabled <see cref="IRenderPassContent"/> in
/// <see cref="Content"/> inside it. Use it for passes that many content providers
/// share (a G-buffer pass, a custom depth prepass, ...), so all draws land in one
/// render pass and render bundles are replayed without re-opening.
/// </summary>
public sealed class RGNode_GeometryPass : AutoDisposable, IRenderGraphNode
{
    private readonly RenderGraphTexture _target;
    private readonly ClearColorData[] _clearColors;
    private readonly float? _clearDepth;

    /// <summary>
    /// Creates the pass node.
    /// </summary>
    /// <param name="target">The resource the pass renders into (a full write: the pass
    /// clears the attachments first).</param>
    /// <param name="clearColors">The per-attachment clear colors, or null to not clear
    /// any color attachment.</param>
    /// <param name="clearDepth">The depth clear value, or null to not clear depth.</param>
    public RGNode_GeometryPass(RenderGraphTexture target,
        ClearColorData[]? clearColors = null, float? clearDepth = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _clearColors = clearColors ?? [];
        _clearDepth = clearDepth;
    }

    /// <summary>The content drawn inside the pass, in list order. Register content
    /// providers here — the pipeline the pass belongs to is not involved.
    /// <br/>Ownership: the pass does not take ownership of registered providers;
    /// disposing them (when disposable) is the caller's responsibility, unlike
    /// nodes handed to <c>RenderGraph.Use</c>, which transfers ownership to the
    /// graph.</summary>
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
        RenderPassScope pass = Instrumentation != null
            ? Instrumentation.BeginPass(context.RenderContext, _target.Texture.FrameBuffer, _clearColors, _clearDepth)
            : context.RenderContext.BeginPass(_target.Texture.FrameBuffer, _clearColors, _clearDepth);
        using (pass)
        {
            List<IRenderPassContent> content = Content;
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].IsEnabled)
                {
                    content[i].OnRender(pass, _target.Texture.AttachmentLayout);
                }
            }

            Instrumentation?.ScheduleResolve(pass);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) { }
}
