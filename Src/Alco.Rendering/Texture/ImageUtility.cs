using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

namespace Alco.Rendering;

/// <summary>
/// Image asset decoding, streaming creation, and pixel preprocessing utilities producing
/// immutable <see cref="Image"/> assets.
/// </summary>
public static unsafe class ImageUtility
{
    /// <summary>Creates an <see cref="Image"/> by decoding an image or DDS stream synchronously.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="stream">The readable image stream.</param>
    /// <param name="option">Optional import settings.</param>
    /// <param name="sprites">Optional named sprite regions in pixel coordinates.</param>
    /// <returns>A new <see cref="Image"/> asset.</returns>
    public static Image CreateFromStream(
        RenderingSystem system,
        Stream stream,
        ImageLoadOption? option = null,
        IReadOnlyDictionary<string, RectInt>? sprites = null)
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
            return CreateFromFile(system, fileBytes, optionReal, sprites);
        }
        finally
        {
            NativeMemory.Free(nativeBuffer);
        }
    }

    /// <summary>
    /// Creates an <see cref="Image"/> from encoded file bytes. DDS files upload their mip chain verbatim
    /// when BC is supported; other formats decode to RGBA8 first.
    /// </summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="fileBytes">The encoded image file bytes.</param>
    /// <param name="option">Optional import settings.</param>
    /// <param name="sprites">Optional named sprite regions in pixel coordinates.</param>
    /// <returns>A new <see cref="Image"/> asset.</returns>
    public static Image CreateFromFile(
        RenderingSystem system,
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null,
        IReadOnlyDictionary<string, RectInt>? sprites = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (DdsDecoder.IsDds(fileBytes))
        {
            return CreateFromDds(system, fileBytes, option, sprites);
        }

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        byte* pixels = DecodeRgbaPixels(fileBytes, optionReal.PremultiplyAlpha, out int width, out int height);
        try
        {
            Texture texture = TextureUtility.CreateTexture(
                system,
                pixels,
                checked((uint)(width * height * 4)),
                (uint)width,
                (uint)height,
                optionReal.Format,
                optionReal.Usage,
                optionReal.MipLevels);
            return WrapOwnedTexture(texture, optionReal, sprites);
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    /// <summary>
    /// Creates an <see cref="Image"/> from DDS BC1-BC7 bytes, uploading the stored mip chain verbatim
    /// when BC is supported or CPU-decoding level 0 to RGBA8 otherwise.
    /// </summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="fileBytes">The complete DDS file bytes.</param>
    /// <param name="option">Optional import settings.</param>
    /// <param name="sprites">Optional named sprite regions in pixel coordinates.</param>
    /// <returns>A new <see cref="Image"/> asset.</returns>
    /// <exception cref="ImageDecodeException">Invalid, uncompressed, or unsupported DDS data.</exception>
    public static Image CreateFromDds(
        RenderingSystem system,
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null,
        IReadOnlyDictionary<string, RectInt>? sprites = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        return CreateFromDds(system, fileBytes, option, sprites, system.Device.Caps.TextureCompressionBc);
    }

    internal static Image CreateFromDds(
        RenderingSystem system,
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option,
        IReadOnlyDictionary<string, RectInt>? sprites,
        bool bcSupported)
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        bool srgb = TextureUtility.IsSrgbFormat(optionReal.Format);
        DdsDecoder.Decode(
            fileBytes,
            srgb,
            out DdsDecoder.BcFamily family,
            out Format format,
            out int width,
            out int height,
            out int mipLevels,
            out int dataOffset);

        if (!bcSupported)
        {
            byte* pixels = BcDecoder.DecodeLevel(fileBytes, dataOffset, family, width, height, level: 0);
            try
            {
                Texture fallbackTexture = TextureUtility.CreateTexture(
                    system,
                    pixels,
                    checked((uint)(width * height * 4)),
                    (uint)width,
                    (uint)height,
                    optionReal.Format,
                    optionReal.Usage,
                    optionReal.MipLevels);
                return WrapOwnedTexture(fallbackTexture, optionReal, sprites);
            }
            finally
            {
                NativeMemory.Free(pixels);
            }
        }

        if (!TextureUtility.TryGetBlockBytes(format, out uint blockBytes))
        {
            throw new ImageDecodeException($"DDS pixel format {format} is not block-compressed.");
        }

        Texture texture = TextureUtility.CreateTexture(
            system,
            (uint)width,
            (uint)height,
            format,
            optionReal.Usage,
            (uint)mipLevels);
        try
        {
            UploadDdsMipChain(system, texture, fileBytes, dataOffset, width, height, mipLevels, blockBytes);
            return new Image(texture, optionReal, sprites, ownsResource: true);
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

    /// <summary>
    /// Creates an <see cref="Image"/> whose texel content decodes in the background and uploads in budget slices via <paramref name="uploadQueue"/>.
    /// Stream ownership transfers on success; on header probe failure the caller retains ownership.
    /// </summary>
    /// <param name="uploadQueue">The streaming image upload queue.</param>
    /// <param name="stream">A seekable stream positioned at the start of the image file.</param>
    /// <param name="option">Optional import settings.</param>
    /// <param name="sprites">Optional named sprite regions in pixel coordinates.</param>
    /// <returns>A new <see cref="Image"/> whose <see cref="Image.ContentArrival"/> tracks upload completion.</returns>
    /// <exception cref="ImageDecodeException">Unrecognized, truncated, or corrupt header.</exception>
    public static Image CreateStreaming(
        ImageUploadQueue uploadQueue,
        Stream stream,
        ImageLoadOption? option = null,
        IReadOnlyDictionary<string, RectInt>? sprites = null)
    {
        ArgumentNullException.ThrowIfNull(uploadQueue);
        return uploadQueue.CreateStreaming(stream, option, sprites);
    }

    /// <summary>Wraps an existing GPU <see cref="Texture"/> as an <see cref="Image"/> asset.</summary>
    /// <param name="texture">The GPU texture to wrap.</param>
    /// <param name="option">Optional import settings describing sampler, slice, and alpha properties.</param>
    /// <param name="sprites">Optional named sprite regions in pixel coordinates.</param>
    /// <param name="ownsResource">Whether disposing the returned <see cref="Image"/> destroys <paramref name="texture"/>.</param>
    /// <returns>A new <see cref="Image"/> wrapping <paramref name="texture"/>.</returns>
    public static Image CreateFromTexture(
        Texture texture,
        ImageLoadOption? option = null,
        IReadOnlyDictionary<string, RectInt>? sprites = null,
        bool ownsResource = false)
    {
        return new Image(texture, option, sprites, ownsResource);
    }

    internal static Image CreateFromHeader(
        RenderingSystem system,
        in ImageFileInfo info,
        ImageLoadOption? option = null,
        IReadOnlyDictionary<string, RectInt>? sprites = null)
    {
        return CreateFromHeader(system, info, option, sprites, system.Device.Caps.TextureCompressionBc);
    }

    internal static Image CreateFromHeader(
        RenderingSystem system,
        in ImageFileInfo info,
        ImageLoadOption? option,
        IReadOnlyDictionary<string, RectInt>? sprites,
        bool bcSupported)
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        Texture texture = !info.IsBlockCompressed || !bcSupported
            ? TextureUtility.CreateTexture(
                system,
                (uint)info.Width,
                (uint)info.Height,
                optionReal.Format,
                optionReal.Usage,
                optionReal.MipLevels)
            : TextureUtility.CreateTexture(
                system,
                (uint)info.Width,
                (uint)info.Height,
                info.Format,
                optionReal.Usage,
                (uint)info.MipLevels);

        return WrapOwnedTexture(texture, optionReal, sprites, isContentLoaded: false);
    }

    internal static void UploadContent(
        RenderingSystem system,
        Image image,
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(image);

        Texture target = image.Texture;
        if ((target.Desc.Usage & TextureUsage.TransferDestination) == 0)
        {
            throw new InvalidOperationException("The texture is not writeable.");
        }

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        bool srgb = TextureUtility.IsSrgbFormat(optionReal.Format);

        if (DdsDecoder.IsDds(fileBytes))
        {
            UploadDdsContent(system, image, target, fileBytes, srgb);
            return;
        }

        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(fileBytes);
        if (info.Width != (int)image.Width || info.Height != (int)image.Height)
        {
            throw new ImageDecodeException(
                $"Image specification {info.Width}x{info.Height} does not match the target image {image.Width}x{image.Height}.");
        }

        byte* pixels = DecodeRgbaPixels(fileBytes, optionReal.PremultiplyAlpha, out int width, out int height);
        try
        {
            lock (image.UploadLock)
            {
                ObjectDisposedException.ThrowIf(image.IsDisposed, image);
                TextureUtility.UploadTextureData(system, target, new ReadOnlySpan<byte>(pixels, checked(width * height * 4)));
                image.MarkContentLoaded();
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    private static void UploadDdsContent(
        RenderingSystem system,
        Image image,
        Texture target,
        ReadOnlySpan<byte> fileBytes,
        bool srgb)
    {
        DdsDecoder.Decode(
            fileBytes,
            srgb,
            out DdsDecoder.BcFamily family,
            out Format format,
            out int width,
            out int height,
            out int mipLevels,
            out int dataOffset);

        if (!TextureUtility.TryGetBlockBytes(image.Format, out uint blockBytes))
        {
            if (width != (int)image.Width || height != (int)image.Height || image.MipLevels != 1)
            {
                throw new ImageDecodeException(
                    $"DDS fallback specification {width}x{height} x1 does not match the target image " +
                    $"{image.Width}x{image.Height} x{image.MipLevels}.");
            }

            byte* fallbackPixels = BcDecoder.DecodeLevel(fileBytes, dataOffset, family, width, height, level: 0);
            try
            {
                lock (image.UploadLock)
                {
                    ObjectDisposedException.ThrowIf(image.IsDisposed, image);
                    TextureUtility.UploadTextureData(system, target, new ReadOnlySpan<byte>(fallbackPixels, checked(width * height * 4)));
                    image.MarkContentLoaded();
                }
            }
            finally
            {
                NativeMemory.Free(fallbackPixels);
            }
            return;
        }

        if (width != (int)image.Width || height != (int)image.Height
            || format != image.Format || mipLevels != (int)image.MipLevels)
        {
            throw new ImageDecodeException(
                $"DDS specification {width}x{height} {format} x{mipLevels} does not match the target image " +
                $"{image.Width}x{image.Height} {image.Format} x{image.MipLevels}.");
        }

        lock (image.UploadLock)
        {
            ObjectDisposedException.ThrowIf(image.IsDisposed, image);
            UploadDdsMipChain(system, target, fileBytes, dataOffset, width, height, mipLevels, blockBytes);
            image.MarkContentLoaded();
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
        int offset = dataOffset;
        for (int level = 0; level < mipLevels; level++)
        {
            uint byteCount = DdsDecoder.GetMipByteCount(width, height, level, blockBytes);
            TextureUtility.UploadTextureData(
                system,
                target,
                fileBytes.Slice(offset, checked((int)byteCount)),
                new TextureCopyDesc { MipLevel = (uint)level });
            offset += (int)byteCount;
        }
    }

    private static byte* DecodeRgbaPixels(ReadOnlySpan<byte> fileBytes, bool premultiplyAlpha, out int width, out int height)
    {
        byte* pixels = ImageDecodeUtility.DecodeAuto(fileBytes, out width, out height);
        if (premultiplyAlpha)
        {
            PremultiplyAlpha(pixels, width * height);
        }
        return pixels;
    }

    private static Image WrapOwnedTexture(
        Texture texture,
        in ImageLoadOption option,
        IReadOnlyDictionary<string, RectInt>? sprites,
        bool isContentLoaded = true)
    {
        try
        {
            return new Image(texture, option, sprites, ownsResource: true, isContentLoaded: isContentLoaded);
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

    /// <summary>
    /// Converts tightly packed RGBA8 pixel data from straight alpha to premultiplied alpha in place.
    /// </summary>
    /// <param name="rgbaPixels">Tightly packed RGBA8 pixels whose byte length is divisible by 4.</param>
    public static void PremultiplyRgba8(Span<byte> rgbaPixels)
    {
        if (rgbaPixels.Length % 4 != 0)
        {
            throw new ArgumentException("RGBA8 buffer length must be divisible by 4.", nameof(rgbaPixels));
        }
        if (rgbaPixels.IsEmpty)
        {
            return;
        }
        fixed (byte* ptr = rgbaPixels)
        {
            PremultiplyAlpha(ptr, rgbaPixels.Length / 4);
        }
    }

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
