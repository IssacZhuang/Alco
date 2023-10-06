using Veldrid;
using Veldrid.SPIRV;

public class RenderPipeline
{
    private GraphicsDevice _device;
    private ResourceFactory _factory;
    public RenderPipeline(GraphicsDevice _graphicsDevice)
    {
        _device = _graphicsDevice;
        _factory = _device.ResourceFactory;
    }
}