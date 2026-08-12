using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Base class for processors that render texture content into a target with a fullscreen
/// pass (bloom, FXAA, ...). Typically wrapped by a <see cref="ChainTransformNode"/>
/// for pipeline orchestration.
/// </summary>
public abstract class TextureProcessor : AutoDisposable
{
    private readonly GPUDevice _device;

    private readonly Mesh _mesh;


    protected Mesh FullScreenMesh => _mesh;



    internal TextureProcessor(RenderingSystem renderingSystem, Shader processorShader)
    {
        _device = renderingSystem.GraphicsDevice;
        _mesh = renderingSystem.MeshFullScreen;
    }

    /// <summary>
    /// Processes <paramref name="input"/> and renders the result into
    /// <paramref name="target"/>. Implementations rebuild their resolution-dependent
    /// resources lazily from the input's current size, so an in-place resized input
    /// needs no other notification.
    /// </summary>
    public abstract void Blit(RenderTexture input, GPUFrameBuffer target);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Blit(RenderTexture input, RenderTexture target)
    {
        Blit(input, target.FrameBuffer);
    }


    protected override void Dispose(bool disposing)
    {

    }
}
