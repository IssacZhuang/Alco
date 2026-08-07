using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Scene-rendering middleware that draws objects into the pipeline's own render
/// passes (shadow map, G-buffer, future forward pass, etc.). Register via
/// <see cref="PBRDeferredPipeline.AddSceneRenderer"/>; the pipeline invokes each
/// registered renderer at every applicable pass between Begin and End.
/// <br/>Unlike <see cref="IRenderPlugin"/> (which runs <em>between</em> passes and
/// produces intermediate screen-space textures), a scene renderer runs <em>inside</em>
/// a pass and draws actual scene geometry into the pass's render targets — which it
/// does not own (the pipeline owns them).
/// <br/>All methods have default empty implementations: a renderer only overrides the
/// passes it cares about.
/// </summary>
public interface ISceneRenderer
{
    /// <summary>
    /// Whether this renderer has any forward-pass content to draw. The pipeline
    /// uses this to skip the forward pass (and its depth-copy pre-pass) entirely
    /// when no transparent objects are registered. Default is <c>false</c>.
    /// </summary>
    bool HasForwardContent => false;

    /// <summary>
    /// Draw casters into one cascade of the shadow map. Called by the pipeline
    /// between <see cref="PBRDeferredPipeline.BeginShadowPass"/> and
    /// <see cref="PBRDeferredPipeline.EndShadowPass"/>, once per cascade.
    /// Default implementation does nothing.
    /// </summary>
    /// <param name="context">The live shadow render context.</param>
    /// <param name="cascadeIndex">The cascade being rendered (0 = nearest).</param>
    void OnRenderShadow(RenderContext context, int cascadeIndex) { }

    /// <summary>
    /// Draw objects into the G-buffer. Called by the pipeline between
    /// <see cref="PBRDeferredPipeline.BeginGBufferPass"/> and
    /// <see cref="PBRDeferredPipeline.EndGBufferPass"/>.
    /// Default implementation does nothing.
    /// </summary>
    /// <param name="context">The live G-buffer render context.</param>
    /// <param name="layout">The G-buffer attachment layout (for bundle recording).</param>
    void OnRenderGBuffer(RenderContext context, GPUAttachmentLayout layout) { }

    /// <summary>
    /// Draw transparent objects in a forward pass after deferred lighting, using the
    /// lighting result as a background. Called by the pipeline between
    /// <see cref="PBRDeferredPipeline.BeginForwardPass"/> and
    /// <see cref="PBRDeferredPipeline.EndForwardPass"/>.
    /// <br/>The render target has both color (the lit HDR scene) and depth (copied from
    /// the G-buffer via native CopyTexture), so transparent objects depth-test against
    /// opaque geometry and blend onto the lit result.
    /// Default implementation does nothing.
    /// </summary>
    /// <param name="context">The live forward render context.</param>
    /// <param name="layout">The forward pass attachment layout (for bundle recording).</param>
    void OnRenderForward(RenderContext context, GPUAttachmentLayout layout) { }
}
