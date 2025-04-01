using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL3;
using Alco.Graphics;
using Alco.Engine.MacOS;

using static SDL3.SDL3;

namespace Alco.Engine;

public unsafe partial class Sdl3Window : Window
{
    private static readonly byte* PropertyId_HWND = Utf8CustomMarshaller.ConvertToUnmanaged("SDL.window.win32.hwnd");
    private static readonly byte* PropertyId_NS_WINDOW = Utf8CustomMarshaller.ConvertToUnmanaged("SDL.window.cocoa.window");

    private static readonly byte* PropertyId_WAYLAND_DISPLAY = Utf8CustomMarshaller.ConvertToUnmanaged("SDL.window.wayland.display");
    private static readonly byte* PropertyId_WAYLAND_SURFACE = Utf8CustomMarshaller.ConvertToUnmanaged("SDL.window.wayland.surface");

    private static readonly byte* PropertyId_X11_DISPLAY = Utf8CustomMarshaller.ConvertToUnmanaged("SDL.window.x11.display");
    private static readonly byte* PropertyId_X11_WINDOW = Utf8CustomMarshaller.ConvertToUnmanaged("SDL.window.x11.window");


    private readonly SDL_Window _window;
    private readonly GPUSwapchain _swapchain;
    private string _title;

    internal SDL_WindowID WindowId => SDL_GetWindowID(_window);

    public SDL_Window NativeWindow => _window;

    public override WindowMode WindowMode
    {
        get
        {
            return ConvertWindowMode(SDL_GetWindowFlags(_window));
        }
        set
        {
            switch (value)
            {
                case WindowMode.Normal:
                    //_ = SDL_SetWindowFullscreen(_window, false);
                    _ = SDL_RestoreWindow(_window);
                    break;
                case WindowMode.Minimized:
                    _ = SDL_MinimizeWindow(_window);
                    break;
                case WindowMode.Maximized:
                    _ = SDL_MaximizeWindow(_window);
                    break;
                case WindowMode.Fullscreen:
                    _ = SDL_SetWindowFullscreen(_window, true);
                    break;
            }
        }
    }

    public override int2 Position
    {
        get
        {
            int2 result = default;
            SDL_GetWindowPosition(_window, out result.X, out result.Y);
            return result;
        }
        set
        {
            _ = SDL_SetWindowPosition(_window, value.X, value.Y);
        }
    }

    public override int2 MousePosition
    {
        get
        {
            Vector2 globalPosition = default;
            SDL_GetGlobalMouseState(&globalPosition.X, &globalPosition.Y);
            int2 result = new int2((int)globalPosition.X, (int)globalPosition.Y);
            return result - Position;
        }
    }

    public override uint2 Size
    {
        get
        {
            SDL_GetWindowSize(_window, out int x, out int y);
            return new uint2(x, y);
        }
        set
        {
            _ = SDL_SetWindowSize(_window, (int)value.X, (int)value.Y);
        }
    }
    public override string Title
    {
        get
        {
            return _title;
        }
        set
        {
            SDL_SetWindowTitle(_window, value);
            _title = value;
        }
    }

    public override GPUSwapchain? Swapchain
    {
        get => _swapchain;
    }



    public Sdl3Window(GPUDevice device, WindowSetting setting)
    {
        SDL_WindowFlags flags = ConvetWindowMode(setting.WindowMode);

        if (setting.IsBorderless)
        {
            flags |= SDL_WindowFlags.Borderless;
        }

        if (setting.IsTransparent)
        {
            flags |= SDL_WindowFlags.Transparent;
        }

        _window = SDL_CreateWindow(setting.Title, setting.Width, setting.Height, flags);
        _title = setting.Title;
        if (_window.IsNull)
        {
            throw new Exception("Failed to create SDL window");
        }

        SwapchainDescriptor descriptor = new SwapchainDescriptor()
        {
            Name = $"{Title}_swapchain",
            SurfaceSource = GetSurfaceSource(_window, setting.LinuxUseWayland),
            Width = Size.X,
            Height = Size.Y,
            ColorFormat = device.PrefferedSurfaceFomat,
            IsVSyncEnabled = setting.VSync,
        };

        _swapchain = device.CreateSwapchain(descriptor);
    }

