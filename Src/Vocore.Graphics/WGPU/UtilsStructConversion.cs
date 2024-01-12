using System.Text;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

public static partial class UtilsWebGPU
{
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

    public unsafe static WGPUShaderModule CreateShaderModule(this WGPUDevice device, in ShaderStageSource source)
    {
        WGPUShaderModuleDescriptor shaderDesc = new()
        {
            hintCount = 0,
            hints = null
        };
        if (source.Language == ShaderLanguage.SPIRV)
        {
            fixed (byte* ptr = source.Source)
            {
                WGPUShaderModuleSPIRVDescriptor descriptor = new WGPUShaderModuleSPIRVDescriptor()
                {
                    codeSize = (uint)source.Source.Length / sizeof(uint),
                    code = (uint*)ptr,
                    chain = new WGPUChainedStruct()
                    {
                        next = null,
                        sType = WGPUSType.ShaderModuleSPIRVDescriptor,
                    },
                };

                shaderDesc.nextInChain = (WGPUChainedStruct*)&descriptor;

                return wgpuDeviceCreateShaderModule(device, &shaderDesc);
            }
        }
        else if (source.Language == ShaderLanguage.WGSL)
        {
            string code = Encoding.UTF8.GetString(source.Source);
            fixed (sbyte* ptr = code.GetUtf8Span())
            {
                WGPUShaderModuleWGSLDescriptor descriptor = new WGPUShaderModuleWGSLDescriptor()
                {
                    code = ptr,
                    chain = new WGPUChainedStruct()
                    {
                        next = null,
                        sType = WGPUSType.ShaderModuleWGSLDescriptor,
                    },
                };

                shaderDesc.nextInChain = (WGPUChainedStruct*)&descriptor;

                return wgpuDeviceCreateShaderModule(device, &shaderDesc);
            }
        }

        throw new GraphicsException($"Unsupported shader language {source.Language}, only SPIRV and WGSL are supported. Try compiling your shader to SPIRV if you are using HLSL or GLSL.");
    }
}