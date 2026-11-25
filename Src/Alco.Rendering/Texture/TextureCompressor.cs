using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Buffer = Alco.Graphics.Buffer;

namespace Alco.Rendering;

/// <summary>Selects how source texels are converted before sRGB block compression.</summary>
public enum CompressionInputColorSpace : byte
{
    /// <summary>
    /// Preserves raw byte codes for UNORM and MutableFormat sRGB textures, and converts linear/float or non-mutable sRGB sources to sRGB.
    /// </summary>
    Auto,

    /// <summary>Treats sampled source texels as already encoded in the target storage color space.</summary>
    EncodedCodeDomain,

    /// <summary>Converts sampled linear RGB texels to sRGB before encoding an sRGB BC format.</summary>
    Linear,
}

/// <summary>GPU compute block compressor supporting BC1-BC5 and BC7 formats across all bindless rendering backends.</summary>
public sealed unsafe class TextureCompressor
{
    private enum EncoderSlot : byte
    {
        Bc1 = 0,
        Bc2 = 1,
        Bc3 = 2,
        Bc4 = 3,
        Bc4Snorm = 4,
        Bc5 = 5,
        Bc7 = 6,
    }

    private const int EncoderSlotCount = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureCompressionRoot
    {
        public GPUBufferView Output;
        public uint Source;
        public uint Mip;
        public Extent2D Extent;
    }

    private readonly RenderingSystem _rendering;
    private readonly ShaderSystem _shaderSystem;
    private readonly Shader?[] _shaders = new Shader?[EncoderSlotCount];
    private readonly Material?[,] _materials = new Material?[EncoderSlotCount, 2];
    private readonly Lock _cacheLock = new();
    private bool _librariesRegistered;

