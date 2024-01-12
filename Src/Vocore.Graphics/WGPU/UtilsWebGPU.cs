using System.Text;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics;

public static class UtilsWebGPU
{
    // Graphics backend mapping
    public static readonly Tuple<GraphicsBackend, WGPUInstanceBackend>[] BackendCast = new Tuple<GraphicsBackend, WGPUInstanceBackend>[]
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

    public static readonly Func<GraphicsBackend, WGPUInstanceBackend> BackendToWebGPU;
    public static readonly Func<WGPUInstanceBackend, GraphicsBackend> BackendToAbstract;

    // Primitive topology mapping
    public static readonly Tuple<PrimitiveTopology, WGPUPrimitiveTopology>[] PrimitiveTopologyCast = new Tuple<PrimitiveTopology, WGPUPrimitiveTopology>[]
    {
        new(PrimitiveTopology.PointList, WGPUPrimitiveTopology.PointList),
        new(PrimitiveTopology.LineList, WGPUPrimitiveTopology.LineList),
        new(PrimitiveTopology.LineStrip, WGPUPrimitiveTopology.LineStrip),
        new(PrimitiveTopology.TriangleList, WGPUPrimitiveTopology.TriangleList),
        new(PrimitiveTopology.TriangleStrip, WGPUPrimitiveTopology.TriangleStrip),
    };

    public static readonly Func<PrimitiveTopology, WGPUPrimitiveTopology> PrimitiveTopologyToWebGPU;
    public static readonly Func<WGPUPrimitiveTopology, PrimitiveTopology> PrimitiveTopologyToAbstract;

    // Cull mode mapping
    public static readonly Tuple<CullMode, WGPUCullMode>[] CullModeCast = new Tuple<CullMode, WGPUCullMode>[]
    {
        new(CullMode.None, WGPUCullMode.None),
        new(CullMode.Front, WGPUCullMode.Front),
        new(CullMode.Back, WGPUCullMode.Back),
    };

    public static readonly Func<CullMode, WGPUCullMode> CullModeToWebGPU;
    public static readonly Func<WGPUCullMode, CullMode> CullModeToAbstract;

    //Front face mapping
    public static readonly Tuple<FrontFace, WGPUFrontFace>[] FrontFaceCast = new Tuple<FrontFace, WGPUFrontFace>[]
    {
        new(FrontFace.Clockwise, WGPUFrontFace.CW),
        new(FrontFace.CounterClockwise, WGPUFrontFace.CCW),
    };

    public static readonly Func<FrontFace, WGPUFrontFace> FrontFaceToWebGPU;
    public static readonly Func<WGPUFrontFace, FrontFace> FrontFaceToAbstract;

    // Index format mapping
    public static readonly Tuple<IndexFormat, WGPUIndexFormat>[] IndexFormatCast = new Tuple<IndexFormat, WGPUIndexFormat>[]
    {
        new(IndexFormat.Uint16, WGPUIndexFormat.Uint16),
        new(IndexFormat.Uint32, WGPUIndexFormat.Uint32),
    };
    
    public static readonly Func<IndexFormat, WGPUIndexFormat> IndexFormatToWebGPU;
    public static readonly Func<WGPUIndexFormat, IndexFormat> IndexFormatToAbstract;

    // Pixel format mapping

