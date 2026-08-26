using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using StbImageSharp;
using StbImageWriteSharp;
using Alco;
using Alco.Graphics;

using static Alco.MemoryUtility;

namespace Alco.Rendering;

// texture factory

public partial class RenderingSystem
{
    /// <summary>
    /// Creates a Texture2D from a stream.
    /// </summary>
    /// <param name="stream">The stream containing image data.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2DFromStream(
        Stream stream,
        ImageLoadOption? option = null
    )
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;

        long length = stream.Length;
        byte* nativeBuffer = (byte*)NativeMemory.Alloc((nuint)length);
        try
        {
            stream.ReadExactly(new Span<byte>(nativeBuffer, (int)length));

            if (DdsDecoder.IsDds(new ReadOnlySpan<byte>(nativeBuffer, (int)length)))
            {
                return CreateTexture2DFromDds(new ReadOnlySpan<byte>(nativeBuffer, (int)length), option);
            }

            byte* pixels = ImageDecodeUtility.DecodeAuto(
                new ReadOnlySpan<byte>(nativeBuffer, (int)length),
                out int w, out int h);
            try
            {
                if (optionReal.PremultiplyAlpha)
                    PremultiplyAlpha(pixels, w * h);

                return CreateTexture2D(pixels, (uint)(w * h * 4), (uint)w, (uint)h, option);
            }
            finally
            {
                NativeMemory.Free(pixels);
            }
        }
        finally
        {
            NativeMemory.Free(nativeBuffer);
        }
    }

    /// <summary>
    /// Creates a Texture2D from file bytes.
    /// DDS files (BC1-BC7) upload their blocks and mip chain verbatim; other formats
    /// (PNG/JPEG) decode to RGBA8 first.
    /// </summary>
    /// <param name="fileBytes">The file bytes containing image data.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2DFromFile(
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null
    )
    {
        if (DdsDecoder.IsDds(fileBytes))
        {
            return CreateTexture2DFromDds(fileBytes, option);
        }

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;

        byte* pixels = ImageDecodeUtility.DecodeAuto(fileBytes, out int w, out int h);
        try
        {
            if (optionReal.PremultiplyAlpha)
                PremultiplyAlpha(pixels, w * h);

            return CreateTexture2D(pixels, (uint)(w * h * 4), (uint)w, (uint)h, option);
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    /// <summary>
    /// Creates a Texture2D from a DDS file holding block-compressed data (BC1-BC7).
    /// No pixel decoding happens; the mip chain stored in the file is uploaded as-is,
    /// overriding <see cref="ImageLoadOption.MipLevels"/>. The sRGB-ness of the BC format
    /// follows <see cref="ImageLoadOption.Format"/>.
    /// </summary>
    /// <param name="fileBytes">The complete DDS file bytes.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    /// <exception cref="ImageDecodeException">Invalid, uncompressed or unsupported DDS data.</exception>
    public unsafe Texture2D CreateTexture2DFromDds(
        ReadOnlySpan<byte> fileBytes,
        ImageLoadOption? option = null
    )
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        bool srgb = PixelFormatUtility.IsSrgbFormat(optionReal.Format);
        DdsDecoder.Decode(fileBytes, srgb, out PixelFormat format, out int width, out int height, out int mipLevels, out int dataOffset);

        if (!PixelFormatUtility.TryGetCompressedBlockSize(format, out uint blockBytes))
        {
            throw new ImageDecodeException($"DDS pixel format {format} is not block-compressed.");
        }

        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            format,
            (uint)width,
            (uint)height,
            1,
            (uint)mipLevels,
            optionReal.Usage,
            1,
            optionReal.Name
        );
        GPUTexture texture = _device.CreateTexture(textureDescriptor);

        TextureViewDescriptor textureViewDescriptor = new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture2D,
            mipLevelCount: (uint)mipLevels
        );
        GPUTextureView textureView = _device.CreateTextureView(textureViewDescriptor);

        fixed (byte* basePointer = fileBytes)
        {
            byte* pointer = basePointer + dataOffset;
            for (int level = 0; level < mipLevels; level++)
            {
                uint byteCount = DdsDecoder.GetMipByteCount(width, height, level, blockBytes);
                _device.WriteTexture(texture, pointer, byteCount, (uint)level);
                pointer += byteCount;
            }
        }

        return new Texture2D(
            _device,
            texture,
            textureView,
            optionReal.SlicePadding
        );
    }

    /// <summary>
    /// Creates an empty Texture2D at the specification dictated by an image file's
    /// header (see <see cref="ImageDecodeUtility.GetImageFileInfo"/>), for streaming
    /// loads: the texture's identity and specification are final from creation, and its
    /// content is uploaded in place later via <see cref="UploadTexture2DContent"/>.
    /// The backend zero-initializes the content, so until the upload arrives sampling
    /// yields transparent black.
    /// <br/>Internal building block of <see cref="CreateTexture2DStreaming"/>; not a
    /// public creation path.
    /// </summary>
    /// <param name="info">The probed file header.</param>
    /// <param name="option">Image load options. For block-compressed files the format
    /// and mip count come from <paramref name="info"/> instead (the same rule as
    /// <see cref="CreateTexture2DFromDds"/>).</param>
    /// <returns>A new Texture2D instance with zero-initialized content.</returns>
    internal Texture2D CreateTexture2DFromHeader(in ImageFileInfo info, ImageLoadOption? option = null)
    {
        if (!info.IsBlockCompressed)
        {
            return CreateTexture2D((uint)info.Width, (uint)info.Height, option);
        }

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;

        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            info.Format,
            (uint)info.Width,
            (uint)info.Height,
            1,
            (uint)info.MipLevels,
            optionReal.Usage,
            1,
            optionReal.Name
        );
        GPUTexture texture = _device.CreateTexture(textureDescriptor);

        TextureViewDescriptor textureViewDescriptor = new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture2D,
            mipLevelCount: (uint)info.MipLevels
        );
        GPUTextureView textureView = _device.CreateTextureView(textureViewDescriptor);

        return new Texture2D(
            _device,
            texture,
            textureView,
            optionReal.SlicePadding
        );
    }

    /// <summary>
    /// Decodes image file bytes and uploads them into an existing texture in place,
    /// preserving its identity: the native texture, its views and every bind group
    /// built from them stay valid. Pair with <see cref="CreateTexture2DFromHeader"/>
    /// for streaming loads. DDS files (BC1-BC7) upload their blocks and mip chain
    /// verbatim; other formats (PNG/JPEG) decode to RGBA8 and upload mip 0.
    /// <br/>There is no thread constraint: the upload may run on any thread.
    /// <br/>Internal building block of <see cref="CreateTexture2DStreaming"/>; not a
    /// public upload path.
    /// </summary>
    /// <param name="texture">The target texture, previously created at the file's
    /// specification.</param>
    /// <param name="fileBytes">The complete image file bytes.</param>
    /// <param name="option">Image load options (premultiply, sRGB-ness of DDS formats).
    /// Should match the options the texture was created with.</param>
    /// <exception cref="InvalidOperationException">The texture is not writable.</exception>
    /// <exception cref="ImageDecodeException">The file is invalid, or its specification
    /// differs from the texture's (the texture is left untouched).</exception>
    internal unsafe void UploadTexture2DContent(Texture2D texture, ReadOnlySpan<byte> fileBytes, ImageLoadOption? option = null)
    {
        if (!texture.IsWriteable)
        {
            throw new InvalidOperationException("The texture is not writeable");
        }

        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        bool srgb = PixelFormatUtility.IsSrgbFormat(optionReal.Format);

        if (DdsDecoder.IsDds(fileBytes))
        {
            // Full validation, including the mip chain length.
            DdsDecoder.Decode(fileBytes, srgb, out PixelFormat format, out int width, out int height, out int mipLevels, out int dataOffset);

            if (width != (int)texture.Width || height != (int)texture.Height
                || format != texture.NativeTexture.PixelFormat || mipLevels != (int)texture.MipLevels)
            {
                throw new ImageDecodeException(
                    $"DDS specification {width}x{height} {format} x{mipLevels} does not match the target texture " +
                    $"{texture.Width}x{texture.Height} {texture.NativeTexture.PixelFormat} x{texture.MipLevels}.");
            }

            if (!PixelFormatUtility.TryGetCompressedBlockSize(format, out uint blockBytes))
            {
                throw new ImageDecodeException($"DDS pixel format {format} is not block-compressed.");
            }

            fixed (byte* basePointer = fileBytes)
            {
                byte* pointer = basePointer + dataOffset;
                for (int level = 0; level < mipLevels; level++)
                {
                    uint byteCount = DdsDecoder.GetMipByteCount(width, height, level, blockBytes);
                    _device.WriteTexture(texture.NativeTexture, pointer, byteCount, (uint)level);
                    pointer += byteCount;
                }
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
                PremultiplyAlpha(pixels, w * h);

            _device.WriteTexture(texture.NativeTexture, pixels, (uint)(w * h * 4));
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    /// <summary>
    /// Creates a Texture2D whose content streams in asynchronously: the header is probed
    /// from the stream (reading only the bytes each format's header needs, see
    /// <see cref="ImageDecodeUtility.GetImageFileInfo(Stream, bool)"/>), the texture is
    /// created at its final specification, and the file content is then read and uploaded
    /// in place on a thread-pool thread. The texture's identity never changes; a failed
    /// upload leaves the zero-initialized content and logs a warning.
    /// <br/>On success the stream's ownership transfers to the streaming task, which
    /// disposes it on completion; when probing fails (an <see cref="ImageDecodeException"/>
    /// escapes this call) the caller keeps ownership of the stream.
    /// </summary>
    /// <param name="stream">A seekable stream over the image file, positioned at the start.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance with zero-initialized content.</returns>
    /// <exception cref="ImageDecodeException">Unrecognized, truncated or corrupt header.</exception>
    public Texture2D CreateTexture2DStreaming(Stream stream, ImageLoadOption? option = null)
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        bool srgb = PixelFormatUtility.IsSrgbFormat(optionReal.Format);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(stream, srgb);
        try
        {
            Texture2D texture = CreateTexture2DFromHeader(info, optionReal);
            // Fire-and-forget: the task captures everything it needs, observes its own
            // failures, and is referenced by nothing once it completes.
            _ = StreamTexture2DContentAsync(texture, stream, optionReal);
            return texture;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Read the whole file and upload its content in place off-thread; the stream is
    /// disposed when done. The file bytes are held in native memory so a multi-MB
    /// texture never lands on the LOH; the few header bytes consumed by the probe are
    /// re-read, which is negligible next to the full-file sequential read.
    /// </summary>
    private async Task StreamTexture2DContentAsync(Texture2D texture, Stream stream, ImageLoadOption option)
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
            // Includes a disposed texture when its owner was disposed mid-upload.
            Log.Warning($"Failed to stream texture '{texture.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a Texture2D with a solid color.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="color">The solid color to fill the texture with.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        uint width,
        uint height,
        Color32 color,
        ImageLoadOption? option = null
    )
    {
        int length = (int)(width * height);
        Color32* data = Alloc<Color32>(length);
        Memset(data, length, color);
        Texture2D texture = CreateTexture2D(
            (byte*)data,
            (uint)sizeof(Color32) * width * height,
            width,
            height,
            option
        );
        Free(data);
        return texture;
    }

    /// <summary>
    /// Creates a Texture2D from raw data.
    /// </summary>
    /// <param name="data">The raw image data.</param>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        ReadOnlySpan<byte> data,
        uint width,
        uint height,
        ImageLoadOption? option = null
    )
    {
        fixed (byte* ptr = data)
        {
            return CreateTexture2D(
                ptr,
                (uint)data.Length,
                width,
                height,
                option
            );
        }
    }

    /// <summary>
    /// Creates a Texture2D from raw data pointer.
    /// </summary>
    /// <param name="data">Pointer to the raw image data.</param>
    /// <param name="size">Size of the data in bytes.</param>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        byte* data,
        uint size,
        uint width,
        uint height,
        ImageLoadOption? option = null
    )
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;

        CreateTextureCore(width, height, option, out GPUTexture texture, out GPUTextureView textureView);

        _device.WriteTexture(
            texture,
            data,
            size
        );

        return new Texture2D(
            _device,
            texture,
            textureView,
            optionReal.SlicePadding
        );
    }

    /// <summary>
    /// Creates an empty Texture2D.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="option">Image load options.</param>
    /// <returns>A new Texture2D instance.</returns>
    public unsafe Texture2D CreateTexture2D(
        uint width,
        uint height,
        ImageLoadOption? option = null
    )
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;

        CreateTextureCore(width, height, option, out GPUTexture texture, out GPUTextureView textureView);

        return new Texture2D(
            _device,
            texture,
            textureView,
            optionReal.SlicePadding
        );
    }

    /// <summary>
    /// Creates a Texture2D from existing GPU resources.
    /// <br/>By default the wrapper does NOT take ownership of
    /// <paramref name="texture"/> and <paramref name="textureView"/>: being created
    /// outside, their lifetime is managed by the caller (e.g. the frame buffer whose
    /// attachments they are). Pass <paramref name="ownsResources"/> to transfer
    /// ownership to the wrapper instead (its disposal then releases the texture and
    /// view).
    /// </summary>
    /// <param name="texture">The GPU texture.</param>
    /// <param name="textureView">The GPU texture view.</param>
    /// <param name="ownsResources">Whether the wrapper owns (and disposes) the
    /// texture and view.</param>
    /// <returns>A new Texture2D instance.</returns>
    public Texture2D CreateTexture2D(
        GPUTexture texture,
        GPUTextureView textureView,
        bool ownsResources = false
    )
    {
        return new Texture2D(
            _device,
            texture,
            textureView,
            null,
            ownsResources
        );
    }

    /// <summary>
    /// Creates the core GPU texture and texture view.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="option">Image load options.</param>
    /// <param name="texture">The created GPU texture.</param>
    /// <param name="textureView">The created GPU texture view.</param>
    public void CreateTextureCore(uint width, uint height, ImageLoadOption? option, out GPUTexture texture, out GPUTextureView textureView)
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;
        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            optionReal.Format,
            width,
            height,
            1,
            optionReal.MipLevels,
            optionReal.Usage,
            1,
            optionReal.Name
        );

        texture = _device.CreateTexture(textureDescriptor);

        TextureViewDescriptor textureViewDescriptor = new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture2D
        );

        textureView = _device.CreateTextureView(textureViewDescriptor);
    }

    /// <summary>
    /// Creates a BC3 texture compressor from the texture-compress-bc3 shader
    /// (MainCS&lt;let IsSRGB&gt;): the linear (false) and sRGB (true) compression
    /// dispatchers are the shader's specializations, construction-bound per variant.
    /// </summary>
    /// <param name="shader">The texture-compress-bc3 shader.</param>
    /// <returns>A new TextureCompressorBC3 instance.</returns>
    public TextureCompressorBC3 CreateTextureCompressorBC3(Shader shader)
    {
        return new TextureCompressorBC3(this, shader);
    }

    /// <summary>
    /// Hot reloads a Texture2D with new image data, optimizing for when dimensions match.
    /// </summary>
    /// <param name="texture2D">The existing Texture2D to hot reload.</param>
    /// <param name="fileBytes">The new image file bytes.</param>
    /// <param name="option">Image load options.</param>
    public unsafe void UnsafeHotReloadTexture2DByFile(Texture2D texture2D, ReadOnlySpan<byte> fileBytes, ImageLoadOption? option = null)
    {
        ImageLoadOption optionReal = option ?? ImageLoadOption.Default;

        byte* pixels = ImageDecodeUtility.DecodeAuto(fileBytes, out int w, out int h);
        try
        {
            if (optionReal.PremultiplyAlpha)
                PremultiplyAlpha(pixels, w * h);

            if (w == texture2D.Width && h == texture2D.Height)
            {
                _device.WriteTexture(texture2D.NativeTexture, pixels, (uint)(w * h * 4));
            }
            else
            {
                CreateTextureCore(
                    (uint)w,
                    (uint)h,
                    option ?? ImageLoadOption.Default,
                    out GPUTexture texture, out GPUTextureView textureView);

                _device.WriteTexture(texture, pixels, (uint)(w * h * 4));
                texture2D.UnsafeHotReload(texture, textureView);
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    /// <summary>
    /// Encodes a Texture2D to PNG format and writes to the specified stream.
    /// </summary>
    /// <param name="texture">The texture to encode. Must be RGBA8Unorm or RGBA8UnormSrgb format.</param>
    /// <param name="output">The output stream to write the PNG data.</param>
    /// <exception cref="ArgumentException">Thrown when texture format is not supported.</exception>
    public unsafe void EncodeTextureToPNG(Texture2D texture, Stream output)
    {
        PixelFormat format = texture.NativeTexture.PixelFormat;
        if (format != PixelFormat.RGBA8Unorm && format != PixelFormat.RGBA8UnormSrgb)
        {
            throw new ArgumentException(
                $"Texture format {format} is not supported. Only RGBA8Unorm and RGBA8UnormSrgb are supported.",
                nameof(texture));
        }

        uint width = texture.Width;
        uint height = texture.Height;
        nuint dataSize = (nuint)(width * height * 4);

        byte* data = (byte*)NativeMemory.Alloc(dataSize);
        try
        {
            _device.ReadTexture(texture.NativeTexture, data, (uint)dataSize);

            ImageWriter writer = new ImageWriter();
            writer.WritePng(data, (int)width, (int)height, ColorComponents.RedGreenBlueAlpha, output);
        }
        finally
        {
            NativeMemory.Free(data);
        }
    }

    /// <summary>
    /// Converts RGBA8 pixel data from straight alpha to premultiplied alpha in-place.
    /// Each pixel [R, G, B, A] becomes [R*A/255, G*A/255, B*A/255, A].
    /// Uses AVX2 (8 pixels/cycle), SSSE3+SSE2 (4 pixels/cycle), or scalar fallback.
    /// </summary>
    internal static unsafe void PremultiplyAlpha(byte* data, int pixelCount)
    {
        int offset = 0;

        // AVX2: 8 RGBA pixels (32 bytes) per iteration
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

                // Exact division by 255 for values 0-65025: (x + 1 + (x >> 8)) >> 8
                Vector256<ushort> divLo = Avx2.ShiftRightLogical(
                    Avx2.Add(Avx2.Add(mulLo, one16), Avx2.ShiftRightLogical(mulLo, 8)), 8);
                Vector256<ushort> divHi = Avx2.ShiftRightLogical(
                    Avx2.Add(Avx2.Add(mulHi, one16), Avx2.ShiftRightLogical(mulHi, 8)), 8);

                // Pack 16-bit back to 8-bit with lane-crossing fix
                Vector128<byte> pack01 = Sse2.PackUnsignedSaturate(
                    divLo.GetLower().AsInt16(), divHi.GetLower().AsInt16());
                Vector128<byte> pack45 = Sse2.PackUnsignedSaturate(
                    divLo.GetUpper().AsInt16(), divHi.GetUpper().AsInt16());
                Vector256<byte> premul = Vector256.Create(pack01, pack45);

                // Restore original alpha channel
                Vector256<byte> result = Avx2.Or(
                    Avx2.And(premul, rgbMask),
                    Avx2.And(src, alphaMask));

                result.StoreUnsafe(ref *p);
            }
        }

        // SSSE3 + SSE2: 4 RGBA pixels (16 bytes) per iteration
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

        // Scalar fallback for remaining pixels
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
