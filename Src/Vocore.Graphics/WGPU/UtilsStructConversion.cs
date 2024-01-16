using System.Runtime.InteropServices;
using System.Text;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

public static partial class UtilsWebGPU
{
    public unsafe static WGPUSurface CreateSurface(this WGPUInstance instance, SurfaceSource surface)
    {
        WGPUSurfaceDescriptor descriptor = new WGPUSurfaceDescriptor()
        {
            
        };
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

                descriptor.nextInChain = (WGPUChainedStruct*)&widnowChain;
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

                descriptor.nextInChain = (WGPUChainedStruct*)&metalLayerChain;
                break;
            case Win32SurfaceSource win32Surface:
                WGPUSurfaceDescriptorFromWindowsHWND win32Chain =
                new WGPUSurfaceDescriptorFromWindowsHWND()
                {
                    hinstance =win32Surface.HInstance,
                    hwnd = win32Surface.Hwnd,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceDescriptorFromWindowsHWND,
                    },
                };

                descriptor.nextInChain = (WGPUChainedStruct*)&win32Chain;
                break;
            case WaylandSurfaceSource waylandSurface:
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

                descriptor.nextInChain = (WGPUChainedStruct*)&surfaceChain;
                break;
            case XcbWindowSurfaceSource xcbWindowSurface:
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

                descriptor.nextInChain = (WGPUChainedStruct*)&surfaceXlibChain;
                break;
            case XlibWindowSurfaceSource xlibWindowSurface:
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

                descriptor.nextInChain = (WGPUChainedStruct*)&surfaceXcbChain;
                break;
        }

        

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

    public static WGPUVertexAttribute ConvertToWebGPU(this VertexElement attribute)
    {
        return new WGPUVertexAttribute()
        {
            format = VertexFormatToWebGPU(attribute.Format),
            offset = attribute.Offset,
            shaderLocation = attribute.Location,
        };
    }

    public static WGPUBlendComponent ConvertToWebGPU(this BlendComponent component)
    {
        return new WGPUBlendComponent()
        {
            srcFactor = BlendFactorToWebGPU(component.SrcFactor),
            dstFactor = BlendFactorToWebGPU(component.DstFactor),
            operation = BlendOperationToWebGPU(component.Operation),
        };
    }

    [LibraryImport("kernel32")]
    private unsafe static partial nint GetModuleHandleW(ushort* lpModuleName);
}