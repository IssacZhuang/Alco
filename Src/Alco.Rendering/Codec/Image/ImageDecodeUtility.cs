namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Static facade for image decoding. Dispatches to format-specific decoders.
/// All methods are thread-safe. Returned pointers are caller-owned and must be freed via <c>NativeMemory.Free</c>.
/// </summary>
public static unsafe class ImageDecodeUtility
{
    /// <summary>
    /// Query image dimensions without full decode.
    /// Detects format by magic bytes: PNG (89 50 4E 47) or JPEG (FF D8).
    /// </summary>
    /// <param name="data">Raw image file bytes.</param>
    /// <returns>Image width and height in pixels.</returns>
    /// <exception cref="ImageDecodeException">Unrecognized format or corrupt header.</exception>
    public static (int Width, int Height) GetImageInfo(ReadOnlySpan<byte> data)
    {
        throw new NotImplementedException();
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
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Decode JPEG data to RGBA8 pixel buffer.
    /// </summary>
    /// <param name="data">JPEG-encoded file bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Pointer to RGBA8 pixel data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="ImageDecodeException">Invalid or unsupported JPEG data.</exception>
    public static byte* DecodeJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        throw new NotImplementedException();
    }

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
        throw new NotImplementedException();
    }
}
