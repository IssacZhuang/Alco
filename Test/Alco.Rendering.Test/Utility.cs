using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

internal static class Utility
{

    internal static DummyRenderingSystemHost CreateRenderingSystem()
    {
        GPUDevice device = GraphicsDeviceFactory.GetNoGPUDevice();
        DummyRenderingSystemHost host = new DummyRenderingSystemHost();
        RenderingSystem renderingSystem = new RenderingSystem(
            host,
            device,
            PixelFormat.RGBA16Float,
            PixelFormat.Depth24PlusStencil8
        );
        // Tests register module sources explicitly (GetShaderFromModule); the
        // resolver only answers imports, so an empty one suffices.
        renderingSystem.SetShaderModuleResolver(ShaderModuleResolver.Create(
            _ => null, () => []));
        host.RenderingSystem = renderingSystem;
        return host;
    }
}




