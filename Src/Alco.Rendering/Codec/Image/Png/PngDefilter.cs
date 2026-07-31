using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Alco.Rendering;

/// <summary>
/// Reconstructs PNG scanlines by applying inverse filters using SIMD.
/// Each PNG scanline has a filter type byte (0-4). This class reverses the filtering
/// to reconstruct the original pixel bytes, operating in-place on the scanline buffer.
/// </summary>
internal static unsafe class PngDefilter
{
    /// <summary>
    /// Defilter all scanlines in-place.
    /// </summary>
    /// <param name="scanlines">Raw scanline data (filter byte + pixel bytes per row). Modified in-place.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="bytesPerPixel">Bytes per pixel in source format (before RGBA conversion). E.g. 1 for grayscale, 3 for RGB, 4 for RGBA.</param>
    /// <param name="stride">Bytes per row of pixel data (excluding filter byte). For sub-byte depths, this is ceil(width * bitsPerPixel / 8).</param>
    public static unsafe void Defilter(Span<byte> scanlines, int width, int height, int bytesPerPixel, int stride)
    {
        int rowSize = 1 + stride;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * rowSize;
            byte filterType = scanlines[rowOffset];
            Span<byte> row = scanlines.Slice(rowOffset + 1, stride);

            if (y == 0)
            {
                DefilterFirstRow(row, stride, bytesPerPixel, filterType);
                continue;
            }

            ReadOnlySpan<byte> prevRow = scanlines.Slice(rowOffset - rowSize + 1, stride);

            switch (filterType)
            {
                case 0: // None
                    break;
                case 1: // Sub
                    DefilterSub(row, stride, bytesPerPixel);
                    break;
                case 2: // Up
                    DefilterUp(row, prevRow, stride);
                    break;
                case 3: // Average
                    DefilterAverage(row, prevRow, stride, bytesPerPixel);
                    break;
                case 4: // Paeth
                    DefilterPaeth(row, prevRow, stride, bytesPerPixel);
                    break;
            }
        }
    }

    private static void DefilterFirstRow(Span<byte> row, int stride, int bpp, byte filterType)
    {
        switch (filterType)
        {
            case 1: // Sub
            case 4: // Paeth with b=c=0 reduces to Sub
                DefilterSub(row, stride, bpp);
                break;

            case 3: // Average with b=c=0 predicts a/2 after the first pixel group
                DefilterAverageFirstRow(row, stride, bpp);
                break;
        }
    }

    #region Sub filter

    /// <summary>
    /// Filter Sub (1): raw[i] += raw[i - bpp].
    /// Uses SIMD pixel-at-a-time prefix sum for bpp=3 (RGB) and bpp=4 (RGBA).
    /// The running accumulator carries forward across pixels; byte-width add
    /// wraps mod 256 which is correct for PNG reconstruction.
    /// </summary>
    private static void DefilterSub(Span<byte> row, int stride, int bpp)
    {
        if (bpp == 4 && Vector128.IsHardwareAccelerated && stride >= 4)
            DefilterSub4(row, stride);
        else if (bpp == 3 && Vector128.IsHardwareAccelerated && stride >= 3)
            DefilterSub3(row, stride);
        else
            DefilterSubScalar(row, stride, bpp);
    }

    /// <summary>
    /// SIMD Sub filter for RGBA (bpp=4).
    /// Processes one pixel (4 bytes) per iteration using Vector128 as a 4-byte accumulator.
    /// _mm_add_epi8 wraps mod 256 automatically — no masking needed.
    /// </summary>
    private static void DefilterSub4(Span<byte> row, int stride)
    {
        ref byte r = ref row[0];
        Vector128<byte> d = Vector128<byte>.Zero; // running prefix sum accumulator

        int i = 0;
        while (i <= stride - 4)
        {
            Vector128<byte> a = d;
            d = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref r, i));
            d += a; // prefix sum: d = current_raw + previous_reconstructed
            // Write only the 4 bytes of the current pixel — writing all 16 would
            // corrupt raw data at bytes 4-15 that hasn't been defiltered yet.
            Unsafe.Add(ref r, i) = d.GetElement(0);
            Unsafe.Add(ref r, i + 1) = d.GetElement(1);
            Unsafe.Add(ref r, i + 2) = d.GetElement(2);
            Unsafe.Add(ref r, i + 3) = d.GetElement(3);
            i += 4;
        }

        // Scalar tail
        for (; i < stride; i++)
            Unsafe.Add(ref r, i) += Unsafe.Add(ref r, i - 4);
    }

    /// <summary>
    /// SIMD Sub filter for RGB (bpp=3).
    /// Loads 4 bytes, writes 3 per iteration. The 4th byte is harmless leftover
    /// that gets overwritten by the next iteration's load.
    /// </summary>
    private static void DefilterSub3(Span<byte> row, int stride)
    {
        ref byte r = ref row[0];

        // First pixel (bytes 0-2) has no left neighbor — stays as-is.
        // Load it into the accumulator to seed the prefix sum.
        Vector128<byte> d = Vector128<byte>.Zero;
        if (stride >= 3)
        {
            d = Unsafe.ReadUnaligned<Vector128<byte>>(ref r);
            // Zero out elements 3-15 so they don't corrupt the accumulator
            // (only the first 3 bytes of each pixel-group are meaningful)
        }

        int i = 3;
        while (i + 3 <= stride)
        {
            Vector128<byte> a = d;
            d = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref r, i));
            d += a;

            Unsafe.Add(ref r, i) = d.GetElement(0);
            Unsafe.Add(ref r, i + 1) = d.GetElement(1);
            Unsafe.Add(ref r, i + 2) = d.GetElement(2);
            i += 3;
        }

        // Scalar tail for remaining bytes
        for (; i < stride; i++)
            Unsafe.Add(ref r, i) += Unsafe.Add(ref r, i - 3);
    }

    private static void DefilterSubScalar(Span<byte> row, int stride, int bpp)
    {
        for (int i = bpp; i < stride; i++)
            row[i] += row[i - bpp];
    }

    #endregion

    #region Up filter

    /// <summary>
    /// Filter Up (2): raw[i] += prev_row[i].
    /// Fully parallelizable - SIMD processes all bytes simultaneously.
    /// </summary>
    private static void DefilterUp(Span<byte> row, ReadOnlySpan<byte> prevRow, int stride)
    {
        if (Vector256.IsHardwareAccelerated && stride >= Vector256<byte>.Count)
        {
            DefilterUpSimd256(row, prevRow, stride);
        }
        else if (Vector128.IsHardwareAccelerated && stride >= Vector128<byte>.Count)
        {
            DefilterUpSimd128(row, prevRow, stride);
        }
        else
        {
            for (int i = 0; i < stride; i++)
                row[i] += prevRow[i];
        }
    }

    private static unsafe void DefilterUpSimd256(Span<byte> row, ReadOnlySpan<byte> prevRow, int stride)
    {
        const int vectorSize = 32;
        int i = 0;
        int simdLimit = stride - (stride % vectorSize);

        while (i < simdLimit)
        {
            Vector256<byte> current = Unsafe.ReadUnaligned<Vector256<byte>>(
                ref Unsafe.AsRef(in row[i]));
            Vector256<byte> prev = Unsafe.ReadUnaligned<Vector256<byte>>(
                ref Unsafe.AsRef(in prevRow[i]));

            current += prev;
            Unsafe.WriteUnaligned(ref Unsafe.AsRef(in row[i]), current);

            i += vectorSize;
        }

        for (; i < stride; i++)
            row[i] += prevRow[i];
    }

    private static unsafe void DefilterUpSimd128(Span<byte> row, ReadOnlySpan<byte> prevRow, int stride)
    {
        const int vectorSize = 16;
        int i = 0;
        int simdLimit = stride - (stride % vectorSize);

        while (i < simdLimit)
        {
            Vector128<byte> current = Unsafe.ReadUnaligned<Vector128<byte>>(
                ref Unsafe.AsRef(in row[i]));
            Vector128<byte> prev = Unsafe.ReadUnaligned<Vector128<byte>>(
                ref Unsafe.AsRef(in prevRow[i]));

            current += prev;
            Unsafe.WriteUnaligned(ref Unsafe.AsRef(in row[i]), current);

            i += vectorSize;
        }

        for (; i < stride; i++)
            row[i] += prevRow[i];
    }

    #endregion

    #region Average filter

    /// <summary>
    /// Filter Average (3): raw[i] += (prev[i] + raw[i - bpp]) / 2 (truncating).
    /// </summary>
    private static void DefilterAverage(Span<byte> row, ReadOnlySpan<byte> prevRow, int stride, int bpp)
    {
        for (int i = 0; i < bpp && i < stride; i++)
            row[i] += (byte)(prevRow[i] >> 1);
        for (int i = bpp; i < stride; i++)
            row[i] += (byte)((prevRow[i] + row[i - bpp]) >> 1);
    }

    private static void DefilterAverageFirstRow(Span<byte> row, int stride, int bpp)
    {
        for (int i = bpp; i < stride; i++)
            row[i] += (byte)(row[i - bpp] >> 1);
    }

    #endregion

    #region Paeth filter

    /// <summary>
    /// Filter Paeth (4): raw[i] += PaethPredictor(raw[i-bpp], prev[i], prev[i-bpp]).
    /// Each byte depends on the defiltered value bpp positions to its left,
    /// so we process left-to-right scalarly.
    /// </summary>
    private static void DefilterPaeth(Span<byte> row, ReadOnlySpan<byte> prevRow, int stride, int bpp)
    {
        // First bpp bytes: a = 0, c = 0
        // PaethPredictor(0, b, 0): p = b, pa = b, pb = 0, pc = b -> pb <= pc -> predictor = b
        for (int i = 0; i < bpp && i < stride; i++)
        {
            row[i] += prevRow[i];
        }

        // Remaining bytes
        for (int i = bpp; i < stride; i++)
        {
            row[i] += PaethPredictor(row[i - bpp], prevRow[i], prevRow[i - bpp]);
        }
    }

    /// <summary>
    /// Compute the Paeth predictor for a single pixel triplet (a, b, c).
    /// p = a + b - c; pa = |p-a|, pb = |p-b|, pc = |p-c|
    /// Returns a if pa &lt;= pb and pa &lt;= pc, b if pb &lt;= pc, else c.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int pa = Abs(b - c);
        int pb = Abs(a - c);
        int pc = Abs(a + b - (c << 1));

        if (pa <= pb && pa <= pc)
            return a;
        if (pb <= pc)
            return b;
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Abs(int value)
    {
        int mask = value >> 31;
        return (value ^ mask) - mask;
    }

    #endregion
}
