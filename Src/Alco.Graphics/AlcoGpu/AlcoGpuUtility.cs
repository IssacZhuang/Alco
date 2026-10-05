using System.Runtime.CompilerServices;
using System.Text;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>
/// Conversions between Alco graphics enums/descriptors and the alco-gpu ABI.
/// The ABI passes Alco enum values through unchanged, so most conversions are
/// direct casts; only data layouts and shader-module staging differ.
/// </summary>
internal static unsafe class AlcoGpuUtility
{
    /// <summary>
    /// Converts a managed string into a NUL-terminated UTF-8 byte span valid
    /// for pinning and passing as an ABI label/entry-point pointer.
    /// </summary>
    public static ReadOnlySpan<byte> Utf8Z(this string? source)
    {
        if (source is null)
        {
            return default;
        }

        byte[] bytes = new byte[Encoding.UTF8.GetMaxByteCount(source.Length) + 1];
        int length = Encoding.UTF8.GetBytes(source, bytes);
        return bytes.AsSpan(0, length + 1);
    }

    /// <summary>Computes the tight copy layout for one mip of an uncompressed texture.</summary>
    public static AlcoCopyLayout GetTextureDataLayout(PixelFormat pixelFormat, uint width, uint height)
    {
        // Uncompressed formats.
        if (PixelFormatUtility.TryGetPixelSize(pixelFormat, out uint pixelSize))
        {
            return new AlcoCopyLayout
            {
                Offset = 0,
                BytesPerRow = width * pixelSize,
                RowsPerImage = height,
            };
        }

        // Compressed formats: BC/ETC2/ASTC use 4x4 texel blocks; bytesPerRow covers
        // one block row while rowsPerImage stays in texel rows (multiple of block height).
        if (PixelFormatUtility.TryGetCompressedBlockSize(pixelFormat, out uint blockSize))
        {
            uint blocksPerRow = (width + 3) / 4;
            uint blockRowsPerImage = (height + 3) / 4;
            return new AlcoCopyLayout
            {
                Offset = 0,
                BytesPerRow = blocksPerRow * blockSize,
                RowsPerImage = blockRowsPerImage * 4,
            };
        }

        throw new NotImplementedException($"Format {pixelFormat} is not supported yet");
    }

    /// <summary>
    /// Creates a native shader module from a slang-produced module. The returned
    /// handle is owned by the caller and must be destroyed after pipeline creation.
    /// </summary>
    public static AlcoHandle CreateShaderModule(this AlcoGpuDevice device, in ShaderModule source)
    {
        if (source.Language is not (ShaderLanguage.SPIRV or ShaderLanguage.WGSL or ShaderLanguage.DXIL
            or ShaderLanguage.MSL or ShaderLanguage.MetalLib))
        {
            throw new GraphicsException(
                $"Unsupported shader language {source.Language}, only SPIRV, DXIL, MSL, MetalLib and WGSL are supported.");
        }

        if (source.Language == ShaderLanguage.SPIRV && (source.Source.Length & 3) != 0)
        {
            throw new GraphicsException("SPIR-V shader bytecode length must be a multiple of four bytes.");
        }

        // DXIL/MSL/MetalLib have no translation fallback; fail with the actionable
        // reason before crossing the ABI when passthrough is unavailable.
        if (source.Language is ShaderLanguage.DXIL or ShaderLanguage.MSL or ShaderLanguage.MetalLib
            && !device.ShaderPassthroughEnabled)
        {
            throw new GraphicsException(
                $"{source.Language} shaders require the PassthroughShaders capability, which the active device does not expose.");
        }

        ReadOnlySpan<byte> code = source.Source.Span;
        ReadOnlySpan<byte> entry = source.EntryPoint.Utf8Z();
        fixed (byte* ptrCode = code)
        fixed (byte* ptrEntry = entry)
        {
            AlcoShaderModuleDesc desc = new()
            {
                Language = (uint)source.Language,
                Data = ptrCode,
                Size = (uint)code.Length,
                EntryPoint = ptrEntry,
                WorkgroupX = source.WorkgroupSize.X,
                WorkgroupY = source.WorkgroupSize.Y,
                WorkgroupZ = source.WorkgroupSize.Z,
            };

            AlcoGpuNative.ShaderModuleCreate(device.Native, in desc, out AlcoHandle module);
            return module;
        }
    }

    /// <summary>Destroys a shader module created through <see cref="CreateShaderModule"/>.</summary>
    public static void DestroyShaderModule(this AlcoGpuDevice device, AlcoHandle module)
    {
        if (module.IsNull)
        {
            return;
        }

        AlcoGpuNative.ShaderModuleDestroy(device.Native, module);
    }

    /// <summary>Allocates and fills native bind-group-layout entries from managed descriptors.</summary>
    /// <returns>Native array pointer; caller frees with <see cref="InteropUtility.Free"/>.</returns>
    public static AlcoBindGroupLayoutEntry* AllocBindGroupLayoutEntries(ReadOnlySpan<BindGroupEntry> bindings)
    {
        AlcoBindGroupLayoutEntry* entries = InteropUtility.Alloc<AlcoBindGroupLayoutEntry>(bindings.Length);
        for (int i = 0; i < bindings.Length; i++)
        {
            entries[i] = ConvertEntry(bindings[i]);
        }

        return entries;
    }

    private static AlcoBindGroupLayoutEntry ConvertEntry(BindGroupEntry binding)
    {
        AlcoBindGroupLayoutEntry entry = new()
        {
            Binding = binding.Binding,
            Visibility = (uint)binding.Stage,
            Type = (uint)binding.Type,
        };

        switch (binding.Type)
        {
            case BindingType.Texture:
                entry.TextureSampleType = (uint)binding.TextureInfo.SampleType;
                entry.ViewDimension = (uint)binding.TextureInfo.ViewDimension;
                break;
            case BindingType.StorageTexture:
                entry.StorageAccess = (uint)binding.StorageTextureInfo.Access;
                entry.StorageFormat = (uint)binding.StorageTextureInfo.Format;
                entry.ViewDimension = (uint)binding.StorageTextureInfo.ViewDimension;
                break;
            case BindingType.SamplerComparison:
                entry.SamplerKind = 2;
                break;
        }

        return entry;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LoadOpToAbi(AttachmentLoadOp loadOp)
    {
        return loadOp switch
        {
            AttachmentLoadOp.Load => 0,
            AttachmentLoadOp.Clear => 1,
            _ => 0,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint StoreOpToAbi(AttachmentStoreOp storeOp)
    {
        return storeOp switch
        {
            AttachmentStoreOp.Store => 0,
            AttachmentStoreOp.Discard => 1,
            _ => 0,
        };
    }
}
