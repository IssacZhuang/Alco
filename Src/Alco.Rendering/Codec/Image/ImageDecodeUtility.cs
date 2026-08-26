using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Static facade for image decoding. Dispatches to format-specific decoders.
/// All methods are thread-safe. Returned pointers are caller-owned and must be freed via <c>NativeMemory.Free</c>.
/// </summary>
public static unsafe class ImageDecodeUtility
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Query image dimensions without full decode.
    /// Detects format by magic bytes: PNG (89 50 4E 47) or JPEG (FF D8).
    /// </summary>
    /// <param name="data">Raw image file bytes.</param>
    /// <returns>Image width and height in pixels.</returns>
    /// <exception cref="ImageDecodeException">Unrecognized format or corrupt header.</exception>
    public static (int Width, int Height) GetImageInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[..8].SequenceEqual(PngSignature))
            return PngDecoder.GetInfo(data);

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
            return JpegDecoder.GetInfo(data);

        throw new ImageDecodeException("Unrecognized image format. Expected PNG or JPEG header.");
    }

    /// <summary>
    /// Probe the file-level specification of an image from a stream, reading only the
    /// bytes each format's header needs: 33 for PNG, 128 (148 with the DX10 extension)
    /// for DDS, and the segment headers up to the SOF marker for JPEG (payloads are
    /// skipped, not read). Detects format by magic bytes: PNG (89 50 4E 47),
    /// JPEG (FF D8) or DDS ("DDS ").
    /// </summary>
    /// <param name="stream">The stream positioned at the start of the image file.</param>
    /// <param name="srgb">
    /// Pick the sRGB variant of a DDS file's BC format (for albedo textures); ignored
    /// for PNG/JPEG, whose final texture format is chosen via load options instead.
    /// </param>
    /// <returns>The file-dictated texture specification.</returns>
    /// <exception cref="ImageDecodeException">Unrecognized format, or the header is
    /// truncated or corrupt.</exception>
    public static ImageFileInfo GetImageFileInfo(Stream stream, bool srgb = false)
    {
        // The largest header: a DDS with the DX10 extension.
        Span<byte> header = stackalloc byte[148];
        try
        {
            stream.ReadExactly(header[..2]);

            // JPEG (FF D8): walk segment headers from the stream.
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                (int jpegWidth, int jpegHeight) = JpegDecoder.GetInfo(stream);
                return Uncompressed(jpegWidth, jpegHeight);
            }

            stream.ReadExactly(header[2..8]);

            // PNG: the 8-byte signature plus the whole IHDR chunk.
            if (header[..8].SequenceEqual(PngSignature))
            {
                stream.ReadExactly(header[8..33]);
                (int pngWidth, int pngHeight) = PngDecoder.GetInfo(header[..33]);
                return Uncompressed(pngWidth, pngHeight);
            }

            // DDS: the 128-byte header, plus 20 bytes when the DX10 extension follows.
            if (DdsDecoder.IsDds(header[..8]))
            {
                stream.ReadExactly(header[8..128]);
                int headerLength = 128;
                if (DdsDecoder.HasDx10Header(header[..128]))
                {
                    stream.ReadExactly(header[128..148]);
                    headerLength = 148;
                }
                DdsDecoder.ParseHeader(header[..headerLength], srgb, out _, out PixelFormat format, out int width, out int height, out int mipLevels, out int dataOffset);
                return new ImageFileInfo
                {
                    Width = width,
                    Height = height,
                    IsBlockCompressed = true,
                    Format = format,
                    MipLevels = mipLevels,
                    DataOffset = dataOffset,
                };
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new ImageDecodeException("The stream ended before the image header was complete.", ex);
        }

        throw new ImageDecodeException("Unrecognized image format. Expected DDS, PNG or JPEG header.");

        static ImageFileInfo Uncompressed(int width, int height) => new()
        {
            Width = width,
            Height = height,
            IsBlockCompressed = false,
            Format = PixelFormat.RGBA8Unorm,
            MipLevels = 1,
            DataOffset = 0,
        };
    }

    /// <summary>
    /// Probe the file-level specification from the leading header bytes alone,
    /// without decoding pixels — the per-format header byte requirements and
    /// format detection are those of the stream overload
    /// (<see cref="GetImageFileInfo(Stream, bool)"/>); a partially read file can
    /// be probed ahead of streaming its content.
    /// </summary>
    /// <param name="data">The leading bytes of the image file.</param>
    /// <param name="srgb">
    /// Pick the sRGB variant of a DDS file's BC format (for albedo textures); ignored
    /// for PNG/JPEG, whose final texture format is chosen via load options instead.
    /// </param>
    /// <returns>The file-dictated texture specification.</returns>
    /// <exception cref="ImageDecodeException">Unrecognized format, or the header is
    /// truncated or corrupt (for JPEG, the SOF marker lies beyond the given bytes).</exception>
    public static ImageFileInfo GetImageFileInfo(ReadOnlySpan<byte> data, bool srgb = false)
    {
        if (DdsDecoder.IsDds(data))
        {
            DdsDecoder.ParseHeader(data, srgb, out _, out PixelFormat format, out int width, out int height, out int mipLevels, out int dataOffset);
            return new ImageFileInfo
            {
                Width = width,
                Height = height,
                IsBlockCompressed = true,
                Format = format,
                MipLevels = mipLevels,
                DataOffset = dataOffset,
            };
        }

        (int imageWidth, int imageHeight) = GetImageInfo(data);
        return new ImageFileInfo
        {
            Width = imageWidth,
            Height = imageHeight,
            IsBlockCompressed = false,
            Format = PixelFormat.RGBA8Unorm,
            MipLevels = 1,
            DataOffset = 0,
        };
    }

    /// <summary>
    /// Decode PNG data to RGBA8 pixel buffer.
    /// </summary>
    /// <param name="data">PNG-encoded file bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Pointer to RGBA8 pixel data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="ImageDecodeException">Invalid or unsupported PNG data.</exception>
    public static byte* DecodePng(ReadOnlySpan<byte> data, out int width, out int height)
        => PngDecoder.Decode(data, out width, out height);

    /// <summary>
    /// Decode JPEG data to RGBA8 pixel buffer.
    /// </summary>
    /// <param name="data">JPEG-encoded file bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Pointer to RGBA8 pixel data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="ImageDecodeException">Invalid or unsupported JPEG data.</exception>
    public static byte* DecodeJpeg(ReadOnlySpan<byte> data, out int width, out int height)
        => JpegDecoder.Decode(data, out width, out height);

    /// <summary>
    /// Auto-detect format by header magic and decode to RGBA8.
    /// PNG (89 50 4E 47) decodes via DecodePng, JPEG (FF D8) via DecodeJpeg.
    /// </summary>
    /// <param name="data">Raw image file bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Pointer to RGBA8 pixel data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="ImageDecodeException">Unknown format or corrupt data.</exception>
    public static byte* DecodeAuto(ReadOnlySpan<byte> data, out int width, out int height)
    {
        if (data.Length >= 8 && data[..8].SequenceEqual(PngSignature))
            return PngDecoder.Decode(data, out width, out height);

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
            return JpegDecoder.Decode(data, out width, out height);

        throw new ImageDecodeException("Unrecognized image format. Expected PNG or JPEG header.");
    }
}
