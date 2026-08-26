using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// A content provider for a <see cref="RGNode_ShadowPass"/>: draws shadow casters into
/// one cascade of the shadow atlas. The pass owns the render passes — it applies the
/// per-cascade scissor rect and keeps one render pass open per cascade for all
/// content.
/// <br/>This is not a pipeline-level registration category: content is registered on
/// the pass node itself (<see cref="RGNode_ShadowPass.Content"/>), keeping the pipeline
/// agnostic of the kinds of casters its shadow pass carries.
/// </summary>
public interface IShadowPassContent : IRenderNode
{
    /// <summary>
    /// Draws casters into one cascade of the shadow map. Called by the pass node
    /// inside the cascade's pass scope, once per cascade.
    /// </summary>
    /// <param name="context">The live shadow pass scope, already open on the shadow
    /// atlas with the cascade's scissor applied.</param>
    /// <param name="cascadeIndex">The cascade being rendered (0 = nearest).</param>
    void OnRenderShadow(RenderPassScope context, int cascadeIndex);
}
