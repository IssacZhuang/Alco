using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Alco.Rendering;

/// <summary>Reconstructs PNG scanlines with or without retaining their filter bytes.</summary>
internal static unsafe class PngDefilter
{
    /// <summary>Reconstructs the scanlines in place, retaining their filter bytes.</summary>
    public static void Defilter(Span<byte> scanlines, int width, int height, int bytesPerPixel, int stride)
        => Reconstruct(scanlines, height, bytesPerPixel, stride, compact: false);

    /// <summary>Reconstructs RGBA8 scanlines into tightly packed pixels at the start of the buffer.</summary>
    public static void CompactRgba8(Span<byte> scanlines, int height, int stride)
        => Reconstruct(scanlines, height, 4, stride, compact: true);

    private static void Reconstruct(Span<byte> scanlines, int height, int bpp, int stride, bool compact)
    {
        fixed (byte* data = scanlines)
        {
            int scanlineSize = stride + 1;
            int outputStride = compact ? stride : scanlineSize;
            byte* scanline = data;
            byte* destination = compact ? data : data + 1;
            byte* previous = null;

            for (int y = 0; y < height; y++)
            {
                byte* source = scanline + 1;
                // Packed rows overlap their source, so read each group before storing it and always advance forward.
                switch (*scanline)
                {
                    case 0:
                    case 2 when previous == null:
                        if (source != destination)
                            new ReadOnlySpan<byte>(source, stride).CopyTo(new Span<byte>(destination, stride));
                        break;
                    case 1:
                    case 4 when previous == null:
                        ReconstructSub(source, destination, stride, bpp);
                        break;
                    case 2:
                        ReconstructUp(source, destination, previous, stride);
                        break;
                    case 3:
                        ReconstructAverage(source, destination, previous, stride, bpp);
                        break;
                    case 4:
                        ReconstructPaeth(source, destination, previous, stride, bpp);
                        break;
                    default:
                        throw new ImageDecodeException($"Invalid PNG filter type: {*scanline}.");
                }

                previous = destination;
                scanline += scanlineSize;
                destination += outputStride;
            }
        }
    }

    private static void ReconstructSub(byte* source, byte* destination, int stride, int bpp)
    {
        int i = 0;
        for (; i < bpp; i++)
            destination[i] = source[i];
        for (; i < stride; i++)
            destination[i] = (byte)(source[i] + destination[i - bpp]);
    }

    private static void ReconstructUp(byte* source, byte* destination, byte* previous, int stride)
    {
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            for (; i <= stride - Vector256<byte>.Count; i += Vector256<byte>.Count)
            {
                Vector256<byte> raw = Unsafe.ReadUnaligned<Vector256<byte>>(source + i);
                Vector256<byte> up = Unsafe.ReadUnaligned<Vector256<byte>>(previous + i);
                Unsafe.WriteUnaligned(destination + i, raw + up);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; i <= stride - Vector128<byte>.Count; i += Vector128<byte>.Count)
            {
                Vector128<byte> raw = Unsafe.ReadUnaligned<Vector128<byte>>(source + i);
                Vector128<byte> up = Unsafe.ReadUnaligned<Vector128<byte>>(previous + i);
                Unsafe.WriteUnaligned(destination + i, raw + up);
            }
        }

        for (; i < stride; i++)
            destination[i] = (byte)(source[i] + previous[i]);
    }

    private static void ReconstructAverage(byte* source, byte* destination, byte* previous, int stride, int bpp)
    {
        int i = 0;
        if (previous == null)
        {
            for (; i < bpp; i++)
                destination[i] = source[i];
            for (; i < stride; i++)
                destination[i] = (byte)(source[i] + (destination[i - bpp] >> 1));
            return;
        }

        for (; i < bpp; i++)
            destination[i] = (byte)(source[i] + (previous[i] >> 1));
        for (; i < stride; i++)
            destination[i] = (byte)(source[i] + ((previous[i] + destination[i - bpp]) >> 1));
    }

    private static void ReconstructPaeth(byte* source, byte* destination, byte* previous, int stride, int bpp)
    {
        int i = 0;
        for (; i < bpp; i++)
            destination[i] = (byte)(source[i] + previous[i]);
        for (; i < stride; i++)
            destination[i] = (byte)(source[i] + PaethPredictor(destination[i - bpp], previous[i], previous[i - bpp]));
    }

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
}
