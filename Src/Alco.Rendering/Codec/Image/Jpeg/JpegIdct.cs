using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Inverse Discrete Cosine Transform for JPEG 8x8 blocks.
/// Uses separable 1D passes with a precomputed cosine table and SIMD-accelerated output.
/// </summary>
internal static class JpegIdct
{
    // Level shift for JPEG (converts from signed to unsigned).
    private const float LevelShift = 128.0f;

    /// <summary>
    /// Precomputed cosine table for the 8-point 1D DCT-III.
    /// Layout: CosTable[n * 8 + k] = cos((2*n+1) * k * pi / 16) for n,k = 0..7.
    /// The first column (k=0) is all 1.0 since cos(0) = 1.
    /// </summary>
    private static ReadOnlySpan<float> CosTable => new float[]
    {
        // n=0: cos(k*pi/16)
         1.000000000000000f,  0.980785280403230f,  0.923879532511287f,  0.831469612302545f,
         0.707106781186548f,  0.555570233019602f,  0.382683432365090f,  0.195090322016128f,
        // n=1: cos(3*k*pi/16)
         1.000000000000000f,  0.831469612302545f,  0.382683432365090f, -0.195090322016128f,
        -0.707106781186548f, -0.980785280403230f, -0.923879532511287f, -0.555570233019602f,
        // n=2: cos(5*k*pi/16)
         1.000000000000000f,  0.555570233019602f, -0.382683432365090f, -0.980785280403230f,
        -0.707106781186548f,  0.195090322016128f,  0.923879532511287f,  0.831469612302545f,
        // n=3: cos(7*k*pi/16)
         1.000000000000000f,  0.195090322016128f, -0.923879532511287f, -0.555570233019602f,
         0.707106781186548f,  0.831469612302545f, -0.382683432365090f, -0.980785280403230f,
        // n=4: cos(9*k*pi/16)
         1.000000000000000f, -0.195090322016128f, -0.923879532511287f,  0.555570233019602f,
         0.707106781186548f, -0.831469612302545f, -0.382683432365090f,  0.980785280403230f,
        // n=5: cos(11*k*pi/16)
         1.000000000000000f, -0.555570233019602f, -0.382683432365090f,  0.980785280403230f,
        -0.707106781186548f, -0.195090322016128f,  0.923879532511287f, -0.831469612302545f,
        // n=6: cos(13*k*pi/16)
         1.000000000000000f, -0.831469612302545f,  0.382683432365090f,  0.195090322016128f,
        -0.707106781186548f,  0.980785280403230f, -0.923879532511287f,  0.555570233019602f,
        // n=7: cos(15*k*pi/16)
         1.000000000000000f, -0.980785280403230f,  0.923879532511287f, -0.831469612302545f,
         0.707106781186548f, -0.555570233019602f,  0.382683432365090f, -0.195090322016128f,
    };

    /// <summary>
    /// Standard JPEG zigzag-to-natural-order lookup table.
    /// Maps from zigzag index (array position) to natural 8x8 position (row-major).
    /// </summary>
    private static ReadOnlySpan<byte> ZigzagOrder => new byte[]
    {
         0,  1,  5,  6, 14, 15, 27, 28,
         2,  4,  7, 13, 16, 26, 29, 42,
         3,  8, 12, 17, 25, 30, 41, 43,
         9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63,
    };

    /// <summary>
    /// Perform IDCT on a dequantized 8x8 block and output as 8-bit samples.
    /// </summary>
    /// <param name="coeffs">64 float coefficients in zigzag order, already dequantized.</param>
    /// <param name="output">Output buffer for 8x8 pixel samples.</param>
    /// <param name="outputStride">Stride between rows in output buffer (typically 8 for a standalone block).</param>
    public static void Transform(ReadOnlySpan<float> coeffs, Span<byte> output, int outputStride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(coeffs.Length, 64, nameof(coeffs));
        ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, outputStride * 8, nameof(output));

        // Stack-allocated 8x8 block in natural order (row-major).
        Span<float> block = stackalloc float[64];
        ZigzagReorder(coeffs, block);

        // Row pass: apply 8-point IDCT to each row.
        for (int row = 0; row < 8; row++)
            Idct8(block.Slice(row * 8, 8));

