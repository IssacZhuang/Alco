using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Base class for processors that render texture content into a target with full-screen
/// passes. Processors record passes on a render context but never submit.
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
