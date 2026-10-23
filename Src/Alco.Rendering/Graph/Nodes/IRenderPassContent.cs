using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A content provider for a pass node that draws into the pass's single open render context.
/// Content is registered on the pass node itself rather than with the pipeline.
/// </summary>
public interface IRenderPassContent : IRenderNode
{
    /// <summary>
    /// Draws the content. Called by the pass node inside the open pass scope.
    /// </summary>
    /// <param name="context">The live pass scope, already open on the pass's target.</param>
    /// <param name="layout">The target's attachment layout (for bundle recording).</param>
    void OnRender(RenderPassScope context, GPUAttachmentLayout layout);
}
