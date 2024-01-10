namespace Vocore.Graphics;

public abstract class SurfaceHandle
{
    protected SurfaceHandle()
    {
    }

    public static SurfaceHandle CreateAndroidWindow(IntPtr window) => new AndroidWindowSurfaceHandle(window);
    public static SurfaceHandle CreateMetalLayer(IntPtr layer) => new MetalLayerSurfaceHandle(layer);
    public static SurfaceHandle CreateWin32Window(IntPtr hwnd) => new Win32SurfaceHandle(hwnd);
    public static SurfaceHandle CreateSwapChainPanel(object swapChainPanelNative, float logicalDpi) => new SwapChainPanelSurfaceHandle(swapChainPanelNative, logicalDpi);
    public static SurfaceHandle CreateWaylandSurface(IntPtr display, IntPtr surface) => new WaylandSurfaceHandle(display, surface);
    public static SurfaceHandle CreateXcbWindow(IntPtr connection, uint window) => new XcbWindowSurfaceHandle(connection, window);
    public static SurfaceHandle CreateXlibWindow(IntPtr display, ulong window) => new XlibWindowSurfaceHandle(display, window);
}

internal class AndroidWindowSurfaceHandle : SurfaceHandle
{
    public IntPtr Window { get; }

    public AndroidWindowSurfaceHandle(IntPtr window) => Window = window;
}

internal class MetalLayerSurfaceHandle : SurfaceHandle
{
    public IntPtr Layer { get; }

    public MetalLayerSurfaceHandle(IntPtr layer) => Layer = layer;
}

internal class Win32SurfaceHandle : SurfaceHandle
{
    public IntPtr Hwnd { get; }

    public Win32SurfaceHandle(IntPtr hwnd) => Hwnd = hwnd;
}

internal class SwapChainPanelSurfaceHandle : SurfaceHandle
{
    public object SwapChainPanelNative { get; }
    public float LogicalDpi { get; }

    public SwapChainPanelSurfaceHandle(object swapChainPanelNative, float logicalDpi)
    {
        SwapChainPanelNative = swapChainPanelNative;
        LogicalDpi = logicalDpi;
    }
}

internal class WaylandSurfaceHandle : SurfaceHandle
{
    public IntPtr Display { get; }
    public IntPtr Surface { get; }

    public WaylandSurfaceHandle(IntPtr display, IntPtr surface)
    {
        Display = display;
        Surface = surface;
    }
}

internal class XcbWindowSurfaceHandle : SurfaceHandle
{
    public IntPtr Connection { get; }
    public uint Window { get; }

    public XcbWindowSurfaceHandle(IntPtr connection, uint window)
    {
        Connection = connection;
        Window = window;
    }
}

internal class XlibWindowSurfaceHandle : SurfaceHandle
{
    public IntPtr Display { get; }
    public ulong Window { get; }

    public XlibWindowSurfaceHandle(IntPtr display, ulong window)
    {
        Display = display;
        Window = window;
    }
}