using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

/// <summary>Selects and applies a PNG filter to an RGBA8 scanline.</summary>
internal static class PngFilter
{
    private const int PixelBytes = 4;

    private enum Filter : byte
    {
        None,
        Sub,
        Up,
        Average,
        Paeth,
    }

    /// <summary>Writes the selected filter byte and its residuals to the scanline.</summary>
    public static void FilterRow(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Span<byte> scanline, PngEncodeMode mode)
    {
        Filter best = Filter.None;
        long bestScore = ProcessRow(row, previous, Filter.None);
        Filter last = mode == PngEncodeMode.Fast ? Filter.Up : Filter.Paeth;
        for (Filter candidate = Filter.Sub; candidate <= last && bestScore != 0; candidate++)
        {
            if (previous.IsEmpty && candidate is Filter.Up or Filter.Paeth)
            {
                continue;
            }

            long score = ProcessRow(row, previous, candidate);
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        scanline[0] = (byte)best;
        if (best == Filter.None)
        {
            row.CopyTo(scanline[1..]);
            return;
        }

        ProcessRow(row, previous, best, scanline[1..]);
    }

    private static long ProcessRow(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Filter filter, Span<byte> output = default)
    {
        long score = 0;
        int prefix = filter is Filter.None or Filter.Up ? 0 : PixelBytes;
        int i = 0;
        for (; i < prefix; i++)
        {
            score += ProcessByte(row, previous, filter, output, i);
        }

        if (Vector.IsHardwareAccelerated && i <= row.Length - Vector<byte>.Count)
        {
            ref byte current = ref MemoryMarshal.GetReference(row);
            ref byte above = ref MemoryMarshal.GetReference(previous);
            ref byte destination = ref MemoryMarshal.GetReference(output);
            bool scoreOnly = output.IsEmpty;
            for (; i <= row.Length - Vector<byte>.Count; i += Vector<byte>.Count)
            {
                nuint offset = (nuint)i;
                Vector<byte> delta = Vector.LoadUnsafe(ref current, offset)
                    - PredictVector(ref current, ref above, offset, filter, !previous.IsEmpty);
                if (scoreOnly)
                {
                    Vector<byte> magnitude = Vector.Min(delta, Vector<byte>.Zero - delta);
                    score += Vector.Sum(Vector.WidenLower(magnitude) + Vector.WidenUpper(magnitude));
                }
                else
                {
                    Vector.StoreUnsafe(delta, ref destination, offset);
                }
            }
        }

        for (; i < row.Length; i++)
        {
            score += ProcessByte(row, previous, filter, output, i);
        }

        return score;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ProcessByte(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Filter filter, Span<byte> output, int i)
    {
        byte delta = unchecked((byte)(row[i] - PredictByte(row, previous, filter, i)));
        if (output.IsEmpty)
        {
            return Math.Abs((int)unchecked((sbyte)delta));
        }

        output[i] = delta;
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PredictByte(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Filter filter, int i)
    {
        if (filter == Filter.None)
        {
            return 0;
        }

        int left = i < PixelBytes ? 0 : row[i - PixelBytes];
        if (filter == Filter.Sub)
        {
            return left;
        }

        int above = previous.IsEmpty ? 0 : previous[i];
        return filter switch
        {
            Filter.Up => above,
            Filter.Average => (left + above) >> 1,
            _ => PredictPaeth(left, above, i < PixelBytes ? 0 : previous[i - PixelBytes]),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<byte> PredictVector(ref byte row, ref byte previous, nuint i, Filter filter, bool hasPrevious)
    {
        if (filter == Filter.None)
        {
            return Vector<byte>.Zero;
        }

        if (filter == Filter.Up)
        {
            return Vector.LoadUnsafe(ref previous, i);
        }

        Vector<byte> left = Vector.LoadUnsafe(ref row, i - PixelBytes);
        if (filter == Filter.Sub)
        {
            return left;
        }

        Vector<byte> above = hasPrevious ? Vector.LoadUnsafe(ref previous, i) : Vector<byte>.Zero;
        if (filter == Filter.Average)
        {
            return (left & above) + Vector.ShiftRightLogical(left ^ above, 1);
        }

        Vector<byte> upperLeft = Vector.LoadUnsafe(ref previous, i - PixelBytes);
        Vector.Widen(left, out Vector<ushort> leftLo, out Vector<ushort> leftHi);
        Vector.Widen(above, out Vector<ushort> aboveLo, out Vector<ushort> aboveHi);
        Vector.Widen(upperLeft, out Vector<ushort> cornerLo, out Vector<ushort> cornerHi);
        return Vector.Narrow(PredictPaeth(leftLo, aboveLo, cornerLo), PredictPaeth(leftHi, aboveHi, cornerHi));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PredictPaeth(int left, int above, int upperLeft)
    {
        int leftDelta = above - upperLeft;
        int aboveDelta = left - upperLeft;
        int leftDistance = Math.Abs(leftDelta);
        int aboveDistance = Math.Abs(aboveDelta);
        int cornerDistance = Math.Abs(leftDelta + aboveDelta);
        if (leftDistance <= aboveDistance && leftDistance <= cornerDistance)
        {
            return left;
        }

        return aboveDistance <= cornerDistance ? above : upperLeft;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<ushort> PredictPaeth(Vector<ushort> left, Vector<ushort> above, Vector<ushort> upperLeft)
    {
        Vector<short> leftDelta = Vector.AsVectorInt16(above - upperLeft);
        Vector<short> aboveDelta = Vector.AsVectorInt16(left - upperLeft);
        Vector<short> leftDistance = Vector.Abs(leftDelta);
        Vector<short> aboveDistance = Vector.Abs(aboveDelta);
        Vector<short> cornerDistance = Vector.Abs(leftDelta + aboveDelta);
        Vector<short> useLeft = Vector.LessThanOrEqual(leftDistance, aboveDistance)
            & Vector.LessThanOrEqual(leftDistance, cornerDistance);
        Vector<short> useAbove = Vector.LessThanOrEqual(aboveDistance, cornerDistance);
        return Vector.ConditionalSelect(Vector.AsVectorUInt16(useLeft), left,
            Vector.ConditionalSelect(Vector.AsVectorUInt16(useAbove), above, upperLeft));
    }
}
