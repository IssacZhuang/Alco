namespace Alco.Rendering;

/// <summary>
/// The threading state of a linear texture chain inside a <see cref="RenderGraph"/>, tracking
/// the resource currently holding the content produced so far. Transform nodes advance it in
/// setup, and the composing pipeline resets it to its root each frame.
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
