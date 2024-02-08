using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Reflect;

namespace Vocore.Graphics;

internal static class UtilsRelfectType
{
    private static readonly Tuple<Format, VertexFormat>[] RelfectFormatToVertexCast = new Tuple<Format, VertexFormat>[]{
        new (Format.R16G16Uint, VertexFormat.Uint16x2),
        new (Format.R16G16B16A16Uint, VertexFormat.Uint16x4),
        new (Format.R16G16Sint, VertexFormat.Sint16x2),
        new (Format.R16G16B16A16Sint, VertexFormat.Sint16x4),
        new (Format.R16G16Sfloat, VertexFormat.Float16x2),
        new (Format.R16G16B16A16Sfloat, VertexFormat.Float16x4),
        new (Format.R32Uint, VertexFormat.Uint32),
        new (Format.R32G32Uint, VertexFormat.Uint32x2),
        new (Format.R32G32B32Uint, VertexFormat.Uint32x3),
        new (Format.R32G32B32A32Uint, VertexFormat.Uint32x4),
        new (Format.R32Sint, VertexFormat.Sint32),
        new (Format.R32G32Sint, VertexFormat.Sint32x2),
        new (Format.R32G32B32Sint, VertexFormat.Sint32x3),
        new (Format.R32G32B32A32Sint, VertexFormat.Sint32x4),
        new (Format.R32Sfloat, VertexFormat.Float32),
        new (Format.R32G32Sfloat, VertexFormat.Float32x2),
        new (Format.R32G32B32Sfloat, VertexFormat.Float32x3),
        new (Format.R32G32B32A32Sfloat, VertexFormat.Float32x4),
    };

    public static readonly Func<Format, VertexFormat> ConvertFormat;

    private static readonly Tuple<ImageFormat, PixelFormat>[] ReflectFormatToPixelCast = new Tuple<ImageFormat, PixelFormat>[]{
        // 8-bits
        new(ImageFormat.R8, PixelFormat.R8Unorm),
        new(ImageFormat.R8Snorm, PixelFormat.R8Snorm),
        new(ImageFormat.R8ui, PixelFormat.R8Uint),
        new(ImageFormat.R8i, PixelFormat.R8Sint),
        // 16-bits
        new(ImageFormat.R16ui, PixelFormat.R16Uint),
        new(ImageFormat.R16i, PixelFormat.R16Sint),
        new(ImageFormat.R16f, PixelFormat.R16Float),
        new(ImageFormat.Rg8, PixelFormat.RG8Unorm),
        new(ImageFormat.Rg8ui, PixelFormat.RG8Uint),
        new(ImageFormat.Rg8Snorm, PixelFormat.RG8Snorm),
        new(ImageFormat.Rg8i, PixelFormat.RG8Sint),
        // 32-bits
        new(ImageFormat.R32f, PixelFormat.R32Float),
        new(ImageFormat.R32ui, PixelFormat.R32Uint),
        new(ImageFormat.R32i, PixelFormat.R32Sint),
        new(ImageFormat.Rg16ui, PixelFormat.RG16Uint),
        new(ImageFormat.Rg16i, PixelFormat.RG16Sint),
        new(ImageFormat.Rg16f, PixelFormat.RG16Float),
        new(ImageFormat.Rgba8, PixelFormat.RGBA8Unorm),
        new(ImageFormat.Rgba8Snorm, PixelFormat.RGBA8Snorm),
        new(ImageFormat.Rgba8ui, PixelFormat.RGBA8Uint),
        new(ImageFormat.Rgba8i, PixelFormat.RGBA8Sint),
        // Packed 32-bit
        new(ImageFormat.Rgb10a2ui, PixelFormat.RGB10A2Uint),
        new(ImageFormat.Rgb10A2, PixelFormat.RGB10A2Unorm),
        new(ImageFormat.R11fG11fB10f, PixelFormat.RG11B10Ufloat),   
        // 64-bits
        new(ImageFormat.Rg32f, PixelFormat.RG32Float),
        new(ImageFormat.Rg32ui, PixelFormat.RG32Uint),
        new(ImageFormat.Rg32i, PixelFormat.RG32Sint),
        new(ImageFormat.Rgba16ui, PixelFormat.RGBA16Uint),
        new(ImageFormat.Rgba16i, PixelFormat.RGBA16Sint),
        new(ImageFormat.Rgba16f, PixelFormat.RGBA16Float),
        // 128-bits
        new(ImageFormat.Rgba32f, PixelFormat.RGBA32Float),
        new(ImageFormat.Rgba32ui, PixelFormat.RGBA32Uint),
        new(ImageFormat.Rgba32i, PixelFormat.RGBA32Sint),
    };

    public static readonly Func<ImageFormat, PixelFormat> ConvertImageFormat;


    public static BindingType ConvertBindingType(DescriptorType type)
    {
        switch (type)
        {
            case DescriptorType.Sampler:
                return BindingType.Sampler;
            case DescriptorType.SampledImage:
                return BindingType.Texture;
            case DescriptorType.StorageImage:
                return BindingType.StorageTexture;
            case DescriptorType.UniformBuffer:
                return BindingType.UniformBuffer;
            case DescriptorType.StorageBuffer:
                return BindingType.StorageBuffer;
            case DescriptorType.CombinedImageSampler:
            case DescriptorType.UniformTexelBuffer:
            case DescriptorType.StorageTexelBuffer:
            case DescriptorType.UniformBufferDynamic:
            case DescriptorType.StorageBufferDynamic:
            case DescriptorType.InputAttachment:
                throw new Exception($"Unsupported descriptor type {type}");
            default:
                throw new Exception($"Unknown descriptor type {type}");
        }
    }

    public static TextureViewDimension ConvertTextureViewDimension(ImageTraits traits)
    {
        TextureViewDimension dimension = TextureViewDimension.Undefined;

        switch (traits.Dim)
        {
            case Dim.Dim1D:
                dimension = TextureViewDimension.Texture1D;
                break;
            case Dim.Dim2D:
                dimension = TextureViewDimension.Texture2D;
                break;
            case Dim.Dim3D:
                dimension = TextureViewDimension.Texture3D;
                break;
            case Dim.DimCube:
                dimension = TextureViewDimension.Cube;
                break;
            default:
                throw new Exception($"Unsupported texture dimension {traits.Dim}");
        }

        if (traits.Arrayed > 1)
        {
            switch (dimension)
            {
                case TextureViewDimension.Texture1D:
                    dimension = TextureViewDimension.Texture1DArray;
                    break;
                case TextureViewDimension.Texture2D:
                    dimension = TextureViewDimension.Texture2DArray;
                    break;
                case TextureViewDimension.Cube:
                    dimension = TextureViewDimension.CubeArray;
                    break;
                default:
                    throw new Exception($"Unsupported texture dimension for array {traits.Dim}");
            }
        }

        return dimension;
    }

    static UtilsRelfectType()
    {
        ConvertFormat = UtilsCast.GenerateCastFunc(RelfectFormatToVertexCast);
        ConvertImageFormat = UtilsCast.GenerateCastFunc(ReflectFormatToPixelCast);
    }
}