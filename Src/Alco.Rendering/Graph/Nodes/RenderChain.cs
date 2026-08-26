namespace Alco.Rendering;

/// <summary>
/// The threading state of a linear texture chain inside a <see cref="RenderGraph"/>:
/// the <see cref="RenderGraphTexture"/> currently holding the content produced so far.
/// A chain is rooted at a scene content resource (e.g. a pipeline's scene color
/// target); chain-aware nodes read <see cref="Current"/> in their
/// <see cref="IRenderGraphNode.Setup"/> and — when they transform the content into a
/// new texture (post-process effects) — call <see cref="Advance"/> so later nodes
/// continue from their output. In-place nodes (forward content, additive overlays)
/// read <see cref="Current"/> without advancing.
/// <br/>The chain relies on the graph's contract that <see cref="IRenderGraphNode.Setup"/>
/// runs strictly in registration order, which is what makes this threading
/// deterministic. The composing pipeline resets the chain to its root resource at the
/// start of every frame, before <see cref="RenderGraph.Execute"/>.
/// </summary>
public sealed class RenderChain
{
    /// <summary>The resource currently holding the content produced so far.</summary>
    public RenderGraphTexture? Current { get; private set; }

    /// <summary>
    /// Resets the chain to <paramref name="root"/>. Called by the composing pipeline
    /// once per frame, before <see cref="RenderGraph.Execute"/>.
    /// </summary>
    /// <param name="root">The resource holding this frame's initial content.</param>
    public void Reset(RenderGraphTexture root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Current = root;
    }

    /// <summary>
    /// Advances the chain: <paramref name="next"/> now holds the content produced so
    /// far. Called by transform nodes during <see cref="IRenderGraphNode.Setup"/>,
    /// after declaring their read of the previous <see cref="Current"/>.
    /// </summary>
    /// <param name="next">The resource the calling node writes the transformed content into.</param>
    public void Advance(RenderGraphTexture next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Current = next;
    }
}
