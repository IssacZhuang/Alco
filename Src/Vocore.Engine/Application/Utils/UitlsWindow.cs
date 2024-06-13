using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Vocore.Graphics;

namespace Vocore.Engine;

public static class UtilsWindow
{
    public static Window CreateWindow(this GPUDevice device, WindowSetting setting, out InputSystem input, out GPUSwapchain? swapchain)
    {
        if (setting.IsWindowDisabled)
        {
            input = new NoInputSystem();
            swapchain = null;
            return new NoWindow();
        }

        WindowOptions silkWindowOptions = WindowOptions.Default;
        silkWindowOptions.API = GraphicsAPI.None;
        silkWindowOptions.Size = new Vector2D<int>(setting.Width, setting.Height);
        silkWindowOptions.FramesPerSecond = 60;
        silkWindowOptions.UpdatesPerSecond = 60;
        silkWindowOptions.Position = new Vector2D<int>(100, 100);
        silkWindowOptions.Title = setting.Title;
        silkWindowOptions.IsVisible = true;
        silkWindowOptions.ShouldSwapAutomatically = false;
        silkWindowOptions.IsContextControlDisabled = true;

        IWindow silkWindow = Silk.NET.Windowing.Window.Create(silkWindowOptions);
        silkWindow.Initialize();

        Window window = new SilkWindow(silkWindow);
        input = new SilkInputSystem(silkWindow);

        //todo: format
        SwapchainDescriptor descriptor = new SwapchainDescriptor()
        {
            Name = $"{setting.Title}_swapchain",
            SurfaceSource = GetSurfaceSource(window),
            Width = (uint)setting.Width,
            Height = (uint)setting.Height,
            IsVSyncEnabled = setting.VSync,
            
        };
        
        swapchain = device.CreateSwapchain(descriptor);


        return new SilkWindow(silkWindow);
    }

    public static SurfaceSource GetSurfaceSource(Window window)
    {
        if (window is NoWindow)
        {
            return SurfaceSource.CreateNoSurface();
        }

        if (window is SilkWindow silkWindow)
        {
            return GetSilkSurfaceSource(silkWindow.InternalWindow.Native!);
        }

        throw new PlatformNotSupportedException();
    }

    public static SurfaceSource GetSilkSurfaceSource(INativeWindow window)
    {
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
}