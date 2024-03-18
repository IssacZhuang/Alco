using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Vocore.Graphics;

namespace Vocore.Engine;

public static class GraphicsWindow
{
    private static string LogPrefix = "[Graphics]";

    public static void CreateGraphicsDeviceWithWindow(GraphicsSetting graphicsSetting, WindowSetting windowSetting, out GPUDevice device, out SilkWindow window)
    {
        WindowOptions silkWindowOptions = WindowOptions.Default;
        silkWindowOptions.API = GraphicsAPI.None;
        silkWindowOptions.Size = new Vector2D<int>(windowSetting.Width, windowSetting.Height);
        silkWindowOptions.FramesPerSecond = 60;
        silkWindowOptions.UpdatesPerSecond = 60;
        silkWindowOptions.Position = new Vector2D<int>(100, 100);
        silkWindowOptions.Title = windowSetting.Title;
        silkWindowOptions.IsVisible = true;
        silkWindowOptions.ShouldSwapAutomatically = false;
        silkWindowOptions.IsContextControlDisabled = true;

        var silkWindow = Silk.NET.Windowing.Window.Create(silkWindowOptions);
        silkWindow.Initialize();

        window = new SilkWindow(silkWindow);
        SurfaceSource surfaceSource = GetSurfaceSource(silkWindow.Native);

        DeviceDescriptor deviceDescriptor = new DeviceDescriptor()
        {
            Debug = graphicsSetting.DebugInfo,
            VSync = graphicsSetting.VSync,
            Backend = graphicsSetting.Backend,
            SurfaceSource = surfaceSource,
            InitialSurfaceSizeWidth = (uint)windowSetting.Width,
            InitialSurfaceSizeHeight = (uint)windowSetting.Height,
            SurfaceClearColor = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
            DepthFormat = PixelFormat.Depth24PlusStencil8,
            PushConstantsSize = 256,
            Name = windowSetting.Title
        };


        // if (backend == GraphicsBackend.Vulkan)
        // {
        //     RegisterLogger("[Vulkan]");
        //     device = GraphicsFactory.CreateVulkanDevice(deviceDescriptor);
        // }
        // else
        // {
        //     RegisterLogger("[WebGPU]");
        //     device = GraphicsFactory.CreateWebGPUDevice(deviceDescriptor);
        // }

        RegisterLogger("[WebGPU]");
        device = GraphicsFactory.CreateWebGPUDevice(deviceDescriptor);
    }

    public static SurfaceSource GetSurfaceSource(INativeWindow? window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.WinRT.HasValue)
        {
            throw new NotSupportedException("Silk.NET only supports CoreWindow UWP views, which WebGPU does not support today");
        }

        if (window.Win32.HasValue)
        {
            Log.Info("Creating Win32 window");
            return SurfaceSource.CreateWin32Window(window.Win32.Value.Hwnd, window.Win32.Value.HInstance);
        }

        if (window.Wayland.HasValue)
        {
            Log.Info("Creating Wayland window");
            return SurfaceSource.CreateWaylandSurface(window.Wayland.Value.Display, window.Wayland.Value.Surface);
        }

        if (window.X11.HasValue)
        {
            Log.Info("Creating X11 window");
            return SurfaceSource.CreateXlibWindow(window.X11.Value.Display, window.X11.Value.Window);
        }

        if (window.Android.HasValue)
        {
            Log.Info("Creating Android window");
            return SurfaceSource.CreateAndroidWindow(window.Android.Value.Window);
        }

        if (window.Cocoa.HasValue)
        {
            Log.Info("Creating Cocoa window");

            return SurfaceSource.CreateMetalLayer(window.Cocoa.Value);
        }

        if (window.UIKit.HasValue)
        {
            throw new NotSupportedException("Silk.NET only supports UIViews, which WebGPU does not support today");
        }

        throw new PlatformNotSupportedException();
    }

    

    internal static void RegisterLogger(string prefix)
    {
        LogPrefix = prefix;
        Graphics.GraphicsLogger.ErrorCallback = LogError;
        Graphics.GraphicsLogger.WarningCallback = LogWarning;
        Graphics.GraphicsLogger.InfoCallback = LogInfo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LogError(string message)
    {
        Log.Error(LogPrefix, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LogWarning(string message)
    {
        Log.Warning(LogPrefix, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LogInfo(string message)
    {
        Log.Info(LogPrefix, message);
    }
}
