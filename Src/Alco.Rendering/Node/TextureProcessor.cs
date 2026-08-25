using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Base class for processors that render texture content into a target with fullscreen
/// passes (bloom, FXAA, ...). Typically wrapped by a <see cref="RGNode_ChainTransform"/>
/// for pipeline orchestration.
/// <br/>Processors record their passes through a <see cref="RenderContext"/> (the same
/// high-level path every renderer uses: <see cref="RenderContext.BeginPass"/> plus
/// materials) and never submit: the frame scope owning the context submits.
/// </summary>
public abstract class TextureProcessor : AutoDisposable
{
    private readonly Mesh _mesh;

    protected Mesh FullScreenMesh => _mesh;

    internal TextureProcessor(RenderingSystem renderingSystem)
    {
        _mesh = renderingSystem.MeshFullScreen;
    }

    /// <summary>
    /// Processes <paramref name="input"/> and records the passes onto
    /// <paramref name="context"/>, rendering the result into <paramref name="target"/>.
    /// Implementations rebuild their resolution-dependent resources lazily from the
    /// input's current size, so an in-place resized input needs no other notification.
    /// </summary>
    /// <param name="context">The render context recording the frame; passes open and
    /// close inside this call and the context is never submitted here.</param>
    /// <param name="input">The input render texture.</param>
    /// <param name="target">The target framebuffer.</param>
    public abstract void Blit(RenderContext context, RenderTexture input, GPUFrameBuffer target);

    protected override void Dispose(bool disposing)
    {

    }
}
