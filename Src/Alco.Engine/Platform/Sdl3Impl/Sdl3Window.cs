using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL3;
using Alco.Graphics;
using Alco.Engine.MacOS;

using static SDL3.SDL3;
using System.Text;

namespace Alco.Engine;

public unsafe partial class Sdl3Window : View
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

    public override Vector2 MousePosition
    {
        get
        {
            Vector2 globalPosition = default;
            SDL_GetGlobalMouseState(&globalPosition.X, &globalPosition.Y);
            Vector2 windowPosition = Position;
            return globalPosition - windowPosition;
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



    public Sdl3Window(GPUDevice device, ViewSetting setting)
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

    public override void SetTextInputArea(int x, int y, int width, int height, int cursor)
    {
        Rectangle rectangle = new Rectangle(x, y, width, height);
        var _ = SDL_SetTextInputArea(_window, &rectangle, cursor);
    }

    protected override void StartTextInput()
    {
        _ = SDL_StartTextInput(_window);
    }

    protected override void EndTextInput()
    {
        _ = SDL_StopTextInput(_window);
    }

    public override void Close()
    {

    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
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
    public static Sdl3Window CreateFromHWND(GPUDevice device, IntPtr hwnd, ViewSetting setting)
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

    public static Sdl3Window CreateFromXID(GPUDevice device, long xWindow, ViewSetting setting)
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

    public static Sdl3Window CreateFromNSWindow(GPUDevice device, IntPtr NSWindow, IntPtr NSView, ViewSetting setting)
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

    public static Sdl3Window CreateFromWaylandSurface(GPUDevice device, IntPtr surface, ViewSetting setting)
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

    public void DoTextInputCore(ReadOnlySpan<char> text)
    {
        DoTextInput(text);
    }

    private static SDL_PropertiesID CreateProperties(ViewSetting setting)
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

    public Task<string[]> OpenFilePickerAsync(string? defaultPath, bool allowMultiple, params ReadOnlySpan<DialogFileFilter> filters)
    {
        defaultPath ??= Environment.CurrentDirectory;
        TaskCompletionSource<string[]> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Sdl3FilePickerContext context = new Sdl3FilePickerContext
        {
            Completion = tcs
        };
        context.Handle = GCHandle.Alloc(context, GCHandleType.Normal);

        Span<byte> defaultPathBytes = stackalloc byte[Encoding.UTF8.GetByteCount(defaultPath) + 1];
        Encoding.UTF8.GetBytes(defaultPath, defaultPathBytes);
        defaultPathBytes[defaultPathBytes.Length - 1] = 0;

        int filterCount = filters.Length;
        SDL_DialogFileFilter* nativeFilters = stackalloc SDL_DialogFileFilter[filterCount]; ;
        List<NativeUtf8String> nativeStrings = new();

        for (int i = 0; i < filterCount; i++)
        {
            NativeUtf8String nativeName = new NativeUtf8String(filters[i].Name);
            NativeUtf8String nativePattern = new NativeUtf8String(filters[i].Pattern);
            nativeFilters[i].name = nativeName.UnsafePointer;
            nativeFilters[i].pattern = nativePattern.UnsafePointer;
            nativeStrings.Add(nativeName);
            nativeStrings.Add(nativePattern);
        }


        SDL_ShowOpenFileDialog(&DialogFileCallback, (nint)context.Handle, _window, nativeFilters, filterCount, defaultPathBytes, allowMultiple);

        for (int i = 0; i < nativeStrings.Count; i++)
        {
            nativeStrings[i].Dispose();
        }

        return tcs.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private unsafe static void DialogFileCallback(nint userdata, byte** fileList, int filter)
    {
        if (userdata == 0)
        {
            return;
        }

        GCHandle handle = GCHandle.FromIntPtr(userdata);
        if (handle.Target is not Sdl3FilePickerContext context)
        {
            return;
        }

        try
        {
            if (fileList == null)
            {
                context.Completion.TrySetException(new Exception(SDL_GetError() ?? "SDL file dialog error"));
                return;
            }

            List<string> results = new List<string>(4);
            int idx = 0;
            while (true)
            {
                byte* entry = fileList[idx];
                if (entry == null)
                {
                    break;
                }
                string? path = Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(entry));
                if (path != null)
                {
                    results.Add(path);
                }
                idx++;
            }

            context.Completion.TrySetResult(results.ToArray());
        }
        catch (Exception e)
        {
            context.Completion.TrySetException(e);
        }
        finally
        {
            context.Cleanup();
        }
    }


    [LibraryImport("kernel32")]
    private static partial nint GetModuleHandleW(ushort* lpModuleName);
}