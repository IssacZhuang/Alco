using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using StbImageSharp;
using StbImageWriteSharp;
using static Alco.MemoryUtility;

namespace Alco.Rendering;

/// <summary>
/// 2D texture decoding, GPU creation, streaming uploads, hot reload, and readback utilities built over
/// <see cref="Alco.Graphics.RenderingSystem"/>.
/// </summary>
public static unsafe class Texture2DUtility
{
    private const ulong MinStagingBytes = 4096;
    private const ulong MaxStagingBytes = 4UL * 1024 * 1024;
    private static readonly Lock s_uploadLock = new();

    /// <summary>Returns whether the device supports BC-family block-compressed textures.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <returns>True when BC textures are supported.</returns>
    public static bool IsBcSupported(RenderingSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.Device.Caps.TextureCompressionBc;
    }

    /// <summary>Creates a <see cref="Texture2D"/> by decoding an image or DDS stream synchronously.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="stream">The readable image stream.</param>
    /// <param name="option">Optional image load options.</param>
    /// <returns>A new <see cref="Texture2D"/> instance.</returns>
    public static Texture2D CreateTexture2DFromStream(
        RenderingSystem system,
        Stream stream,
        ImageLoadOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(stream);

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        int length = checked((int)stream.Length);
        byte* nativeBuffer = (byte*)NativeMemory.Alloc((nuint)length);
        try
        {
            stream.ReadExactly(new Span<byte>(nativeBuffer, length));
            ReadOnlySpan<byte> fileBytes = new(nativeBuffer, length);
            return CreateTexture2DFromFile(system, fileBytes, optionReal);
        }
        finally
        {
            NativeMemory.Free(nativeBuffer);
        }
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> from image file bytes. DDS files upload their mip chain verbatim
    /// when BC is supported; other formats decode to RGBA8 first.
    /// </summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="fileBytes">The encoded file bytes.</param>
    /// <param name="option">Optional image load options.</param>
    /// <returns>A new <see cref="Texture2D"/> instance.</returns>
    public static Texture2D CreateTexture2DFromFile(
        RenderingSystem system,
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (DdsDecoder.IsDds(fileBytes))
        {
            return CreateTexture2DFromDds(system, fileBytes, option);
        }

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        byte* pixels = ImageDecodeUtility.DecodeAuto(fileBytes, out int w, out int h);
        try
        {
            if (optionReal.PremultiplyAlpha)
            {
                PremultiplyAlpha(pixels, w * h);
            }

            return CreateTexture2D(system, pixels, (uint)(w * h * 4), (uint)w, (uint)h, optionReal);
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> from DDS BC1-BC7 bytes, uploading the stored mip chain verbatim
    /// when BC is supported or CPU-decoding level 0 to RGBA8 otherwise.
    /// </summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="fileBytes">The complete DDS file bytes.</param>
    /// <param name="option">Optional image load options.</param>
    /// <returns>A new <see cref="Texture2D"/> instance.</returns>
    /// <exception cref="ImageDecodeException">Invalid, uncompressed, or unsupported DDS data.</exception>
    public static Texture2D CreateTexture2DFromDds(
        RenderingSystem system,
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        return CreateTexture2DFromDds(system, fileBytes, option, system.Device.Caps.TextureCompressionBc);
    }

    internal static Texture2D CreateTexture2DFromDds(
        RenderingSystem system,
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option,
        bool bcSupported)
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        bool srgb = IsSrgbFormat(optionReal.Format);
        DdsDecoder.Decode(fileBytes, srgb, out DdsDecoder.BcFamily family, out Format format, out int width, out int height, out int mipLevels, out int dataOffset);

        if (!bcSupported)
        {
            byte* pixels = BcDecoder.DecodeLevel(fileBytes, dataOffset, family, width, height, level: 0);
            try
            {
                return CreateTexture2D(system, pixels, (uint)(width * height * 4), (uint)width, (uint)height, optionReal);
            }
            finally
            {
                NativeMemory.Free(pixels);
            }
        }

        if (!TryGetBlockBytes(format, out uint blockBytes))
        {
            throw new ImageDecodeException($"DDS pixel format {format} is not block-compressed.");
        }

        Texture texture = CreateGpuTexture(system, (uint)width, (uint)height, (uint)mipLevels, format, optionReal.Usage);
        try
        {
            UploadDdsMipChain(system, texture, fileBytes, dataOffset, width, height, mipLevels, blockBytes);
            return new Texture2D(system, texture, optionReal.Name, optionReal.SlicePadding);
        }
        catch
        {
            if (!texture.IsDestroyed)
            {
                texture.Destroy();
            }
            throw;
        }
    }

    internal static Texture2D CreateTexture2DFromHeader(RenderingSystem system, in ImageFileInfo info, ImageLoadOption? option = null)
    {
        return CreateTexture2DFromHeader(system, info, option, system.Device.Caps.TextureCompressionBc);
    }

    internal static Texture2D CreateTexture2DFromHeader(
        RenderingSystem system,
        in ImageFileInfo info,
        ImageLoadOption? option,
        bool bcSupported)
    {
        if (!info.IsBlockCompressed || !bcSupported)
        {
            Texture2D plain = CreateTexture2D(system, (uint)info.Width, (uint)info.Height, option);
            plain.MarkContentPending();
            return plain;
        }

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        Texture texture = CreateGpuTexture(system, (uint)info.Width, (uint)info.Height, (uint)info.MipLevels, info.Format, optionReal.Usage);
        Texture2D result = new(system, texture, optionReal.Name, optionReal.SlicePadding);
        result.MarkContentPending();
        return result;
    }

    internal static void UploadTexture2DContent(Texture2D texture, ReadOnlySpan<byte> fileBytes, ImageLoadOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (!texture.IsWriteable)
        {
            throw new InvalidOperationException("The texture is not writeable.");
        }

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        bool srgb = IsSrgbFormat(optionReal.Format);

        if (DdsDecoder.IsDds(fileBytes))
        {
            DdsDecoder.Decode(fileBytes, srgb, out DdsDecoder.BcFamily family, out Format format, out int width, out int height, out int mipLevels, out int dataOffset);

            if (!TryGetBlockBytes(texture.Format, out uint blockBytes))
            {
                if (width != (int)texture.Width || height != (int)texture.Height || texture.MipLevels != 1)
                {
                    throw new ImageDecodeException(
                        $"DDS fallback specification {width}x{height} x1 does not match the target texture " +
                        $"{texture.Width}x{texture.Height} x{texture.MipLevels}.");
                }

                byte* fallbackPixels = BcDecoder.DecodeLevel(fileBytes, dataOffset, family, width, height, level: 0);
                try
                {
                    lock (texture.UploadLock)
                    {
                        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
                        UploadTextureData(
                            texture.RenderingSystem,
                            texture.NativeTexture,
                            new ReadOnlySpan<byte>(fallbackPixels, width * height * 4));
                        texture.MarkContentLoaded();
                    }
                }
                finally
                {
                    NativeMemory.Free(fallbackPixels);
                }
                return;
            }

            if (width != (int)texture.Width || height != (int)texture.Height
                || format != texture.Format || mipLevels != (int)texture.MipLevels)
            {
                throw new ImageDecodeException(
                    $"DDS specification {width}x{height} {format} x{mipLevels} does not match the target texture " +
                    $"{texture.Width}x{texture.Height} {texture.Format} x{texture.MipLevels}.");
            }

            lock (texture.UploadLock)
            {
                ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
                UploadDdsMipChain(texture.RenderingSystem, texture.NativeTexture, fileBytes, dataOffset, width, height, mipLevels, blockBytes);
                texture.MarkContentLoaded();
            }
            return;
        }

        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(fileBytes);
        if (info.Width != (int)texture.Width || info.Height != (int)texture.Height)
        {
            throw new ImageDecodeException(
                $"Image specification {info.Width}x{info.Height} does not match the target texture {texture.Width}x{texture.Height}.");
        }

        byte* pixels = ImageDecodeUtility.DecodeAuto(fileBytes, out int w, out int h);
        try
        {
            if (optionReal.PremultiplyAlpha)
            {
                PremultiplyAlpha(pixels, w * h);
            }

            lock (texture.UploadLock)
            {
                ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
                UploadTextureData(
                    texture.RenderingSystem,
                    texture.NativeTexture,
                    new ReadOnlySpan<byte>(pixels, w * h * 4));
                texture.MarkContentLoaded();
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    /// <summary>
    /// Creates a <see cref="Texture2D"/> whose content streams in asynchronously while keeping its identity fixed.
    /// Stream ownership transfers to the streaming task on success; on header probe failure the caller retains ownership.
    /// </summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="stream">A seekable stream positioned at the start of the image file.</param>
    /// <param name="option">Optional image load options.</param>
    /// <returns>A new <see cref="Texture2D"/> with <see cref="Texture2D.ContentArrival"/> tracking upload completion.</returns>
    /// <exception cref="ImageDecodeException">Unrecognized, truncated, or corrupt header.</exception>
    public static Texture2D CreateTexture2DStreaming(
        RenderingSystem system,
        Stream stream,
        ImageLoadOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(stream);

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        bool srgb = IsSrgbFormat(optionReal.Format);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(stream, srgb);
        try
        {
            Texture2D texture = CreateTexture2DFromHeader(system, info, optionReal);
            texture.SetContentUpload(StreamTexture2DContentAsync(texture, stream, optionReal));
            return texture;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static async Task StreamTexture2DContentAsync(Texture2D texture, Stream stream, ImageLoadOption option)
    {
        try
        {
            await Task.Run(() =>
            {
                using (stream)
                {
                    using var fileData = new SafeMemoryHandle(stream.Length);
                    stream.Position = 0;
                    stream.ReadExactly(fileData.AsSpan());
                    UploadTexture2DContent(texture, fileData.AsReadOnlySpan(), option);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to stream texture '{texture.Name}': {ex.Message}");
        }
    }

    /// <summary>Creates a solid-color <see cref="Texture2D"/>.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="width">The texture width in texels.</param>
    /// <param name="height">The texture height in texels.</param>
    /// <param name="color">The fill color.</param>
    /// <param name="option">Optional image load options.</param>
    /// <returns>A new <see cref="Texture2D"/> instance.</returns>
    public static Texture2D CreateTexture2D(
        RenderingSystem system,
        uint width,
        uint height,
        Color32 color,
        ImageLoadOption? option = null)
    {
        int length = checked((int)(width * height));
        Color32* data = Alloc<Color32>(length);
        try
        {
            Memset(data, length, color);
            return CreateTexture2D(system, (byte*)data, (uint)(sizeof(Color32) * length), width, height, option);
        }
        finally
        {
            Free(data);
        }
    }

    /// <summary>Creates a <see cref="Texture2D"/> initialized from raw texel bytes.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="data">The raw pixel bytes matching <paramref name="option"/> format.</param>
    /// <param name="width">The texture width in texels.</param>
    /// <param name="height">The texture height in texels.</param>
    /// <param name="option">Optional image load options.</param>
    /// <returns>A new <see cref="Texture2D"/> instance.</returns>
    public static Texture2D CreateTexture2D(
        RenderingSystem system,
        ReadOnlySpan<byte> data,
        uint width,
        uint height,
        ImageLoadOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(system);

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        Texture texture = CreateGpuTexture(system, width, height, optionReal.MipLevels, optionReal.Format, optionReal.Usage);
        try
        {
            if (!data.IsEmpty)
            {
                UploadTextureData(system, texture, data);
            }
            return new Texture2D(system, texture, optionReal.Name, optionReal.SlicePadding);
        }
        catch
        {
            if (!texture.IsDestroyed)
            {
                texture.Destroy();
            }
            throw;
        }
    }

    /// <summary>Creates a <see cref="Texture2D"/> initialized from an unmanaged texel buffer.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="data">Pointer to the raw texel bytes.</param>
    /// <param name="size">The buffer length in bytes.</param>
    /// <param name="width">The texture width in texels.</param>
    /// <param name="height">The texture height in texels.</param>
    /// <param name="option">Optional image load options.</param>
    /// <returns>A new <see cref="Texture2D"/> instance.</returns>
    public static Texture2D CreateTexture2D(
        RenderingSystem system,
        byte* data,
        uint size,
        uint width,
        uint height,
        ImageLoadOption? option = null)
    {
        ReadOnlySpan<byte> span = data == null || size == 0
            ? ReadOnlySpan<byte>.Empty
            : new ReadOnlySpan<byte>(data, checked((int)size));
        return CreateTexture2D(system, span, width, height, option);
    }

    /// <summary>Creates an empty <see cref="Texture2D"/> without uploading initial texels.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="width">The texture width in texels.</param>
    /// <param name="height">The texture height in texels.</param>
    /// <param name="option">Optional image load options.</param>
    /// <returns>A new <see cref="Texture2D"/> instance.</returns>
    public static Texture2D CreateTexture2D(
        RenderingSystem system,
        uint width,
        uint height,
        ImageLoadOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(system);

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        Texture texture = CreateGpuTexture(system, width, height, optionReal.MipLevels, optionReal.Format, optionReal.Usage);
        return new Texture2D(system, texture, optionReal.Name, optionReal.SlicePadding);
    }

    /// <summary>Wraps an existing <see cref="Texture"/> in a <see cref="Texture2D"/>.</summary>
    /// <param name="system">The graphics rendering system owning <paramref name="texture"/>.</param>
    /// <param name="texture">The GPU texture to wrap.</param>
    /// <param name="name">The diagnostic name.</param>
    /// <param name="slicePadding">Optional 9-slice padding.</param>
    /// <param name="ownsResources">Whether the wrapper owns and destroys <paramref name="texture"/>.</param>
    /// <returns>A new <see cref="Texture2D"/> instance.</returns>
    public static Texture2D CreateTexture2D(
        RenderingSystem system,
        Texture texture,
        string name = "unnamed_texture",
        Padding? slicePadding = null,
        bool ownsResources = false)
    {
        return new Texture2D(system, texture, name, slicePadding, ownsResources);
    }

    /// <summary>Hot reloads a <see cref="Texture2D"/> from encoded image bytes, uploading in place when sizes match.</summary>
    /// <param name="texture2D">The target <see cref="Texture2D"/>.</param>
    /// <param name="fileBytes">The encoded image file bytes.</param>
    /// <param name="option">Optional image load options.</param>
    public static void UnsafeHotReloadTexture2DByFile(
        Texture2D texture2D,
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(texture2D);
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;

        byte* pixels = ImageDecodeUtility.DecodeAuto(fileBytes, out int w, out int h);
        try
        {
            if (optionReal.PremultiplyAlpha)
            {
                PremultiplyAlpha(pixels, w * h);
            }

            ReadOnlySpan<byte> pixelSpan = new(pixels, checked(w * h * 4));
            if (w == texture2D.Width && h == texture2D.Height && texture2D.IsWriteable)
            {
                lock (texture2D.UploadLock)
                {
                    ObjectDisposedException.ThrowIf(texture2D.IsDisposed, texture2D);
                    UploadTextureData(texture2D.RenderingSystem, texture2D.NativeTexture, pixelSpan);
                    texture2D.MarkContentLoaded();
                }
            }
            else
            {
                Texture replacement = CreateGpuTexture(
                    texture2D.RenderingSystem,
                    (uint)w,
                    (uint)h,
                    optionReal.MipLevels,
                    optionReal.Format,
                    optionReal.Usage);
                try
                {
                    UploadTextureData(texture2D.RenderingSystem, replacement, pixelSpan);
                    texture2D.UnsafeHotReload(replacement, optionReal.Name, ownsResources: true);
                    texture2D.MarkContentLoaded();
                }
                catch
                {
                    if (!replacement.IsDestroyed)
                    {
                        replacement.Destroy();
                    }
                    throw;
                }
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    /// <summary>Reads back an RGBA8 <see cref="Texture2D"/> and writes it as a PNG stream.</summary>
    /// <param name="texture">The RGBA8 texture created with <see cref="TextureUsage.TransferSource"/>.</param>
    /// <param name="output">The destination stream.</param>
    public static void EncodeTextureToPNG(Texture2D texture, Stream output)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(output);

        Format format = texture.Format;
        if (format != Format.Rgba8Unorm && format != Format.Rgba8Srgb)
        {
            throw new ArgumentException(
                $"Texture format {format} is not supported. Only Rgba8Unorm and Rgba8Srgb are supported.",
                nameof(texture));
        }

        Texture native = texture.NativeTexture;
        if ((native.Desc.Usage & TextureUsage.TransferSource) == 0)
        {
            throw new InvalidOperationException("Texture must be created with TextureUsage.TransferSource for PNG encoding.");
        }

        uint width = texture.Width;
        uint height = texture.Height;
        ulong dataSize = (ulong)width * height * 4;
        RenderingSystem system = texture.RenderingSystem;
        GPUDevice device = system.Device;

        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            lock (s_uploadLock)
            {
                system.FlushInitializations();
                readback = device.CreateGPUHeap(dataSize, MemoryType.Readback);
                pool = device.CreateCommandPool(0);
                timeline = device.CreateTimelineSemaphore();
                GPUCommandBuffer commands = pool.BeginCommands();
                commands.Barrier(Stage.AllCommands, Access.ShaderRead | Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                commands.CopyTextureToMemory(native.NativeTexture, readback.Range.GPURange);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                completion.Wait();
            }

            ImageWriter writer = new();
            writer.WritePng(readback.Range.Cpu, (int)width, (int)height, ColorComponents.RedGreenBlueAlpha, output);
        }
        finally
        {
            timeline?.Destroy();
            pool?.Destroy();
            readback.Destroy();
        }
    }

    internal static void UploadTextureData(
        RenderingSystem system,
        Texture target,
        ReadOnlySpan<byte> data,
        in TextureCopyDesc copy = default)
    {
        if (data.IsEmpty)
        {
            return;
        }

        TextureDesc desc = target.Desc;
        TextureFormatInfo formatInfo = Gpu.GetTextureFormatInfo(desc.Format);
        uint copyWidth = copy.Extent.X != 0 ? copy.Extent.X : Math.Max(1u, desc.Extent.X >> (int)copy.MipLevel);
        ulong blocksX = (copyWidth + formatInfo.BlockExtent.X - 1) / formatInfo.BlockExtent.X;
        ulong minRowBytes = blocksX * formatInfo.BytesPerBlock;
        ulong stagingBytes = AlignStagingCapacity(Math.Max(TextureUpload.GetSizeBytes(desc, copy), minRowBytes), minRowBytes);

        lock (s_uploadLock)
        {
            system.FlushInitializations();
            UploadQueue? queue = null;
            try
            {
                queue = new UploadQueue(system.Device, stagingBytes, queueIndex: 0, maxPendingBatches: 1);
                TextureUpload.UploadTexture(queue, target.NativeTexture, desc, data, copy);
                queue.Wait();
            }
            finally
            {
                queue?.Destroy();
            }
        }
    }

    private static void UploadDdsMipChain(
        RenderingSystem system,
        Texture target,
        ReadOnlySpan<byte> fileBytes,
        int dataOffset,
        int width,
        int height,
        int mipLevels,
        uint blockBytes)
    {
        TextureDesc desc = target.Desc;
        uint largestMipBytes = DdsDecoder.GetMipByteCount(width, height, 0, blockBytes);
        uint topBlocksX = ((uint)width + 3u) / 4u;
        ulong minRowBytes = (ulong)topBlocksX * blockBytes;
        ulong stagingBytes = AlignStagingCapacity(largestMipBytes, minRowBytes);

        lock (s_uploadLock)
        {
            system.FlushInitializations();
            UploadQueue? queue = null;
            try
            {
                queue = new UploadQueue(system.Device, stagingBytes, queueIndex: 0, maxPendingBatches: 1);
                int offset = dataOffset;
                for (int level = 0; level < mipLevels; level++)
                {
                    uint byteCount = DdsDecoder.GetMipByteCount(width, height, level, blockBytes);
                    TextureUpload.UploadTexture(
                        queue,
                        target.NativeTexture,
                        desc,
                        fileBytes.Slice(offset, checked((int)byteCount)),
                        new TextureCopyDesc { MipLevel = (uint)level });
                    offset += (int)byteCount;
                }
                queue.Wait();
            }
            finally
            {
                queue?.Destroy();
            }
        }
    }

    private static Texture CreateGpuTexture(
        RenderingSystem system,
        uint width,
        uint height,
        uint mipLevels,
        Format format,
        TextureUsage usage)
    {
        return system.CreateTexture(new TextureDesc
        {
            Type = TextureType.TwoD,
            Extent = new Extent3D(width, height, 1),
            MipLevels = mipLevels,
            LayerCount = 1,
            Format = format,
            Usage = usage,
            SampleCount = 1,
        });
    }

    private static ulong AlignStagingCapacity(ulong desiredBytes, ulong minRowBytes)
    {
        ulong clamped = Math.Min(desiredBytes, MaxStagingBytes);
        ulong minimum = Math.Max(MinStagingBytes, minRowBytes);
        ulong capacity = Math.Max(clamped, minimum);
        return (capacity + (UploadQueue.Alignment - 1)) & ~(UploadQueue.Alignment - 1);
    }

    private static bool TryGetBlockBytes(Format format, out uint blockBytes)
    {
        TextureFormatInfo info = Gpu.GetTextureFormatInfo(format);
        if (info.BlockExtent.X <= 1 && info.BlockExtent.Y <= 1)
        {
            blockBytes = 0;
            return false;
        }

        blockBytes = info.BytesPerBlock;
        return blockBytes > 0;
    }

    internal static bool IsSrgbFormat(Format format) => format switch
    {
        Format.R8Srgb or
        Format.Rg8Srgb or
        Format.Rgba8Srgb or
        Format.Bgra8Srgb or
        Format.Astc4x4Srgb or
        Format.Bc1Srgb or
        Format.Bc2Srgb or
        Format.Bc3Srgb or
        Format.Bc7Srgb => true,
        _ => false,
    };

    /// <summary>
    /// Converts RGBA8 pixel data from straight alpha to premultiplied alpha in place.
    /// Uses AVX2, SSSE3+SSE2, or scalar fallback.
    /// </summary>
    internal static void PremultiplyAlpha(byte* data, int pixelCount)
    {
        int offset = 0;

        if (Avx2.IsSupported)
        {
            Vector256<byte> alphaShuffle = Vector256.Create(
                (byte)3, (byte)3, (byte)3, (byte)3, (byte)7, (byte)7, (byte)7, (byte)7,
                (byte)11, (byte)11, (byte)11, (byte)11, (byte)15, (byte)15, (byte)15, (byte)15,
                (byte)3, (byte)3, (byte)3, (byte)3, (byte)7, (byte)7, (byte)7, (byte)7,
                (byte)11, (byte)11, (byte)11, (byte)11, (byte)15, (byte)15, (byte)15, (byte)15
            );
            Vector256<byte> zero = Vector256<byte>.Zero;
            Vector256<ushort> one16 = Vector256<ushort>.One;
            Vector256<byte> rgbMask = Vector256.Create(
                byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0, byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0,
                byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0, byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0,
                byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0, byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0,
                byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0, byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0
            );
            Vector256<byte> alphaMask = Vector256.Create(
                (byte)0, (byte)0, (byte)0, byte.MaxValue, (byte)0, (byte)0, (byte)0, byte.MaxValue,
                (byte)0, (byte)0, (byte)0, byte.MaxValue, (byte)0, (byte)0, (byte)0, byte.MaxValue,
                (byte)0, (byte)0, (byte)0, byte.MaxValue, (byte)0, (byte)0, (byte)0, byte.MaxValue,
                (byte)0, (byte)0, (byte)0, byte.MaxValue, (byte)0, (byte)0, (byte)0, byte.MaxValue
            );

            int avxEnd = pixelCount & ~7;
            for (; offset < avxEnd; offset += 8)
            {
                byte* p = data + offset * 4;
                Vector256<byte> src = Vector256.LoadUnsafe(ref *p);
                Vector256<byte> alpha = Avx2.Shuffle(src, alphaShuffle);

                Vector256<ushort> srcLo = Avx2.UnpackLow(src, zero).AsUInt16();
                Vector256<ushort> srcHi = Avx2.UnpackHigh(src, zero).AsUInt16();
                Vector256<ushort> aLo = Avx2.UnpackLow(alpha, zero).AsUInt16();
                Vector256<ushort> aHi = Avx2.UnpackHigh(alpha, zero).AsUInt16();

                Vector256<ushort> mulLo = Avx2.MultiplyLow(srcLo, aLo);
                Vector256<ushort> mulHi = Avx2.MultiplyLow(srcHi, aHi);

                Vector256<ushort> divLo = Avx2.ShiftRightLogical(
                    Avx2.Add(Avx2.Add(mulLo, one16), Avx2.ShiftRightLogical(mulLo, 8)), 8);
                Vector256<ushort> divHi = Avx2.ShiftRightLogical(
                    Avx2.Add(Avx2.Add(mulHi, one16), Avx2.ShiftRightLogical(mulHi, 8)), 8);

                Vector128<byte> pack01 = Sse2.PackUnsignedSaturate(
                    divLo.GetLower().AsInt16(), divHi.GetLower().AsInt16());
                Vector128<byte> pack45 = Sse2.PackUnsignedSaturate(
                    divLo.GetUpper().AsInt16(), divHi.GetUpper().AsInt16());
                Vector256<byte> premul = Vector256.Create(pack01, pack45);

                Vector256<byte> result = Avx2.Or(
                    Avx2.And(premul, rgbMask),
                    Avx2.And(src, alphaMask));

                result.StoreUnsafe(ref *p);
            }
        }

        if (Sse2.IsSupported && Ssse3.IsSupported)
        {
            Vector128<byte> alphaShuffle = Vector128.Create(
                (byte)3, (byte)3, (byte)3, (byte)3, (byte)7, (byte)7, (byte)7, (byte)7,
                (byte)11, (byte)11, (byte)11, (byte)11, (byte)15, (byte)15, (byte)15, (byte)15
            );
            Vector128<byte> zero = Vector128<byte>.Zero;
            Vector128<ushort> one16 = Vector128<ushort>.One;
            Vector128<byte> rgbMask = Vector128.Create(
                byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0, byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0,
                byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0, byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0
            );
            Vector128<byte> alphaMask = Vector128.Create(
                (byte)0, (byte)0, (byte)0, byte.MaxValue, (byte)0, (byte)0, (byte)0, byte.MaxValue,
                (byte)0, (byte)0, (byte)0, byte.MaxValue, (byte)0, (byte)0, (byte)0, byte.MaxValue
            );

            int sseEnd = pixelCount & ~3;
            for (; offset < sseEnd; offset += 4)
            {
                byte* p = data + offset * 4;
                Vector128<byte> src = Vector128.LoadUnsafe(ref *p);
                Vector128<byte> alpha = Ssse3.Shuffle(src, alphaShuffle);

                Vector128<ushort> srcLo = Sse2.UnpackLow(src, zero).AsUInt16();
                Vector128<ushort> srcHi = Sse2.UnpackHigh(src, zero).AsUInt16();
                Vector128<ushort> aLo = Sse2.UnpackLow(alpha, zero).AsUInt16();
                Vector128<ushort> aHi = Sse2.UnpackHigh(alpha, zero).AsUInt16();

                Vector128<ushort> mulLo = Sse2.MultiplyLow(srcLo, aLo);
                Vector128<ushort> mulHi = Sse2.MultiplyLow(srcHi, aHi);

                Vector128<ushort> divLo = Sse2.ShiftRightLogical(
                    Sse2.Add(Sse2.Add(mulLo, one16), Sse2.ShiftRightLogical(mulLo, 8)), 8);
                Vector128<ushort> divHi = Sse2.ShiftRightLogical(
                    Sse2.Add(Sse2.Add(mulHi, one16), Sse2.ShiftRightLogical(mulHi, 8)), 8);

                Vector128<byte> premul = Sse2.PackUnsignedSaturate(divLo.AsInt16(), divHi.AsInt16());

                Vector128<byte> result = Sse2.Or(
                    Sse2.And(premul, rgbMask),
                    Sse2.And(src, alphaMask));

                result.StoreUnsafe(ref *p);
            }
        }

        for (; offset < pixelCount; offset++)
        {
            byte* p = data + offset * 4;
            int a = p[3];
            p[0] = (byte)((p[0] * a + 128) / 255);
            p[1] = (byte)((p[1] * a + 128) / 255);
            p[2] = (byte)((p[2] * a + 128) / 255);
        }
    }
}
