using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal static partial class WebGPUUtility
{
    public unsafe static WGPUSurface CreateSurface(this WGPUInstance instance, SurfaceSource surface)
    {
        WGPUSurfaceDescriptor descriptor = new WGPUSurfaceDescriptor()
        {
            
        };
        switch (surface)
        {
            case AndroidWindowSurfaceSource androidWindowSurface:
                WGPUSurfaceSourceAndroidNativeWindow widnowChain =
                new WGPUSurfaceSourceAndroidNativeWindow()
                {
                    window = (void*)androidWindowSurface.Window,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceSourceAndroidNativeWindow,
                    },
                };

                descriptor.nextInChain = (WGPUChainedStruct*)&widnowChain;
                break;

            case MetalLayerSurfaceHandle metalLayerSurface:
                WGPUSurfaceSourceMetalLayer metalLayerChain =
                new WGPUSurfaceSourceMetalLayer()
                {
                    layer = (void*)metalLayerSurface.Layer,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceSourceMetalLayer,
                    },
                };

                descriptor.nextInChain = (WGPUChainedStruct*)&metalLayerChain;
                break;

            case Win32SurfaceSource win32Surface:
                WGPUSurfaceSourceWindowsHWND win32Chain =
                new WGPUSurfaceSourceWindowsHWND()
                {
                    hinstance = (void*)win32Surface.HInstance,
                    hwnd = (void*)win32Surface.Hwnd,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceSourceWindowsHWND,
                    },
                };

                descriptor.nextInChain = (WGPUChainedStruct*)&win32Chain;
                break;

            case WaylandSurfaceSource waylandSurface:
                WGPUSurfaceSourceWaylandSurface surfaceChain =
                new WGPUSurfaceSourceWaylandSurface()
                {
                    display = (void*)waylandSurface.Display,
                    surface = (void*)waylandSurface.Surface,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceSourceWaylandSurface,
                    },
                };

                descriptor.nextInChain = (WGPUChainedStruct*)&surfaceChain;
                break;

            case XcbWindowSurfaceSource xcbWindowSurface:
                WGPUSurfaceSourceXCBWindow surfaceXlibChain =
                new WGPUSurfaceSourceXCBWindow()
                {
                    connection = (void*)xcbWindowSurface.Connection,
                    window = xcbWindowSurface.Window,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceSourceXCBWindow,
                    },
                };

                descriptor.nextInChain = (WGPUChainedStruct*)&surfaceXlibChain;
                break;

            case XlibWindowSurfaceSource xlibWindowSurface:
                WGPUSurfaceSourceXlibWindow surfaceXcbChain =
                new WGPUSurfaceSourceXlibWindow()
                {
                    display = (void*)xlibWindowSurface.Display,
                    window = xlibWindowSurface.Window,
                    chain = new WGPUChainedStruct()
                    {
                        sType = WGPUSType.SurfaceSourceXlibWindow,
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
            label = WGPUStringView.Empty
        };

        
        if (source.Language == ShaderLanguage.SPIRV)
        {
            fixed (byte* ptr = source.Source.Span)
            {
                WGPUShaderSourceSPIRV descriptor = new WGPUShaderSourceSPIRV()
                {
                    codeSize = (uint)source.Source.Length / sizeof(uint),
                    code = (uint*)ptr,
                    chain = new WGPUChainedStruct()
                    {
                        next = null,
                        sType = WGPUSType.ShaderSourceSPIRV,
                    },
                };

                shaderDesc.nextInChain = &descriptor.chain;

                return wgpuDeviceCreateShaderModule(device, &shaderDesc);
            }
        }
        else if (source.Language == ShaderLanguage.WGSL)
        {
            ReadOnlySpan<byte> code = source.Source.Span;
            fixed (byte* ptr = code)
            {
                WGPUShaderSourceWGSL descriptor = new WGPUShaderSourceWGSL()
                {
                    code = new WGPUStringView(ptr, code.Length),
                    chain = new WGPUChainedStruct()
                    {
                        next = null,
                        sType = WGPUSType.ShaderSourceWGSL,
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
                sampleType = TextureSampleTypeToWebGPU(binding.TextureInfo.SampleType),
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