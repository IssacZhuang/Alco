
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A render node that forward-renders content into a target: scene objects or UI.
/// For texture-to-texture content transforms (bloom, tone mapping, ...) see
/// <see cref="IContentProcessorNode"/>.
/// <br/>A <see cref="ForwardPipeline"/>'s chain interleaves forward nodes with content
/// processors (<see cref="IContentProcessorNode"/>). A
/// <see cref="PBRDeferredPipeline"/> invokes forward nodes in its resolve chain, after
/// the deferred passes, starting from its forward render texture.
/// <br/>The node owns its render pass: it begins and ends its own render context on the
/// given target (render bundles recorded via <see cref="SubRenderContext"/> are replayed
/// into any open <see cref="RenderContext"/> via
/// <see cref="RenderContext.ExecuteSubContext"/>).
/// </summary>
public interface IForwardRenderNode : IRenderNode
{
    /// <summary>
    /// Renders the node's content into <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The frame buffer assigned by the pipeline: the pipeline's
    /// content texture or, once a content processor has run, a chain-owned ping-pong
    /// temporary holding the content produced so far.</param>
    /// <param name="layout">The attachment layout of <paramref name="target"/>, for
    /// material compatibility and render bundle recording.</param>
    void OnRenderForward(GPUFrameBuffer target, GPUAttachmentLayout layout);
}
