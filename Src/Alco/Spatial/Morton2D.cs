namespace Alco;

/// <summary>
/// Encodes signed coordinates in Morton order with X in even bits and Y in odd bits.
/// </summary>
public static class Morton2D
{
    private const uint CoordinateBias = 0x8000_0000u;
    private const ulong EvenBitMask = 0x5555_5555_5555_5555UL;
    private const ulong EvenPairMask = 0x3333_3333_3333_3333UL;
    private const ulong EvenNibbleMask = 0x0F0F_0F0F_0F0F_0F0FUL;
    private const ulong EvenByteMask = 0x00FF_00FF_00FF_00FFUL;
    private const ulong EvenWordMask = 0x0000_FFFF_0000_FFFFUL;

    /// <summary>
    /// Encodes two signed coordinates into an unsigned 64-bit Morton code.
    /// </summary>
    /// <param name="x">The signed X coordinate.</param>
    /// <param name="y">The signed Y coordinate.</param>
    /// <returns>The code, which increases with either coordinate when the other is fixed.</returns>
    public static ulong Encode(int x, int y)
    {
        uint biasedX = unchecked((uint)x + CoordinateBias);
        uint biasedY = unchecked((uint)y + CoordinateBias);
        return SpreadBits(biasedX) | (SpreadBits(biasedY) << 1);
    }

    /// <summary>
    /// Decodes an unsigned 64-bit Morton code into its signed coordinates.
    /// </summary>
    /// <param name="code">The Morton code to decode.</param>
    /// <param name="x">The decoded X coordinate.</param>
    /// <param name="y">The decoded Y coordinate.</param>
    public static void Decode(ulong code, out int x, out int y)
    {
        x = unchecked((int)(CompactBits(code) - CoordinateBias));
        y = unchecked((int)(CompactBits(code >> 1) - CoordinateBias));
    }

    /// <summary>
    /// Compares two points by their unsigned Morton codes.
    /// </summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>A negative value, zero, or a positive value if <paramref name="a"/> precedes, equals, or follows <paramref name="b"/>, respectively.</returns>
    public static int Compare(int2 a, int2 b)
    {
        return Encode(a.X, a.Y).CompareTo(Encode(b.X, b.Y));
    }

    private static ulong SpreadBits(uint value)
    {
        ulong bits = value;
        bits = (bits | (bits << 16)) & EvenWordMask;
        bits = (bits | (bits << 8)) & EvenByteMask;
        bits = (bits | (bits << 4)) & EvenNibbleMask;
        bits = (bits | (bits << 2)) & EvenPairMask;
        return (bits | (bits << 1)) & EvenBitMask;
    }

    private static uint CompactBits(ulong bits)
    {
        bits &= EvenBitMask;
        bits = (bits | (bits >> 1)) & EvenPairMask;
        bits = (bits | (bits >> 2)) & EvenNibbleMask;
        bits = (bits | (bits >> 4)) & EvenByteMask;
        bits = (bits | (bits >> 8)) & EvenWordMask;
        return (uint)((bits | (bits >> 16)) & uint.MaxValue);
    }
}
