namespace Alco.Rendering;

/// <summary>
/// A content provider for a <see cref="RGNode_RsmPass"/>: draws sun-lit geometry
/// (albedo + world normal) into the reflective shadow map of the voxel GI's
/// sun-bounce injection. The pass owns the render pass — it clears the RSM
/// attachments and keeps one pass open for all content.
/// <br/><see cref="ShadowRenderer"/> implements this after
/// <see cref="ShadowRenderer.EnableRsm"/>, replaying the same caster registry as
/// the shadow pass with per-item RSM materials.
/// <br/>This is not a pipeline-level registration category: content is registered
/// on the pass node itself (<see cref="RGNode_RsmPass.Content"/>).
/// </summary>
public interface IRsmPassContent : IRenderNode
{
    /// <summary>
    /// Draws content into the reflective shadow map. Called by the pass node inside
    /// its open pass scope.
    /// </summary>
    /// <param name="context">The live RSM pass scope, already open on the RSM target.</param>
    /// <param name="cascadeIndex">The CSM cascade whose sun view-projection defines
    /// the RSM view (the shared folded cascade matrices are unfolded in Rsm.hlsl).</param>
    void OnRenderRsm(RenderPassScope context, int cascadeIndex);
}
