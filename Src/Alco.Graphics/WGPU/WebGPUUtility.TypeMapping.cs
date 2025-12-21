using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal static partial class WebGPUUtility
{
    // Graphics backend mapping
    private static readonly Tuple<GraphicsBackend, WGPUInstanceBackend>[] BackendCast = new Tuple<GraphicsBackend, WGPUInstanceBackend>[]
    {
        new(GraphicsBackend.None, WGPUInstanceBackend.All),
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
    //public static readonly Func<WGPUInstanceBackend, GraphicsBackend> BackendToAbstract;

    private static readonly Tuple<GraphicsBackend, WGPUBackendType>[] BackendType = new Tuple<GraphicsBackend, WGPUBackendType>[]
    {
        new(GraphicsBackend.None, WGPUBackendType.Null),
        new(GraphicsBackend.Auto, WGPUBackendType.Undefined),
        new(GraphicsBackend.WebGPU, WGPUBackendType.WebGPU),
        new(GraphicsBackend.Vulkan, WGPUBackendType.Vulkan),
        new(GraphicsBackend.D3D11, WGPUBackendType.D3D11),
        new(GraphicsBackend.D3D12, WGPUBackendType.D3D12),
        new(GraphicsBackend.Metal, WGPUBackendType.Metal),
        new(GraphicsBackend.OpenGL, WGPUBackendType.OpenGL),
        new(GraphicsBackend.OpenGLES, WGPUBackendType.OpenGLES),
    };

    public static readonly Func<GraphicsBackend, WGPUBackendType> BackendTypeToWebGPU;

    // Primitive topology mapping
    private static readonly Tuple<PrimitiveTopology, WGPUPrimitiveTopology>[] PrimitiveTopologyCast = new Tuple<PrimitiveTopology, WGPUPrimitiveTopology>[]
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
    private static readonly Tuple<CullMode, WGPUCullMode>[] CullModeCast = new Tuple<CullMode, WGPUCullMode>[]
    {
        new(CullMode.None, WGPUCullMode.None),
        new(CullMode.Front, WGPUCullMode.Front),
        new(CullMode.Back, WGPUCullMode.Back),
    };

    public static readonly Func<CullMode, WGPUCullMode> CullModeToWebGPU;
    public static readonly Func<WGPUCullMode, CullMode> CullModeToAbstract;

    //Front face mapping
    private static readonly Tuple<FrontFace, WGPUFrontFace>[] FrontFaceCast = new Tuple<FrontFace, WGPUFrontFace>[]
    {
        new(FrontFace.Clockwise, WGPUFrontFace.CW),
        new(FrontFace.CounterClockwise, WGPUFrontFace.CCW),
    };

    public static readonly Func<FrontFace, WGPUFrontFace> FrontFaceToWebGPU;
    public static readonly Func<WGPUFrontFace, FrontFace> FrontFaceToAbstract;

    // Index format mapping

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WGPUIndexFormat IndexFormatToWebGPU(IndexFormat format)
    {
        return format switch
        {
            IndexFormat.UInt16 => WGPUIndexFormat.Uint16,
            IndexFormat.UInt32 => WGPUIndexFormat.Uint32,
            _ => WGPUIndexFormat.Undefined,
        };
    }

    // Pixel format mapping

    private static readonly Tuple<PixelFormat, WGPUTextureFormat>[] PixelFormatCast = new Tuple<PixelFormat, WGPUTextureFormat>[]
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

    // Blend factor mapping

    private static readonly Tuple<BlendFactor, WGPUBlendFactor>[] BlendFactorCast = new Tuple<BlendFactor, WGPUBlendFactor>[]
    {
        new(BlendFactor.Zero, WGPUBlendFactor.Zero),
        new(BlendFactor.One, WGPUBlendFactor.One),
        new(BlendFactor.Src, WGPUBlendFactor.Src),
        new(BlendFactor.OneMinusSrc, WGPUBlendFactor.OneMinusSrc),
        new(BlendFactor.SrcAlpha, WGPUBlendFactor.SrcAlpha),
        new(BlendFactor.OneMinusSrcAlpha, WGPUBlendFactor.OneMinusSrcAlpha),
        new(BlendFactor.Dst, WGPUBlendFactor.Dst),
        new(BlendFactor.OneMinusDst, WGPUBlendFactor.OneMinusDst),
        new(BlendFactor.DstAlpha, WGPUBlendFactor.DstAlpha),
        new(BlendFactor.OneMinusDstAlpha, WGPUBlendFactor.OneMinusDstAlpha),
        new(BlendFactor.SrcAlphaSaturated, WGPUBlendFactor.SrcAlphaSaturated),
        new(BlendFactor.Constant, WGPUBlendFactor.Constant),
        new(BlendFactor.OneMinusConstant, WGPUBlendFactor.OneMinusConstant),
    };

    public static readonly Func<BlendFactor, WGPUBlendFactor> BlendFactorToWebGPU;
    public static readonly Func<WGPUBlendFactor, BlendFactor> BlendFactorToAbstract;

    // Blend Operation mapping

    private static readonly Tuple<BlendOperation, WGPUBlendOperation>[] BlendOperationCast = new Tuple<BlendOperation, WGPUBlendOperation>[]
    {
        new(BlendOperation.Add, WGPUBlendOperation.Add),
        new(BlendOperation.Subtract, WGPUBlendOperation.Subtract),
        new(BlendOperation.ReverseSubtract, WGPUBlendOperation.ReverseSubtract),
        new(BlendOperation.Min, WGPUBlendOperation.Min),
        new(BlendOperation.Max, WGPUBlendOperation.Max),
    };

    public static readonly Func<BlendOperation, WGPUBlendOperation> BlendOperationToWebGPU;
    public static readonly Func<WGPUBlendOperation, BlendOperation> BlendOperationToAbstract;

    // Vertex step mode mapping

    private static readonly Tuple<VertexStepMode, WGPUVertexStepMode>[] VertexStepModeCast = new Tuple<VertexStepMode, WGPUVertexStepMode>[]
    {
        new(VertexStepMode.Vertex, WGPUVertexStepMode.Vertex),
        new(VertexStepMode.Instance, WGPUVertexStepMode.Instance),
    };

    public static readonly Func<VertexStepMode, WGPUVertexStepMode> VertexStepModeToWebGPU;
    public static readonly Func<WGPUVertexStepMode, VertexStepMode> VertexStepModeToAbstract;

    // Vertex format mapping

    private static readonly Tuple<VertexFormat, WGPUVertexFormat>[] VertexFormatCast = new Tuple<VertexFormat, WGPUVertexFormat>[]
    {
        new(VertexFormat.Undefined, WGPUVertexFormat.None),
        new(VertexFormat.Uint8x2, WGPUVertexFormat.Uint8x2),
        new(VertexFormat.Uint8x4, WGPUVertexFormat.Uint8x4),
        new(VertexFormat.Sint8x2, WGPUVertexFormat.Sint8x2),
        new(VertexFormat.Sint8x4, WGPUVertexFormat.Sint8x4),
        new(VertexFormat.Unorm8x2, WGPUVertexFormat.Unorm8x2),
        new(VertexFormat.Unorm8x4, WGPUVertexFormat.Unorm8x4),
        new(VertexFormat.Snorm8x2, WGPUVertexFormat.Snorm8x2),
        new(VertexFormat.Snorm8x4, WGPUVertexFormat.Snorm8x4),
        new(VertexFormat.Uint16x2, WGPUVertexFormat.Uint16x2),
        new(VertexFormat.Uint16x4, WGPUVertexFormat.Uint16x4),
        new(VertexFormat.Sint16x2, WGPUVertexFormat.Sint16x2),
        new(VertexFormat.Sint16x4, WGPUVertexFormat.Sint16x4),
        new(VertexFormat.Unorm16x2, WGPUVertexFormat.Unorm16x2),
        new(VertexFormat.Unorm16x4, WGPUVertexFormat.Unorm16x4),
        new(VertexFormat.Snorm16x2, WGPUVertexFormat.Snorm16x2),
        new(VertexFormat.Snorm16x4, WGPUVertexFormat.Snorm16x4),
        new(VertexFormat.Float16x2, WGPUVertexFormat.Float16x2),
        new(VertexFormat.Float16x4, WGPUVertexFormat.Float16x4),
        new(VertexFormat.Float32, WGPUVertexFormat.Float32),
        new(VertexFormat.Float32x2, WGPUVertexFormat.Float32x2),
        new(VertexFormat.Float32x3, WGPUVertexFormat.Float32x3),
        new(VertexFormat.Float32x4, WGPUVertexFormat.Float32x4),
        new(VertexFormat.Uint32, WGPUVertexFormat.Uint32),
        new(VertexFormat.Uint32x2, WGPUVertexFormat.Uint32x2),
        new(VertexFormat.Uint32x3, WGPUVertexFormat.Uint32x3),
        new(VertexFormat.Uint32x4, WGPUVertexFormat.Uint32x4),
        new(VertexFormat.Sint32, WGPUVertexFormat.Sint32),
        new(VertexFormat.Sint32x2, WGPUVertexFormat.Sint32x2),
        new(VertexFormat.Sint32x3, WGPUVertexFormat.Sint32x3),
        new(VertexFormat.Sint32x4, WGPUVertexFormat.Sint32x4),
    };

    public static readonly Func<VertexFormat, WGPUVertexFormat> VertexFormatToWebGPU;
    public static readonly Func<WGPUVertexFormat, VertexFormat> VertexFormatToAbstract;

    // compare function mapping
    private static readonly Tuple<CompareFunction, WGPUCompareFunction>[] CompareFunctionCast = new Tuple<CompareFunction, WGPUCompareFunction>[]
    {
        new(CompareFunction.Undefined, WGPUCompareFunction.Undefined),
        new(CompareFunction.Never, WGPUCompareFunction.Never),
        new(CompareFunction.Less, WGPUCompareFunction.Less),
        new(CompareFunction.Equal, WGPUCompareFunction.Equal),
        new(CompareFunction.LessEqual, WGPUCompareFunction.LessEqual),
        new(CompareFunction.Greater, WGPUCompareFunction.Greater),
        new(CompareFunction.NotEqual, WGPUCompareFunction.NotEqual),
        new(CompareFunction.GreaterEqual, WGPUCompareFunction.GreaterEqual),
        new(CompareFunction.Always, WGPUCompareFunction.Always),
    };

    public static readonly Func<CompareFunction, WGPUCompareFunction> CompareFunctionToWebGPU;
    public static readonly Func<WGPUCompareFunction, CompareFunction> CompareFunctionToAbstract;

    private static readonly Tuple<TextureDimension, WGPUTextureDimension>[] TextureDimensionCast = new Tuple<TextureDimension, WGPUTextureDimension>[]
    {
        new(TextureDimension.Texture1D, WGPUTextureDimension._1D),
        new(TextureDimension.Texture2D, WGPUTextureDimension._2D),
        new(TextureDimension.Texture3D, WGPUTextureDimension._3D),
    };

    public static readonly Func<TextureDimension, WGPUTextureDimension> TextureDimensionToWebGPU;
    public static readonly Func<WGPUTextureDimension, TextureDimension> TextureDimensionToAbstract;

    private static readonly Tuple<TextureViewDimension, WGPUTextureViewDimension>[] TextureViewDimensionCast = new Tuple<TextureViewDimension, WGPUTextureViewDimension>[]
    {
        new(TextureViewDimension.Texture1D, WGPUTextureViewDimension._1D),
        new(TextureViewDimension.Texture2D, WGPUTextureViewDimension._2D),
        new(TextureViewDimension.Texture2DArray, WGPUTextureViewDimension._2DArray),
        new(TextureViewDimension.Cube, WGPUTextureViewDimension.Cube),
        new(TextureViewDimension.CubeArray, WGPUTextureViewDimension.CubeArray),
        new(TextureViewDimension.Texture3D, WGPUTextureViewDimension._3D),
    };

    public static readonly Func<TextureViewDimension, WGPUTextureViewDimension> TextureViewDimensionToWebGPU;
    public static readonly Func<WGPUTextureViewDimension, TextureViewDimension> TextureViewDimensionToAbstract;

    private static readonly Tuple<StencilOperation, WGPUStencilOperation>[] StencilOperationCast = new Tuple<StencilOperation, WGPUStencilOperation>[]
    {
        new(StencilOperation.Keep, WGPUStencilOperation.Keep),
        new(StencilOperation.Zero, WGPUStencilOperation.Zero),
        new(StencilOperation.Replace, WGPUStencilOperation.Replace),
        new(StencilOperation.Invert, WGPUStencilOperation.Invert),
        new(StencilOperation.IncrementClamp, WGPUStencilOperation.IncrementClamp),
        new(StencilOperation.DecrementClamp, WGPUStencilOperation.DecrementClamp),
        new(StencilOperation.IncrementWrap, WGPUStencilOperation.IncrementWrap),
        new(StencilOperation.DecrementWrap, WGPUStencilOperation.DecrementWrap),
    };

    public static readonly Func<StencilOperation, WGPUStencilOperation> StencilOperationToWebGPU;
    public static readonly Func<WGPUStencilOperation, StencilOperation> StencilOperationToAbstract;

    private static readonly Tuple<AddressMode, WGPUAddressMode>[] AddressModeCast = new Tuple<AddressMode, WGPUAddressMode>[]
    {
        new(AddressMode.ClampToEdge, WGPUAddressMode.ClampToEdge),
        new(AddressMode.Repeat, WGPUAddressMode.Repeat),
        new(AddressMode.MirrorRepeat, WGPUAddressMode.MirrorRepeat),
    };

    public static readonly Func<AddressMode, WGPUAddressMode> AddressModeToWebGPU;
    public static readonly Func<WGPUAddressMode, AddressMode> AddressModeToAbstract;

    private static readonly Tuple<FilterMode, WGPUFilterMode>[] FilterModeCast = new Tuple<FilterMode, WGPUFilterMode>[]
    {
        new(FilterMode.None, WGPUFilterMode.None),
        new(FilterMode.Nearest, WGPUFilterMode.Nearest),
        new(FilterMode.Linear, WGPUFilterMode.Linear),
    };

    public static readonly Func<FilterMode, WGPUFilterMode> FilterModeToWebGPU;
    public static readonly Func<WGPUFilterMode, FilterMode> FilterModeToAbstract;

    private static readonly Tuple<FilterMode, WGPUMipmapFilterMode>[] MipmapFilterModeCast = new Tuple<FilterMode, WGPUMipmapFilterMode>[]
    {
        new(FilterMode.None, WGPUMipmapFilterMode.None),
        new(FilterMode.Nearest, WGPUMipmapFilterMode.Nearest),
        new(FilterMode.Linear, WGPUMipmapFilterMode.Linear),
    };

    public static readonly Func<FilterMode, WGPUMipmapFilterMode> MipmapFilterModeToWebGPU;
    public static readonly Func<WGPUMipmapFilterMode, FilterMode> MipmapFilterModeToAbstract;

    private static readonly Tuple<TextureAspect, WGPUTextureAspect>[] TextureAspectCast = new Tuple<TextureAspect, WGPUTextureAspect>[]
    {
        new(TextureAspect.None, WGPUTextureAspect.None),
        new(TextureAspect.All, WGPUTextureAspect.All),
        new(TextureAspect.StencilOnly, WGPUTextureAspect.StencilOnly),
        new(TextureAspect.DepthOnly, WGPUTextureAspect.DepthOnly),
    };

    public static readonly Func<TextureAspect, WGPUTextureAspect> TextureAspectToWebGPU;
    public static readonly Func<WGPUTextureAspect, TextureAspect> TextureAspectToAbstract;


    private static readonly Tuple<TextureSampleType, WGPUTextureSampleType>[] TextureSampleTypeCast = new Tuple<TextureSampleType, WGPUTextureSampleType>[]
    {
        new(TextureSampleType.None, WGPUTextureSampleType.None),
        new(TextureSampleType.Float, WGPUTextureSampleType.Float),
        new(TextureSampleType.UnfilterableFloat, WGPUTextureSampleType.UnfilterableFloat),
        new(TextureSampleType.Depth, WGPUTextureSampleType.Depth),
        new(TextureSampleType.Sint, WGPUTextureSampleType.Sint),
        new(TextureSampleType.Uint, WGPUTextureSampleType.Uint),
    };

    public static readonly Func<TextureSampleType, WGPUTextureSampleType> TextureSampleTypeToWebGPU;
    public static readonly Func<WGPUTextureSampleType, TextureSampleType> TextureSampleTypeToAbstract;

    static WebGPUUtility()
    {
        BackendToWebGPU = CastUtility.GenerateCastFunc(BackendCast);
        BackendTypeToWebGPU = CastUtility.GenerateCastFunc(BackendType);
        CastUtility.GenerateCastFunc(PrimitiveTopologyCast, out PrimitiveTopologyToWebGPU, out PrimitiveTopologyToAbstract);
        CastUtility.GenerateCastFunc(CullModeCast, out CullModeToWebGPU, out CullModeToAbstract);
        CastUtility.GenerateCastFunc(FrontFaceCast, out FrontFaceToWebGPU, out FrontFaceToAbstract);
        CastUtility.GenerateCastFunc(PixelFormatCast, out PixelFormatToWebGPU, out PixelFormatToAbstract);
        CastUtility.GenerateCastFunc(BlendFactorCast, out BlendFactorToWebGPU, out BlendFactorToAbstract);
        CastUtility.GenerateCastFunc(BlendOperationCast, out BlendOperationToWebGPU, out BlendOperationToAbstract);
        CastUtility.GenerateCastFunc(VertexStepModeCast, out VertexStepModeToWebGPU, out VertexStepModeToAbstract);
        CastUtility.GenerateCastFunc(VertexFormatCast, out VertexFormatToWebGPU, out VertexFormatToAbstract);
        CastUtility.GenerateCastFunc(CompareFunctionCast, out CompareFunctionToWebGPU, out CompareFunctionToAbstract);
        CastUtility.GenerateCastFunc(TextureDimensionCast, out TextureDimensionToWebGPU, out TextureDimensionToAbstract);
        CastUtility.GenerateCastFunc(TextureViewDimensionCast, out TextureViewDimensionToWebGPU, out TextureViewDimensionToAbstract);
        CastUtility.GenerateCastFunc(StencilOperationCast, out StencilOperationToWebGPU, out StencilOperationToAbstract);
        CastUtility.GenerateCastFunc(AddressModeCast, out AddressModeToWebGPU, out AddressModeToAbstract);
        CastUtility.GenerateCastFunc(FilterModeCast, out FilterModeToWebGPU, out FilterModeToAbstract);
        CastUtility.GenerateCastFunc(MipmapFilterModeCast, out MipmapFilterModeToWebGPU, out MipmapFilterModeToAbstract);
        CastUtility.GenerateCastFunc(TextureAspectCast, out TextureAspectToWebGPU, out TextureAspectToAbstract);
        CastUtility.GenerateCastFunc(TextureSampleTypeCast, out TextureSampleTypeToWebGPU, out TextureSampleTypeToAbstract);
    }
}