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
    public static AlcoGPU.CopyLayout GetTextureDataLayout(PixelFormat pixelFormat, uint width, uint height)
    {
        // Uncompressed formats.
        if (PixelFormatUtility.TryGetPixelSize(pixelFormat, out uint pixelSize))
        {
            return new AlcoGPU.CopyLayout
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
            return new AlcoGPU.CopyLayout
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
    public static AlcoGPU.ShaderModuleHandle CreateShaderModule(this AlcoGpuDevice device, in ShaderModule source)
    {
        try
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
                AlcoGPU.ShaderModuleDesc desc = new()
                {
                    Language = source.Language,
                    Data = ptrCode,
                    Size = (uint)code.Length,
                    EntryPoint = ptrEntry,
                    WorkgroupX = source.WorkgroupSize.X,
                    WorkgroupY = source.WorkgroupSize.Y,
                    WorkgroupZ = source.WorkgroupSize.Z,
                    Flags = source.SpirvAdjustedCoordinates
                        ? AlcoGPU.ShaderModuleFlags.SpirvAdjustedCoordinates
                        : 0,
                };

                AlcoGpuNative.ShaderModuleCreate(device.Native, in desc, out AlcoGPU.ShaderModuleHandle module);
                return module;
            }
        }
        finally
        {
            GC.KeepAlive(device);
        }
    }

    /// <summary>Destroys a shader module created through <see cref="CreateShaderModule"/>.</summary>
    public static void DestroyShaderModule(this AlcoGpuDevice device, AlcoGPU.ShaderModuleHandle module)
    {
        try
        {
            if (module.IsNull)
            {
                return;
            }

            AlcoGpuNative.ShaderModuleDestroy(module);
        }
        finally
        {
            GC.KeepAlive(device);
        }
    }

    /// <summary>Converts one managed binding declaration into its ABI representation.</summary>
    /// <param name="binding">The managed binding declaration.</param>
    /// <returns>The native binding declaration.</returns>
    public static AlcoGPU.BindGroupLayoutEntry ConvertBindGroupLayoutEntry(BindGroupEntry binding)
    {
        AlcoGPU.BindGroupLayoutEntry entry = new()
        {
            Binding = binding.Binding,
            Visibility = binding.Stage,
            Type = binding.Type,
        };

        switch (binding.Type)
        {
            case BindingType.Texture:
                entry.TextureSampleType = binding.TextureInfo.SampleType;
                entry.ViewDimension = binding.TextureInfo.ViewDimension;
                break;
            case BindingType.StorageTexture:
                entry.StorageAccess = binding.StorageTextureInfo.Access;
                entry.StorageFormat = binding.StorageTextureInfo.Format;
                entry.ViewDimension = binding.StorageTextureInfo.ViewDimension;
                break;
            case BindingType.SamplerComparison:
                entry.SamplerKind = 2;
                break;
        }

        return entry;
    }

    /// <summary>Converts an attachment load op to its ABI value.</summary>
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

    /// <summary>Converts an attachment store op to its ABI value.</summary>
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
