using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D.Test;

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
        host.RenderingSystem = renderingSystem;
        return host;
    }
}
