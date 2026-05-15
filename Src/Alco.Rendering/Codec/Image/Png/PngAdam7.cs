namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Adam7 interlace pass layout computation and pixel merging.
/// </summary>
internal static class PngAdam7
{
    private static readonly (int StartX, int StartY, int IncX, int IncY)[] s_passParams =
    [
        (0, 0, 8, 8),  // Pass 1
        (4, 0, 8, 8),  // Pass 2
        (0, 4, 4, 8),  // Pass 3
        (2, 0, 4, 4),  // Pass 4
        (0, 2, 2, 4),  // Pass 5
        (1, 0, 2, 2),  // Pass 6
        (0, 1, 1, 2),  // Pass 7
    ];

    /// <summary>
    /// Adam7 pass parameters: (startX, startY, incrementX, incrementY).
    /// </summary>
    public static ReadOnlySpan<(int StartX, int StartY, int IncX, int IncY)> PassParams =>
        s_passParams.AsSpan();

    /// <summary>
    /// Compute the dimensions of a specific interlace pass.
    /// </summary>
    /// <param name="pass">Pass index (0-6).</param>
    /// <param name="imageWidth">Full image width in pixels.</param>
    /// <param name="imageHeight">Full image height in pixels.</param>
    /// <returns>Width and height of the sub-image for this pass, or (0, 0) if the pass is empty.</returns>
    public static (int Width, int Height) GetPassSize(int pass, int imageWidth, int imageHeight)
    {
        var (startX, startY, incX, incY) = PassParams[pass];

        int passWidth = Math.Max(0, (imageWidth - startX + incX - 1) / incX);
        int passHeight = Math.Max(0, (imageHeight - startY + incY - 1) / incY);

        if (passWidth <= 0 || passHeight <= 0)
            return (0, 0);

        return (passWidth, passHeight);
    }

    /// <summary>
    /// Merge one pass's sub-image data into the final output buffer.
    /// </summary>
    /// <param name="output">Full output image buffer (width * height * bytesPerPixel).</param>
    /// <param name="outputStride">Stride of the output buffer (width * bytesPerPixel).</param>
    /// <param name="passData">Defiltered scanline data for this pass (filter byte + pixel bytes per row).</param>
    /// <param name="passStride">Stride of pass scanline data (1 + passWidth * bytesPerPixel).</param>
    /// <param name="pass">Pass index (0-6).</param>
    /// <param name="imageWidth">Full image width.</param>
    /// <param name="imageHeight">Full image height.</param>
    /// <param name="bytesPerPixel">Bytes per pixel in source format.</param>
    public static unsafe void MergePass(
        byte* output, int outputStride,
        ReadOnlySpan<byte> passData, int passStride,
        int pass, int imageWidth, int imageHeight, int bytesPerPixel)
    {
        var (startX, startY, incX, incY) = PassParams[pass];
        var (passWidth, passHeight) = GetPassSize(pass, imageWidth, imageHeight);

        if (passWidth == 0 || passHeight == 0)
            return;

        fixed (byte* passPtr = passData)
        {
            for (int y = 0; y < passHeight; y++)
            {
                int passRowOffset = y * passStride + 1; // Skip filter byte
                int destY = startY + y * incY;
                int destRowBase = destY * outputStride;

                for (int x = 0; x < passWidth; x++)
                {
                    int destX = startX + x * incX;
                    int srcOffset = passRowOffset + x * bytesPerPixel;
                    int destOffset = destRowBase + destX * bytesPerPixel;

                    for (int b = 0; b < bytesPerPixel; b++)
                        output[destOffset + b] = passPtr[srcOffset + b];
                }
            }
        }
    }
}
