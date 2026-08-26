using Alco.Graphics;
using Alco.Rendering;
using Alco.ShaderCompiler;

namespace Alco.Rendering.Test;

internal static class Utility
{

    internal static DummyRenderingSystemHost CreateRenderingSystem(SlangFileResolver? resolver = null)
    {
        GPUDevice device = GraphicsDeviceFactory.GetNoGPUDevice();
        DummyRenderingSystemHost host = new DummyRenderingSystemHost();
        // Tests register module sources explicitly (GetShaderFromModule); a
        // resolver is only passed when a test serves importable files.
        RenderingSystem renderingSystem = new RenderingSystem(
            host,
            device,
            PixelFormat.RGBA16Float,
            PixelFormat.Depth24PlusStencil8,
            resolver
        );
        host.RenderingSystem = renderingSystem;
        return host;
    }
}




