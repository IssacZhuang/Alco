// Vulkan-specific enum conversions, capability constants, and error reporting.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Vk = Vulkan;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal enum DescriptorHeapType : byte
{
    Texture,
    Sampler,
}

internal enum TextureCompression : byte
{
    None,
    Etc2,
    Astc,
    Bc,
}

internal static unsafe partial class Backend
{
    public const uint MaxDeviceExtensions = 512;
    public const uint MaxInstanceExtensions = 256;
    public const uint MaxInstanceLayers = 64;
    public const uint MaxPhysicalDevices = 32;
    public const uint MaxQueueFamilies = 64;
    public const uint MaxSwapchainImages = 8;
    public const uint MaxSurfaceFormats = 64;

    /// <summary>Formats a Vulkan operation failure.</summary>
    /// <param name="result">The failed native result.</param>
    /// <param name="operation">The native operation.</param>
    /// <param name="byteCount">The requested allocation size, if applicable.</param>
    /// <returns>The backend diagnostic.</returns>
    public static string VkFailureMessage(Vk.VkResult result, string operation, ulong byteCount = 0)
    {
        string reason = result switch
        {
            Vk.VkResult.ErrorInitializationFailed => "VK_ERROR_INITIALIZATION_FAILED (graphics initialization failed)",
            Vk.VkResult.ErrorOutOfHostMemory => "VK_ERROR_OUT_OF_HOST_MEMORY (not enough system memory)",
            Vk.VkResult.ErrorOutOfDeviceMemory => "VK_ERROR_OUT_OF_DEVICE_MEMORY (not enough GPU memory or CPU-visible GPU memory)",
            Vk.VkResult.ErrorTooManyObjects => "VK_ERROR_TOO_MANY_OBJECTS (graphics resource limit reached)",
            Vk.VkResult.ErrorDeviceLost => "VK_ERROR_DEVICE_LOST (graphics device lost)",
            Vk.VkResult.ErrorMemoryMapFailed => "VK_ERROR_MEMORY_MAP_FAILED (GPU memory could not be mapped for CPU access)",
            Vk.VkResult.ErrorLayerNotPresent => "VK_ERROR_LAYER_NOT_PRESENT (required Vulkan layer unavailable)",
            Vk.VkResult.ErrorExtensionNotPresent => "VK_ERROR_EXTENSION_NOT_PRESENT (required Vulkan extension unavailable)",
            Vk.VkResult.ErrorFeatureNotPresent => "VK_ERROR_FEATURE_NOT_PRESENT (required graphics feature unavailable)",
            Vk.VkResult.ErrorIncompatibleDriver => "VK_ERROR_INCOMPATIBLE_DRIVER (incompatible graphics driver)",
            Vk.VkResult.ErrorFormatNotSupported => "VK_ERROR_FORMAT_NOT_SUPPORTED (required texture or display format unsupported)",
            Vk.VkResult.ErrorInvalidShaderNV => "VK_ERROR_INVALID_SHADER_NV (the graphics driver rejected the shader)",
            Vk.VkResult.ErrorUnknown => "VK_ERROR_UNKNOWN (unspecified graphics driver error)",
            _ => "graphics driver error",
        };
        return byteCount != 0
            ? $"{operation} failed: {reason} (Vulkan result {(int)result}, {byteCount / (1024.0 * 1024.0):F2} MiB requested)."
            : $"{operation} failed: {reason} (Vulkan result {(int)result}).";
    }

    /// <summary>Reports a Vulkan failure to the diagnostic receiver.</summary>
    /// <param name="result">The failed native result.</param>
    /// <param name="operation">The native operation.</param>
    /// <param name="fatal">Whether execution will terminate.</param>
    /// <param name="byteCount">The requested allocation size, if applicable.</param>
    public static void ReportVkFailure(Vk.VkResult result, string operation, bool fatal, ulong byteCount = 0)
    {
        Gpu.ReportError(VkFailureMessage(result, operation, byteCount), fatal);
    }

    /// <summary>Throws when a Vulkan creation operation fails.</summary>
    /// <param name="result">The native result.</param>
    /// <param name="operation">The native operation.</param>
    /// <param name="byteCount">The requested allocation size, if applicable.</param>
    /// <exception cref="GPUException">The operation failed.</exception>
    public static void CheckVk(Vk.VkResult result, string operation, ulong byteCount = 0)
    {
        if (result == Vk.VkResult.Success) return;
        string message = VkFailureMessage(result, operation, byteCount);
        Gpu.ReportError(message, false);
        throw new GPUException(VkError(result), message);
    }

