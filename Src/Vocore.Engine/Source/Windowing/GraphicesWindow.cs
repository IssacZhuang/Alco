using System.Runtime.CompilerServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Input.Glfw;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Vocore.Graphics;
using Vocore.Graphics.WebGPU;

namespace Vocore.Engine
{
    public class GraphicsWindow
    {

        private readonly IWindow _window;
        private readonly GPUDevice _graphicsDevice;
        public GPUDevice GraphicsDevice
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _graphicsDevice;
        }
        public GraphicsWindow(WindowSetting setting)
        {
            GlfwInput.RegisterPlatform();
            GlfwWindowing.RegisterPlatform();
            WindowOptions silkWindowOptions = new WindowOptions(
                true,
                new Vector2D<int>(0, 0),
                new Vector2D<int>(setting.Width, setting.Height),
                -1,
                -1,
                new GraphicsAPI
                (
                    ContextAPI.None,
                    ContextProfile.Core,
                    ContextFlags.ForwardCompatible,
                    new APIVersion(1, 0)
                ),
                setting.Title,
                WindowState.Normal,
                WindowBorder.Resizable,
                false,
                false,
                VideoMode.Default,
                null);

            _window = Window.Create(silkWindowOptions);
            _window.Initialize();

            SurfaceSource surfaceSource = GetSurfaceSource(_window.Native);

            DeviceDescriptor deviceDescriptor = new DeviceDescriptor()
            {
                Debug = false,
                VSync = false,
                Backend = GraphicsBackend.Auto,
                SurfaceSource = surfaceSource,
                Name = setting.Title = " Graphics Device"
            };

            _graphicsDevice = new WebGPUDevice(deviceDescriptor);
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
                return SurfaceSource.CreateWin32Window(window.Win32.Value.Hwnd, window.Win32.Value.HInstance);
            }

            if (window.X11.HasValue)
            {
                return SurfaceSource.CreateXlibWindow(window.X11.Value.Display, window.X11.Value.Window);
            }

            if (window.Wayland.HasValue)
            {
                return SurfaceSource.CreateWaylandSurface(window.Wayland.Value.Display, window.Wayland.Value.Surface);
            }

            if (window.Android.HasValue)
            {
                return SurfaceSource.CreateAndroidWindow(window.Android.Value.Window);
            }

            if (window.Cocoa.HasValue)
            {
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