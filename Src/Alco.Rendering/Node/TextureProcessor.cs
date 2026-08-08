using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Base class for processors that render texture content into a target with a fullscreen
/// pass (bloom, FXAA, ...). Typically wrapped by a <see cref="IContentProcessorNode"/>
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

    public virtual void SetInput(RenderTexture input)
    {

    }

    public abstract void Blit(GPUFrameBuffer target);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Blit(RenderTexture renderTexture)
    {
        Blit(renderTexture.FrameBuffer);
    }


    protected override void Dispose(bool disposing)
    {

    }
}
