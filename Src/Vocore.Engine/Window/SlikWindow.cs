using System.Runtime.CompilerServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Vocore.Graphics;

namespace Vocore.Engine;

/// <summary>
/// The window implementation based on Silk.NET.
/// </summary>
public class SilkWindow : Window
{
    private readonly IWindow _slikWindow;
    private readonly GPUSwapchain _swapchain;
    private readonly SilkInputSystem _input;

    public IWindow InternalWindow => _slikWindow;

    public SilkWindow(GPUDevice device, WindowSetting setting)
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

        IWindow silkWindow = Silk.NET.Windowing.Window.Create(silkWindowOptions);
        silkWindow.Initialize();

        _slikWindow = silkWindow;
        _slikWindow.Resize += OnSilkResize;

        SwapchainDescriptor descriptor = new SwapchainDescriptor()
        {
            Name = $"{Title}_swapchain",
            SurfaceSource = GetSurfaceSource(silkWindow.Native!),
            Width = (uint)Size.x,
            Height = (uint)Size.y,
            ColorFormat = device.PrefferedSurfaceFomat,
            IsVSyncEnabled = setting.VSync,
        };

        _swapchain = device.CreateSwapchain(descriptor);

        _input = new SilkInputSystem(_slikWindow);
    }

    /// <inheritdoc />
    public override WindowMode WindowMode
    {
        get
        {
            return (WindowMode)_slikWindow.WindowState;
        }
        set
        {
            _slikWindow.WindowState = value;
        }
    }

    /// <inheritdoc />
    public override int2 Size
    {
        get
        {
            return new int2(_slikWindow.Size.X, _slikWindow.Size.Y);
        }
        set
        {
            _slikWindow.Size = new Vector2D<int>(value.x, value.y);
        }
    }

    /// <inheritdoc />
    public override string Title
    {
        get
        {
            return _slikWindow.Title;
        }
        set
        {
            _slikWindow.Title = value;
        }
    }

    public override GPUSwapchain Swapchain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _swapchain;
    }

    public override InputSystem Input
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input;
    }

    private void OnSilkResize(Vector2D<int> size)
    {
        OnResize?.Invoke(new int2(size.X, size.Y));
    }

    public override void Close()
    {
        _slikWindow.Close();
        _slikWindow.Dispose();   
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _slikWindow.Dispose();
        }
    }

    public static SurfaceSource GetSurfaceSource(INativeWindow window)
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