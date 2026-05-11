using System.Runtime.CompilerServices;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Inverse Discrete Cosine Transform for JPEG 8x8 blocks.
/// Uses separable 1D passes with a precomputed cosine table.
/// Each 1D pass computes: f(n) = (1/2) * [C(0)*X[0] + sum_{k=1}^{7} X[k]*cos((2n+1)*k*pi/16)]
/// where C(0) = 1/sqrt(2).
/// </summary>
internal static class JpegIdct
{
    private const float LevelShift = 128.0f;

    private static ReadOnlySpan<float> CosTable => new float[]
    {
        1.000000000000000f,  0.980785280403230f,  0.923879532511287f,  0.831469612302545f,
        0.707106781186548f,  0.555570233019602f,  0.382683432365090f,  0.195090322016128f,
        1.000000000000000f,  0.831469612302545f,  0.382683432365090f, -0.195090322016128f,
       -0.707106781186548f, -0.980785280403230f, -0.923879532511287f, -0.555570233019602f,
        1.000000000000000f,  0.555570233019602f, -0.382683432365090f, -0.980785280403230f,
       -0.707106781186548f,  0.195090322016128f,  0.923879532511287f,  0.831469612302545f,
        1.000000000000000f,  0.195090322016128f, -0.923879532511287f, -0.555570233019602f,
        0.707106781186548f,  0.831469612302545f, -0.382683432365090f, -0.980785280403230f,
        1.000000000000000f, -0.195090322016128f, -0.923879532511287f,  0.555570233019602f,
        0.707106781186548f, -0.831469612302545f, -0.382683432365090f,  0.980785280403230f,
        1.000000000000000f, -0.555570233019602f, -0.382683432365090f,  0.980785280403230f,
       -0.707106781186548f, -0.195090322016128f,  0.923879532511287f, -0.831469612302545f,
        1.000000000000000f, -0.831469612302545f,  0.382683432365090f,  0.195090322016128f,
       -0.707106781186548f,  0.980785280403230f, -0.923879532511287f,  0.555570233019602f,
        1.000000000000000f, -0.980785280403230f,  0.923879532511287f, -0.831469612302545f,
        0.707106781186548f, -0.555570233019602f,  0.382683432365090f, -0.195090322016128f,
    };

    private static ReadOnlySpan<byte> ZigzagOrder => new byte[]
    {
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    };

    /// <summary>
    /// Perform IDCT on a dequantized 8x8 block and output as 8-bit samples.
    /// </summary>
    public static void Transform(ReadOnlySpan<float> coeffs, Span<byte> output, int outputStride)
    {
        Span<float> block = stackalloc float[64];
        var zigzag = ZigzagOrder;
        for (int i = 0; i < 64; i++)
            block[zigzag[i]] = coeffs[i];

        for (int row = 0; row < 8; row++)
            Idct8(block.Slice(row * 8, 8));

        Span<float> column = stackalloc float[8];
        for (int col = 0; col < 8; col++)
        {
            for (int row = 0; row < 8; row++)
                column[row] = block[row * 8 + col];

            Idct8(column);

            for (int row = 0; row < 8; row++)
            {
                float val = column[row] + LevelShift;
                output[row * outputStride + col] = (byte)Math.Clamp((int)(val + 0.5f), 0, 255);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Idct8(Span<float> x)
    {
        float x0 = x[0], x1 = x[1], x2 = x[2], x3 = x[3];
        float x4 = x[4], x5 = x[5], x6 = x[6], x7 = x[7];

        const float InvSqrt2 = 0.707106781186548f;
        var cos = CosTable;

        for (int n = 0; n < 8; n++)
        {
            int row = n << 3;
            float sum = InvSqrt2 * x0;
            sum += x1 * cos[row + 1];
            sum += x2 * cos[row + 2];
            sum += x3 * cos[row + 3];
            sum += x4 * cos[row + 4];
            sum += x5 * cos[row + 5];
            sum += x6 * cos[row + 6];
            sum += x7 * cos[row + 7];
            x[n] = sum * 0.5f;
        }
    }
}