    public static void AbortVkFailure(Vk.VkResult result, string operation, ulong byteCount = 0)
    {
        ReportVkFailure(result, operation, true, byteCount);
        Debug.Assert(result == Vk.VkResult.Success, "unexpected Vulkan failure");
        Environment.FailFast($"{operation} failed: Vulkan result {(int)result}");
    }

    /// <summary>Reports a failed Vulkan operation and returns its status category.</summary>
    /// <param name="result">The native result.</param>
    /// <param name="operation">The native operation.</param>
    /// <param name="byteCount">The requested allocation size, if applicable.</param>
    /// <returns>The backend-independent status.</returns>
    public static Error ErrorFromVk(Vk.VkResult result, string operation, ulong byteCount = 0)
    {
        if (result != Vk.VkResult.Success) ReportVkFailure(result, operation, false, byteCount);
        return VkError(result);
    }

    /// <summary>Maps a Vulkan result to its status category.</summary>
    /// <param name="result">The native result.</param>
    /// <returns>The backend-independent status.</returns>
    public static Error VkError(Vk.VkResult result) => result switch
    {
        Vk.VkResult.Success => Error.None,
        Vk.VkResult.ErrorOutOfHostMemory or Vk.VkResult.ErrorOutOfDeviceMemory or Vk.VkResult.ErrorTooManyObjects => Error.OutOfMemory,
        Vk.VkResult.ErrorDeviceLost => Error.DeviceLost,
        Vk.VkResult.ErrorLayerNotPresent or Vk.VkResult.ErrorExtensionNotPresent or Vk.VkResult.ErrorFeatureNotPresent
            or Vk.VkResult.ErrorIncompatibleDriver or Vk.VkResult.ErrorFormatNotSupported => Error.Unsupported,
        _ => Error.DriverError,
    };

    public static void RequireVk(Vk.VkResult result, string operation, ulong byteCount = 0)
    {
        if (result != Vk.VkResult.Success)
            AbortVkFailure(result, operation, byteCount);
    }

    public static void AssertVk(Vk.VkResult result)
    {
        Debug.Assert(result == Vk.VkResult.Success, "unexpected Vulkan failure");
    }





    public static void NoteMissingExtension(ReadOnlySpan<Vk.VkExtensionProperties> available, ReadOnlySpan<byte> name, List<string> missing)
    {
        if (!HasName(available, name))
            missing.Add(Encoding.UTF8.GetString(name));
    }

    public static bool HasName(ReadOnlySpan<Vk.VkExtensionProperties> values, ReadOnlySpan<byte> name)
    {
        foreach (ref readonly Vk.VkExtensionProperties value in values)
        {
            fixed (byte* extensionName = value.extensionName)
            {
                nint index = 0;
                while (index < name.Length && extensionName[index] == name[(int)index]) ++index;
                if (index == name.Length && extensionName[index] == 0) return true;
            }
        }
        return false;
    }

    public static string ReadVkString(byte* characters)
    {
        nint length = 0;
        while (characters[length] != 0) ++length;
        return length == 0 ? string.Empty : Encoding.UTF8.GetString(characters, (int)length);
    }

    // Writes a NUL-terminated UTF-8 encoding of value into buffer and returns a pointer to it.
    public static byte* utf8(string value, Span<byte> buffer)
    {
        int written = 0;
        if (value.Length != 0)
        {
            Span<byte> trimmed = buffer[..^1];
            written = Encoding.UTF8.GetBytes(value, trimmed);
        }
        buffer[written] = 0;
        fixed (byte* pinned = buffer)
            return pinned;
    }

    /// <summary>Allocates a null-terminated UTF-8 string in native memory.</summary>
    /// <param name="value">The string to encode.</param>
    /// <returns>The encoded string; release it with NativeMemory.Free.</returns>
    public static byte* AllocateUtf8(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        byte* buffer = (byte*)NativeMemory.Alloc((nuint)byteCount + 1);
        Encoding.UTF8.GetBytes(value, new Span<byte>(buffer, byteCount));
        buffer[byteCount] = 0;
        return buffer;
    }

