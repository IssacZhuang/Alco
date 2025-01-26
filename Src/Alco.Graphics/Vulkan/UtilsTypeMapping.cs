using System;
using System.Runtime.CompilerServices;
using Vortice.Vulkan;

using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

internal static partial class UtilsVulkan
{
    private static readonly ValueTuple<PrimitiveTopology, VkPrimitiveTopology>[] PrimitiveTopologyCast = new ValueTuple<PrimitiveTopology, VkPrimitiveTopology>[]
    {
        (PrimitiveTopology.PointList, VkPrimitiveTopology.PointList),
        (PrimitiveTopology.LineList, VkPrimitiveTopology.LineList),
        (PrimitiveTopology.LineStrip, VkPrimitiveTopology.LineStrip),
        (PrimitiveTopology.TriangleList, VkPrimitiveTopology.TriangleList),
        (PrimitiveTopology.TriangleStrip, VkPrimitiveTopology.TriangleStrip),
    };

    public static readonly Func<PrimitiveTopology, VkPrimitiveTopology> PrimitiveTopologyToVulkan;
    public static readonly Func<VkPrimitiveTopology, PrimitiveTopology> PrimitiveTopologyToAbstract;

    private static readonly ValueTuple<CullMode, VkCullModeFlags>[] CullModeCast = new ValueTuple<CullMode, VkCullModeFlags>[]
    {
        (CullMode.None, VkCullModeFlags.None),
        (CullMode.Front, VkCullModeFlags.Front),
        (CullMode.Back, VkCullModeFlags.Back),
    };

    public static readonly Func<CullMode, VkCullModeFlags> CullModeToVulkan;
    public static readonly Func<VkCullModeFlags, CullMode> CullModeToAbstract;

    private static readonly ValueTuple<FrontFace, VkFrontFace>[] FrontFaceCast = new ValueTuple<FrontFace, VkFrontFace>[]
    {
        (FrontFace.Clockwise, VkFrontFace.Clockwise),
        (FrontFace.CounterClockwise, VkFrontFace.CounterClockwise),
    };

    public static readonly Func<FrontFace, VkFrontFace> FrontFaceToVulkan;
    public static readonly Func<VkFrontFace, FrontFace> FrontFaceToAbstract;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VkIndexType IndexFormatToVulkan(IndexFormat format)
    {
        return format switch
        {
            IndexFormat.Uint16 => VkIndexType.Uint16,
            IndexFormat.Uint32 => VkIndexType.Uint32,
            _ => throw new NotSupportedException(),
        };
    }

