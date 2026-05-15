namespace Alco.Rendering.Codec.Image;

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
