using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics;

public static class UtilsWebGPU
{
    public static readonly Tuple<GraphicsBackend, WGPUInstanceBackend>[] BackendMap = new Tuple<GraphicsBackend, WGPUInstanceBackend>[]
    {
        new(GraphicsBackend.None, WGPUInstanceBackend.None),
        new(GraphicsBackend.Auto, WGPUInstanceBackend.Primary),
        new(GraphicsBackend.WebGPU, WGPUInstanceBackend.BrowserWebGPU),
        new(GraphicsBackend.Vulkan, WGPUInstanceBackend.Vulkan),
        new(GraphicsBackend.D3D11, WGPUInstanceBackend.DX11),
        new(GraphicsBackend.D3D12, WGPUInstanceBackend.DX12),
        new(GraphicsBackend.Metal, WGPUInstanceBackend.Metal),
        new(GraphicsBackend.OpenGL, WGPUInstanceBackend.GL),
        new(GraphicsBackend.OpenGLES, WGPUInstanceBackend.GL),
    };

    public static readonly Func<GraphicsBackend, WGPUInstanceBackend> ToWebGPU;
    public static readonly Func<WGPUInstanceBackend, GraphicsBackend> ToAbstract;

    static UtilsWebGPU()
    {
        UtilsCast.GenerateCastFunc(BackendMap, out ToWebGPU, out ToAbstract);
    }

    public unsafe static WGPUSurface CreateSurface(this WGPUInstance instance, SurfaceSource surface)
    {
        WGPUChainedStruct* chainStruct = default;
        switch (surface)
        {
            case AndroidWindowSurfaceSource androidWindowSurface:
                WGPUSurfaceDescriptorFromAndroidNativeWindow widnowChain =
                new WGPUSurfaceDescriptorFromAndroidNativeWindow()
                {
                    window = androidWindowSurface.Window,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceDescriptorFromAndroidNativeWindow,
                    },
                };

                chainStruct = (WGPUChainedStruct*)&widnowChain;
                break;
            case MetalLayerSurfaceHandle metalLayerSurface:
                WGPUSurfaceDescriptorFromMetalLayer metalLayerChain =
                new WGPUSurfaceDescriptorFromMetalLayer()
                {
                    layer = metalLayerSurface.Layer,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceDescriptorFromMetalLayer,
                    },
                };

                chainStruct = (WGPUChainedStruct*)&metalLayerChain;
                break;
            case Win32SurfaceHandle win32Surface:
                WGPUSurfaceDescriptorFromWindowsHWND win32Chain =
                new WGPUSurfaceDescriptorFromWindowsHWND()
                {
                    hinstance = (IntPtr)null,
                    hwnd = win32Surface.Hwnd,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceDescriptorFromWindowsHWND,
                    },
                };

                chainStruct = (WGPUChainedStruct*)&win32Chain;
                break;
            case WaylandSurfaceHandle waylandSurface:
                WGPUSurfaceDescriptorFromWaylandSurface surfaceChain =
                new WGPUSurfaceDescriptorFromWaylandSurface()
                {
                    display = waylandSurface.Display,
                    surface = waylandSurface.Surface,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceDescriptorFromWaylandSurface,
                    },
                };

                chainStruct = (WGPUChainedStruct*)&surfaceChain;
                break;
            case XcbWindowSurfaceHandle xcbWindowSurface:
                WGPUSurfaceDescriptorFromXcbWindow surfaceXlibChain =
                new WGPUSurfaceDescriptorFromXcbWindow()
                {
                    connection = xcbWindowSurface.Connection,
                    window = xcbWindowSurface.Window,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceDescriptorFromXcbWindow,
                    },
                };

                chainStruct = (WGPUChainedStruct*)&surfaceXlibChain;
                break;
            case XlibWindowSurfaceHandle xlibWindowSurface:
                WGPUSurfaceDescriptorFromXlibWindow surfaceXcbChain =
                new WGPUSurfaceDescriptorFromXlibWindow()
                {
                    display = xlibWindowSurface.Display,
                    window = xlibWindowSurface.Window,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceDescriptorFromXlibWindow,
                    },
                };

                chainStruct = (WGPUChainedStruct*)&surfaceXcbChain;
                break;
        }

        WGPUSurfaceDescriptor descriptor = new WGPUSurfaceDescriptor()
        {
            nextInChain = chainStruct,
        };

        return wgpuInstanceCreateSurface(instance, &descriptor);
    }
}