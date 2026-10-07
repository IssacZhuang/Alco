namespace Alco.Graphics;

/// <summary>
/// Describes the native surface a swapchain attaches to, per platform: each factory wraps one
/// platform's native window or surface handle for <see cref="SwapchainDescriptor"/>.
/// </summary>
public abstract class SurfaceSource
{
    protected SurfaceSource()
    {
    }

    /// <summary>Creates a surface source for an Android window, wrapping the native window pointer.</summary>
    /// <param name="window">The native window handle.</param>
    /// <returns>The surface source for the Android window.</returns>
    public static SurfaceSource CreateAndroidWindow(IntPtr window) => new AndroidWindowSurfaceSource(window);

    /// <summary>Creates a surface source for a macOS/iOS Metal layer, wrapping the native layer pointer.</summary>
    /// <param name="layer">The native Metal layer handle.</param>
    /// <returns>The surface source for the Metal layer.</returns>
    public static SurfaceSource CreateMetalLayer(IntPtr layer) => new MetalLayerSurfaceHandle(layer);

    /// <summary>Creates a surface source for a Windows (Win32) window, wrapping the window and module-instance handles.</summary>
    /// <param name="hwnd">The Win32 window handle (HWND).</param>
    /// <param name="hInstance">The Win32 instance handle (HINSTANCE).</param>
    /// <returns>The surface source for the Win32 window.</returns>
    public static SurfaceSource CreateWin32Window(IntPtr hwnd, IntPtr hInstance) => new Win32SurfaceSource(hwnd, hInstance);

    /// <summary>Creates a surface source for a UWP/WinUI swap-chain panel, wrapping the native panel object and its logical DPI.</summary>
    /// <param name="swapChainPanelNative">The native swap-chain panel object.</param>
    /// <param name="logicalDpi">The panel's logical DPI.</param>
    /// <returns>The surface source for the swap-chain panel.</returns>
    public static SurfaceSource CreateSwapChainPanel(object swapChainPanelNative, float logicalDpi) => new SwapChainPanelSurfaceSource(swapChainPanelNative, logicalDpi);

    /// <summary>Creates a surface source for a Linux Wayland surface, wrapping the display and surface handles.</summary>
    /// <param name="display">The Wayland display connection handle.</param>
    /// <param name="surface">The Wayland surface handle.</param>
    /// <returns>The surface source for the Wayland surface.</returns>
    public static SurfaceSource CreateWaylandSurface(IntPtr display, IntPtr surface) => new WaylandSurfaceSource(display, surface);

    /// <summary>Creates a surface source for a Linux X11 window via XCB, wrapping the connection and window id.</summary>
    /// <param name="connection">The XCB connection handle.</param>
    /// <param name="window">The XCB window id.</param>
    /// <returns>The surface source for the XCB window.</returns>
    public static SurfaceSource CreateXcbWindow(IntPtr connection, uint window) => new XcbWindowSurfaceSource(connection, window);

    /// <summary>Creates a surface source for a Linux X11 window via Xlib, wrapping the display and window handles.</summary>
    /// <param name="display">The Xlib display handle.</param>
    /// <param name="window">The Xlib window id.</param>
    /// <returns>The surface source for the Xlib window.</returns>
    public static SurfaceSource CreateXlibWindow(IntPtr display, ulong window) => new XlibWindowSurfaceSource(display, window);

    /// <summary>Creates a surface source for an HTML canvas element (web builds), wrapping its CSS selector.</summary>
    /// <param name="selector">The CSS selector of the canvas element.</param>
    /// <returns>The surface source for the HTML canvas.</returns>
    public static SurfaceSource CreateHtmlCanvas(string selector) => new HtmlCanvasSurfaceSource(selector);

    /// <summary>Creates a surface source for headless/offscreen rendering with no native surface at all.</summary>
    /// <returns>The headless surface source.</returns>
    public static SurfaceSource CreateNoSurface() => new NoSurfaceSource();
}

internal class NoSurfaceSource : SurfaceSource
{
}

internal class HtmlCanvasSurfaceSource : SurfaceSource
{
    public string Selector { get; }
    public HtmlCanvasSurfaceSource(string selector) => Selector = selector;
}

internal class AndroidWindowSurfaceSource : SurfaceSource
{
    public IntPtr Window { get; }

    public AndroidWindowSurfaceSource(IntPtr window) => Window = window;
}

internal class MetalLayerSurfaceHandle : SurfaceSource
{
    public IntPtr Layer { get; }

    public MetalLayerSurfaceHandle(IntPtr layer) => Layer = layer;
}

internal class Win32SurfaceSource : SurfaceSource
{
    public IntPtr Hwnd { get; }
    public IntPtr HInstance { get; }

    public Win32SurfaceSource(IntPtr hwnd, IntPtr hinstance)
    {
        Hwnd = hwnd;
        HInstance = hinstance;
    }
}

internal class SwapChainPanelSurfaceSource : SurfaceSource
{
    public object SwapChainPanelNative { get; }
    public float LogicalDpi { get; }

    public SwapChainPanelSurfaceSource(object swapChainPanelNative, float logicalDpi)
    {
        SwapChainPanelNative = swapChainPanelNative;
        LogicalDpi = logicalDpi;
    }
}

internal class WaylandSurfaceSource : SurfaceSource
{
    public IntPtr Display { get; }
    public IntPtr Surface { get; }

    public WaylandSurfaceSource(IntPtr display, IntPtr surface)
    {
        Display = display;
        Surface = surface;
    }
}

internal class XcbWindowSurfaceSource : SurfaceSource
{
    public IntPtr Connection { get; }
    public uint Window { get; }

    public XcbWindowSurfaceSource(IntPtr connection, uint window)
    {
        Connection = connection;
        Window = window;
    }
}

internal class XlibWindowSurfaceSource : SurfaceSource
{
    public IntPtr Display { get; }
    public ulong Window { get; }

    public XlibWindowSurfaceSource(IntPtr display, ulong window)
    {
        Display = display;
        Window = window;
    }
}