using System.Runtime.CompilerServices;
using Vortice.Vulkan;

namespace Alco.Graphics.Vulkan;

/// <summary>
/// Conversions between the abstract graphics types and the Vulkan native types.
/// </summary>
internal static unsafe class VulkanUtility
{
    // ===== pixel formats =====

    public static VkFormat PixelFormatToVulkan(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.R8Unorm => VkFormat.R8Unorm,
            PixelFormat.R8Snorm => VkFormat.R8Snorm,
            PixelFormat.R8Uint => VkFormat.R8Uint,
            PixelFormat.R8Sint => VkFormat.R8Sint,
            PixelFormat.R16Uint => VkFormat.R16Uint,
            PixelFormat.R16Sint => VkFormat.R16Sint,
            PixelFormat.R16Float => VkFormat.R16Sfloat,
            PixelFormat.RG8Unorm => VkFormat.R8G8Unorm,
            PixelFormat.RG8Snorm => VkFormat.R8G8Snorm,
            PixelFormat.RG8Uint => VkFormat.R8G8Uint,
            PixelFormat.RG8Sint => VkFormat.R8G8Sint,
            PixelFormat.R32Float => VkFormat.R32Sfloat,
            PixelFormat.R32Uint => VkFormat.R32Uint,
            PixelFormat.R32Sint => VkFormat.R32Sint,
            PixelFormat.RG16Uint => VkFormat.R16G16Uint,
            PixelFormat.RG16Sint => VkFormat.R16G16Sint,
            PixelFormat.RG16Float => VkFormat.R16G16Sfloat,
            PixelFormat.RGBA8Unorm => VkFormat.R8G8B8A8Unorm,
            PixelFormat.RGBA8UnormSrgb => VkFormat.R8G8B8A8Srgb,
            PixelFormat.RGBA8Snorm => VkFormat.R8G8B8A8Snorm,
            PixelFormat.RGBA8Uint => VkFormat.R8G8B8A8Uint,
            PixelFormat.RGBA8Sint => VkFormat.R8G8B8A8Sint,
            PixelFormat.BGRA8Unorm => VkFormat.B8G8R8A8Unorm,
            PixelFormat.BGRA8UnormSrgb => VkFormat.B8G8R8A8Srgb,
            PixelFormat.RGB10A2Uint => VkFormat.A2B10G10R10UintPack32,
            PixelFormat.RGB10A2Unorm => VkFormat.A2B10G10R10UnormPack32,
            PixelFormat.RG11B10Ufloat => VkFormat.B10G11R11UfloatPack32,
            PixelFormat.RGB9E5Ufloat => VkFormat.E5B9G9R9UfloatPack32,
            PixelFormat.RG32Float => VkFormat.R32G32Sfloat,
            PixelFormat.RG32Uint => VkFormat.R32G32Uint,
            PixelFormat.RG32Sint => VkFormat.R32G32Sint,
            PixelFormat.RGBA16Uint => VkFormat.R16G16B16A16Uint,
            PixelFormat.RGBA16Sint => VkFormat.R16G16B16A16Sint,
            PixelFormat.RGBA16Float => VkFormat.R16G16B16A16Sfloat,
            PixelFormat.RGBA32Float => VkFormat.R32G32B32A32Sfloat,
            PixelFormat.RGBA32Uint => VkFormat.R32G32B32A32Uint,
            PixelFormat.RGBA32Sint => VkFormat.R32G32B32A32Sint,
            PixelFormat.Stencil8 => VkFormat.S8Uint,
            PixelFormat.Depth16Unorm => VkFormat.D16Unorm,
            // Vulkan has no stencil-less 24-bit depth format; the portable
            // substitute is D32 (Depth24Plus means "at least 24 bits").
            PixelFormat.Depth24Plus => VkFormat.D32Sfloat,
            PixelFormat.Depth24PlusStencil8 => VkFormat.D24UnormS8Uint,
            PixelFormat.Depth32Float => VkFormat.D32Sfloat,
            PixelFormat.Depth32FloatStencil8 => VkFormat.D32SfloatS8Uint,
            PixelFormat.BC1RGBAUnorm => VkFormat.Bc1RgbaUnormBlock,
            PixelFormat.BC1RGBAUnormSrgb => VkFormat.Bc1RgbaSrgbBlock,
            PixelFormat.BC2RGBAUnorm => VkFormat.Bc2UnormBlock,
            PixelFormat.BC2RGBAUnormSrgb => VkFormat.Bc2SrgbBlock,
            PixelFormat.BC3RGBAUnorm => VkFormat.Bc3UnormBlock,
            PixelFormat.BC3RGBAUnormSrgb => VkFormat.Bc3SrgbBlock,
            PixelFormat.BC4RUnorm => VkFormat.Bc4UnormBlock,
            PixelFormat.BC4RSnorm => VkFormat.Bc4SnormBlock,
            PixelFormat.BC5RGUnorm => VkFormat.Bc5UnormBlock,
            PixelFormat.BC5RGSnorm => VkFormat.Bc5SnormBlock,
            PixelFormat.BC6HRGBUfloat => VkFormat.Bc6hUfloatBlock,
            PixelFormat.BC6HRGBFloat => VkFormat.Bc6hSfloatBlock,
            PixelFormat.BC7RGBAUnorm => VkFormat.Bc7UnormBlock,
            PixelFormat.BC7RGBAUnormSrgb => VkFormat.Bc7SrgbBlock,
            PixelFormat.ETC2RGB8Unorm => VkFormat.Etc2R8G8B8UnormBlock,
            PixelFormat.ETC2RGB8UnormSrgb => VkFormat.Etc2R8G8B8SrgbBlock,
            PixelFormat.ETC2RGB8A1Unorm => VkFormat.Etc2R8G8B8A1UnormBlock,
            PixelFormat.ETC2RGB8A1UnormSrgb => VkFormat.Etc2R8G8B8A1SrgbBlock,
            PixelFormat.ETC2RGBA8Unorm => VkFormat.Etc2R8G8B8A8UnormBlock,
            PixelFormat.ETC2RGBA8UnormSrgb => VkFormat.Etc2R8G8B8A8SrgbBlock,
            PixelFormat.EACR11Unorm => VkFormat.EacR11UnormBlock,
            PixelFormat.EACR11Snorm => VkFormat.EacR11SnormBlock,
            PixelFormat.EACRG11Unorm => VkFormat.EacR11G11UnormBlock,
            PixelFormat.EACRG11Snorm => VkFormat.EacR11G11SnormBlock,
            _ => throw new GraphicsException($"The pixel format {format} is not supported by the Vulkan backend."),
        };
    }

    public static PixelFormat VkFormatToPixelFormat(VkFormat format)
    {
        return format switch
        {
            VkFormat.R8Unorm => PixelFormat.R8Unorm,
            VkFormat.R8Snorm => PixelFormat.R8Snorm,
            VkFormat.R8Uint => PixelFormat.R8Uint,
            VkFormat.R8Sint => PixelFormat.R8Sint,
            VkFormat.R16Uint => PixelFormat.R16Uint,
            VkFormat.R16Sint => PixelFormat.R16Sint,
            VkFormat.R16Sfloat => PixelFormat.R16Float,
            VkFormat.R8G8Unorm => PixelFormat.RG8Unorm,
            VkFormat.R8G8Snorm => PixelFormat.RG8Snorm,
            VkFormat.R8G8Uint => PixelFormat.RG8Uint,
            VkFormat.R8G8Sint => PixelFormat.RG8Sint,
            VkFormat.R32Sfloat => PixelFormat.R32Float,
            VkFormat.R32Uint => PixelFormat.R32Uint,
            VkFormat.R32Sint => PixelFormat.R32Sint,
            VkFormat.R16G16Uint => PixelFormat.RG16Uint,
            VkFormat.R16G16Sint => PixelFormat.RG16Sint,
            VkFormat.R16G16Sfloat => PixelFormat.RG16Float,
            VkFormat.R8G8B8A8Unorm => PixelFormat.RGBA8Unorm,
            VkFormat.R8G8B8A8Srgb => PixelFormat.RGBA8UnormSrgb,
            VkFormat.R8G8B8A8Snorm => PixelFormat.RGBA8Snorm,
            VkFormat.R8G8B8A8Uint => PixelFormat.RGBA8Uint,
            VkFormat.R8G8B8A8Sint => PixelFormat.RGBA8Sint,
            VkFormat.B8G8R8A8Unorm => PixelFormat.BGRA8Unorm,
            VkFormat.B8G8R8A8Srgb => PixelFormat.BGRA8UnormSrgb,
            VkFormat.A2B10G10R10UnormPack32 => PixelFormat.RGB10A2Unorm,
            VkFormat.A2B10G10R10UintPack32 => PixelFormat.RGB10A2Uint,
            VkFormat.B10G11R11UfloatPack32 => PixelFormat.RG11B10Ufloat,
            VkFormat.E5B9G9R9UfloatPack32 => PixelFormat.RGB9E5Ufloat,
            VkFormat.R32G32Sfloat => PixelFormat.RG32Float,
            VkFormat.R32G32Uint => PixelFormat.RG32Uint,
            VkFormat.R32G32Sint => PixelFormat.RG32Sint,
            VkFormat.R16G16B16A16Uint => PixelFormat.RGBA16Uint,
            VkFormat.R16G16B16A16Sint => PixelFormat.RGBA16Sint,
            VkFormat.R16G16B16A16Sfloat => PixelFormat.RGBA16Float,
            VkFormat.R32G32B32A32Sfloat => PixelFormat.RGBA32Float,
            VkFormat.R32G32B32A32Uint => PixelFormat.RGBA32Uint,
            VkFormat.R32G32B32A32Sint => PixelFormat.RGBA32Sint,
            VkFormat.D16Unorm => PixelFormat.Depth16Unorm,
            VkFormat.X8D24UnormPack32 => PixelFormat.Depth24Plus,
            VkFormat.D24UnormS8Uint => PixelFormat.Depth24PlusStencil8,
            VkFormat.D32Sfloat => PixelFormat.Depth32Float,
            VkFormat.D32SfloatS8Uint => PixelFormat.Depth32FloatStencil8,
            VkFormat.S8Uint => PixelFormat.Stencil8,
            _ => PixelFormat.Undefined,
        };
    }

    /// <summary>Whether the Vulkan format is one of the depth-stencil formats.</summary>
    public static bool IsDepthFormat(VkFormat format)
    {
        return format is VkFormat.D16Unorm
            or VkFormat.X8D24UnormPack32
            or VkFormat.D32Sfloat
            or VkFormat.D24UnormS8Uint
            or VkFormat.D32SfloatS8Uint
            or VkFormat.S8Uint;
    }

    public static bool HasStencil(VkFormat format)
    {
        return format is VkFormat.S8Uint or VkFormat.D24UnormS8Uint or VkFormat.D32SfloatS8Uint;
    }

    public static VkImageAspectFlags AspectToVulkan(TextureAspect aspect, VkFormat format)
    {
        // "All" must map to every aspect the image actually contains: a view on a
        // depth-only image cannot claim the (absent) stencil plane.
        VkImageAspectFlags all = IsDepthFormat(format)
            ? (HasStencil(format)
                ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                : VkImageAspectFlags.Depth)
            : VkImageAspectFlags.Color;

        return aspect switch
        {
            TextureAspect.All => all,
            TextureAspect.None => all,
            TextureAspect.DepthOnly => VkImageAspectFlags.Depth,
            TextureAspect.StencilOnly => VkImageAspectFlags.Stencil,
            _ => all,
        };
    }

    // ===== usages =====

    public static VkImageUsageFlags ConvertTextureUsage(TextureUsage usage)
    {
        VkImageUsageFlags result = VkImageUsageFlags.None;
        if ((usage & TextureUsage.Read) != 0)
        {
            result |= VkImageUsageFlags.TransferSrc;
        }
        if ((usage & TextureUsage.Write) != 0)
        {
            result |= VkImageUsageFlags.TransferDst;
        }
        if ((usage & TextureUsage.TextureBinding) != 0)
        {
            result |= VkImageUsageFlags.Sampled;
        }
        if ((usage & TextureUsage.StorageBinding) != 0)
        {
            result |= VkImageUsageFlags.Storage;
        }
        if ((usage & TextureUsage.ColorAttachment) != 0)
        {
            result |= VkImageUsageFlags.ColorAttachment;
        }
        if ((usage & TextureUsage.DepthAttachment) != 0)
        {
            result |= VkImageUsageFlags.DepthStencilAttachment;
        }
        return result;
    }

    public static VkBufferUsageFlags ConvertBufferUsage(BufferUsage usage)
    {
        VkBufferUsageFlags result = VkBufferUsageFlags.None;
        if ((usage & BufferUsage.MapRead) != 0)
        {
            // wgpu requires MapRead buffers to be copy destinations.
            result |= VkBufferUsageFlags.TransferDst;
        }
        if ((usage & BufferUsage.MapWrite) != 0)
        {
            // wgpu requires MapWrite buffers to be copy sources.
            result |= VkBufferUsageFlags.TransferSrc;
        }
        if ((usage & BufferUsage.CopySrc) != 0)
        {
            result |= VkBufferUsageFlags.TransferSrc;
        }
        if ((usage & BufferUsage.CopyDst) != 0)
        {
            result |= VkBufferUsageFlags.TransferDst;
        }
        if ((usage & BufferUsage.Uniform) != 0)
        {
            result |= VkBufferUsageFlags.UniformBuffer;
        }
        if ((usage & BufferUsage.Storage) != 0)
        {
            result |= VkBufferUsageFlags.StorageBuffer;
        }
        if ((usage & BufferUsage.Index) != 0)
        {
            result |= VkBufferUsageFlags.IndexBuffer;
        }
        if ((usage & BufferUsage.Vertex) != 0)
        {
            result |= VkBufferUsageFlags.VertexBuffer;
        }
        if ((usage & BufferUsage.Indirect) != 0)
        {
            result |= VkBufferUsageFlags.IndirectBuffer;
        }
        if ((usage & BufferUsage.QueryResolve) != 0)
        {
            result |= VkBufferUsageFlags.TransferDst;
        }
        return result;
    }

    public static VkShaderStageFlags ConvertShaderStage(ShaderStage stage)
    {
        VkShaderStageFlags result = VkShaderStageFlags.None;
        if ((stage & ShaderStage.Vertex) != 0)
        {
            result |= VkShaderStageFlags.Vertex;
        }
        if ((stage & ShaderStage.Hull) != 0)
        {
            result |= VkShaderStageFlags.TessellationControl;
        }
        if ((stage & ShaderStage.Domain) != 0)
        {
            result |= VkShaderStageFlags.TessellationEvaluation;
        }
        if ((stage & ShaderStage.Geometry) != 0)
        {
            result |= VkShaderStageFlags.Geometry;
        }
        if ((stage & ShaderStage.Fragment) != 0)
        {
            result |= VkShaderStageFlags.Fragment;
        }
        if ((stage & ShaderStage.Compute) != 0)
        {
            result |= VkShaderStageFlags.Compute;
        }
        return result;
    }

    // ===== pipeline state =====

    public static VkPrimitiveTopology PrimitiveTopologyToVulkan(PrimitiveTopology topology)
    {
        return topology switch
        {
            PrimitiveTopology.PointList => VkPrimitiveTopology.PointList,
            PrimitiveTopology.LineList => VkPrimitiveTopology.LineList,
            PrimitiveTopology.LineStrip => VkPrimitiveTopology.LineStrip,
            PrimitiveTopology.TriangleList => VkPrimitiveTopology.TriangleList,
            PrimitiveTopology.TriangleStrip => VkPrimitiveTopology.TriangleStrip,
            _ => VkPrimitiveTopology.TriangleList,
        };
    }

    public static VkCullModeFlags CullModeToVulkan(CullMode mode)
    {
        return mode switch
        {
            CullMode.None => VkCullModeFlags.None,
            CullMode.Front => VkCullModeFlags.Front,
            CullMode.Back => VkCullModeFlags.Back,
            _ => VkCullModeFlags.None,
        };
    }

    public static VkFrontFace FrontFaceToVulkan(FrontFace face)
    {
        return face switch
        {
            FrontFace.Clockwise => VkFrontFace.Clockwise,
            FrontFace.CounterClockwise => VkFrontFace.CounterClockwise,
            _ => VkFrontFace.CounterClockwise,
        };
    }

    public static VkPolygonMode FillModeToVulkan(FillMode mode)
    {
        return mode switch
        {
            FillMode.Solid => VkPolygonMode.Fill,
            FillMode.Wireframe => VkPolygonMode.Line,
            _ => VkPolygonMode.Fill,
        };
    }

    public static VkCompareOp CompareFunctionToVulkan(CompareFunction func)
    {
        return func switch
        {
            CompareFunction.Never => VkCompareOp.Never,
            CompareFunction.Less => VkCompareOp.Less,
            CompareFunction.Equal => VkCompareOp.Equal,
            CompareFunction.LessEqual => VkCompareOp.LessOrEqual,
            CompareFunction.Greater => VkCompareOp.Greater,
            CompareFunction.NotEqual => VkCompareOp.NotEqual,
            CompareFunction.GreaterEqual => VkCompareOp.GreaterOrEqual,
            CompareFunction.Always => VkCompareOp.Always,
            // Undefined behaves like Always: with no depth attachment the state is inert.
            _ => VkCompareOp.Always,
        };
    }

    public static VkBlendFactor BlendFactorToVulkan(BlendFactor factor)
    {
        return factor switch
        {
            BlendFactor.Zero => VkBlendFactor.Zero,
            BlendFactor.One => VkBlendFactor.One,
            BlendFactor.Src => VkBlendFactor.SrcColor,
            BlendFactor.OneMinusSrc => VkBlendFactor.OneMinusSrcColor,
            BlendFactor.SrcAlpha => VkBlendFactor.SrcAlpha,
            BlendFactor.OneMinusSrcAlpha => VkBlendFactor.OneMinusSrcAlpha,
            BlendFactor.Dst => VkBlendFactor.DstColor,
            BlendFactor.OneMinusDst => VkBlendFactor.OneMinusDstColor,
            BlendFactor.DstAlpha => VkBlendFactor.DstAlpha,
            BlendFactor.OneMinusDstAlpha => VkBlendFactor.OneMinusDstAlpha,
            BlendFactor.SrcAlphaSaturated => VkBlendFactor.SrcAlphaSaturate,
            BlendFactor.Constant => VkBlendFactor.ConstantColor,
            BlendFactor.OneMinusConstant => VkBlendFactor.OneMinusConstantColor,
            _ => VkBlendFactor.Zero,
        };
    }

    public static VkBlendOp BlendOperationToVulkan(BlendOperation op)
    {
        return op switch
        {
            BlendOperation.Add => VkBlendOp.Add,
            BlendOperation.Subtract => VkBlendOp.Subtract,
            BlendOperation.ReverseSubtract => VkBlendOp.ReverseSubtract,
            BlendOperation.Min => VkBlendOp.Min,
            BlendOperation.Max => VkBlendOp.Max,
            _ => VkBlendOp.Add,
        };
    }

    public static VkStencilOp StencilOperationToVulkan(StencilOperation op)
    {
        return op switch
        {
            StencilOperation.Keep => VkStencilOp.Keep,
            StencilOperation.Zero => VkStencilOp.Zero,
            StencilOperation.Replace => VkStencilOp.Replace,
            StencilOperation.Invert => VkStencilOp.Invert,
            StencilOperation.IncrementClamp => VkStencilOp.IncrementAndClamp,
            StencilOperation.DecrementClamp => VkStencilOp.DecrementAndClamp,
            StencilOperation.IncrementWrap => VkStencilOp.IncrementAndWrap,
            StencilOperation.DecrementWrap => VkStencilOp.DecrementAndWrap,
            _ => VkStencilOp.Keep,
        };
    }

    public static VkVertexInputRate VertexStepModeToVulkan(VertexStepMode mode)
    {
        return mode switch
        {
            VertexStepMode.Vertex => VkVertexInputRate.Vertex,
            VertexStepMode.Instance => VkVertexInputRate.Instance,
            _ => VkVertexInputRate.Vertex,
        };
    }

    public static VkFormat VertexFormatToVulkan(VertexFormat format)
    {
        return format switch
        {
            VertexFormat.Uint8x2 => VkFormat.R8G8Uint,
            VertexFormat.Uint8x4 => VkFormat.R8G8B8A8Uint,
            VertexFormat.Sint8x2 => VkFormat.R8G8Sint,
            VertexFormat.Sint8x4 => VkFormat.R8G8B8A8Sint,
            VertexFormat.Unorm8x2 => VkFormat.R8G8Unorm,
            VertexFormat.Unorm8x4 => VkFormat.R8G8B8A8Unorm,
            VertexFormat.Snorm8x2 => VkFormat.R8G8Snorm,
            VertexFormat.Snorm8x4 => VkFormat.R8G8B8A8Snorm,
            VertexFormat.Uint16x2 => VkFormat.R16G16Uint,
            VertexFormat.Uint16x4 => VkFormat.R16G16B16A16Uint,
            VertexFormat.Sint16x2 => VkFormat.R16G16Sint,
            VertexFormat.Sint16x4 => VkFormat.R16G16B16A16Sint,
            VertexFormat.Unorm16x2 => VkFormat.R16G16Unorm,
            VertexFormat.Unorm16x4 => VkFormat.R16G16B16A16Unorm,
            VertexFormat.Snorm16x2 => VkFormat.R16G16Snorm,
            VertexFormat.Snorm16x4 => VkFormat.R16G16B16A16Snorm,
            VertexFormat.Float16x2 => VkFormat.R16G16Sfloat,
            VertexFormat.Float16x4 => VkFormat.R16G16B16A16Sfloat,
            VertexFormat.Float32 => VkFormat.R32Sfloat,
            VertexFormat.Float32x2 => VkFormat.R32G32Sfloat,
            VertexFormat.Float32x3 => VkFormat.R32G32B32Sfloat,
            VertexFormat.Float32x4 => VkFormat.R32G32B32A32Sfloat,
            VertexFormat.Uint32 => VkFormat.R32Uint,
            VertexFormat.Uint32x2 => VkFormat.R32G32Uint,
            VertexFormat.Uint32x3 => VkFormat.R32G32B32Uint,
            VertexFormat.Uint32x4 => VkFormat.R32G32B32A32Uint,
            VertexFormat.Sint32 => VkFormat.R32Sint,
            VertexFormat.Sint32x2 => VkFormat.R32G32Sint,
            VertexFormat.Sint32x3 => VkFormat.R32G32B32Sint,
            VertexFormat.Sint32x4 => VkFormat.R32G32B32A32Sint,
            _ => throw new GraphicsException($"The vertex format {format} is not supported by the Vulkan backend."),
        };
    }

    public static VkIndexType IndexFormatToVulkan(IndexFormat format)
    {
        return format switch
        {
            IndexFormat.UInt16 => VkIndexType.Uint16,
            IndexFormat.UInt32 => VkIndexType.Uint32,
            _ => VkIndexType.NoneKHR,
        };
    }

    // ===== samplers =====

    public static VkSamplerAddressMode AddressModeToVulkan(AddressMode mode)
    {
        return mode switch
        {
            AddressMode.Repeat => VkSamplerAddressMode.Repeat,
            AddressMode.MirrorRepeat => VkSamplerAddressMode.MirroredRepeat,
            AddressMode.ClampToEdge => VkSamplerAddressMode.ClampToEdge,
            _ => VkSamplerAddressMode.ClampToEdge,
        };
    }

    public static VkFilter FilterModeToVulkan(FilterMode mode)
    {
        return mode switch
        {
            FilterMode.Nearest => VkFilter.Nearest,
            FilterMode.Linear => VkFilter.Linear,
            _ => VkFilter.Nearest,
        };
    }

    public static VkSamplerMipmapMode MipmapFilterModeToVulkan(FilterMode mode)
    {
        return mode switch
        {
            FilterMode.Nearest => VkSamplerMipmapMode.Nearest,
            FilterMode.Linear => VkSamplerMipmapMode.Linear,
            _ => VkSamplerMipmapMode.Nearest,
        };
    }

    // ===== descriptor types =====

    public static VkDescriptorType BindingTypeToDescriptorType(BindingType type)
    {
        return type switch
        {
            BindingType.UniformBuffer => VkDescriptorType.UniformBuffer,
            BindingType.StorageBuffer => VkDescriptorType.StorageBuffer,
            BindingType.Sampler => VkDescriptorType.Sampler,
            BindingType.SamplerComparison => VkDescriptorType.Sampler,
            BindingType.Texture => VkDescriptorType.SampledImage,
            BindingType.StorageTexture => VkDescriptorType.StorageImage,
            _ => throw new GraphicsException($"The binding type {type} is not supported by the Vulkan backend."),
        };
    }

    // ===== texture data layout =====

    public const uint TexelRowAlignment = 256;

    public static VkBufferImageCopy GetBufferImageCopy(
        uint mipLevel,
        uint width,
        uint height,
        uint depthOrLayers,
        VkImageAspectFlags aspect,
        uint offset)
    {
        return new VkBufferImageCopy
        {
            bufferOffset = offset,
            bufferRowLength = 0, // tightly packed
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers
            {
                aspectMask = aspect,
                mipLevel = mipLevel,
                baseArrayLayer = 0,
                layerCount = depthOrLayers,
            },
            imageOffset = default,
            imageExtent = new VkExtent3D
            {
                width = width,
                height = height,
                depth = 1,
            },
        };
    }

    /// <summary>Bytes per pixel for an uncompressed format, or 0 when compressed.</summary>
    public static uint PixelFormatSize(PixelFormat format)
    {
        return PixelFormatUtility.TryGetPixelSize(format, out uint size) ? size : 0;
    }

    // ===== misc =====

    public static ReadOnlySpan<byte> GetUtf8(string value)
    {
        return System.Text.Encoding.UTF8.GetBytes(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AlignUp(uint value, uint alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong AlignUp(ulong value, ulong alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }
}