    private static readonly ValueTuple<PixelFormat, VkFormat>[] PixelFormatCast = new ValueTuple<PixelFormat, VkFormat>[]{
        new(PixelFormat.Undefined, VkFormat.Undefined),
        new(PixelFormat.R8Unorm, VkFormat.R8Unorm),
        new(PixelFormat.R8Snorm, VkFormat.R8Snorm),
        new(PixelFormat.R8Uint, VkFormat.R8Uint),
        new(PixelFormat.R8Sint, VkFormat.R8Sint),
        new(PixelFormat.R16Uint, VkFormat.R16Uint),
        new(PixelFormat.R16Sint, VkFormat.R16Sint),
        new(PixelFormat.R16Float, VkFormat.R16Sfloat),
        new(PixelFormat.RG8Unorm, VkFormat.R8G8Unorm),
        new(PixelFormat.RG8Snorm, VkFormat.R8G8Snorm),
        new(PixelFormat.RG8Uint, VkFormat.R8G8Uint),
        new(PixelFormat.RG8Sint, VkFormat.R8G8Sint),
        new(PixelFormat.R32Float, VkFormat.R32Sfloat),
        new(PixelFormat.R32Uint, VkFormat.R32Uint),
        new(PixelFormat.R32Sint, VkFormat.R32Sint),
        new(PixelFormat.RG16Uint, VkFormat.R16G16Uint),
        new(PixelFormat.RG16Sint, VkFormat.R16G16Sint),
        new(PixelFormat.RG16Float, VkFormat.R16G16Sfloat),
        new(PixelFormat.RGBA8Unorm, VkFormat.R8G8B8A8Unorm),
        new(PixelFormat.RGBA8UnormSrgb, VkFormat.R8G8B8A8Srgb),
        new(PixelFormat.RGBA8Snorm, VkFormat.R8G8B8A8Snorm),
        new(PixelFormat.RGBA8Uint, VkFormat.R8G8B8A8Uint),
        new(PixelFormat.RGBA8Sint, VkFormat.R8G8B8A8Sint),
        new(PixelFormat.BGRA8Unorm, VkFormat.B8G8R8A8Unorm),
        new(PixelFormat.BGRA8UnormSrgb, VkFormat.B8G8R8A8Srgb),
        new(PixelFormat.RGB10A2Uint, VkFormat.A2R10G10B10UintPack32),
        new(PixelFormat.RGB10A2Unorm, VkFormat.A2R10G10B10UnormPack32),
        new(PixelFormat.RG11B10Ufloat, VkFormat.B10G11R11UfloatPack32),
        new(PixelFormat.RGB9E5Ufloat, VkFormat.E5B9G9R9UfloatPack32),
        new(PixelFormat.RG32Float, VkFormat.R32G32Sfloat),
        new(PixelFormat.RG32Uint, VkFormat.R32G32Uint),
        new(PixelFormat.RG32Sint, VkFormat.R32G32Sint),
        new(PixelFormat.RGBA16Uint, VkFormat.R16G16B16A16Uint),
        new(PixelFormat.RGBA16Sint, VkFormat.R16G16B16A16Sint),
        new(PixelFormat.RGBA16Float, VkFormat.R16G16B16A16Sfloat),
        new(PixelFormat.RGBA32Float, VkFormat.R32G32B32A32Sfloat),
        new(PixelFormat.RGBA32Uint, VkFormat.R32G32B32A32Uint),
        new(PixelFormat.RGBA32Sint, VkFormat.R32G32B32A32Sint),
        new(PixelFormat.Stencil8, VkFormat.S8Uint),
        new(PixelFormat.Depth16Unorm, VkFormat.D16Unorm),
        new(PixelFormat.Depth24Plus, VkFormat.X8D24UnormPack32),
        new(PixelFormat.Depth24PlusStencil8, VkFormat.D24UnormS8Uint),
        new(PixelFormat.Depth32Float, VkFormat.D32Sfloat),
        new(PixelFormat.Depth32FloatStencil8, VkFormat.D32SfloatS8Uint),
        new(PixelFormat.BC1RGBAUnorm, VkFormat.Bc1RgbaUnormBlock),
        new(PixelFormat.BC1RGBAUnormSrgb, VkFormat.Bc1RgbaSrgbBlock),
        new(PixelFormat.BC2RGBAUnorm, VkFormat.Bc2UnormBlock),
        new(PixelFormat.BC2RGBAUnormSrgb, VkFormat.Bc2SrgbBlock),
        new(PixelFormat.BC3RGBAUnorm, VkFormat.Bc3UnormBlock),
        new(PixelFormat.BC3RGBAUnormSrgb, VkFormat.Bc3SrgbBlock),
        new(PixelFormat.BC4RUnorm, VkFormat.Bc4UnormBlock),
        new(PixelFormat.BC4RSnorm, VkFormat.Bc4SnormBlock),
        new(PixelFormat.BC5RGUnorm, VkFormat.Bc5UnormBlock),
        new(PixelFormat.BC5RGSnorm, VkFormat.Bc5SnormBlock),
        new(PixelFormat.BC6HRGBUfloat, VkFormat.Bc6hUfloatBlock),
        new(PixelFormat.BC6HRGBFloat, VkFormat.Bc6hSfloatBlock),
        new(PixelFormat.BC7RGBAUnorm, VkFormat.Bc7UnormBlock),
        new(PixelFormat.BC7RGBAUnormSrgb, VkFormat.Bc7SrgbBlock),
        new(PixelFormat.ETC2RGB8Unorm, VkFormat.Etc2R8G8B8UnormBlock),
        new(PixelFormat.ETC2RGB8UnormSrgb, VkFormat.Etc2R8G8B8SrgbBlock),
        new(PixelFormat.ETC2RGB8A1Unorm, VkFormat.Etc2R8G8B8A1UnormBlock),
        new(PixelFormat.ETC2RGB8A1UnormSrgb, VkFormat.Etc2R8G8B8A1SrgbBlock),
        new(PixelFormat.ETC2RGBA8Unorm, VkFormat.Etc2R8G8B8A8UnormBlock),
        new(PixelFormat.ETC2RGBA8UnormSrgb, VkFormat.Etc2R8G8B8A8SrgbBlock),
        new(PixelFormat.EACR11Unorm, VkFormat.EacR11UnormBlock),
        new(PixelFormat.EACR11Snorm, VkFormat.EacR11SnormBlock),
        new(PixelFormat.EACRG11Unorm, VkFormat.EacR11G11UnormBlock),
        new(PixelFormat.EACRG11Snorm, VkFormat.EacR11G11SnormBlock),
        new(PixelFormat.ASTC4x4Unorm, VkFormat.Astc4x4UnormBlock),
        new(PixelFormat.ASTC4x4UnormSrgb, VkFormat.Astc4x4SrgbBlock),
        new(PixelFormat.ASTC5x4Unorm, VkFormat.Astc5x4UnormBlock),
        new(PixelFormat.ASTC5x4UnormSrgb, VkFormat.Astc5x4SrgbBlock),
        new(PixelFormat.ASTC5x5Unorm, VkFormat.Astc5x5UnormBlock),
        new(PixelFormat.ASTC5x5UnormSrgb, VkFormat.Astc5x5SrgbBlock),
        new(PixelFormat.ASTC6x5Unorm, VkFormat.Astc6x5UnormBlock),
        new(PixelFormat.ASTC6x5UnormSrgb, VkFormat.Astc6x5SrgbBlock),
        new(PixelFormat.ASTC6x6Unorm, VkFormat.Astc6x6UnormBlock),
        new(PixelFormat.ASTC6x6UnormSrgb, VkFormat.Astc6x6SrgbBlock),
        new(PixelFormat.ASTC8x5Unorm, VkFormat.Astc8x5UnormBlock),
        new(PixelFormat.ASTC8x5UnormSrgb, VkFormat.Astc8x5SrgbBlock),
        new(PixelFormat.ASTC8x6Unorm, VkFormat.Astc8x6UnormBlock),
        new(PixelFormat.ASTC8x6UnormSrgb, VkFormat.Astc8x6SrgbBlock),
        new(PixelFormat.ASTC8x8Unorm, VkFormat.Astc8x8UnormBlock),
        new(PixelFormat.ASTC8x8UnormSrgb, VkFormat.Astc8x8SrgbBlock),
        new(PixelFormat.ASTC10x5Unorm, VkFormat.Astc10x5UnormBlock),
        new(PixelFormat.ASTC10x5UnormSrgb, VkFormat.Astc10x5SrgbBlock),
        new(PixelFormat.ASTC10x6Unorm, VkFormat.Astc10x6UnormBlock),
        new(PixelFormat.ASTC10x6UnormSrgb, VkFormat.Astc10x6SrgbBlock),
        new(PixelFormat.ASTC10x8Unorm, VkFormat.Astc10x8UnormBlock),
        new(PixelFormat.ASTC10x8UnormSrgb, VkFormat.Astc10x8SrgbBlock),
        new(PixelFormat.ASTC10x10Unorm, VkFormat.Astc10x10UnormBlock),
        new(PixelFormat.ASTC10x10UnormSrgb, VkFormat.Astc10x10SrgbBlock),
        new(PixelFormat.ASTC12x10Unorm, VkFormat.Astc12x10UnormBlock),
        new(PixelFormat.ASTC12x10UnormSrgb, VkFormat.Astc12x10SrgbBlock),
        new(PixelFormat.ASTC12x12Unorm, VkFormat.Astc12x12UnormBlock),
        new(PixelFormat.ASTC12x12UnormSrgb, VkFormat.Astc12x12SrgbBlock),
    };

