using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

public partial class GameEngine
{
    public View CreateView(ViewSetting setting)
    {
        return _platform.CreateView(_graphicsDevice, setting);
    }

    public ViewRenderTarget CreateViewRenderTarget(View view, GPURenderPass renderPass, Shader blitShader)
    {
        return new ViewRenderTarget(this, view, renderPass, blitShader);
    }

    private GPUDevice CreateGraphicsDevice(GraphicsSetting setting, uint disposeDelay)
    {
        if (setting.Backend == GraphicsBackend.None)
        {
            return GraphicsDeviceFactory.GetNoGPUDevice();
        }

        DeviceDescriptor deviceDescriptor = new DeviceDescriptor()
        {
            Host = this,
            Debug = setting.DebugInfo,
            Backend = setting.Backend,
            PreferredSurfaceFormat = setting.PreferredSurfaceFormat,
            PushConstantsSize = 128,
            DisposeDelay = disposeDelay,
            Name = "graphics_device"
        };

        return GraphicsDeviceFactory.CreateWebGPUDevice(deviceDescriptor);
    }

}