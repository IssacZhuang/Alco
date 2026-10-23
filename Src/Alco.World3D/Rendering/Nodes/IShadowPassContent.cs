using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// A content provider for <see cref="RGNode_ShadowPass"/> that draws shadow casters into one cascade of the shadow atlas; content is registered on the pass node itself.
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
