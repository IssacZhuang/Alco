using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class PostProcess : AutoDisposable
{
    private readonly GPUDevice _device;
    
    private readonly Mesh _mesh;
  

    protected Mesh FullScreenMesh => _mesh;
    


    internal PostProcess(RenderingSystem renderingSystem, Shader postProcessShader)
    {
        _device = renderingSystem.GraphicsDevice;
        _mesh = renderingSystem.MeshFullScreen;
    }

    public virtual void SetInput(GPUFrameBuffer input)
    {
        
       
    }

    public abstract void Blit(GPUFrameBuffer target);


    protected override void Dispose(bool disposing)
    {

    }
}