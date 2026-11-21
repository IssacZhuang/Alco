namespace Alco.Rendering;

/// <summary>Encodes image pixels without retaining the input.</summary>
public static unsafe class ImageEncodeUtility
{
    /// <summary>Encodes tightly packed, top-down, straight-alpha RGBA8 using <see cref="PngEncodeMode.Balanced"/>.</summary>
    /// <param name="rgba">Source pixels without row padding.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>A caller-owned PNG byte array.</returns>
    /// <exception cref="ImageEncodeException">The dimensions or amount of pixel data are invalid.</exception>
    public static byte[] EncodePng(ReadOnlySpan<byte> rgba, int width, int height)
        => PngEncoder.Encode(rgba, width, height, PngEncodeMode.Balanced, rowStrideBytes: 0);

    /// <summary>Encodes tightly packed, top-down, straight-alpha RGBA8 using <see cref="PngEncodeMode.Balanced"/>.</summary>
    /// <param name="rgba">Pointer to at least <paramref name="width"/> × <paramref name="height"/> × 4 readable bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>A caller-owned PNG byte array.</returns>
    /// <exception cref="ImageEncodeException">The dimensions are invalid or the pointer is null.</exception>
    public static byte[] EncodePng(byte* rgba, int width, int height)
        => PngEncoder.Encode(rgba, width, height);

    /// <summary>Encodes top-down, straight-alpha RGBA8 as PNG.</summary>
    /// <param name="rgba">Source pixels; row padding is ignored.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="mode">The encoding speed and compression preset.</param>
    /// <param name="rowStrideBytes">Distance between row starts in bytes, or zero for tightly packed rows.</param>
    /// <returns>A caller-owned PNG byte array.</returns>
    /// <exception cref="ImageEncodeException">The dimensions, stride or amount of pixel data are invalid.</exception>
    public static byte[] EncodePng(ReadOnlySpan<byte> rgba, int width, int height, PngEncodeMode mode, int rowStrideBytes = 0)
        => PngEncoder.Encode(rgba, width, height, mode, rowStrideBytes);

    /// <summary>Writes top-down, straight-alpha RGBA8 as PNG without seeking or closing the stream.</summary>
    /// <param name="rgba">Source pixels; row padding is ignored.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="destination">A writable stream at the position where the PNG should start.</param>
    /// <param name="mode">The encoding speed and compression preset.</param>
    /// <param name="rowStrideBytes">Distance between row starts in bytes, or zero for tightly packed rows.</param>
    /// <exception cref="ImageEncodeException">The dimensions, stride or amount of pixel data are invalid.</exception>
    public static void WritePng(
        ReadOnlySpan<byte> rgba, int width, int height, Stream destination,
        PngEncodeMode mode = PngEncodeMode.Balanced, int rowStrideBytes = 0)
        => PngEncoder.Write(rgba, width, height, destination, mode, rowStrideBytes);
}

/// <summary>Selects the speed and compression trade-off for PNG encoding.</summary>
public enum PngEncodeMode
{
    /// <summary>Evaluates all PNG filters and uses standard compression for smaller files.</summary>
    Balanced,

    /// <summary>Evaluates only inexpensive filters and uses fast compression for low latency.</summary>
    Fast,
}
