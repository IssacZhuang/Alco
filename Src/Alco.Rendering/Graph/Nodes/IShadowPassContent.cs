namespace Alco.Rendering;

/// <summary>
/// A content provider for a <see cref="ShadowPassNode"/>: draws shadow casters into
/// one cascade of the shadow atlas. The pass owns the render passes — it applies the
/// per-cascade scissor rect and keeps one render pass open per cascade for all
/// content.
/// <br/>This is not a pipeline-level registration category: content is registered on
/// the pass node itself (<see cref="ShadowPassNode.Content"/>), keeping the pipeline
/// agnostic of the kinds of casters its shadow pass carries.
/// </summary>
public interface IShadowPassContent : IRenderNode
{
    /// <summary>
    /// Draws casters into one cascade of the shadow map. Called by the pass node
    /// inside the cascade's pass, once per cascade.
    /// </summary>
    /// <param name="context">The live shadow render context, already open on the shadow
    /// atlas with the cascade's scissor applied.</param>
    /// <param name="cascadeIndex">The cascade being rendered (0 = nearest).</param>
    void OnRenderShadow(RenderContext context, int cascadeIndex);
}