    public static Vk.VkFormat ToVk(Format format) => format switch
    {
        Format.R8Srgb => Vk.VkFormat.R8Srgb,
        Format.Rg8Srgb => Vk.VkFormat.R8G8Srgb,
        Format.Rgba8Srgb => Vk.VkFormat.R8G8B8A8Srgb,
        Format.Bgra8Srgb => Vk.VkFormat.B8G8R8A8Srgb,
        Format.Rgba4Unorm => Vk.VkFormat.R4G4B4A4UnormPack16,
        Format.R5g5b5a1Unorm => Vk.VkFormat.R5G5B5A1UnormPack16,
        Format.R5g6b5Unorm => Vk.VkFormat.R5G6B5UnormPack16,
        Format.R8Unorm => Vk.VkFormat.R8Unorm,
        Format.Rg8Unorm => Vk.VkFormat.R8G8Unorm,
        Format.Rgba8Unorm => Vk.VkFormat.R8G8B8A8Unorm,
        Format.Bgra8Unorm => Vk.VkFormat.B8G8R8A8Unorm,
        Format.R16Unorm => Vk.VkFormat.R16Unorm,
        Format.Rg16Unorm => Vk.VkFormat.R16G16Unorm,
        Format.Rgba16Unorm => Vk.VkFormat.R16G16B16A16Unorm,
        Format.R8Uint => Vk.VkFormat.R8Uint,
        Format.Rg8Uint => Vk.VkFormat.R8G8Uint,
        Format.Rgba8Uint => Vk.VkFormat.R8G8B8A8Uint,
        Format.Bgra8Uint => Vk.VkFormat.B8G8R8A8Uint,
        Format.R16Uint => Vk.VkFormat.R16Uint,
        Format.Rg16Uint => Vk.VkFormat.R16G16Uint,
        Format.Rgba16Uint => Vk.VkFormat.R16G16B16A16Uint,
        Format.R32Uint => Vk.VkFormat.R32Uint,
        Format.Rg32Uint => Vk.VkFormat.R32G32Uint,
        Format.Rgba32Uint => Vk.VkFormat.R32G32B32A32Uint,
        Format.R16Float => Vk.VkFormat.R16Sfloat,
        Format.Rg16Float => Vk.VkFormat.R16G16Sfloat,
        Format.Rgba16Float => Vk.VkFormat.R16G16B16A16Sfloat,
        Format.R32Float => Vk.VkFormat.R32Sfloat,
        Format.Rg32Float => Vk.VkFormat.R32G32Sfloat,
        Format.Rgba32Float => Vk.VkFormat.R32G32B32A32Sfloat,
        Format.Rgb10a2Unorm => Vk.VkFormat.A2B10G10R10UnormPack32,
        Format.Rg11b10Float => Vk.VkFormat.B10G11R11UfloatPack32,
        Format.D16Unorm => Vk.VkFormat.D16Unorm,
        Format.D24UnormS8Uint => Vk.VkFormat.D24UnormS8Uint,
        Format.D32Float => Vk.VkFormat.D32Sfloat,
        Format.S8Uint => Vk.VkFormat.S8Uint,
        Format.D32FloatS8Uint => Vk.VkFormat.D32SfloatS8Uint,
        Format.EacRg => Vk.VkFormat.EacR11G11UnormBlock,
        Format.Astc4x4Srgb => Vk.VkFormat.Astc4x4SrgbBlock,
        Format.Astc4x4Unorm => Vk.VkFormat.Astc4x4UnormBlock,
        Format.Bc3Srgb => Vk.VkFormat.Bc3SrgbBlock,
        Format.Bc3Unorm => Vk.VkFormat.Bc3UnormBlock,
        Format.Bc5Rg => Vk.VkFormat.Bc5UnormBlock,
        Format.Bc6HUfloat => Vk.VkFormat.Bc6hUfloatBlock,
        Format.Bc6HSfloat => Vk.VkFormat.Bc6hSfloatBlock,
        Format.Bc7Srgb => Vk.VkFormat.Bc7SrgbBlock,
        Format.Bc7Unorm => Vk.VkFormat.Bc7UnormBlock,
        Format.Bc1Srgb => Vk.VkFormat.Bc1RgbaSrgbBlock,
        Format.Bc1Unorm => Vk.VkFormat.Bc1RgbaUnormBlock,
        Format.Bc2Srgb => Vk.VkFormat.Bc2SrgbBlock,
        Format.Bc2Unorm => Vk.VkFormat.Bc2UnormBlock,
        Format.Bc4Unorm => Vk.VkFormat.Bc4UnormBlock,
        Format.Bc4Snorm => Vk.VkFormat.Bc4SnormBlock,
        _ => Vk.VkFormat.Undefined,
    };

