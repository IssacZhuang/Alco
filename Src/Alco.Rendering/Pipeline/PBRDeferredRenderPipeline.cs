
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Render pipeline adapter over <see cref="PBRDeferredPipeline"/>. The deferred pipeline's
/// internal forward target (HDR + depth) acts as the pipeline's scene texture: the deferred
/// passes render into it during <see cref="RenderScene"/> and the post-process chain resolves
/// it straight into the final destination — no intermediate engine render target and no
/// composite blit.
/// </summary>
public sealed class PBRDeferredRenderPipeline : RenderPipeline
{
    private readonly PBRDeferredPipeline _deferred;

    /// <summary>
    /// The underlying deferred pipeline. Camera, scene properties, scene renderers and
    /// render plugins are configured on it directly.
    /// </summary>
    public PBRDeferredPipeline Deferred => _deferred;

    /// <summary>
    /// Creates the adapter and takes ownership of <paramref name="deferred"/>. The deferred
    /// pipeline must already be sized to the view; the adapter drives its per-frame passes
    /// and resizes.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="deferred">The deferred pipeline to drive.</param>
    /// <param name="blitShader">The shader the post-process chain uses for plain copies.</param>
    public PBRDeferredRenderPipeline(RenderingSystem rendering, PBRDeferredPipeline deferred, Shader blitShader)
        : base(rendering, deferred.ForwardLayout, blitShader)
    {
        _deferred = deferred;
        SetSceneRenderTexture(_deferred.ForwardRenderTexture);
    }

    /// <inheritdoc />
    protected override bool OwnsSceneRenderTexture => false;

    /// <inheritdoc />
    protected override void ResizeScene(uint width, uint height)
    {
        _deferred.Resize(width, height);
        SetSceneRenderTexture(_deferred.ForwardRenderTexture);
    }

    /// <inheritdoc />
    protected override void ClearScene()
    {
        // The deferred passes clear their own targets and the lighting pass overwrites
        // every pixel of the forward target, so no upfront clear is needed.
    }

    /// <inheritdoc />
    protected override void RenderScene()
    {
        _deferred.RenderFrame();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deferred.Dispose();
        }

        base.Dispose(disposing);
    }
}
