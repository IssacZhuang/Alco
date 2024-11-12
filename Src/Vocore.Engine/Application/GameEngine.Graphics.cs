using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public partial class GameEngine
{
    private static string GraphicsLogPrefix = "[Graphics]";

    public Window CreateWindow(WindowSetting setting)
    {
        return _platform.CreateWindow(_graphicsDevice, setting);
    }

    public WindowRenderTarget CreateWindowRenderTarget(Window window, GPURenderPass renderPass, Shader blitShader)
    {
        return new WindowRenderTarget(this, window, renderPass, blitShader);
    }

    private static GPUDevice CreateGraphicsDevice(GraphicsSetting setting)
    {
        if (setting.Backend == GraphicsBackend.None)
        {
            return GraphicsFactory.GetNoGPUDevice();
        }

        DeviceDescriptor deviceDescriptor = new DeviceDescriptor()
        {
            Debug = setting.DebugInfo,
            Backend = setting.Backend,
            PreferredSurfaceFormat = setting.PreferredSurfaceFormat,
            PushConstantsSize = 256,
            Name = "graphics_device"
        };

        RegisterLogger();
        return GraphicsFactory.CreateWebGPUDevice(deviceDescriptor);
    }

    private static void RegisterLogger()
    {
        GraphicsLogger.ErrorCallback = GraphicsLogError;
        GraphicsLogger.WarningCallback = GraphicsLogWarning;
        GraphicsLogger.InfoCallback = GraphicsLogInfo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GraphicsLogError(string message)
    {
        Log.Error(GraphicsLogPrefix, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GraphicsLogWarning(string message)
    {
        Log.Warning(GraphicsLogPrefix, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GraphicsLogInfo(string message)
    {
        Log.Info(GraphicsLogPrefix, message);
    }
}