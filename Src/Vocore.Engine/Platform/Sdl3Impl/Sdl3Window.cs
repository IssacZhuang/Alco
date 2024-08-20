
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL3;
using Vocore.Graphics;

using static SDL3.SDL3;

namespace Vocore.Engine;

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
            SDL_GetWindowPosition(_window, out result.x, out result.y);
            return result;
        }
        set
        {
            _ = SDL_SetWindowPosition(_window, value.x, value.y);
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
            _ = SDL_SetWindowSize(_window, (int)value.x, (int)value.y);
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
        _window = SDL_CreateWindow(setting.Title, setting.Width, setting.Height, ConvetWindowMode(setting.WindowMode));
        _title = setting.Title;
        if (_window.IsNull)
        {
            throw new Exception("Failed to create SDL window");
        }

        SwapchainDescriptor descriptor = new SwapchainDescriptor()
        {
            Name = $"{Title}_swapchain",
            SurfaceSource = GetSurfaceSource(_window, setting.LinuxUseWayland),
            Width = Size.x,
            Height = Size.y,
            ColorFormat = device.PrefferedSurfaceFomat,
            IsVSyncEnabled = setting.VSync,
        };

        _swapchain = device.CreateSwapchain(descriptor);
    }

    public override void StartTextInput(int x, int y, int width, int height, int cursor)
    {
        Rectangle rectangle = new Rectangle(x, y, width, height);
        int result = SDL_SetTextInputArea(_window, &rectangle, cursor);
        SDL_StartTextInput(_window);
        
        
        Log.Info("StartTextInput", result);
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


    private static SurfaceSource GetSurfaceSource(SDL_Window window, bool useWayland)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void DoResize(uint2 size)
    {
        OnResize?.Invoke(size);
    }

    [LibraryImport("kernel32")]
    private static partial nint GetModuleHandleW(ushort* lpModuleName);
}