    /// <summary>Initializes a GPU block compressor backed by <paramref name="rendering"/>.</summary>
    /// <param name="rendering">The rendering system owning descriptor heaps and command submission.</param>
    /// <param name="shaders">Optional override shader system; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    public TextureCompressor(RenderingSystem rendering, ShaderSystem? shaders = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        _rendering = rendering;
        _shaderSystem = shaders ?? rendering.Shaders;
    }

    /// <summary>Returns whether <paramref name="format"/> is supported by the GPU block encoders.</summary>
    /// <param name="format">The target compressed texture format.</param>
    /// <returns><c>true</c> for BC1-BC5 and BC7 formats; otherwise <c>false</c>.</returns>
    public static bool IsSupportedFormat(Format format) => TryGetSlot(format, out _);

    /// <summary>Returns whether <paramref name="rendering"/> supports both compute BC compression and textures in <paramref name="format"/>.</summary>
    /// <param name="rendering">The target rendering system.</param>
    /// <param name="format">The compressed texture format.</param>
    /// <returns><c>true</c> if the device can encode and upload <paramref name="format"/> textures.</returns>
    public static bool CanCompressOnGpu(RenderingSystem rendering, Format format)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        return rendering.Caps.TextureCompressionBc &&
               IsSupportedFormat(format) &&
               rendering.SupportsTextureFormat(format, TextureUsage.Sampled | TextureUsage.TransferDestination);
    }

    /// <summary>Returns the minimum 4x4 block-aligned extent required when allocating a GPU BC texture for <paramref name="extent"/>.</summary>
    /// <param name="extent">The logical texel extent; both dimensions must be positive.</param>
    /// <returns>The padded extent rounded up to multiples of 4 texels.</returns>
    public static Extent2D GetPaddedExtent(Extent2D extent)
    {
        if (extent.X == 0 || extent.Y == 0)
            throw new ArgumentOutOfRangeException(nameof(extent), extent, "Texture extent must be positive.");
        return new Extent2D((extent.X + 3u) & ~3u, (extent.Y + 3u) & ~3u);
    }

    /// <summary>Computes the tightly packed compressed byte size for one 2D mip level of <paramref name="extent"/>.</summary>
    /// <param name="format">The compressed BC format.</param>
    /// <param name="extent">The logical mip extent; both dimensions must be positive.</param>
    /// <returns>The exact compressed payload size in bytes.</returns>
    public static ulong GetCompressedSizeBytes(Format format, Extent2D extent)
    {
        if (!IsSupportedFormat(format))
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported block-compressed texture format.");
        if (extent.X == 0 || extent.Y == 0)
            throw new ArgumentOutOfRangeException(nameof(extent), extent, "Texture extent must be positive.");

        TextureFormatInfo info = Texture.GetFormatInfo(format);
        uint blocksX = (extent.X + 3u) / 4u;
        uint blocksY = (extent.Y + 3u) / 4u;
        return (ulong)blocksX * blocksY * info.BytesPerBlock;
    }

    /// <summary>Computes the tightly packed byte size across a contiguous range of 2D mip levels.</summary>
    /// <param name="format">The compressed BC format.</param>
    /// <param name="mipZeroExtent">The level-0 logical extent.</param>
    /// <param name="baseMip">The zero-based starting mip level.</param>
    /// <param name="mipLevels">The number of mip levels starting at <paramref name="baseMip"/>.</param>
    /// <returns>The summed compressed byte count for all selected mip levels.</returns>
    public static ulong GetTotalCompressedSizeBytes(Format format, Extent2D mipZeroExtent, uint baseMip, uint mipLevels)
    {
        if (mipLevels == 0)
            throw new ArgumentOutOfRangeException(nameof(mipLevels), mipLevels, "Mip level count must be positive.");

        ulong total = 0;
        for (uint index = 0; index < mipLevels; ++index)
        {
            uint mip = baseMip + index;
            Extent2D mipExtent = GetMipExtent(mipZeroExtent, mip);
            total += GetCompressedSizeBytes(format, mipExtent);
        }
        return total;
    }

    /// <summary>Pre-compiles and caches the shader variant for <paramref name="format"/>.</summary>
    /// <param name="format">The target compressed BC format.</param>
    /// <param name="convertLinearToSrgb">Whether to convert sampled linear RGB values to sRGB in the compute pass.</param>
    public void WarmUp(Format format, bool convertLinearToSrgb = false)
    {
        if (!TryGetSlot(format, out EncoderSlot slot))
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported block-compressed texture format.");
        GetOrCreateMaterial(slot, convertLinearToSrgb);
    }

    /// <summary>Records one 2D mip block-compression dispatch into <paramref name="compute"/> without heap allocations once warmed.</summary>
    /// <param name="context">The active recording render context.</param>
    /// <param name="compute">The open compute scope on <paramref name="context"/>.</param>
    /// <param name="destinationFormat">The target BC format.</param>
    /// <param name="sourceDescriptorSlot">The bindless sampled texture descriptor slot to read from.</param>
    /// <param name="sourceMip">The zero-based mip level relative to <paramref name="sourceDescriptorSlot"/>.</param>
    /// <param name="extent">The logical extent of the compressed mip.</param>
    /// <param name="outputSlice">The output buffer slice sized to at least <see cref="GetCompressedSizeBytes"/>.</param>
    /// <param name="convertLinearToSrgb">Whether to apply linear-to-sRGB conversion before encoding.</param>
    public void Record(
        RenderContext context,
        RenderContext.ComputeScope compute,
        Format destinationFormat,
        uint sourceDescriptorSlot,
        uint sourceMip,
        Extent2D extent,
        BufferSlice outputSlice,
        bool convertLinearToSrgb = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryGetSlot(destinationFormat, out EncoderSlot slot))
            throw new ArgumentOutOfRangeException(nameof(destinationFormat), destinationFormat, "Unsupported block-compressed texture format.");
        if (sourceDescriptorSlot == Texture.NoIndex)
            throw new ArgumentOutOfRangeException(nameof(sourceDescriptorSlot), sourceDescriptorSlot, "Source descriptor slot is invalid.");
        GPUBufferView outputView = outputSlice.View;
        if (outputView.Index == 0)
            throw new ArgumentException("Output slice must reference a shader-visible GPU buffer.", nameof(outputSlice));

        ulong requiredBytes = GetCompressedSizeBytes(destinationFormat, extent);
        if (outputSlice.SizeBytes < requiredBytes)
            throw new ArgumentException(
                $"Output buffer slice ({outputSlice.SizeBytes} bytes) is smaller than required ({requiredBytes} bytes).",
                nameof(outputSlice));

        Material material = GetOrCreateMaterial(slot, convertLinearToSrgb);
        TextureCompressionRoot root = new()
        {
            Output = outputView,
            Source = sourceDescriptorSlot,
            Mip = sourceMip,
            Extent = extent,
        };
        Extent3D groupCount = new((extent.X + 31u) / 32u, (extent.Y + 31u) / 32u, 1u);
        compute.Dispatch(material, in root, groupCount);
    }

    /// <summary>Records one 2D mip block-compression dispatch reading from <paramref name="source"/>.</summary>
    /// <param name="context">The active recording render context.</param>
    /// <param name="compute">The open compute scope on <paramref name="context"/>.</param>
    /// <param name="destinationFormat">The target BC format.</param>
    /// <param name="source">The sampled source texture belonging to this compressor's rendering system.</param>
    /// <param name="sourceMip">The zero-based mip level to compress.</param>
    /// <param name="outputSlice">The destination GPU buffer slice.</param>
    /// <param name="inputColorSpace">Controls sRGB code preservation versus linear-to-sRGB conversion.</param>
    public void Record(
        RenderContext context,
        RenderContext.ComputeScope compute,
        Format destinationFormat,
        Texture source,
        uint sourceMip,
        BufferSlice outputSlice,
        CompressionInputColorSpace inputColorSpace = CompressionInputColorSpace.Auto)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (sourceMip >= source.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(sourceMip), sourceMip, "Mip level exceeds source texture mip count.");

        ResolveSourceInput(source, destinationFormat, inputColorSpace, out uint descriptorSlot, out bool convertLinearToSrgb);
        Extent2D mipExtent = GetMipExtent(new Extent2D(source.Extent.X, source.Extent.Y), sourceMip);
        Record(context, compute, destinationFormat, descriptorSlot, sourceMip, mipExtent, outputSlice, convertLinearToSrgb);
    }

    /// <summary>Synchronously compresses <paramref name="source"/> on the GPU into a newly allocated BC texture.</summary>
    /// <param name="source">The sampled 2D source texture belonging to this compressor's rendering system.</param>
    /// <param name="targetFormat">The target BC format.</param>
    /// <param name="includeMips">Whether to compress the full mip chain starting at mip zero (requires 4x4-aligned mip zero extent).</param>
    /// <param name="inputColorSpace">Controls sRGB code preservation versus linear-to-sRGB conversion.</param>
    /// <param name="mutableFormat">Whether to enable <see cref="TextureDesc.MutableFormat"/> on the destination texture.</param>
    /// <returns>The newly created BC-compressed texture; caller owns its lifetime.</returns>
    public Texture CompressToTexture(
        Texture source,
        Format targetFormat,
        bool includeMips = false,
        CompressionInputColorSpace inputColorSpace = CompressionInputColorSpace.Auto,
        bool mutableFormat = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsSupportedFormat(targetFormat))
            throw new ArgumentOutOfRangeException(nameof(targetFormat), targetFormat, "Unsupported block-compressed texture format.");
        if (includeMips && source.MipLevels > 1 && ((source.Extent.X & 3u) != 0 || (source.Extent.Y & 3u) != 0))
            throw new ArgumentException(
                "Multi-mip GPU BC texture compression requires mip-zero dimensions to be multiples of 4. " +
                "Use CompressMipChainToBytes for non-aligned multi-mip export.",
                nameof(source));

        uint mipCount = includeMips ? source.MipLevels : 1u;
        Extent2D logicalExtent = new(source.Extent.X, source.Extent.Y);
        Extent2D paddedExtent = GetPaddedExtent(logicalExtent);
        ulong totalBytes = GetTotalCompressedSizeBytes(targetFormat, logicalExtent, 0, mipCount);

        Texture? destination = null;
        Buffer? scratch = null;
        RenderContext? context = null;
        try
        {
            destination = _rendering.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(paddedExtent.X, paddedExtent.Y, 1u),
                MipLevels = mipCount,
                Format = targetFormat,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
                MutableFormat = mutableFormat,
            });
            scratch = _rendering.CreateBuffer(totalBytes, MemoryType.GpuOnly);
            context = new RenderContext(_rendering, 256);

            ResolveSourceInput(source, targetFormat, inputColorSpace, out uint descriptorSlot, out bool convertLinearToSrgb);
            context.BeginFrame();
            ulong byteOffset = 0;
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                for (uint mip = 0; mip < mipCount; ++mip)
                {
                    Extent2D mipExtent = GetMipExtent(logicalExtent, mip);
                    ulong mipBytes = GetCompressedSizeBytes(targetFormat, mipExtent);
                    Record(context, compute, targetFormat, descriptorSlot, mip, mipExtent,
                        scratch.Slice(byteOffset, mipBytes), convertLinearToSrgb);
                    byteOffset += mipBytes;
                }
            }

            context.Barrier(
                Stage.Compute,
                Access.ShaderWrite,
                Stage.Transfer,
                Access.TransferRead | Access.TransferWrite);
            byteOffset = 0;
            for (uint mip = 0; mip < mipCount; ++mip)
            {
                Extent2D mipExtent = GetMipExtent(logicalExtent, mip);
                ulong mipBytes = GetCompressedSizeBytes(targetFormat, mipExtent);
                context.CopyBufferToTexture(
                    scratch.Slice(byteOffset, mipBytes),
                    destination,
                    new TextureCopyDesc { MipLevel = mip });
                byteOffset += mipBytes;
            }
            context.Barrier(
                Stage.Transfer,
                Access.TransferWrite,
                Stage.AllCommands,
                Access.ShaderRead | Access.TransferRead | Access.TransferWrite);

            context.Submit();
            _rendering.WaitIdle();
            Texture result = destination;
            destination = null;
            return result;
        }
        finally
        {
            context?.DiscardFrame();
            context?.Dispose();
            scratch?.Dispose();
            destination?.Dispose();
        }
    }

    /// <summary>Synchronously compresses one or more 2D mip levels of <paramref name="source"/> into tightly packed CPU block bytes.</summary>
    /// <param name="source">The sampled 2D source texture belonging to this compressor's rendering system.</param>
    /// <param name="targetFormat">The target BC format.</param>
    /// <param name="baseMip">The zero-based starting mip level.</param>
    /// <param name="mipLevels">The mip level count to compress, or zero for all remaining levels from <paramref name="baseMip"/>.</param>
    /// <param name="inputColorSpace">Controls sRGB code preservation versus linear-to-sRGB conversion.</param>
    /// <returns>Tightly packed BC block bytes across the selected mip range.</returns>
    public byte[] CompressMipChainToBytes(
        Texture source,
        Format targetFormat,
        uint baseMip = 0,
        uint mipLevels = 0,
        CompressionInputColorSpace inputColorSpace = CompressionInputColorSpace.Auto)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsSupportedFormat(targetFormat))
            throw new ArgumentOutOfRangeException(nameof(targetFormat), targetFormat, "Unsupported block-compressed texture format.");
        if (baseMip >= source.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(baseMip), baseMip, "Base mip exceeds source texture mip count.");

        uint count = mipLevels == 0 ? source.MipLevels - baseMip : mipLevels;
        if (count > source.MipLevels - baseMip)
            throw new ArgumentOutOfRangeException(nameof(mipLevels), mipLevels, "Requested mip range exceeds source texture mip count.");

        Extent2D baseExtent = new(source.Extent.X, source.Extent.Y);
        ulong totalBytes = GetTotalCompressedSizeBytes(targetFormat, baseExtent, baseMip, count);
        byte[] output = new byte[checked((int)totalBytes)];

        Buffer? scratch = null;
        RenderContext? context = null;
        try
        {
            scratch = _rendering.CreateBuffer(totalBytes, MemoryType.GpuOnly);
            context = new RenderContext(_rendering, 256);

            ResolveSourceInput(source, targetFormat, inputColorSpace, out uint descriptorSlot, out bool convertLinearToSrgb);
            context.BeginFrame();
            ulong byteOffset = 0;
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                for (uint index = 0; index < count; ++index)
                {
                    uint mip = baseMip + index;
                    Extent2D mipExtent = GetMipExtent(baseExtent, mip);
                    ulong mipBytes = GetCompressedSizeBytes(targetFormat, mipExtent);
                    Record(context, compute, targetFormat, descriptorSlot, mip, mipExtent,
                        scratch.Slice(byteOffset, mipBytes), convertLinearToSrgb);
                    byteOffset += mipBytes;
                }
            }

            context.Submit();
            _rendering.WaitIdle();
            _rendering.ReadBuffer(scratch, output);
            return output;
        }
        finally
        {
            context?.DiscardFrame();
            context?.Dispose();
            scratch?.Dispose();
        }
    }

    private void ResolveSourceInput(
        Texture source,
        Format destinationFormat,
        CompressionInputColorSpace inputColorSpace,
        out uint descriptorSlot,
        out bool convertLinearToSrgb)
    {
        if (!TextureUtility.IsSrgbFormat(destinationFormat))
        {
            descriptorSlot = source.SampledIndex;
            if (descriptorSlot == Texture.NoIndex)
                throw new InvalidOperationException("Source texture must be created with TextureUsage.Sampled.");
            convertLinearToSrgb = false;
            return;
        }

        if (source.Format == Format.Rgba8Srgb)
        {
            if (inputColorSpace == CompressionInputColorSpace.Linear)
            {
                descriptorSlot = source.SampledIndex;
                convertLinearToSrgb = true;
            }
            else if (source.Desc.MutableFormat)
            {
                descriptorSlot = source.GetSampledIndex(Format.Rgba8Unorm);
                convertLinearToSrgb = false;
            }
            else
            {
                descriptorSlot = source.SampledIndex;
                convertLinearToSrgb = true;
            }
            return;
        }

        descriptorSlot = source.SampledIndex;
        if (descriptorSlot == Texture.NoIndex)
            throw new InvalidOperationException("Source texture must be created with TextureUsage.Sampled.");

        if (inputColorSpace == CompressionInputColorSpace.Linear)
        {
            convertLinearToSrgb = true;
            return;
        }

        if (inputColorSpace == CompressionInputColorSpace.EncodedCodeDomain)
        {
            convertLinearToSrgb = false;
            return;
        }

        convertLinearToSrgb = source.Format is Format.Rgba16Float or Format.Rgba32Float or Format.Rg11b10Float or Format.Rgb10a2Unorm;
    }

    private Material GetOrCreateMaterial(EncoderSlot slot, bool convertLinearToSrgb)
    {
        int srgbFlag = convertLinearToSrgb ? 1 : 0;
        Material? cached = Volatile.Read(ref _materials[(int)slot, srgbFlag]);
        if (cached != null)
            return cached;

        lock (_cacheLock)
        {
            cached = _materials[(int)slot, srgbFlag];
            if (cached != null)
                return cached;

            EnsureLibrariesRegisteredLocked();
            Shader? shader = _shaders[(int)slot];
            if (shader == null)
            {
                string moduleName = GetModuleName(slot);
                string source = LoadEmbeddedShaderSource(moduleName + ".slang");
                shader = _shaderSystem.GetShaderFromSource(moduleName, moduleName + ".slang", source, ShaderKind.Compute, moduleName);
                _shaders[(int)slot] = shader;
            }

            Material material = shader.CreateMaterial($"{GetModuleName(slot)}_{srgbFlag}", srgbFlag);
            Volatile.Write(ref _materials[(int)slot, srgbFlag], material);
            return material;
        }
    }

    private void EnsureLibrariesRegisteredLocked()
    {
        if (_librariesRegistered)
            return;

        RegisterLibraryModule("AlcoRendering_TextureCompression");
        RegisterLibraryModule("AlcoRendering_TextureCompressionLegacy");
        RegisterLibraryModule("AlcoRendering_TextureCompressionBc7");
        _librariesRegistered = true;
    }

    private void RegisterLibraryModule(string moduleName)
    {
        string fileName = moduleName + ".slang";
        _shaderSystem.Compiler.RegisterVirtualModule(moduleName, fileName, LoadEmbeddedShaderSource(fileName));
    }

    private static string LoadEmbeddedShaderSource(string logicalName)
    {
        Assembly assembly = typeof(TextureCompressor).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded compression shader '{logicalName}' was not found in {assembly.FullName}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static Extent2D GetMipExtent(Extent2D mipZero, uint mip)
    {
        int shift = (int)Math.Min(mip, 31u);
        return new Extent2D(Math.Max(mipZero.X >> shift, 1u), Math.Max(mipZero.Y >> shift, 1u));
    }

    private static bool TryGetSlot(Format format, out EncoderSlot slot)
    {
        switch (format)
        {
            case Format.Bc1Unorm:
            case Format.Bc1Srgb:
                slot = EncoderSlot.Bc1;
                return true;
            case Format.Bc2Unorm:
            case Format.Bc2Srgb:
                slot = EncoderSlot.Bc2;
                return true;
            case Format.Bc3Unorm:
            case Format.Bc3Srgb:
                slot = EncoderSlot.Bc3;
                return true;
            case Format.Bc4Unorm:
                slot = EncoderSlot.Bc4;
                return true;
            case Format.Bc4Snorm:
                slot = EncoderSlot.Bc4Snorm;
                return true;
            case Format.Bc5Rg:
                slot = EncoderSlot.Bc5;
                return true;
            case Format.Bc7Unorm:
            case Format.Bc7Srgb:
                slot = EncoderSlot.Bc7;
                return true;
            default:
                slot = default;
                return false;
        }
    }

    private static string GetModuleName(EncoderSlot slot) => slot switch
    {
        EncoderSlot.Bc1 => "TextureCompressBc1",
        EncoderSlot.Bc2 => "TextureCompressBc2",
        EncoderSlot.Bc3 => "TextureCompressBc3",
        EncoderSlot.Bc4 => "TextureCompressBc4",
        EncoderSlot.Bc4Snorm => "TextureCompressBc4Snorm",
        EncoderSlot.Bc5 => "TextureCompressBc5",
        EncoderSlot.Bc7 => "TextureCompressBc7",
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };
}
