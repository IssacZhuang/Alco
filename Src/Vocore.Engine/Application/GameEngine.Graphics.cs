using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public partial class GameEngine
{
    public Window CreateWindow(WindowSetting setting)
    {
        return _platform.CreateWindow(_graphicsDevice, setting);
    }

    public WindowRenderTarget CreateWindowRenderTarget(Window window, GPURenderPass renderPass, Shader blitShader)
    {
        return new WindowRenderTarget(this, window, renderPass, blitShader);
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