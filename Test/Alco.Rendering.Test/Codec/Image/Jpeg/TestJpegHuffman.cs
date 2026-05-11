using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering.Codec.Image;

[TestFixture]
public class TestJpegHuffman
{
    /// <summary>
    /// Standard JPEG DC luminance Huffman table (JPEG spec Annex K, Table K.3).
    /// </summary>
    private static readonly byte[] StandardDCLuminanceBits =
        [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];

    private static readonly byte[] StandardDCLuminanceValues =
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    /// <summary>
    /// Standard JPEG AC luminance Huffman table (JPEG spec Annex K, Table K.5).
    /// </summary>
    private static readonly byte[] StandardACLuminanceBits =
        [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D];

    private static readonly byte[] StandardACLuminanceValues =
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
        0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08,
        0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16,
        0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
        0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
        0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
        0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
        0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4,
        0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA,
        0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    [Test]
    public void BuildTable_StandardDCLuminance()
    {
        var table = new JpegHuffman.HuffmanTable();

        bool result = JpegHuffman.BuildTable(StandardDCLuminanceBits, StandardDCLuminanceValues, ref table);

        Assert.That(result, Is.True);

        // 12 symbols total
        int totalSymbols = 0;
        for (int i = 0; i < 16; i++)
            totalSymbols += StandardDCLuminanceBits[i];
        Assert.That(totalSymbols, Is.EqualTo(12));

        // Max code length is 9 (length 9 has 1 code), so TableBits should be 9
        Assert.That(table.TableBits, Is.EqualTo(9));
        Assert.That(table.Table.Length, Is.EqualTo(1 << 9));

        // Length 2: symbol 0 gets code 00 (2-bit), fills 2^(9-2)=128 entries starting at index 0
        Assert.That(table.Table[0].Symbol, Is.EqualTo(0));
        Assert.That(table.Table[0].Length, Is.EqualTo(2));
        Assert.That(table.Table[127].Symbol, Is.EqualTo(0));
        Assert.That(table.Table[127].Length, Is.EqualTo(2));

        // Length 3: symbol 1 gets code 010, fills 2^(9-3)=64 entries starting at index 128
        Assert.That(table.Table[128].Symbol, Is.EqualTo(1));
        Assert.That(table.Table[128].Length, Is.EqualTo(3));

        // Symbol 5 gets code 110, fills entries starting at index 384
        Assert.That(table.Table[384].Symbol, Is.EqualTo(5));
        Assert.That(table.Table[384].Length, Is.EqualTo(3));
    }

    [Test]
    public void BuildTable_StandardACLuminance()
    {
        var table = new JpegHuffman.HuffmanTable();

        bool result = JpegHuffman.BuildTable(StandardACLuminanceBits, StandardACLuminanceValues, ref table);

        Assert.That(result, Is.True);

        // 162 symbols total
        int totalSymbols = 0;
        for (int i = 0; i < 16; i++)
            totalSymbols += StandardACLuminanceBits[i];
        Assert.That(totalSymbols, Is.EqualTo(162));

        // Max code length is 16, so TableBits should be 16
        Assert.That(table.TableBits, Is.EqualTo(16));

        // Length 2: 2 codes, symbols 0x01 and 0x02
        // Code 00 -> symbol 0x01 (fills 2^(16-2)=16384 entries starting at 0)
        Assert.That(table.Table[0].Symbol, Is.EqualTo(0x01));
        Assert.That(table.Table[0].Length, Is.EqualTo(2));

        // Code 01 -> symbol 0x02 (fills entries starting at 16384)
        Assert.That(table.Table[16384].Symbol, Is.EqualTo(0x02));
        Assert.That(table.Table[16384].Length, Is.EqualTo(2));
    }

    [Test]
    public void DecodeSymbol_KnownPattern()
    {
        // Build a simple table: 3 symbols
        byte[] bits = [0, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] values = [0, 1, 2];

        var table = new JpegHuffman.HuffmanTable();
        bool result = JpegHuffman.BuildTable(bits, values, ref table);
        Assert.That(result, Is.True);

        // The Huffman codes generated:
        // Length 2: symbol 0 -> code 00, symbol 1 -> code 01
        // Length 3: symbol 2 -> code 100

        // Encode the bit sequence: symbol 0 (00), symbol 2 (100), symbol 1 (01)
        // Bits MSB-first: 00 100 01 -> 0010001 = 7 bits
        byte[] data = [0x22, 0x00];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int sym0 = JpegHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, table);
        Assert.That(sym0, Is.EqualTo(0), "First symbol should be 0");

