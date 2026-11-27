using System;

namespace Alco.Test;

/// <summary>
/// Tests Morton encoding, decoding, and ordering across signed coordinates.
/// </summary>
[TestFixture]
public class TestMorton2D
{
    /// <summary>
    /// Pins the axis bit layout and the mapping of signed boundaries.
    /// </summary>
    [TestCase(int.MinValue, int.MinValue, 0x0000_0000_0000_0000UL)]
    [TestCase(int.MinValue + 1, int.MinValue, 0x0000_0000_0000_0001UL)]
    [TestCase(int.MinValue, int.MinValue + 1, 0x0000_0000_0000_0002UL)]
    [TestCase(int.MaxValue, int.MinValue, 0x5555_5555_5555_5555UL)]
    [TestCase(int.MinValue, int.MaxValue, 0xAAAA_AAAA_AAAA_AAAAUL)]
    [TestCase(int.MaxValue, int.MaxValue, ulong.MaxValue)]
    [TestCase(int.MinValue / 2, int.MaxValue / 2, 0x9AAA_AAAA_AAAA_AAAAUL)]
    [TestCase(-1, -1, 0x3FFF_FFFF_FFFF_FFFFUL)]
    [TestCase(-1, 0, 0x9555_5555_5555_5555UL)]
    [TestCase(0, -1, 0x6AAA_AAAA_AAAA_AAAAUL)]
    [TestCase(0, 0, 0xC000_0000_0000_0000UL)]
    [TestCase(1, 0, 0xC000_0000_0000_0001UL)]
    [TestCase(0, 1, 0xC000_0000_0000_0002UL)]
    public void EncodeAndDecode_MatchGoldenCodes(int x, int y, ulong code)
    {
        Assert.That(Morton2D.Encode(x, y), Is.EqualTo(code));
        Morton2D.Decode(code, out int decodedX, out int decodedY);
        Assert.That(new int2(decodedX, decodedY), Is.EqualTo(new int2(x, y)));
    }

    /// <summary>
    /// Checks encoding and decoding against bit-by-bit codes for seeded points across the full integer range.
    /// </summary>
    [Test]
    public void EncodeAndDecode_MatchBitByBitReference()
    {
        Random random = new(0x2D);
        for (int i = 0; i < 1024; i++)
        {
            int x = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
            int y = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
            ulong expectedCode = EncodeBitByBit(x, y);

            Assert.That(Morton2D.Encode(x, y), Is.EqualTo(expectedCode), $"Encoding ({x}, {y})");
            Morton2D.Decode(expectedCode, out int decodedX, out int decodedY);
            Assert.That(new int2(decodedX, decodedY), Is.EqualTo(new int2(x, y)), $"Decoding 0x{expectedCode:X16}");
        }
    }

    /// <summary>
    /// Verifies that each coordinate sorts monotonically when the other is held fixed.
    /// </summary>
    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void Encode_IncreasesAlongEitherAxis(int fixedCoordinate)
    {
        ReadOnlySpan<int> coordinates =
        [
            int.MinValue, int.MinValue + 1, int.MinValue / 2, -2, -1, 0, 1, 2,
            int.MaxValue / 2, int.MaxValue - 1, int.MaxValue
        ];

        for (int i = 1; i < coordinates.Length; i++)
        {
            int lower = coordinates[i - 1];
            int higher = coordinates[i];
            Assert.That(Morton2D.Encode(lower, fixedCoordinate), Is.LessThan(Morton2D.Encode(higher, fixedCoordinate)));
            Assert.That(Morton2D.Encode(fixedCoordinate, lower), Is.LessThan(Morton2D.Encode(fixedCoordinate, higher)));
        }
    }

    /// <summary>
    /// Verifies unsigned Morton ordering across axes, including across the highest code bit.
    /// </summary>
    [TestCase(int.MaxValue, -1, int.MinValue, 0)]
    [TestCase(0, 1, 2, 0)]
    public void Compare_OrdersUnsignedMortonCodes(int lowerX, int lowerY, int higherX, int higherY)
    {
        int2 lower = new(lowerX, lowerY);
        int2 higher = new(higherX, higherY);
        Assert.That(Morton2D.Compare(lower, higher), Is.LessThan(0));
        Assert.That(Morton2D.Compare(higher, lower), Is.GreaterThan(0));
        Assert.That(Morton2D.Compare(lower, lower), Is.Zero);
    }

    private static ulong EncodeBitByBit(int x, int y)
    {
        ulong biasedX = (ulong)((long)x - int.MinValue);
        ulong biasedY = (ulong)((long)y - int.MinValue);
        ulong code = 0;
        for (int bit = 0; bit < 32; bit++)
        {
            code |= ((biasedX >> bit) & 1UL) << (2 * bit);
            code |= ((biasedY >> bit) & 1UL) << (2 * bit + 1);
        }

        return code;
    }
}
