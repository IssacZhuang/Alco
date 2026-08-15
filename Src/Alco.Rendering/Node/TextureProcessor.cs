using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Base class for processors that render texture content into a target with fullscreen
/// passes (bloom, FXAA, ...). Typically wrapped by a <see cref="RGNode_ChainTransform"/>
/// for pipeline orchestration.
/// <br/>Processors record onto a caller-owned <see cref="GPUCommandBuffer"/> and never
/// submit: callers inside a render graph pass the frame-shared buffer so the passes
/// execute in graph order with the rest of the frame.
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
    /// <paramref name="command"/>, rendering the result into <paramref name="target"/>.
    /// Implementations rebuild their resolution-dependent resources lazily from the
    /// input's current size, so an in-place resized input needs no other notification.
    /// </summary>
    /// <param name="command">The caller-owned command buffer to record into; it must
    /// already be open and is neither ended nor submitted by this call.</param>
    /// <param name="input">The input render texture.</param>
    /// <param name="target">The target framebuffer.</param>
    public abstract void Blit(GPUCommandBuffer command, RenderTexture input, GPUFrameBuffer target);

    protected override void Dispose(bool disposing)
    {

    }
}
