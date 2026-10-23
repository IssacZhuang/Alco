using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// A content provider for <see cref="RGNode_RsmPass"/> that draws sun-lit albedo and world normals into the reflective shadow map; content is registered on the pass node itself.
/// </summary>
public interface IRsmPassContent : IRenderNode
{
    /// <summary>
    /// Draws content into the reflective shadow map. Called by the pass node inside
    /// its open pass scope.
    /// </summary>
    /// <param name="context">The live RSM pass scope, already open on the RSM target.</param>
    /// <param name="cascadeIndex">The CSM cascade whose sun view-projection defines
    /// the RSM view (the shared folded cascade matrices are unfolded in Rsm.slang).</param>
    void OnRenderRsm(RenderPassScope context, int cascadeIndex);
}