    public static bool CompatibleViewFormats(Format imageFormat, Format viewFormat)
    {
        if (imageFormat == viewFormat) return true;
        TextureFormatInfo image = Gpu.GetTextureFormatInfo(imageFormat);
        TextureFormatInfo view = Gpu.GetTextureFormatInfo(viewFormat);
        if (image.Depth || image.Stencil || view.Depth || view.Stencil) return false;
        if (image.BlockExtent.X == 1 && view.BlockExtent.X == 1)
            return image.BytesPerBlock != 0 && image.BytesPerBlock == view.BytesPerBlock;
        return (imageFormat == Format.Astc4x4Unorm && viewFormat == Format.Astc4x4Srgb) ||
               (imageFormat == Format.Astc4x4Srgb && viewFormat == Format.Astc4x4Unorm) ||
               (imageFormat == Format.Bc1Unorm && viewFormat == Format.Bc1Srgb) ||
               (imageFormat == Format.Bc1Srgb && viewFormat == Format.Bc1Unorm) ||
               (imageFormat == Format.Bc2Unorm && viewFormat == Format.Bc2Srgb) ||
               (imageFormat == Format.Bc2Srgb && viewFormat == Format.Bc2Unorm) ||
               (imageFormat == Format.Bc3Unorm && viewFormat == Format.Bc3Srgb) ||
               (imageFormat == Format.Bc3Srgb && viewFormat == Format.Bc3Unorm) ||
               (imageFormat == Format.Bc4Unorm && viewFormat == Format.Bc4Snorm) ||
               (imageFormat == Format.Bc4Snorm && viewFormat == Format.Bc4Unorm) ||
               (imageFormat == Format.Bc6HUfloat && viewFormat == Format.Bc6HSfloat) ||
               (imageFormat == Format.Bc6HSfloat && viewFormat == Format.Bc6HUfloat) ||
               (imageFormat == Format.Bc7Unorm && viewFormat == Format.Bc7Srgb) ||
               (imageFormat == Format.Bc7Srgb && viewFormat == Format.Bc7Unorm);
    }

    public static bool HasDepthAspect(Format format) => Gpu.GetTextureFormatInfo(format).Depth;

    public static bool HasStencilAspect(Format format) => Gpu.GetTextureFormatInfo(format).Stencil;

    public static Vk.VkImageAspectFlags ImageAspects(Format format)
    {
        Vk.VkImageAspectFlags result = 0;
        if (HasDepthAspect(format)) result |= Vk.VkImageAspectFlags.Depth;
        if (HasStencilAspect(format)) result |= Vk.VkImageAspectFlags.Stencil;
        if (result == 0) result = Vk.VkImageAspectFlags.Color;
        return result;
    }

    public static Vk.VkImageType ToVk(TextureType type) => type switch
    {
        TextureType.OneD => Vk.VkImageType.Image1D,
        TextureType.ThreeD => Vk.VkImageType.Image3D,
        _ => Vk.VkImageType.Image2D,
    };

    public static Vk.VkImageViewType ToVkView(TextureType type) => type switch
    {
        TextureType.OneD => Vk.VkImageViewType.Image1D,
        TextureType.ThreeD => Vk.VkImageViewType.Image3D,
        TextureType.Cube => Vk.VkImageViewType.ImageCube,
        TextureType.TwoDArray => Vk.VkImageViewType.Image2DArray,
        TextureType.CubeArray => Vk.VkImageViewType.ImageCubeArray,
        _ => Vk.VkImageViewType.Image2D,
    };

    public static Vk.VkFormatFeatureFlags2 RequiredFormatFeatures(TextureUsage usage)
    {
        Vk.VkFormatFeatureFlags2 result = 0;
        if (HasFlag(usage, TextureUsage.Sampled))
            result |= Vk.VkFormatFeatureFlags2.SampledImage;
        if (HasFlag(usage, TextureUsage.Storage))
            result |= Vk.VkFormatFeatureFlags2.StorageImage |
                      Vk.VkFormatFeatureFlags2.StorageReadWithoutFormat |
                      Vk.VkFormatFeatureFlags2.StorageWriteWithoutFormat;
        if (HasFlag(usage, TextureUsage.ColorAttachment))
            result |= Vk.VkFormatFeatureFlags2.ColorAttachment;
        if (HasFlag(usage, TextureUsage.DepthStencilAttachment))
            result |= Vk.VkFormatFeatureFlags2.DepthStencilAttachment;
        if (HasFlag(usage, TextureUsage.TransferSource))
            result |= Vk.VkFormatFeatureFlags2.TransferSrc;
        if (HasFlag(usage, TextureUsage.TransferDestination))
            result |= Vk.VkFormatFeatureFlags2.TransferDst;
        return result;
    }