    public static readonly Func<PixelFormat, VkFormat> PixelFormatToVulkan;
    public static readonly Func<VkFormat, PixelFormat> PixelFormatToAbstract;

    private static readonly ValueTuple<BlendFactor, VkBlendFactor>[] BlendFactorCast = new ValueTuple<BlendFactor, VkBlendFactor>[]
    {
        new(BlendFactor.Zero, VkBlendFactor.Zero),
        new(BlendFactor.One, VkBlendFactor.One),
        new(BlendFactor.Src, VkBlendFactor.SrcColor),
        new(BlendFactor.OneMinusSrc, VkBlendFactor.OneMinusSrcColor),
        new(BlendFactor.SrcAlpha, VkBlendFactor.SrcAlpha),
        new(BlendFactor.OneMinusSrcAlpha, VkBlendFactor.OneMinusSrcAlpha),
        new(BlendFactor.Dst, VkBlendFactor.DstColor),
        new(BlendFactor.OneMinusDst, VkBlendFactor.OneMinusDstColor),
        new(BlendFactor.DstAlpha, VkBlendFactor.DstAlpha),
        new(BlendFactor.OneMinusDstAlpha, VkBlendFactor.OneMinusDstAlpha),
        new(BlendFactor.SrcAlphaSaturated, VkBlendFactor.SrcAlphaSaturate),
        new(BlendFactor.Constant, VkBlendFactor.ConstantColor),
        new(BlendFactor.OneMinusConstant, VkBlendFactor.OneMinusConstantColor),
    };

