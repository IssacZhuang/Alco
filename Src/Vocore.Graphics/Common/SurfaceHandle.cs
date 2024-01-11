namespace Vocore.Graphics;

public abstract class SurfaceSource
{
    protected SurfaceSource()
    {
    }

    public static SurfaceSource CreateAndroidWindow(IntPtr window) => new AndroidWindowSurfaceSource(window);
    public static SurfaceSource CreateMetalLayer(IntPtr layer) => new MetalLayerSurfaceHandle(layer);
    public static SurfaceSource CreateWin32Window(IntPtr hwnd) => new Win32SurfaceHandle(hwnd);
    public static SurfaceSource CreateSwapChainPanel(object swapChainPanelNative, float logicalDpi) => new SwapChainPanelSurfaceHandle(swapChainPanelNative, logicalDpi);
    public static SurfaceSource CreateWaylandSurface(IntPtr display, IntPtr surface) => new WaylandSurfaceHandle(display, surface);
    public static SurfaceSource CreateXcbWindow(IntPtr connection, uint window) => new XcbWindowSurfaceHandle(connection, window);
    public static SurfaceSource CreateXlibWindow(IntPtr display, ulong window) => new XlibWindowSurfaceHandle(display, window);
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

internal class Win32SurfaceHandle : SurfaceSource
{
    public IntPtr Hwnd { get; }

    public Win32SurfaceHandle(IntPtr hwnd) => Hwnd = hwnd;
}

internal class SwapChainPanelSurfaceHandle : SurfaceSource
{
    public object SwapChainPanelNative { get; }
    public float LogicalDpi { get; }

    public SwapChainPanelSurfaceHandle(object swapChainPanelNative, float logicalDpi)
    {
        SwapChainPanelNative = swapChainPanelNative;
        LogicalDpi = logicalDpi;
    }
}

internal class WaylandSurfaceHandle : SurfaceSource
{
    public IntPtr Display { get; }
    public IntPtr Surface { get; }

    public WaylandSurfaceHandle(IntPtr display, IntPtr surface)
    {
        Display = display;
        Surface = surface;
    }
}

internal class XcbWindowSurfaceHandle : SurfaceSource
{
    public IntPtr Connection { get; }
    public uint Window { get; }

    public XcbWindowSurfaceHandle(IntPtr connection, uint window)
    {
        Connection = connection;
        Window = window;
    }
}

internal class XlibWindowSurfaceHandle : SurfaceSource
{
    public IntPtr Display { get; }
    public ulong Window { get; }

    public XlibWindowSurfaceHandle(IntPtr display, ulong window)
    {
        Display = display;
        Window = window;
    }
}