namespace Vocore.Graphics;

public abstract class SurfaceSource
{
    protected SurfaceSource()
    {
    }

    public static SurfaceSource CreateAndroidWindow(IntPtr window) => new AndroidWindowSurfaceSource(window);
    public static SurfaceSource CreateMetalLayer(IntPtr layer) => new MetalLayerSurfaceHandle(layer);
    public static SurfaceSource CreateWin32Window(IntPtr hwnd, IntPtr hInstance) => new Win32SurfaceSource(hwnd, hInstance);
    public static SurfaceSource CreateSwapChainPanel(object swapChainPanelNative, float logicalDpi) => new SwapChainPanelSurfaceSource(swapChainPanelNative, logicalDpi);
    public static SurfaceSource CreateWaylandSurface(IntPtr display, IntPtr surface) => new WaylandSurfaceSource(display, surface);
    public static SurfaceSource CreateXcbWindow(IntPtr connection, uint window) => new XcbWindowSurfaceSource(connection, window);
    public static SurfaceSource CreateXlibWindow(IntPtr display, ulong window) => new XlibWindowSurfaceSource(display, window);
    public static SurfaceSource CreateHtmlCanvas(string selector) => new HtmlCanvasSurfaceSource(selector);
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