    public static readonly Func<BlendFactor, VkBlendFactor> BlendFactorToVulkan;
    public static readonly Func<VkBlendFactor, BlendFactor> BlendFactorToAbstract;

    private static readonly ValueTuple<BlendOperation, VkBlendOp>[] BlendOperationCast = new ValueTuple<BlendOperation, VkBlendOp>[]
    {
        new(BlendOperation.Add, VkBlendOp.Add),
        new(BlendOperation.Subtract, VkBlendOp.Subtract),
        new(BlendOperation.ReverseSubtract, VkBlendOp.ReverseSubtract),
        new(BlendOperation.Min, VkBlendOp.Min),
        new(BlendOperation.Max, VkBlendOp.Max),
    };

    public static readonly Func<BlendOperation, VkBlendOp> BlendOperationToVulkan;
    public static readonly Func<VkBlendOp, BlendOperation> BlendOperationToAbstract;

    private static readonly ValueTuple<VertexStepMode, VkVertexInputRate>[] VertexStepModeCast = new ValueTuple<VertexStepMode, VkVertexInputRate>[]
    {
        new(VertexStepMode.Vertex, VkVertexInputRate.Vertex),
        new(VertexStepMode.Instance, VkVertexInputRate.Instance),
    };

    public static readonly Func<VertexStepMode, VkVertexInputRate> VertexStepModeToVulkan;
    public static readonly Func<VkVertexInputRate, VertexStepMode> VertexStepModeToAbstract;

