namespace Alco.Rendering;

/// <summary>
/// A content provider for <see cref="RGNode_PointLightShadow"/>: draws shadow
/// casters into one face of the point light shadow atlas. The node owns the
/// render pass — it applies the per-face scissor rect and keeps one pass open
/// for all faces rendered in a frame. Content is registered on the node itself
/// (<see cref="RGNode_PointLightShadow.Content"/>), mirroring
/// <see cref="RGNode_ShadowPass.Content"/>.
/// </summary>
public interface IPointLightShadowContent : IRenderNode
{
    /// <summary>
    /// Whether the content contains dynamic casters, forcing every occupied
    /// atlas face to re-render each frame. Static-only content keeps its faces
    /// cached until the light set or the content changes
    /// (<see cref="RGNode_PointLightShadow.MarkAtlasDirty"/>).
    /// </summary>
    bool HasDynamicCasters { get; }

    /// <summary>
    /// Draws casters into one face of the atlas. Called inside the node's open
    /// pass scope with the face's scissor already applied.
    /// </summary>
    /// <param name="context">The live pass scope on the atlas.</param>
    /// <param name="matrixIndex">The global matrix index of the face
    /// (slot * 6 + face) selecting the folded view-projection in the shared
    /// uniform buffer.</param>
    void OnRenderPointLightShadow(RenderPassScope context, int matrixIndex);
}
