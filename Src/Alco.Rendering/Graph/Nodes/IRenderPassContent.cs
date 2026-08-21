using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A content provider for a pass node such as the Alco.World3D module's
/// <c>RGNode_GeometryPass</c>: draws geometry into the
/// pass's single open render context. The pass owns the render pass — it clears the
/// target and keeps one render context open for all content, so render bundles
/// recorded by content (via <see cref="SubRenderContext"/>) are replayed into the
/// same pass.
/// <br/>This is not a pipeline-level registration category: content is registered on
/// the pass node itself (its <c>Content</c> list), keeping the
/// pipeline agnostic of the kinds of content its passes carry.
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
