using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Vocore.Graphics;
using Vocore.Graphics.WebGPU;

namespace Vocore.Engine
{
    public static class GraphicsWindow
    {
        public static void CreateGraphicsDeviceWithWindow(WindowSetting setting, out GPUDevice device, out IWindow window)
        {
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

            window = Window.Create(silkWindowOptions);
            window.Initialize();

            SurfaceSource surfaceSource = GetSurfaceSource(window.Native);

            DeviceDescriptor deviceDescriptor = new DeviceDescriptor()
            {
                Debug = true,
                VSync = false,
                Backend = GraphicsBackend.Auto,
                SurfaceSource = surfaceSource,
                InitialSurfaceSizeWidth = (uint)setting.Width,
                InitialSurfaceSizeHeight = (uint)setting.Height,
                SurfaceClearColor = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                Name = setting.Title
            };

            device = new WebGPUDevice(deviceDescriptor);
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

            if (window.X11.HasValue)
            {
                Log.Info("Creating X11 window");
                return SurfaceSource.CreateXlibWindow(window.X11.Value.Display, window.X11.Value.Window);
            }

            if (window.Wayland.HasValue)
            {
                Log.Info("Creating Wayland window");
                return SurfaceSource.CreateWaylandSurface(window.Wayland.Value.Display, window.Wayland.Value.Surface);
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
}