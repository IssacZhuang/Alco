using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Rendering
{

    public class RenderPipeline
    {
        private GraphicsDevice _device;
        private ResourceFactory _factory;
        private CommandList _commandList;
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        public RenderPipeline(GraphicsDevice _graphicsDevice)
        {
            _device = _graphicsDevice;
            _factory = _device.ResourceFactory;
            _commandList = _factory.CreateCommandList();

        }

    }
}