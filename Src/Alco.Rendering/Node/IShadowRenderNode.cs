namespace Alco.Rendering;

/// <summary>
/// A render node that draws shadow casters into the shadow pass of a
/// <see cref="PBRDeferredPipeline"/>. The pass is owned by the pipeline: it applies the
/// per-cascade scissor rect and keeps one render pass open per cascade for all nodes.
/// </summary>
public interface IShadowRenderNode : IRenderNode
{
    /// <summary>
    /// Draws casters into one cascade of the shadow map. Called by the pipeline inside
    /// the shadow pass, once per cascade.
    /// </summary>
    /// <param name="context">The live shadow render context, already open on the shadow
    /// atlas with the cascade's scissor applied.</param>
    /// <param name="cascadeIndex">The cascade being rendered (0 = nearest).</param>
    void OnRenderShadow(RenderContext context, int cascadeIndex);
}