    public const TextureUsage KnownTextureUsageBits =
        TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.ColorAttachment |
        TextureUsage.DepthStencilAttachment | TextureUsage.TransferSource | TextureUsage.TransferDestination;

    public static bool HasValidTextureUsageBits(TextureUsage usage)
    {
        uint bits = (uint)usage;
        return bits != 0 && (bits & ~(uint)KnownTextureUsageBits) == 0;
    }

    public static TextureCompression FormatCompression(Format format) => format switch
    {
        Format.Bc1Srgb or Format.Bc1Unorm or Format.Bc2Srgb or Format.Bc2Unorm or Format.Bc3Srgb or Format.Bc3Unorm or
        Format.Bc4Unorm or Format.Bc4Snorm or Format.Bc5Rg or Format.Bc6HUfloat or Format.Bc6HSfloat or
        Format.Bc7Srgb or Format.Bc7Unorm => TextureCompression.Bc,
        Format.Astc4x4Srgb or Format.Astc4x4Unorm => TextureCompression.Astc,
        Format.EacRg => TextureCompression.Etc2,
        _ => TextureCompression.None,
    };

    /// <summary>Converts a graphics primitive topology to Vulkan.</summary>
    /// <param name="topology">The vertex assembly mode.</param>
    /// <returns>The native topology, or an invalid value for an unknown mode.</returns>
    public static Vk.VkPrimitiveTopology ToVk(PrimitiveTopology topology) => topology switch
    {
        PrimitiveTopology.TriangleList => Vk.VkPrimitiveTopology.TriangleList,
        PrimitiveTopology.PointList => Vk.VkPrimitiveTopology.PointList,
        PrimitiveTopology.LineList => Vk.VkPrimitiveTopology.LineList,
        PrimitiveTopology.LineStrip => Vk.VkPrimitiveTopology.LineStrip,
        PrimitiveTopology.TriangleStrip => Vk.VkPrimitiveTopology.TriangleStrip,
        _ => (Vk.VkPrimitiveTopology)0x7fffffff,
    };

    public static Vk.VkBlendFactor ToVk(BlendFactor factor) => factor switch
    {
        BlendFactor.Zero => Vk.VkBlendFactor.Zero,
        BlendFactor.One => Vk.VkBlendFactor.One,
        BlendFactor.SourceColor => Vk.VkBlendFactor.SrcColor,
        BlendFactor.OneMinusSourceColor => Vk.VkBlendFactor.OneMinusSrcColor,
        BlendFactor.DestinationColor => Vk.VkBlendFactor.DstColor,
        BlendFactor.OneMinusDestinationColor => Vk.VkBlendFactor.OneMinusDstColor,
        BlendFactor.SourceAlpha => Vk.VkBlendFactor.SrcAlpha,
        BlendFactor.OneMinusSourceAlpha => Vk.VkBlendFactor.OneMinusSrcAlpha,
        BlendFactor.DestinationAlpha => Vk.VkBlendFactor.DstAlpha,
        BlendFactor.OneMinusDestinationAlpha => Vk.VkBlendFactor.OneMinusDstAlpha,
        BlendFactor.SourceAlphaSaturate => Vk.VkBlendFactor.SrcAlphaSaturate,
        _ => (Vk.VkBlendFactor)0x7fffffff,
    };

    public static Vk.VkPipelineStageFlags2 ToVk(Stage stages)
    {
        Vk.VkPipelineStageFlags2 result = 0;
        if (HasFlag(stages, Stage.Indirect)) result |= Vk.VkPipelineStageFlags2.DrawIndirect;
        if (HasFlag(stages, Stage.IndexInput)) result |= Vk.VkPipelineStageFlags2.IndexInput;
        if (HasFlag(stages, Stage.Vertex)) result |= Vk.VkPipelineStageFlags2.VertexShader;
        if (HasFlag(stages, Stage.Task)) result |= Vk.VkPipelineStageFlags2.TaskShaderEXT;
        if (HasFlag(stages, Stage.Mesh)) result |= Vk.VkPipelineStageFlags2.MeshShaderEXT;
        if (HasFlag(stages, Stage.DepthStencilTests)) result |= Vk.VkPipelineStageFlags2.EarlyFragmentTests | Vk.VkPipelineStageFlags2.LateFragmentTests;
        if (HasFlag(stages, Stage.Pixel)) result |= Vk.VkPipelineStageFlags2.FragmentShader;
        if (HasFlag(stages, Stage.ColorOutput)) result |= Vk.VkPipelineStageFlags2.ColorAttachmentOutput;
        if (HasFlag(stages, Stage.Compute)) result |= Vk.VkPipelineStageFlags2.ComputeShader;
        if (HasFlag(stages, Stage.Transfer)) result |= Vk.VkPipelineStageFlags2.Copy;
        if (HasFlag(stages, Stage.Host)) result |= Vk.VkPipelineStageFlags2.Host;
        if (HasFlag(stages, Stage.AllCommands)) result |= Vk.VkPipelineStageFlags2.AllCommands;
        return result;
    }