    internal Sdl3Window(GPUDevice device, SDL_Window window)
    {
        _window = window;
        _title = SDL_GetWindowTitle(window) ?? string.Empty;
        _swapchain = device.CreateSwapchain(new SwapchainDescriptor()
        {
            Name = $"{_title}_swapchain",
            SurfaceSource = GetSurfaceSource(_window, false),
            Width = Size.X,
            Height = Size.Y,
        });
    }

    public override void StartTextInput(int x, int y, int width, int height, int cursor)
    {
        Rectangle rectangle = new Rectangle(x, y, width, height);
        var _ = SDL_SetTextInputArea(_window, &rectangle, cursor);
        _ = SDL_StartTextInput(_window);
    }

    public override void EndTextInput()
    {
        _ = SDL_StopTextInput(_window);
    }

    public override void Close()
    {
        
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            _swapchain.Dispose();
        }
        SDL_DestroyWindow(_window);
    }

    private SDL_WindowFlags ConvetWindowMode(WindowMode mode)
    {
        return mode switch
        {
            WindowMode.Normal => SDL_WindowFlags.Resizable,
            WindowMode.Minimized => SDL_WindowFlags.Minimized,
            WindowMode.Maximized => SDL_WindowFlags.Maximized,
            WindowMode.Fullscreen => SDL_WindowFlags.Fullscreen,
            _ => SDL_WindowFlags.Resizable
        };
    }

    private WindowMode ConvertWindowMode(SDL_WindowFlags flags)
    {
        return flags switch
        {
            SDL_WindowFlags.Resizable => WindowMode.Normal,
            SDL_WindowFlags.Minimized => WindowMode.Minimized,
            SDL_WindowFlags.Maximized => WindowMode.Maximized,
            SDL_WindowFlags.Fullscreen => WindowMode.Fullscreen,
            _ => WindowMode.Normal
        };
    }


    /// <summary>
    /// Get the surface source for the window
    /// </summary>
    /// <param name="window">The window to get the surface source for</param>
    /// <param name="useWayland">Whether to use Wayland. Only used on Linux</param>
    /// <returns>The surface source for the window</returns>
    public static SurfaceSource GetSurfaceSource(SDL_Window window, bool useWayland)
    {
        if (OperatingSystem.IsWindows())
        {

            IntPtr hwnd = SDL_GetPointerProperty(SDL_GetWindowProperties(window), PropertyId_HWND, IntPtr.Zero);
            IntPtr hinstance = GetModuleHandleW(null);
            return SurfaceSource.CreateWin32Window(hwnd, hinstance);
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            NSWindow nsWindow = new NSWindow(SDL_GetPointerProperty(SDL_GetWindowProperties(window), PropertyId_NS_WINDOW, IntPtr.Zero));
            CAMetalLayer layer = CAMetalLayer.New();
            nsWindow.contentView.wantsLayer = true;
            nsWindow.contentView.layer = layer.Handle;

            return SurfaceSource.CreateMetalLayer(layer.Handle);
        }
        else if (OperatingSystem.IsLinux())
        {
            if (useWayland)
            {
                IntPtr display = SDL_GetPointerProperty(SDL_GetWindowProperties(window), PropertyId_WAYLAND_DISPLAY, IntPtr.Zero);
                IntPtr surface = SDL_GetPointerProperty(SDL_GetWindowProperties(window), PropertyId_WAYLAND_SURFACE, IntPtr.Zero);
                return SurfaceSource.CreateWaylandSurface(display, surface);
            }
            else
            {
                IntPtr display = SDL_GetPointerProperty(SDL_GetWindowProperties(window), PropertyId_X11_DISPLAY, IntPtr.Zero);
                ulong xWindow = (ulong)SDL_GetPointerProperty(SDL_GetWindowProperties(window), PropertyId_X11_WINDOW, IntPtr.Zero);
                return SurfaceSource.CreateXlibWindow(display, xWindow);
            }
        }

        throw new PlatformNotSupportedException();
    }


    //create with native window
    public static Sdl3Window CreateFromHWND(GPUDevice device, IntPtr hwnd, WindowSetting setting)
    {
        SDL_PropertiesID props = 0;

        try
        {
            props = CreateProperties(setting);
            SDL_SetPointerProperty(props, SDL_PROP_WINDOW_CREATE_WIN32_HWND_POINTER, hwnd);
            SDL_Window window = SDL_CreateWindowWithProperties(props);
            if (window.IsNull)
            {
                throw new Exception($"Failed to create SDL window: {SDL_GetError()}");
            }

            return new Sdl3Window(device, window);
        }
        finally
        {
            SDL_DestroyProperties(props);
        }
    }

    public static Sdl3Window CreateFromXID(GPUDevice device, long xWindow, WindowSetting setting)
    {
        SDL_PropertiesID props = 0;

        try
        {
            props = CreateProperties(setting);
            SDL_SetNumberProperty(props, SDL_PROP_WINDOW_X11_WINDOW_NUMBER, xWindow);
            SDL_Window window = SDL_CreateWindowWithProperties(props);
            if (window.IsNull)
            {
                throw new Exception($"Failed to create SDL window: {SDL_GetError()}");
            }

            return new Sdl3Window(device, window);
        }
        finally
        {
            SDL_DestroyProperties(props);
        }
    }

    public static Sdl3Window CreateFromNSWindow(GPUDevice device, IntPtr NSWindow, IntPtr NSView, WindowSetting setting)
    {
        SDL_PropertiesID props = 0;
        
        try
        {
            props = CreateProperties(setting);
            SDL_SetPointerProperty(props, SDL_PROP_WINDOW_CREATE_COCOA_WINDOW_POINTER, NSWindow);
            if (NSView != IntPtr.Zero)
            {
                SDL_SetPointerProperty(props, SDL_PROP_WINDOW_CREATE_COCOA_VIEW_POINTER, NSView);
            }
            SDL_Window window = SDL_CreateWindowWithProperties(props);
            if (window.IsNull)
            {
                throw new Exception($"Failed to create SDL window: {SDL_GetError()}");
            }

            return new Sdl3Window(device, window);
        }
        finally
        {
            SDL_DestroyProperties(props);
        }
    }

    public static Sdl3Window CreateFromWaylandSurface(GPUDevice device, IntPtr surface, WindowSetting setting)
    {
        SDL_PropertiesID props = 0;

        try
        {
            props = CreateProperties(setting);
            SDL_SetPointerProperty(props, SDL_PROP_WINDOW_CREATE_WAYLAND_WL_SURFACE_POINTER, surface);
            SDL_Window window = SDL_CreateWindowWithProperties(props);
            if (window.IsNull)
            {
                throw new Exception($"Failed to create SDL window: {SDL_GetError()}");
            }

            return new Sdl3Window(device, window);
        }
        finally
        {
            SDL_DestroyProperties(props);
        }
    }

    private static SDL_PropertiesID CreateProperties(WindowSetting setting)
    {
        SDL_PropertiesID props = SDL_CreateProperties();
        if (props == 0)
        {
            throw new Exception("Failed to create SDL properties");
        }

        if (setting.IsBorderless)
        {
            SDL_SetBooleanProperty(props, SDL_PROP_WINDOW_CREATE_BORDERLESS_BOOLEAN, true);
        }

        if (setting.IsTransparent)
        {
            SDL_SetBooleanProperty(props, SDL_PROP_WINDOW_CREATE_TRANSPARENT_BOOLEAN, true);
        }

        //size
        SDL_SetNumberProperty(props, SDL_PROP_WINDOW_CREATE_WIDTH_NUMBER, setting.Width);
        SDL_SetNumberProperty(props, SDL_PROP_WINDOW_CREATE_HEIGHT_NUMBER, setting.Height);

        return props;

    }


    [LibraryImport("kernel32")]
    private static partial nint GetModuleHandleW(ushort* lpModuleName);
}