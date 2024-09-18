using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal static partial class UtilsWebGPU
{
    public unsafe static WGPUSurface CreateSurface(this WGPUInstance instance, SurfaceSource surface)
    {
        WGPUSurfaceDescriptor descriptor = new WGPUSurfaceDescriptor()
        {
            
        };
        switch (surface)
        {
            case HtmlCanvasSurfaceSource htmlCanvasSurface:
                fixed (byte* ptr = htmlCanvasSurface.Selector.GetUtf8Span())
                {
                    WGPUSurfaceDescriptorFromCanvasHTMLSelector canvasChain =
                    new WGPUSurfaceDescriptorFromCanvasHTMLSelector()
                    {
                        selector = ptr,
                        chain = new WGPUChainedStruct()
                        {
                            sType = WGPUSType.SurfaceDescriptorFromCanvasHTMLSelector,
                        },
                    };

                    descriptor.nextInChain = (WGPUChainedStruct*)&canvasChain;
                    // create the surface immediately because the pointer will be invalid outside of this block
                    return wgpuInstanceCreateSurface(instance, &descriptor);
                }

            case AndroidWindowSurfaceSource androidWindowSurface:
                WGPUSurfaceDescriptorFromAndroidNativeWindow widnowChain =
                new WGPUSurfaceDescriptorFromAndroidNativeWindow()
                {
                    window = (void*)androidWindowSurface.Window,
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
                    layer = (void*)metalLayerSurface.Layer,
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
                    hinstance = (void*)win32Surface.HInstance,
                    hwnd = (void*)win32Surface.Hwnd,
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
                    display = (void*)waylandSurface.Display,
                    surface = (void*)waylandSurface.Surface,
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
                    connection = (void*)xcbWindowSurface.Connection,
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
                    display = (void*)xlibWindowSurface.Display,
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

    public unsafe static WGPUShaderModule CreateShaderModule(this WGPUDevice device, in ShaderModule source)
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

                shaderDesc.nextInChain = &descriptor.chain;

                return wgpuDeviceCreateShaderModule(device, &shaderDesc);
            }
        }
        else if (source.Language == ShaderLanguage.WGSL)
        {
            string code = Encoding.UTF8.GetString(source.Source);
            fixed (byte* ptr = code.GetUtf8Span())
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

                shaderDesc.nextInChain = &descriptor.chain;

                return wgpuDeviceCreateShaderModule(device, &shaderDesc);
            }
        }
        else if (source.Language == ShaderLanguage.GLSL)
        {
            string code = Encoding.UTF8.GetString(source.Source);
            fixed (byte* ptr = code.GetUtf8Span())
            {
                WGPUShaderModuleGLSLDescriptor descriptor = new WGPUShaderModuleGLSLDescriptor()
                {
                    code = ptr,
                    chain = new WGPUChainedStruct()
                    {
                        next = null,
                        sType = (WGPUSType)WGPUNativeSType.ShaderModuleGLSLDescriptor,
                    },
                };

                shaderDesc.nextInChain = &descriptor.chain;

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

    public static WGPUColor ConvertColor(Vector4 color)
    {
        return new WGPUColor()
        {
            r = color.X,
            g = color.Y,
            b = color.Z,
            a = color.W,
        };
    }

    public static WGPUBindGroupLayoutEntry ConvertToWebGPU(this BindGroupEntry binding)
    {
        WGPUBindGroupLayoutEntry result = new WGPUBindGroupLayoutEntry
        {
            binding = binding.Binding,
            visibility = ConvertShaderStage(binding.Stage),
        };

        if (binding.Type == BindingType.UniformBuffer)
        {
            result.buffer = new WGPUBufferBindingLayout
            {
                type = WGPUBufferBindingType.Uniform,
                hasDynamicOffset = false,
                minBindingSize = 0,
            };
        }

        if (binding.Type == BindingType.StorageBuffer)
        {
            result.buffer = new WGPUBufferBindingLayout
            {
                type = WGPUBufferBindingType.Storage,
                hasDynamicOffset = false,
                minBindingSize = 0,
            };
        }


        if (binding.Type == BindingType.Sampler)
        {
            result.sampler = new WGPUSamplerBindingLayout
            {
                type = WGPUSamplerBindingType.Filtering,
            };
        }

        if (binding.Type == BindingType.Texture)
        {
            result.texture = new WGPUTextureBindingLayout
            {
                sampleType = WGPUTextureSampleType.Float,
                viewDimension = TextureViewDimensionToWebGPU(binding.TextureInfo.ViewDimension),
                multisampled = false,
            };
        }

        if (binding.Type == BindingType.StorageTexture)
        {
            result.storageTexture = new WGPUStorageTextureBindingLayout
            {
                access = ConvertAccessMode(binding.StorageTextureInfo.Access),
                format = PixelFormatToWebGPU(binding.StorageTextureInfo.Format),
                viewDimension = TextureViewDimensionToWebGPU(binding.StorageTextureInfo.ViewDimension),
            };
        }

        return result;
    }

    public static WGPUStencilFaceState ConvertToWebGPU(StencilFaceState stencilFaceState){
        return new WGPUStencilFaceState()
        {
            compare = CompareFunctionToWebGPU(stencilFaceState.Compare),
            depthFailOp = StencilOperationToWebGPU(stencilFaceState.DepthFailOperation),
            failOp = StencilOperationToWebGPU(stencilFaceState.StencilFailOperation),
            passOp = StencilOperationToWebGPU(stencilFaceState.PassOperation),
        };
    }
}