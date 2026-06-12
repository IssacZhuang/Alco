namespace Alco.Rendering;

/// <summary>
/// Static facade for image encoding. Dispatches to format-specific encoders.
/// All methods are thread-safe. Returned byte arrays are caller-owned.
/// </summary>
public static unsafe class ImageEncodeUtility
{
    /// <summary>
    /// Encode RGBA8 pixel data to PNG format.
    /// Uses adaptive row filtering (minimum-sum heuristic) for optimal compression.
    /// </summary>
    /// <param name="rgba">RGBA8 pixel data (width * height * 4 bytes, row-major).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>PNG-encoded file bytes.</returns>
    /// <exception cref="ImageEncodeException">Invalid dimensions or encoding failure.</exception>
    public static byte[] EncodePng(ReadOnlySpan<byte> rgba, int width, int height)
        => PngEncoder.Encode(rgba, width, height);

    /// <summary>
    /// Encode RGBA8 pixel data from a pointer to PNG format.
    /// Uses adaptive row filtering (minimum-sum heuristic) for optimal compression.
    /// </summary>
    /// <param name="rgba">Pointer to RGBA8 pixel data (width * height * 4 bytes, row-major).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>PNG-encoded file bytes.</returns>
    /// <exception cref="ImageEncodeException">Invalid dimensions or encoding failure.</exception>
    public static byte[] EncodePng(byte* rgba, int width, int height)
        => PngEncoder.Encode(rgba, width, height);
}
