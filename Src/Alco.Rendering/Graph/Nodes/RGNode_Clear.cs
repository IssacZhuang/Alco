using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A graph node that clears a target resource (an empty render pass with clear
/// values). Typically the first node of a forward pipeline, clearing the scene
/// content target before content nodes draw over it.
/// </summary>
public sealed class RGNode_Clear : AutoDisposable, IRenderGraphNode
{
    private readonly RenderGraphTexture _target;
    private readonly ClearColorData[] _clearColors;
    private readonly float? _clearDepth;
    private readonly uint? _clearStencil;

    /// <summary>
    /// Creates the clear node.
    /// </summary>
    /// <param name="target">The resource to clear.</param>
    /// <param name="clearColors">The per-attachment clear colors, or null to not clear
    /// any color attachment.</param>
    /// <param name="clearDepth">The depth clear value, or null to not clear depth.</param>
    /// <param name="clearStencil">The stencil clear value, or null to not clear stencil.</param>
    public RGNode_Clear(RenderGraphTexture target,
        ClearColorData[]? clearColors = null, float? clearDepth = 1.0f, uint? clearStencil = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _clearColors = clearColors ?? [];
        _clearDepth = clearDepth;
        _clearStencil = clearStencil;
    }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The clear color of the first color attachment. Updating it replaces the first
    /// entry of the clear color list; it has no effect when the node was created with
    /// no color clears.
    /// </summary>
    public ColorFloat ClearColor
    {
        get => _clearColors.Length > 0 ? _clearColors[0].Color : ColorFloat.Black;
        set
        {
            if (_clearColors.Length > 0)
            {
                _clearColors[0] = new ClearColorData(0, value);
            }
        }
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Write(_target);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        using (context.RenderContext.BeginPass(_target.Texture.FrameBuffer, _clearColors, _clearDepth, _clearStencil))
        {
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) { }
}
