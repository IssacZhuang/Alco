namespace Alco.Rendering;

/// <summary>
/// The mutable threading state of the deferred pipeline's post chain during graph
/// Setup: the <see cref="RenderGraphTexture"/> currently holding the scene content.
/// Reset to the scene color resource at the start of every frame; each enabled
/// post-process node advances it to its own output in registration order (the graph
/// runs Setup strictly in registration order, which is what makes this threading
/// deterministic). Not used outside the Setup phase.
/// </summary>
internal sealed class PostChainState
{
    /// <summary>The resource currently holding the scene content.</summary>
    internal RenderGraphTexture? Current;
}