        int sym2 = JpegHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, table);
        Assert.That(sym2, Is.EqualTo(2), "Second symbol should be 2");

        int sym1 = JpegHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, table);
        Assert.That(sym1, Is.EqualTo(1), "Third symbol should be 1");
    }

    [Test]
    public void DecodeSymbol_StandardDCLuminance_KnownCodes()
    {
        var table = new JpegHuffman.HuffmanTable();
        JpegHuffman.BuildTable(StandardDCLuminanceBits, StandardDCLuminanceValues, ref table);

        // Encode: symbol 0 (code=00), symbol 6 (code=1110)
        // Bits: 00 1110 -> 6 bits = 0b001110xx
        byte[] data = [0x38, 0x00];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        Assert.That(JpegHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, table), Is.EqualTo(0));
        Assert.That(JpegHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, table), Is.EqualTo(6));
    }

    [Test]
    public void ReceiveExtend_PositiveValue()
    {
        byte[] data = [0x80];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int result = JpegHuffman.ReceiveExtend(4, ref bitBuffer, ref bitsAvailable, data, ref dataPos);
        Assert.That(result, Is.EqualTo(8));
    }

    [Test]
    public void ReceiveExtend_NegativeValue()
    {
        byte[] data = [0x30];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int result = JpegHuffman.ReceiveExtend(4, ref bitBuffer, ref bitsAvailable, data, ref dataPos);
        Assert.That(result, Is.EqualTo(-12));
    }

    [Test]
    public void ReceiveExtend_ZeroBits()
    {
        byte[] data = [0xFF];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int result = JpegHuffman.ReceiveExtend(0, ref bitBuffer, ref bitsAvailable, data, ref dataPos);

        Assert.That(result, Is.EqualTo(0));
        Assert.That(bitsAvailable, Is.EqualTo(0));
        Assert.That(dataPos, Is.EqualTo(0));
    }

    [Test]
    public void ReceiveExtend_OneBit_Zero()
    {
        byte[] data = [0x00];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int result = JpegHuffman.ReceiveExtend(1, ref bitBuffer, ref bitsAvailable, data, ref dataPos);
        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void ReceiveExtend_OneBit_One()
    {
        byte[] data = [0x80];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int result = JpegHuffman.ReceiveExtend(1, ref bitBuffer, ref bitsAvailable, data, ref dataPos);
        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void BuildTable_InvalidCodeLengthsCount_ReturnsFalse()
    {
        var table = new JpegHuffman.HuffmanTable();
        byte[] bits = [0, 2, 1]; // Only 3 values, need 16
        byte[] values = [0, 1, 2];

        bool result = JpegHuffman.BuildTable(bits, values, ref table);
        Assert.That(result, Is.False);
    }

    [Test]
    public void BuildTable_ValuesTooShort_ReturnsFalse()
    {
        var table = new JpegHuffman.HuffmanTable();
        byte[] bits = [0, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]; // 3 symbols expected
        byte[] values = [0, 1]; // Only 2 values

        bool result = JpegHuffman.BuildTable(bits, values, ref table);
        Assert.That(result, Is.False);
    }

    [Test]
    public void BuildTable_EmptyTable_Succeeds()
    {
        var table = new JpegHuffman.HuffmanTable();
        byte[] bits = new byte[16]; // All zeros
        byte[] values = [];

        bool result = JpegHuffman.BuildTable(bits, values, ref table);

        Assert.That(result, Is.True);
        // Empty table should have TableBits=0
        Assert.That(table.TableBits, Is.EqualTo(0));
    }

    [Test]
    public void DecodeSymbol_EmptyStream_ReturnsMinusOne()
    {
        var table = new JpegHuffman.HuffmanTable();
        byte[] bits = [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] values = [42];
        JpegHuffman.BuildTable(bits, values, ref table);

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;
        ReadOnlySpan<byte> data = [];

        int result = JpegHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, table);

        Assert.That(result, Is.EqualTo(-1), "Should return -1 when no data available");
    }

    [Test]
    public void ReceiveExtend_AllPositiveValues_4Bits()
    {
        byte[] data = [0xF0];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int result = JpegHuffman.ReceiveExtend(4, ref bitBuffer, ref bitsAvailable, data, ref dataPos);
        Assert.That(result, Is.EqualTo(15));
    }

    [Test]
    public void ReceiveExtend_AllNegativeValues_4Bits()
    {
        byte[] data = [0x00];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int result = JpegHuffman.ReceiveExtend(4, ref bitBuffer, ref bitsAvailable, data, ref dataPos);
        Assert.That(result, Is.EqualTo(-15));
    }

    [Test]
    public void DecodeAndExtend_IntegrationTest()
    {
        var table = new JpegHuffman.HuffmanTable();
        JpegHuffman.BuildTable(StandardDCLuminanceBits, StandardDCLuminanceValues, ref table);

        // Symbol 3 is at length 3, code value 4 (binary 100).
        // Encode: category 3 (code=100, 3 bits) followed by value +5 (binary 101, 3 bits)
        byte[] data = [0x94, 0x00];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int category = JpegHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, table);
        Assert.That(category, Is.EqualTo(3), "Category should be 3");

        int value = JpegHuffman.ReceiveExtend(category, ref bitBuffer, ref bitsAvailable, data, ref dataPos);
        Assert.That(value, Is.EqualTo(5), "Value should be +5");
    }
}