    public static Vk.VkAccessFlags2 ToVk(Access accesses, Vk.VkAccessFlags2 descriptorReadAccess)
    {
        Vk.VkAccessFlags2 result = 0;
        if (HasFlag(accesses, Access.TransferRead)) result |= Vk.VkAccessFlags2.TransferRead;
        if (HasFlag(accesses, Access.TransferWrite)) result |= Vk.VkAccessFlags2.TransferWrite;
        if (HasFlag(accesses, Access.ShaderRead))
            result |= Vk.VkAccessFlags2.ShaderStorageRead | Vk.VkAccessFlags2.ShaderSampledRead | Vk.VkAccessFlags2.UniformRead;
        if (HasFlag(accesses, Access.ShaderWrite)) result |= Vk.VkAccessFlags2.ShaderStorageWrite;
        if (HasFlag(accesses, Access.ColorRead)) result |= Vk.VkAccessFlags2.ColorAttachmentRead;
        if (HasFlag(accesses, Access.ColorWrite)) result |= Vk.VkAccessFlags2.ColorAttachmentWrite;
        if (HasFlag(accesses, Access.DepthStencilRead)) result |= Vk.VkAccessFlags2.DepthStencilAttachmentRead;
        if (HasFlag(accesses, Access.DepthStencilWrite)) result |= Vk.VkAccessFlags2.DepthStencilAttachmentWrite;
        if (HasFlag(accesses, Access.IndirectRead)) result |= Vk.VkAccessFlags2.IndirectCommandRead;
        if (HasFlag(accesses, Access.IndexRead)) result |= Vk.VkAccessFlags2.IndexRead;
        if (HasFlag(accesses, Access.HostRead)) result |= Vk.VkAccessFlags2.HostRead;
        if (HasFlag(accesses, Access.DescriptorRead)) result |= descriptorReadAccess;
        return result;
    }

    public const Vk.VkBufferUsageFlags UniversalBufferUsage =
        Vk.VkBufferUsageFlags.ShaderDeviceAddress | Vk.VkBufferUsageFlags.StorageBuffer |
        Vk.VkBufferUsageFlags.IndexBuffer | Vk.VkBufferUsageFlags.IndirectBuffer |
        Vk.VkBufferUsageFlags.TransferSrc | Vk.VkBufferUsageFlags.TransferDst;

    public const Vk.VkMemoryPropertyFlags CpuVisibleMemoryProperties =
        Vk.VkMemoryPropertyFlags.DeviceLocal |
        Vk.VkMemoryPropertyFlags.HostVisible |
        Vk.VkMemoryPropertyFlags.HostCoherent;

    public const Vk.VkMemoryPropertyFlags ForbiddenMemoryProperties =
        Vk.VkMemoryPropertyFlags.LazilyAllocated |
        Vk.VkMemoryPropertyFlags.Protected |
        Vk.VkMemoryPropertyFlags.DeviceCoherentAMD |
        Vk.VkMemoryPropertyFlags.DeviceUncachedAMD;

    public static bool IsUsableMemoryType(in Vk.VkPhysicalDeviceMemoryProperties properties, uint index)
    {
        Vk.VkMemoryType type = properties.memoryTypes[(int)index];
        if ((type.propertyFlags & ForbiddenMemoryProperties) != 0)
            return false;
        return (properties.memoryHeaps[(int)type.heapIndex].flags & Vk.VkMemoryHeapFlags.TileMemoryQCOM) == 0;
    }

    public const Vk.VkAddressCommandFlagsKHR AddressFlags = Vk.VkAddressCommandFlagsKHR.FullyBound | Vk.VkAddressCommandFlagsKHR.StorageBufferUsage;

}