        // Column pass: apply 8-point IDCT to each column and write output.
        // Hoisted outside loop to avoid CA2014 stackalloc-in-loop warning.
        Span<float> column = stackalloc float[8];
        for (int col = 0; col < 8; col++)
        {
            for (int row = 0; row < 8; row++)
                column[row] = block[row * 8 + col];

            Idct8(column);

            // Level shift, clamp, and write to output.
            WriteColumn(column, output, outputStride, col);
        }
    }

    /// <summary>
    /// Write a column of float samples to the output buffer with level shift and clamping.
    /// Uses SIMD when available for the clamp-and-convert operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteColumn(ReadOnlySpan<float> column, Span<byte> output, int outputStride, int col)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            WriteColumnVector256(column, output, outputStride, col);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            WriteColumnVector128(column, output, outputStride, col);
        }
        else
        {
            WriteColumnScalar(column, output, outputStride, col);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteColumnVector256(ReadOnlySpan<float> column, Span<byte> output, int outputStride, int col)
    {
        var vShift = Vector256.Create(LevelShift);
        var vZero = Vector256.Create(0.0f);
        var vMax = Vector256.Create(255.0f);
        var vHalf = Vector256.Create(0.5f);

        // Upper 4 rows
        var upper = Vector256.Create(column[0], column[1], column[2], column[3], 0, 0, 0, 0);
        upper = Vector256.Clamp(upper + vShift, vZero, vMax) + vHalf;
        for (int row = 0; row < 4; row++)
            output[row * outputStride + col] = (byte)upper[row];

        // Lower 4 rows
        var lower = Vector256.Create(column[4], column[5], column[6], column[7], 0, 0, 0, 0);
        lower = Vector256.Clamp(lower + vShift, vZero, vMax) + vHalf;
        for (int row = 4; row < 8; row++)
            output[row * outputStride + col] = (byte)lower[row - 4];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteColumnVector128(ReadOnlySpan<float> column, Span<byte> output, int outputStride, int col)
    {
        var vShift = Vector128.Create(LevelShift);
        var vZero = Vector128.Create(0.0f);
        var vMax = Vector128.Create(255.0f);
        var vHalf = Vector128.Create(0.5f);

        // Upper 4 rows
        var upper = Vector128.Create(column[0], column[1], column[2], column[3]);
        upper = Vector128.Clamp(upper + vShift, vZero, vMax) + vHalf;
        for (int row = 0; row < 4; row++)
            output[row * outputStride + col] = (byte)upper[row];

        // Lower 4 rows
        var lower = Vector128.Create(column[4], column[5], column[6], column[7]);
        lower = Vector128.Clamp(lower + vShift, vZero, vMax) + vHalf;
        for (int row = 4; row < 8; row++)
            output[row * outputStride + col] = (byte)lower[row - 4];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteColumnScalar(ReadOnlySpan<float> column, Span<byte> output, int outputStride, int col)
    {
        for (int row = 0; row < 8; row++)
        {
            float val = column[row] + LevelShift;
            val = Math.Clamp(val, 0.0f, 255.0f);
            output[row * outputStride + col] = (byte)(val + 0.5f);
        }
    }

    /// <summary>
    /// Reorder coefficients from zigzag order to natural (row-major) order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZigzagReorder(ReadOnlySpan<float> coeffs, Span<float> block)
    {
        var zigzag = ZigzagOrder;
        for (int i = 0; i < 64; i++)
            block[zigzag[i]] = coeffs[i];
    }

    /// <summary>
    /// 8-point 1D IDCT (DCT-III) using matrix-vector multiplication with precomputed cosines.
    ///
    /// Computes the standard JPEG 1D IDCT (DCT-III):
    ///   f(n) = (1/2) * [C(0)*X[0] + sum_{k=1}^{7} X[k]*cos((2n+1)*k*pi/16)]
    /// where C(0) = 1/sqrt(2), C(k) = 1 for k &gt; 0.
    ///
    /// Two separable 1D passes with 1/2 scaling each give the standard 2D JPEG
    /// normalization: f(x,y) = (1/4) * sum C(u)*C(v)*F(u,v)*cos*cos.
    ///
    /// Uses a precomputed 8x8 cosine lookup table to avoid runtime cos() evaluations.
    /// </summary>
    private static void Idct8(Span<float> x)
    {
        // Snapshot inputs to local variables since we write output to the same span.
        float x0 = x[0], x1 = x[1], x2 = x[2], x3 = x[3];
        float x4 = x[4], x5 = x[5], x6 = x[6], x7 = x[7];

        // C(0) = 1/sqrt(2) normalization for the DC coefficient.
        const float InvSqrt2 = 0.707106781186548f;

        var cos = CosTable;

        for (int n = 0; n < 8; n++)
        {
            int row = n << 3; // n * 8
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
