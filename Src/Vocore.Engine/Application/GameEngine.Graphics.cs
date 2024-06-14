using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

public partial class GameEngine
{
    private static string GraphicsLogPrefix = "[Graphics]";
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
            PreferredSDRFormat = setting.PreferredSDRFormat,
            PreferredHDRFormat = setting.PreferredHDRFormat,
            DepthFormat = setting.DepthFormat,
            PushConstantsSize = 256,
            Name = "graphics_device"
        };

        RegisterLogger();
        return GraphicsFactory.CreateWebGPUDevice(deviceDescriptor);
    }

    private static void RegisterLogger()
    {
        GraphicsLogger.ErrorCallback = GraphicsLogError;
        //Graphics.GraphicsLogger.WarningCallback = LogWarning;
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