namespace Alco.Rendering;

/// <summary>
/// A render node that transforms the content produced so far: it samples an input
/// texture and renders the result into another texture — a forward renderer over a
/// render texture's content (bloom, tone mapping, FXAA, color grading, ...).
/// <br/>Both endpoints are render textures: the chain owns the ping-pong temporaries and
/// performs the final blit into the destination itself, so a processor never renders
/// into a bare frame buffer (e.g. the swapchain) and needs no separate input-binding
/// lifecycle.
/// </summary>
public interface IContentProcessorNode : IRenderNode
{
    /// <summary>
    /// Renders the processed content of <paramref name="input"/> into
    /// <paramref name="target"/>. The two textures are always distinct. Implementations
    /// that rebuild resolution-dependent resources per input should cache the last input
    /// and rebuild only when it actually changes.
    /// </summary>
    /// <param name="input">The texture holding the content produced so far.</param>
    /// <param name="target">The texture to write the processed content into.</param>
    void OnRenderForward(RenderTexture input, RenderTexture target);
}
