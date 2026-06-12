using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// Forward PNG row filtering (encoding direction).
/// For each scanline, applies one of five filter types and selects the one
/// that minimizes the sum of absolute byte values — a heuristic correlated
/// with better DEFLATE compression.
/// </summary>
internal static unsafe class PngFilter
{
    /// <summary>
    /// Filter all rows using adaptive heuristic selection (minimum sum of absolute values).
    /// Output layout: one filter-type byte followed by filtered pixel bytes per row.
    /// </summary>
    /// <param name="rgba">Source RGBA8 pixel data (width * height * 4 bytes, row-major).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="filtered">Output buffer (height * (1 + width*4) bytes). Written with filter byte + filtered row data per row.</param>
    /// <param name="tempRow">Temporary buffer for trial filtering (width*4 bytes minimum). Avoids per-row allocation.</param>
    public static void FilterAdaptive(
        ReadOnlySpan<byte> rgba, int width, int height,
        Span<byte> filtered, Span<byte> tempRow)
    {
        int stride = width * 4;
        int prevRowBase = 0; // offset into rgba for previous row

        for (int y = 0; y < height; y++)
        {
            int rowBase = y * stride;
            ReadOnlySpan<byte> row = rgba.Slice(rowBase, stride);
            ReadOnlySpan<byte> prevRow = y > 0 ? rgba.Slice(prevRowBase, stride) : ReadOnlySpan<byte>.Empty;
            Span<byte> outRow = filtered.Slice(y * (stride + 1), stride + 1);

            // Try all filters, pick the one with minimum sum of absolute values
            int bestFilter = 0;
            int bestSum = int.MaxValue;

            for (int f = 0; f <= 4; f++)
            {
                ApplyFilter(row, prevRow, tempRow, stride, f);
                int sum = SumAbs(tempRow);

                if (sum < bestSum)
                {
                    bestSum = sum;
                    bestFilter = f;

                    // For filter 0 (None), the data is already identical to row
                    // so we can skip the copy if it ends up being selected.
                    // We'll do a single copy at the end for the chosen filter.
                }
            }

            // Re-apply the winning filter directly into the output
            outRow[0] = (byte)bestFilter;

            if (bestFilter == 0)
            {
                // None filter: raw copy
                row.CopyTo(outRow.Slice(1));
            }
            else
            {
                ApplyFilter(row, prevRow, outRow.Slice(1), stride, bestFilter);
            }

            prevRowBase = rowBase;
        }
    }