    private static readonly ValueTuple<VertexFormat, VkFormat>[] VertexFormatCast = new ValueTuple<VertexFormat, VkFormat>[]
    {
        new(VertexFormat.Undefined, VkFormat.Undefined),
        new(VertexFormat.Uint8x2, VkFormat.R8G8Uint),
        new(VertexFormat.Uint8x4, VkFormat.R8G8B8A8Uint),
        new(VertexFormat.Sint8x2, VkFormat.R8G8Sint),
        new(VertexFormat.Sint8x4, VkFormat.R8G8B8A8Sint),
        new(VertexFormat.Unorm8x2, VkFormat.R8G8Unorm),
        new(VertexFormat.Unorm8x4, VkFormat.R8G8B8A8Unorm),
        new(VertexFormat.Snorm8x2, VkFormat.R8G8Snorm),
        new(VertexFormat.Snorm8x4, VkFormat.R8G8B8A8Snorm),
        new(VertexFormat.Uint16x2, VkFormat.R16G16Uint),
        new(VertexFormat.Uint16x4, VkFormat.R16G16B16A16Uint),
        new(VertexFormat.Sint16x2, VkFormat.R16G16Sint),
        new(VertexFormat.Sint16x4, VkFormat.R16G16B16A16Sint),
        new(VertexFormat.Unorm16x2, VkFormat.R16G16Unorm),
        new(VertexFormat.Unorm16x4, VkFormat.R16G16B16A16Unorm),
        new(VertexFormat.Snorm16x2, VkFormat.R16G16Snorm),
        new(VertexFormat.Snorm16x4, VkFormat.R16G16B16A16Snorm),
        new(VertexFormat.Float16x2, VkFormat.R16G16Sfloat),
        new(VertexFormat.Float16x4, VkFormat.R16G16B16A16Sfloat),
        new(VertexFormat.Float32, VkFormat.R32Sfloat),
        new(VertexFormat.Float32x2, VkFormat.R32G32Sfloat),
        new(VertexFormat.Float32x3, VkFormat.R32G32B32Sfloat),
        new(VertexFormat.Float32x4, VkFormat.R32G32B32A32Sfloat),
        new(VertexFormat.Uint32, VkFormat.R32Uint),
        new(VertexFormat.Uint32x2, VkFormat.R32G32Uint),
        new(VertexFormat.Uint32x3, VkFormat.R32G32B32Uint),
        new(VertexFormat.Uint32x4, VkFormat.R32G32B32A32Uint),
        new(VertexFormat.Sint32, VkFormat.R32Sint),
        new(VertexFormat.Sint32x2, VkFormat.R32G32Sint),
        new(VertexFormat.Sint32x3, VkFormat.R32G32B32Sint),
        new(VertexFormat.Sint32x4, VkFormat.R32G32B32A32Sint),
    };

    public static readonly Func<VertexFormat, VkFormat> VertexFormatToVulkan;
    public static readonly Func<VkFormat, VertexFormat> VertexFormatToAbstract;

    private static readonly ValueTuple<CompareFunction, VkCompareOp>[] CompareFunctionCast = new ValueTuple<CompareFunction, VkCompareOp>[]
    {
        new(CompareFunction.Undefined, VkCompareOp.Never),
        new(CompareFunction.Never, VkCompareOp.Never),
        new(CompareFunction.Less, VkCompareOp.Less),
        new(CompareFunction.Equal, VkCompareOp.Equal),
        new(CompareFunction.LessEqual, VkCompareOp.LessOrEqual),
        new(CompareFunction.Greater, VkCompareOp.Greater),
        new(CompareFunction.NotEqual, VkCompareOp.NotEqual),
        new(CompareFunction.GreaterEqual, VkCompareOp.GreaterOrEqual),
        new(CompareFunction.Always, VkCompareOp.Always),
    };

    //one way cast
    public static readonly Func<CompareFunction, VkCompareOp> CompareFunctionToVulkan;

    private static readonly ValueTuple<TextureDimension, VkImageType>[] TextureDimensionCast = new ValueTuple<TextureDimension, VkImageType>[]
    {
        new(TextureDimension.Texture1D, VkImageType.Image1D),
        new(TextureDimension.Texture2D, VkImageType.Image2D),
        new(TextureDimension.Texture3D, VkImageType.Image3D),
    };

    public static readonly Func<TextureDimension, VkImageType> TextureDimensionToVulkan;
    public static readonly Func<VkImageType, TextureDimension> TextureDimensionToAbstract;

    private static readonly ValueTuple<TextureViewDimension, VkImageViewType>[] TextureViewDimensionCast = new ValueTuple<TextureViewDimension, VkImageViewType>[]
    {
        new(TextureViewDimension.Texture1D, VkImageViewType.Image1D),
        new(TextureViewDimension.Texture2D, VkImageViewType.Image2D),
        new(TextureViewDimension.Texture3D, VkImageViewType.Image3D),
        new(TextureViewDimension.Cube, VkImageViewType.ImageCube),
        new(TextureViewDimension.Texture1DArray, VkImageViewType.Image1DArray),
        new(TextureViewDimension.Texture2DArray, VkImageViewType.Image2DArray),
        new(TextureViewDimension.CubeArray, VkImageViewType.ImageCubeArray),
    };  

    public static readonly Func<TextureViewDimension, VkImageViewType> TextureViewDimensionToVulkan;
    public static readonly Func<VkImageViewType, TextureViewDimension> TextureViewDimensionToAbstract;