    public static readonly Tuple<PixelFormat, WGPUTextureFormat>[] PixelFormatCast = new Tuple<PixelFormat, WGPUTextureFormat>[]
    {
        new(PixelFormat.Undefined, WGPUTextureFormat.Undefined),
        new(PixelFormat.R8Unorm, WGPUTextureFormat.R8Unorm),
        new(PixelFormat.R8Snorm, WGPUTextureFormat.R8Snorm),
        new(PixelFormat.R8Uint, WGPUTextureFormat.R8Uint),
        new(PixelFormat.R8Sint, WGPUTextureFormat.R8Sint),
        new(PixelFormat.R16Uint, WGPUTextureFormat.R16Uint),
        new(PixelFormat.R16Sint, WGPUTextureFormat.R16Sint),
        new(PixelFormat.R16Float, WGPUTextureFormat.R16Float),
        new(PixelFormat.RG8Unorm, WGPUTextureFormat.RG8Unorm),
        new(PixelFormat.RG8Snorm, WGPUTextureFormat.RG8Snorm),
        new(PixelFormat.RG8Uint, WGPUTextureFormat.RG8Uint),
        new(PixelFormat.RG8Sint, WGPUTextureFormat.RG8Sint),
        new(PixelFormat.R32Float, WGPUTextureFormat.R32Float),
        new(PixelFormat.R32Uint, WGPUTextureFormat.R32Uint),
        new(PixelFormat.R32Sint, WGPUTextureFormat.R32Sint),
        new(PixelFormat.RG16Uint, WGPUTextureFormat.RG16Uint),
        new(PixelFormat.RG16Sint, WGPUTextureFormat.RG16Sint),
        new(PixelFormat.RG16Float, WGPUTextureFormat.RG16Float),
        new(PixelFormat.RGBA8Unorm, WGPUTextureFormat.RGBA8Unorm),
        new(PixelFormat.RGBA8UnormSrgb, WGPUTextureFormat.RGBA8UnormSrgb),
        new(PixelFormat.RGBA8Snorm, WGPUTextureFormat.RGBA8Snorm),
        new(PixelFormat.RGBA8Uint, WGPUTextureFormat.RGBA8Uint),
        new(PixelFormat.RGBA8Sint, WGPUTextureFormat.RGBA8Sint),
        new(PixelFormat.BGRA8Unorm, WGPUTextureFormat.BGRA8Unorm),
        new(PixelFormat.BGRA8UnormSrgb, WGPUTextureFormat.BGRA8UnormSrgb),
        new(PixelFormat.RGB10A2Uint, WGPUTextureFormat.RGB10A2Uint),
        new(PixelFormat.RGB10A2Unorm, WGPUTextureFormat.RGB10A2Unorm),
        new(PixelFormat.RG11B10Ufloat, WGPUTextureFormat.RG11B10Ufloat),
        new(PixelFormat.RGB9E5Ufloat, WGPUTextureFormat.RGB9E5Ufloat),
        new(PixelFormat.RG32Float, WGPUTextureFormat.RG32Float),
        new(PixelFormat.RG32Uint, WGPUTextureFormat.RG32Uint),
        new(PixelFormat.RG32Sint, WGPUTextureFormat.RG32Sint),
        new(PixelFormat.RGBA16Uint, WGPUTextureFormat.RGBA16Uint),
        new(PixelFormat.RGBA16Sint, WGPUTextureFormat.RGBA16Sint),
        new(PixelFormat.RGBA16Float, WGPUTextureFormat.RGBA16Float),
        new(PixelFormat.RGBA32Float, WGPUTextureFormat.RGBA32Float),
        new(PixelFormat.RGBA32Uint, WGPUTextureFormat.RGBA32Uint),
        new(PixelFormat.RGBA32Sint, WGPUTextureFormat.RGBA32Sint),
        new(PixelFormat.Stencil8, WGPUTextureFormat.Stencil8),
        new(PixelFormat.Depth16Unorm, WGPUTextureFormat.Depth16Unorm),
        new(PixelFormat.Depth24Plus, WGPUTextureFormat.Depth24Plus),
        new(PixelFormat.Depth24PlusStencil8, WGPUTextureFormat.Depth24PlusStencil8),
        new(PixelFormat.Depth32Float, WGPUTextureFormat.Depth32Float),
        new(PixelFormat.Depth32FloatStencil8, WGPUTextureFormat.Depth32FloatStencil8),
        new(PixelFormat.BC1RGBAUnorm, WGPUTextureFormat.BC1RGBAUnorm),
        new(PixelFormat.BC1RGBAUnormSrgb, WGPUTextureFormat.BC1RGBAUnormSrgb),
        new(PixelFormat.BC2RGBAUnorm, WGPUTextureFormat.BC2RGBAUnorm),
        new(PixelFormat.BC2RGBAUnormSrgb, WGPUTextureFormat.BC2RGBAUnormSrgb),
        new(PixelFormat.BC3RGBAUnorm, WGPUTextureFormat.BC3RGBAUnorm),
        new(PixelFormat.BC3RGBAUnormSrgb, WGPUTextureFormat.BC3RGBAUnormSrgb),
        new(PixelFormat.BC4RUnorm, WGPUTextureFormat.BC4RUnorm),
        new(PixelFormat.BC4RSnorm, WGPUTextureFormat.BC4RSnorm),
        new(PixelFormat.BC5RGUnorm, WGPUTextureFormat.BC5RGUnorm),
        new(PixelFormat.BC5RGSnorm, WGPUTextureFormat.BC5RGSnorm),
        new(PixelFormat.BC6HRGBUfloat, WGPUTextureFormat.BC6HRGBUfloat),
        new(PixelFormat.BC6HRGBFloat, WGPUTextureFormat.BC6HRGBFloat),
        new(PixelFormat.BC7RGBAUnorm, WGPUTextureFormat.BC7RGBAUnorm),
        new(PixelFormat.BC7RGBAUnormSrgb, WGPUTextureFormat.BC7RGBAUnormSrgb),
        new(PixelFormat.ETC2RGB8Unorm, WGPUTextureFormat.ETC2RGB8Unorm),
        new(PixelFormat.ETC2RGB8UnormSrgb, WGPUTextureFormat.ETC2RGB8UnormSrgb),
        new(PixelFormat.ETC2RGB8A1Unorm, WGPUTextureFormat.ETC2RGB8A1Unorm),
        new(PixelFormat.ETC2RGB8A1UnormSrgb, WGPUTextureFormat.ETC2RGB8A1UnormSrgb),
        new(PixelFormat.ETC2RGBA8Unorm, WGPUTextureFormat.ETC2RGBA8Unorm),
        new(PixelFormat.ETC2RGBA8UnormSrgb, WGPUTextureFormat.ETC2RGBA8UnormSrgb),
        new(PixelFormat.EACR11Unorm, WGPUTextureFormat.EACR11Unorm),
        new(PixelFormat.EACR11Snorm, WGPUTextureFormat.EACR11Snorm),
        new(PixelFormat.EACRG11Unorm, WGPUTextureFormat.EACRG11Unorm),
        new(PixelFormat.EACRG11Snorm, WGPUTextureFormat.EACRG11Snorm),
        new(PixelFormat.ASTC4x4Unorm, WGPUTextureFormat.ASTC4x4Unorm),
        new(PixelFormat.ASTC4x4UnormSrgb, WGPUTextureFormat.ASTC4x4UnormSrgb),
        new(PixelFormat.ASTC5x4Unorm, WGPUTextureFormat.ASTC5x4Unorm),
        new(PixelFormat.ASTC5x4UnormSrgb, WGPUTextureFormat.ASTC5x4UnormSrgb),
        new(PixelFormat.ASTC5x5Unorm, WGPUTextureFormat.ASTC5x5Unorm),
        new(PixelFormat.ASTC5x5UnormSrgb, WGPUTextureFormat.ASTC5x5UnormSrgb),
        new(PixelFormat.ASTC6x5Unorm, WGPUTextureFormat.ASTC6x5Unorm),
        new(PixelFormat.ASTC6x5UnormSrgb, WGPUTextureFormat.ASTC6x5UnormSrgb),
        new(PixelFormat.ASTC6x6Unorm, WGPUTextureFormat.ASTC6x6Unorm),
        new(PixelFormat.ASTC6x6UnormSrgb, WGPUTextureFormat.ASTC6x6UnormSrgb),
        new(PixelFormat.ASTC8x5Unorm, WGPUTextureFormat.ASTC8x5Unorm),
        new(PixelFormat.ASTC8x5UnormSrgb, WGPUTextureFormat.ASTC8x5UnormSrgb),
        new(PixelFormat.ASTC8x6Unorm, WGPUTextureFormat.ASTC8x6Unorm),
        new(PixelFormat.ASTC8x6UnormSrgb, WGPUTextureFormat.ASTC8x6UnormSrgb),
        new(PixelFormat.ASTC8x8Unorm, WGPUTextureFormat.ASTC8x8Unorm),
        new(PixelFormat.ASTC8x8UnormSrgb, WGPUTextureFormat.ASTC8x8UnormSrgb),
        new(PixelFormat.ASTC10x5Unorm, WGPUTextureFormat.ASTC10x5Unorm),
        new(PixelFormat.ASTC10x5UnormSrgb, WGPUTextureFormat.ASTC10x5UnormSrgb),
        new(PixelFormat.ASTC10x6Unorm, WGPUTextureFormat.ASTC10x6Unorm),
        new(PixelFormat.ASTC10x6UnormSrgb, WGPUTextureFormat.ASTC10x6UnormSrgb),
        new(PixelFormat.ASTC10x8Unorm, WGPUTextureFormat.ASTC10x8Unorm),
        new(PixelFormat.ASTC10x8UnormSrgb, WGPUTextureFormat.ASTC10x8UnormSrgb),
        new(PixelFormat.ASTC10x10Unorm, WGPUTextureFormat.ASTC10x10Unorm),
        new(PixelFormat.ASTC10x10UnormSrgb, WGPUTextureFormat.ASTC10x10UnormSrgb),
        new(PixelFormat.ASTC12x10Unorm, WGPUTextureFormat.ASTC12x10Unorm),
        new(PixelFormat.ASTC12x10UnormSrgb, WGPUTextureFormat.ASTC12x10UnormSrgb),
        new(PixelFormat.ASTC12x12Unorm, WGPUTextureFormat.ASTC12x12Unorm),
        new(PixelFormat.ASTC12x12UnormSrgb, WGPUTextureFormat.ASTC12x12UnormSrgb),
    };

    public static readonly Func<PixelFormat, WGPUTextureFormat> PixelFormatToWebGPU;
    public static readonly Func<WGPUTextureFormat, PixelFormat> PixelFormatToAbstract;

    static UtilsWebGPU()
    {
        UtilsCast.GenerateCastFunc(BackendCast, out BackendToWebGPU, out BackendToAbstract);
        UtilsCast.GenerateCastFunc(PrimitiveTopologyCast, out PrimitiveTopologyToWebGPU, out PrimitiveTopologyToAbstract);
        UtilsCast.GenerateCastFunc(CullModeCast, out CullModeToWebGPU, out CullModeToAbstract);
        UtilsCast.GenerateCastFunc(FrontFaceCast, out FrontFaceToWebGPU, out FrontFaceToAbstract);
        UtilsCast.GenerateCastFunc(IndexFormatCast, out IndexFormatToWebGPU, out IndexFormatToAbstract);
        UtilsCast.GenerateCastFunc(PixelFormatCast, out PixelFormatToWebGPU, out PixelFormatToAbstract);
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