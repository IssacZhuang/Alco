using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Alco.Rendering.Codec.Image;

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
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref r, i), d);
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
        Vector128<byte> d = Vector128<byte>.Zero;

        int i = 0;
        // Process 4-byte blocks (writing 3 bytes of useful data each)
        // Stop when remaining is too small for a 4-byte load
        while (i + 3 < stride)
        {
            Vector128<byte> a = d;
            d = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref r, i));
            d += a;

            // Write 3 bytes of the reconstructed pixel
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
    /// Uses SIMD pixel-at-a-time processing for bpp=3 and bpp=4, with the
    /// XOR-based truncating average trick from libpng SSE2:
    ///   avg(a,b) = (a+b+1)/2 - ((a^b) &amp; 1)
    /// This converts hardware rounding average to truncating average.
    /// </summary>
    private static void DefilterAverage(Span<byte> row, ReadOnlySpan<byte> prevRow, int stride, int bpp)
    {
        if (bpp == 4 && Vector128.IsHardwareAccelerated && stride >= 4)
            DefilterAverage4(row, prevRow, stride);
        else if (bpp == 3 && Vector128.IsHardwareAccelerated && stride >= 3)
            DefilterAverage3(row, prevRow, stride);
        else
            DefilterAverageScalar(row, prevRow, stride, bpp);
    }

    /// <summary>
    /// SIMD Average filter for RGBA (bpp=4).
    /// Uses Vector128 as a 4-byte accumulator. The truncating average of a and b is:
    ///   rounding_avg = (a + b + 1) / 2  (hardware avg)
    ///   trunc_avg = rounding_avg - ((a ^ b) &amp; 1)
    /// This avoids widening to 16-bit or doing division.
    /// </summary>
    private static void DefilterAverage4(Span<byte> row, ReadOnlySpan<byte> prevRow, int stride)
    {
        ref byte r = ref row[0];
        ref byte pr = ref Unsafe.AsRef(in prevRow[0]);
        Vector128<byte> d = Vector128<byte>.Zero; // running accumulator (left pixel)

        // First pixel: a=0, predictor = prev/2 = prev >> 1
        Vector128<byte> b0 = Unsafe.ReadUnaligned<Vector128<byte>>(ref pr);
        Vector128<byte> raw0 = Unsafe.ReadUnaligned<Vector128<byte>>(ref r);
        d = raw0 + (b0 >> 1); // truncating divide by 2 via shift
        Unsafe.WriteUnaligned(ref r, d);

        int i = 4;
        while (i <= stride - 4)
        {
            Vector128<byte> a = d; // left pixel (already defiltered)
            Vector128<byte> b = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref pr, i));
            Vector128<byte> raw = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref r, i));

            // Truncating average: (a+b)/2 = (a>>1) + (b>>1) + ((a&b)&1)
            // Works entirely in 8-bit — no widening needed.
            Vector128<byte> avg = (a >> 1) + (b >> 1) + ((a & b) & Vector128.Create((byte)1));

            d = raw + avg;
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref r, i), d);
            i += 4;
        }

        // Scalar tail
        for (; i < stride; i++)
            Unsafe.Add(ref r, i) += (byte)((Unsafe.Add(ref pr, i) + Unsafe.Add(ref r, i - 4)) >> 1);
    }

    /// <summary>
    /// SIMD Average filter for RGB (bpp=3).
    /// Same as bpp=4 but with 3-byte stores via element extraction.
    /// </summary>
    private static void DefilterAverage3(Span<byte> row, ReadOnlySpan<byte> prevRow, int stride)
    {
        ref byte r = ref row[0];
        ref byte pr = ref Unsafe.AsRef(in prevRow[0]);
        Vector128<byte> d = Vector128<byte>.Zero;

        // First pixel: a=0
        Vector128<byte> b0 = Unsafe.ReadUnaligned<Vector128<byte>>(ref pr);
        Vector128<byte> raw0 = Unsafe.ReadUnaligned<Vector128<byte>>(ref r);
        d = raw0 + (b0 >> 1);
        Unsafe.Add(ref r, 0) = d.GetElement(0);
        Unsafe.Add(ref r, 1) = d.GetElement(1);
        Unsafe.Add(ref r, 2) = d.GetElement(2);

        int i = 3;
        while (i + 3 <= stride)
        {
            Vector128<byte> a = d;
            Vector128<byte> b = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref pr, i));
            Vector128<byte> raw = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref r, i));

            Vector128<byte> avg = (a >> 1) + (b >> 1) + ((a & b) & Vector128.Create((byte)1));

            d = raw + avg;
            Unsafe.Add(ref r, i) = d.GetElement(0);
            Unsafe.Add(ref r, i + 1) = d.GetElement(1);
            Unsafe.Add(ref r, i + 2) = d.GetElement(2);
            i += 3;
        }

        for (; i < stride; i++)
            Unsafe.Add(ref r, i) += (byte)((Unsafe.Add(ref pr, i) + Unsafe.Add(ref r, i - 3)) >> 1);
    }

    private static void DefilterAverageScalar(Span<byte> row, ReadOnlySpan<byte> prevRow, int stride, int bpp)
    {
        for (int i = 0; i < bpp && i < stride; i++)
            row[i] += (byte)(prevRow[i] >> 1);
        for (int i = bpp; i < stride; i++)
            row[i] += (byte)((prevRow[i] + row[i - bpp]) >> 1);
    }

    private static void DefilterAverageFirstRow(Span<byte> row, int stride, int bpp)
    {
        if (bpp == 4 && Vector128.IsHardwareAccelerated && stride >= 4)
        {
            DefilterAverageFirstRow4(row, stride);
        }
        else
        {
            for (int i = bpp; i < stride; i++)
                row[i] += (byte)(row[i - bpp] >> 1);
        }
    }

    private static void DefilterAverageFirstRow4(Span<byte> row, int stride)
    {
        ref byte r = ref row[0];
        Vector128<byte> d = Vector128<byte>.Zero;

        // First pixel: no prev, no left → raw stays as-is (predictor = 0)
        d = Unsafe.ReadUnaligned<Vector128<byte>>(ref r);
        Unsafe.WriteUnaligned(ref r, d);

        int i = 4;
        while (i <= stride - 4)
        {
            Vector128<byte> a = d; // left pixel
            Vector128<byte> raw = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref r, i));
            // Truncating average of (a, 0) = a >> 1
            d = raw + (a >> 1);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref r, i), d);
            i += 4;
        }

        for (; i < stride; i++)
            Unsafe.Add(ref r, i) += (byte)(Unsafe.Add(ref r, i - 4) >> 1);
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
    /// Compute the Paeth predictor — branch-free using algebraic simplification.
    /// p = a + b - c; pa = |p-a| = |b-c|, pb = |p-b| = |a-c|, pc = |p-c| = pa + pb.
    /// Returns a if pa &lt;= pb and pa &lt;= pc, b if pb &lt;= pc, else c.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int pa = (b - c) << 24 >> 24;   // sign-extended byte diff → |b-c|
        pa = (pa ^ (pa >> 31)) - (pa >> 31);

        int pb = (a - c) << 24 >> 24;   // sign-extended byte diff → |a-c|
        pb = (pb ^ (pb >> 31)) - (pb >> 31);

        int pc = pa + pb;               // |p-c| = |b-c| + |a-c|

        // Branch-free selection with Paeth tie-breaking (a > b > c on ties)
        // Start with c, conditionally replace with b, then with a
        int result = c;

        // If pb <= pc, select b over c
        int maskB = ~(pb - pc) >> 31;   // all-ones if pb <= pc
        result = (result & ~maskB) | (b & maskB);

        // If pa <= pb and pa <= pc, select a
        int maskA = ~((pa - pb) | (pa - pc)) >> 31;  // all-ones if pa <= both
        result = (result & ~maskA) | (a & maskA);

        return (byte)result;
    }

    #endregion
}