    private static readonly ValueTuple<StencilOperation, VkStencilOp>[] StencilOperationCast = new ValueTuple<StencilOperation, VkStencilOp>[]
    {
        new(StencilOperation.Keep, VkStencilOp.Keep),
        new(StencilOperation.Zero, VkStencilOp.Zero),
        new(StencilOperation.Replace, VkStencilOp.Replace),
        new(StencilOperation.Invert, VkStencilOp.Invert),
        new(StencilOperation.IncrementClamp, VkStencilOp.IncrementAndClamp),
        new(StencilOperation.DecrementClamp, VkStencilOp.DecrementAndClamp),
        new(StencilOperation.IncrementWrap, VkStencilOp.IncrementAndWrap),
        new(StencilOperation.DecrementWrap, VkStencilOp.DecrementAndWrap),
    };

    public static readonly Func<StencilOperation, VkStencilOp> StencilOperationToVulkan;
    public static readonly Func<VkStencilOp, StencilOperation> StencilOperationToAbstract;

    private static readonly ValueTuple<AddressMode, VkSamplerAddressMode>[] AddressModeCast = new ValueTuple<AddressMode, VkSamplerAddressMode>[]
    {
        new(AddressMode.ClampToEdge, VkSamplerAddressMode.ClampToEdge),
        new(AddressMode.Repeat, VkSamplerAddressMode.Repeat),
        new(AddressMode.MirrorRepeat, VkSamplerAddressMode.MirroredRepeat),
    };

    public static readonly Func<AddressMode, VkSamplerAddressMode> AddressModeToVulkan;
    public static readonly Func<VkSamplerAddressMode, AddressMode> AddressModeToAbstract;


    private static readonly ValueTuple<FilterMode, VkFilter>[] FilterModeCast = new ValueTuple<FilterMode, VkFilter>[]
    {
        new(FilterMode.Nearest, VkFilter.Nearest),
        new(FilterMode.Linear, VkFilter.Linear),
    };

    public static readonly Func<FilterMode, VkFilter> FilterModeToVulkan;
    public static readonly Func<VkFilter, FilterMode> FilterModeToAbstract;


    static UtilsVulkan()
    {
        UtilsCast.GenerateCastFunc(PrimitiveTopologyCast, out PrimitiveTopologyToVulkan, out PrimitiveTopologyToAbstract);
        UtilsCast.GenerateCastFunc(CullModeCast, out CullModeToVulkan, out CullModeToAbstract);
        UtilsCast.GenerateCastFunc(FrontFaceCast, out FrontFaceToVulkan, out FrontFaceToAbstract);
        UtilsCast.GenerateCastFunc(PixelFormatCast, out PixelFormatToVulkan, out PixelFormatToAbstract);
        UtilsCast.GenerateCastFunc(BlendFactorCast, out BlendFactorToVulkan, out BlendFactorToAbstract);
        UtilsCast.GenerateCastFunc(BlendOperationCast, out BlendOperationToVulkan, out BlendOperationToAbstract);
        UtilsCast.GenerateCastFunc(VertexStepModeCast, out VertexStepModeToVulkan, out VertexStepModeToAbstract);
        UtilsCast.GenerateCastFunc(VertexFormatCast, out VertexFormatToVulkan, out VertexFormatToAbstract);
        CompareFunctionToVulkan = UtilsCast.GenerateCastFunc(CompareFunctionCast);
        UtilsCast.GenerateCastFunc(TextureDimensionCast, out TextureDimensionToVulkan, out TextureDimensionToAbstract);
        UtilsCast.GenerateCastFunc(TextureViewDimensionCast, out TextureViewDimensionToVulkan, out TextureViewDimensionToAbstract);
        UtilsCast.GenerateCastFunc(StencilOperationCast, out StencilOperationToVulkan, out StencilOperationToAbstract);
        UtilsCast.GenerateCastFunc(AddressModeCast, out AddressModeToVulkan, out AddressModeToAbstract);
        UtilsCast.GenerateCastFunc(FilterModeCast, out FilterModeToVulkan, out FilterModeToAbstract);
    }
}