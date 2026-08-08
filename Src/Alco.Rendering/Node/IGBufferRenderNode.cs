using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A render node that draws scene geometry into the G-buffer pass of a
/// <see cref="PBRDeferredPipeline"/>. The pass is owned by the pipeline: it clears the
/// G-buffer and keeps one render pass open for all nodes, so G-buffer bundles are
/// replayed into the same pass.
/// </summary>
public interface IGBufferRenderNode : IRenderNode
{
    /// <summary>
    /// Draws objects into the G-buffer. Called by the pipeline inside the G-buffer pass,
    /// between Begin and End.
    /// </summary>
    /// <param name="context">The live G-buffer render context, already open on the
    /// G-buffer frame buffer.</param>
    /// <param name="layout">The G-buffer attachment layout (for bundle recording).</param>
    void OnRenderGBuffer(RenderContext context, GPUAttachmentLayout layout);
}