    /// <summary>
    /// Apply a single filter type to a row of pixel data.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyFilter(
        ReadOnlySpan<byte> row, ReadOnlySpan<byte> prevRow,
        Span<byte> output, int stride, int filterType)
    {
        switch (filterType)
        {
            case 0: // None
                row.CopyTo(output);
                break;

            case 1: // Sub
                FilterSub(row, output, stride);
                break;

            case 2: // Up
                FilterUp(row, prevRow, output, stride);
                break;

            case 3: // Average
                FilterAverage(row, prevRow, output, stride);
                break;

            case 4: // Paeth
                FilterPaeth(row, prevRow, output, stride);
                break;
        }
    }

    /// <summary>
    /// Filter Sub (1): output[i] = raw[i] - raw[i - bpp] (mod 256).
    /// bpp = 4 for RGBA8.
    /// </summary>
    private static void FilterSub(ReadOnlySpan<byte> row, Span<byte> output, int stride)
    {
        // First 4 bytes: no left neighbor, output = raw
        output[0] = row[0];
        output[1] = row[1];
        output[2] = row[2];
        output[3] = row[3];

        ref byte r = ref Unsafe.AsRef(in row[0]);
        ref byte o = ref output[0];

        for (int i = 4; i < stride; i++)
            Unsafe.Add(ref o, i) = (byte)(Unsafe.Add(ref r, i) - Unsafe.Add(ref r, i - 4));
    }

    /// <summary>
    /// Filter Up (2): output[i] = raw[i] - prev[i] (mod 256).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FilterUp(ReadOnlySpan<byte> row, ReadOnlySpan<byte> prevRow, Span<byte> output, int stride)
    {
        if (prevRow.IsEmpty)
        {
            row.CopyTo(output);
            return;
        }

        ref byte r = ref Unsafe.AsRef(in row[0]);
        ref byte p = ref Unsafe.AsRef(in prevRow[0]);
        ref byte o = ref output[0];

        for (int i = 0; i < stride; i++)
            Unsafe.Add(ref o, i) = (byte)(Unsafe.Add(ref r, i) - Unsafe.Add(ref p, i));
    }

    /// <summary>
    /// Filter Average (3): output[i] = raw[i] - (raw[i-bpp] + prev[i]) / 2 (truncating division).
    /// </summary>
    private static void FilterAverage(ReadOnlySpan<byte> row, ReadOnlySpan<byte> prevRow, Span<byte> output, int stride)
    {
        ref byte r = ref Unsafe.AsRef(in row[0]);
        ref byte o = ref output[0];

        // First 4 bytes: a = 0, so predictor = prev[i] / 2 (or 0 if no prev row)
        if (prevRow.IsEmpty)
        {
            for (int i = 0; i < 4 && i < stride; i++)
                Unsafe.Add(ref o, i) = Unsafe.Add(ref r, i);
        }
        else
        {
            ref byte p = ref Unsafe.AsRef(in prevRow[0]);
            for (int i = 0; i < 4 && i < stride; i++)
                Unsafe.Add(ref o, i) = (byte)(Unsafe.Add(ref r, i) - (Unsafe.Add(ref p, i) >> 1));
        }

        // Remaining bytes
        if (prevRow.IsEmpty)
        {
            ref byte rp = ref Unsafe.AsRef(in row[0]);
            for (int i = 4; i < stride; i++)
                Unsafe.Add(ref o, i) = (byte)(Unsafe.Add(ref r, i) - (Unsafe.Add(ref rp, i - 4) >> 1));
        }
        else
        {
            ref byte p = ref Unsafe.AsRef(in prevRow[0]);
            for (int i = 4; i < stride; i++)
                Unsafe.Add(ref o, i) = (byte)(Unsafe.Add(ref r, i) - ((Unsafe.Add(ref r, i - 4) + Unsafe.Add(ref p, i)) >> 1));
        }
    }

    /// <summary>
    /// Filter Paeth (4): output[i] = raw[i] - PaethPredictor(raw[i-bpp], prev[i], prev[i-bpp]).
    /// </summary>
    private static void FilterPaeth(ReadOnlySpan<byte> row, ReadOnlySpan<byte> prevRow, Span<byte> output, int stride)
    {
        ref byte r = ref Unsafe.AsRef(in row[0]);
        ref byte o = ref output[0];

        if (prevRow.IsEmpty)
        {
            // No previous row: PaethPredictor(a, 0, 0) = a for first bpp bytes,
            // then PaethPredictor(a, 0, 0) = a for rest.
            // So output = raw - raw[i-4] = same as Sub filter
            output[0] = row[0];
            output[1] = row[1];
            output[2] = row[2];
            output[3] = row[3];

            for (int i = 4; i < stride; i++)
                Unsafe.Add(ref o, i) = (byte)(Unsafe.Add(ref r, i) - Unsafe.Add(ref r, i - 4));
        }
        else
        {
            ref byte p = ref Unsafe.AsRef(in prevRow[0]);

            // First 4 bytes: a = 0, c = 0 → PaethPredictor(0, b, 0) = b
            for (int i = 0; i < 4 && i < stride; i++)
                Unsafe.Add(ref o, i) = (byte)(Unsafe.Add(ref r, i) - Unsafe.Add(ref p, i));

            // Remaining bytes
            for (int i = 4; i < stride; i++)
            {
                byte a = Unsafe.Add(ref r, i - 4);
                byte b = Unsafe.Add(ref p, i);
                byte c = Unsafe.Add(ref p, i - 4);
                Unsafe.Add(ref o, i) = (byte)(Unsafe.Add(ref r, i) - PaethPredictor(a, b, c));
            }
        }
    }

    /// <summary>
    /// Paeth predictor: p = a + b - c, pick the neighbor closest to p.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Abs(p - a);
        int pb = Abs(p - b);
        int pc = Abs(p - c);

        if (pa <= pb && pa <= pc)
            return a;
        if (pb <= pc)
            return b;
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Abs(int x) => x >= 0 ? x : -x;

    /// <summary>
    /// Sum of absolute byte values. Used as heuristic for filter selection —
    /// lower sums correlate with better DEFLATE compression.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SumAbs(Span<byte> data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
            sum += data[i];
        return sum;
    }
}
