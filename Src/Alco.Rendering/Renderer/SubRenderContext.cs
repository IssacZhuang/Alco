

using Alco.Graphics;

namespace Alco.Rendering;

public sealed class SubRenderContext : AutoDisposable, IRenderContext, IMaterialResourceCollector
{
    private readonly GPURenderBundle _renderBundle;

    public SubRenderContext(RenderingSystem renderingSystem, string name)
    {
        GPUDevice device = renderingSystem.GraphicsDevice;
        _renderBundle = device.CreateRenderBundle(new RenderBundleDescriptor(name));
    }

    public void Draw(in Mesh mesh, in Material material, in int subMeshIndex = 0)
    {
        throw new NotImplementedException();
    }

    public void DrawInstanced(in Mesh mesh, in Material material, in uint instanceCount, in int subMeshIndex = 0)
    {
        throw new NotImplementedException();
    }

    public void DrawInstancedWithConstant<T>(in Mesh mesh, in Material material, in uint instanceCount, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        throw new NotImplementedException();
    }

    public void DrawInstancedWithConstant<T>(in Mesh mesh, in Material material, in uint instanceCount, in uint instanceStart, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        throw new NotImplementedException();
    }

    public void DrawWithConstant<T>(in Mesh mesh, in Material material, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        throw new NotImplementedException();
    }

    public void SetGraphicsResources(uint slot, GPUResourceGroup resourceGroup)
    {
        throw new NotImplementedException();
    }

    public void SetStencilReference(uint value)
    {
        throw new NotImplementedException();
    }

    protected override void Dispose(bool disposing)
    {
        _renderBundle.Dispose();
    }
}

