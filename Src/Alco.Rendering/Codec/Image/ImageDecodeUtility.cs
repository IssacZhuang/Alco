using System.Runtime.InteropServices;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Static facade for image decoding. Dispatches to format-specific decoders.
/// All methods are thread-safe. Returned pointers are caller-owned and must be freed via <see cref="NativeMemory.Free"/>.
/// </summary>
public static unsafe class ImageDecodeUtility
{
    /// <summary>
    /// Query image dimensions without full decode.
    /// Detects format by magic bytes: PNG (89 50 4E 47) or JPEG (FF D8).
    /// </summary>
    /// <exception cref="ImageDecodeException">Unrecognized format or corrupt header.</exception>
    public static (int Width, int Height) GetImageInfo(ReadOnlySpan<byte> data)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Decode PNG → RGBA8.
    /// </summary>
    /// <exception cref="ImageDecodeException">Invalid or unsupported PNG data.</exception>
    public static byte* DecodePng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Decode JPEG → RGBA8.
    /// </summary>
    /// <exception cref="ImageDecodeException">Invalid or unsupported JPEG data.</exception>
    public static byte* DecodeJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Auto-detect format by header magic and decode.
    /// PNG (89 50 4E 47) → DecodePng, JPEG (FF D8) → DecodeJpeg.
    /// </summary>
    /// <exception cref="ImageDecodeException">Unknown format or corrupt data.</exception>
    public static byte* DecodeAuto(ReadOnlySpan<byte> data, out int width, out int height)
    {
        throw new NotImplementedException();
    }
}
