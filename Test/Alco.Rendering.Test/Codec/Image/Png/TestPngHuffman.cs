using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

[TestFixture]
public class TestPngHuffman
{
    [Test]
    public void BuildFixedLiteralTable_Has288SymbolsAndKnownEntries()
    {
        int tableBits = 9;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        bool result = PngHuffman.BuildFixedLiteralTable(symbols, lengths);

        Assert.That(result, Is.True);

        // Verify some known entries from the DEFLATE fixed table.
        // Symbol 256 (end-of-block) has code length 7.
        // In the fixed table, 7-bit codes are for symbols 256-279.
        // According to DEFLATE spec:
        //   256-279 -> 7 bits, codes 0000000..0010111 (decimal 0..23)
        //   0-143   -> 8 bits
        //   144-255 -> 9 bits
        //   280-287 -> 8 bits
        bool foundSymbol0 = false;
        bool foundSymbol256 = false;
        for (int i = 0; i < tableSize; i++)
        {
            if (symbols[i] == 0 && lengths[i] > 0) foundSymbol0 = true;
            if (symbols[i] == 256 && lengths[i] > 0) foundSymbol256 = true;
        }

        Assert.That(foundSymbol0, Is.True, "Symbol 0 (literal) should be in table");
        Assert.That(foundSymbol256, Is.True, "Symbol 256 (end-of-block) should be in table");
    }

    [Test]
    public void BuildFixedLiteralTable_AllCodeLengthsAreValid()
    {
        int tableBits = 9;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        PngHuffman.BuildFixedLiteralTable(symbols, lengths);

        // Every entry should either have length 0 (unused) or a valid length (7, 8, or 9)
        for (int i = 0; i < tableSize; i++)
        {
            Assert.That(lengths[i], Is.EqualTo(0).Or.EqualTo(7).Or.EqualTo(8).Or.EqualTo(9),
                $"Entry {i} has invalid length {lengths[i]}");
        }
    }

    [Test]
    public void BuildFixedDistanceTable_All32SymbolsPresent()
    {
        int tableBits = 5;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        bool result = PngHuffman.BuildFixedDistanceTable(symbols, lengths);

        Assert.That(result, Is.True);

        // All 32 entries should have length 5 (all symbols have 5-bit codes)
        for (int i = 0; i < tableSize; i++)
        {
            Assert.That(lengths[i], Is.EqualTo(5),
                $"Distance entry {i} should have length 5, got {lengths[i]}");
        }

        // All 32 symbols should be present (each appears exactly once since all codes are 5 bits)
        bool[] found = new bool[tableSize];
        for (int i = 0; i < tableSize; i++)
        {
            Assert.That(symbols[i], Is.InRange(0, 31), $"Entry {i} has invalid symbol {symbols[i]}");
            Assert.That(found[symbols[i]], Is.False, $"Symbol {symbols[i]} appears more than once");
            found[symbols[i]] = true;
        }
    }

    [Test]
    public void BuildUniformTable_AllSymbolsDecodeCorrectly()
    {
        // 4 symbols, each with 2-bit code length
        byte[] codeLengths = [2, 2, 2, 2];
        int tableBits = 2;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        bool result = PngHuffman.BuildHuffmanTable(codeLengths, 0, 4, tableBits, symbols, lengths);

        Assert.That(result, Is.True);

        // All 4 table entries should have length 2
        for (int i = 0; i < tableSize; i++)
        {
            Assert.That(lengths[i], Is.EqualTo(2),
                $"Entry {i} should have length 2");
        }

        // All 4 symbols should be present
        bool[] found = new bool[4];
        for (int i = 0; i < tableSize; i++)
        {
            Assert.That(symbols[i], Is.InRange(0, 3));
            found[symbols[i]] = true;
        }

        for (int i = 0; i < 4; i++)
            Assert.That(found[i], Is.True, $"Symbol {i} not found in table");
    }

    [Test]
    public void DecodeRoundTrip_KnownSymbolsDecodeCorrectly()
    {
        // Build a simple table: 4 symbols with 2-bit codes.
        // With the DEFLATE Huffman algorithm:
        //   bl_count[2] = 4, next_code[2] = 0
        //   Symbol 0 gets code 0 (00), reversed(0,2) = 0 -> table index 0
        //   Symbol 1 gets code 1 (01), reversed(1,2) = 2 -> table index 2
        //   Symbol 2 gets code 2 (10), reversed(2,2) = 1 -> table index 1
        //   Symbol 3 gets code 3 (11), reversed(3,2) = 3 -> table index 3
        // To encode symbol sequence [0,1,2,3], we write their reversed codes in order,
        // LSB first into bytes:
        //   Symbol 0 (reversed code 0 = 00): bits 0-1
        //   Symbol 1 (reversed code 2 = 10): bits 2-3
        //   Symbol 2 (reversed code 1 = 01): bits 4-5
        //   Symbol 3 (reversed code 3 = 11): bits 6-7
        //   Byte: 11_01_10_00 = 0xD8
        byte[] codeLengths = [2, 2, 2, 2];
        int tableBits = 2;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        PngHuffman.BuildHuffmanTable(codeLengths, 0, 4, tableBits, symbols, lengths);

        byte[] data = [0xD8];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(0));
        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(1));
        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(2));
        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(3));
    }

    [Test]
    public void DecodeRoundTrip_FixedDistanceTable()
    {
        // The fixed distance table has 32 symbols with 5-bit codes.
        // Symbol i gets Huffman code i. The table index is reversed(i,5).
        //   reversed(0,5)=0, reversed(1,5)=16, reversed(2,5)=8, reversed(3,5)=24
        //
        // To encode symbols [0,1,2,3], write their table indices (reversed codes) as
        // 5-bit values into the bit stream, LSB first:
        //   Symbol 0: value 0  at bits 0-4
        //   Symbol 1: value 16 at bits 5-9
        //   Symbol 2: value 8  at bits 10-14
        //   Symbol 3: value 24 at bits 15-19
        //
        // Combined value: 0 + (16 << 5) + (8 << 10) + (24 << 15) = 0xC2200
        // Byte 0: 0x00, Byte 1: 0x22, Byte 2: 0x0C
        int tableBits = 5;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        PngHuffman.BuildFixedDistanceTable(symbols, lengths);

        byte[] data = [0x00, 0x22, 0x0C];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(0));
        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(1));
        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(2));
        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(3));
    }

    [Test]
    public void SingleSymbol_AlwaysDecodesToSameValue()
    {
        // Single symbol (symbol 5) with code length 1
        byte[] codeLengths = [0, 0, 0, 0, 0, 1];
        int tableBits = 1;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        bool result = PngHuffman.BuildHuffmanTable(codeLengths, 0, 6, tableBits, symbols, lengths);

        Assert.That(result, Is.True);

        // Both entries should decode to symbol 5
        Assert.That(symbols[0], Is.EqualTo(5));
        Assert.That(symbols[1], Is.EqualTo(5));
        Assert.That(lengths[0], Is.EqualTo(1));
        Assert.That(lengths[1], Is.EqualTo(1));

        // Verify decoding works with arbitrary bit data
        byte[] data = [0xFF];
        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(5));
        Assert.That(PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, lengths, tableBits), Is.EqualTo(5));
    }

    [Test]
    public void InvalidCodeLengths_ExceedsMaxBits_ReturnsFalse()
    {
        // Code length 16 > MaxLiteralBits (15)
        byte[] codeLengths = [1, 16];
        int tableBits = 4;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        bool result = PngHuffman.BuildHuffmanTable(codeLengths, 0, 2, tableBits, symbols, lengths);
        Assert.That(result, Is.False, "Should return false for code length exceeding max bits");
    }

    [Test]
    public void InvalidCodeLengths_AllZero_ReturnsTrueWithEmptyTable()
    {
        byte[] codeLengths = [0, 0, 0, 0];
        int tableBits = 2;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        bool result = PngHuffman.BuildHuffmanTable(codeLengths, 0, 4, tableBits, symbols, lengths);

        Assert.That(result, Is.True, "All-zero lengths should succeed with empty table");

        for (int i = 0; i < tableSize; i++)
        {
            Assert.That(lengths[i], Is.EqualTo(0), $"Entry {i} should have length 0");
        }
    }

    [Test]
    public void DecodeSymbol_EmptyStream_ReturnsMinusOne()
    {
        int[] symbols = [0];
        int[] codeLengths = [1];

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;
        ReadOnlySpan<byte> data = [];

        int result = PngHuffman.DecodeSymbol(ref bitBuffer, ref bitsAvailable, data, ref dataPos, symbols, codeLengths, 1);

        Assert.That(result, Is.EqualTo(-1), "Should return -1 when no data available");
    }

    [Test]
    public void BuildHuffmanTable_OutputBuffersTooSmall_ReturnsFalse()
    {
        byte[] codeLengths = [2, 2, 2, 2];
        int tableBits = 2;
        int[] symbols = new int[1]; // too small
        int[] lengths = new int[4];

        bool result = PngHuffman.BuildHuffmanTable(codeLengths, 0, 4, tableBits, symbols, lengths);
        Assert.That(result, Is.False, "Should return false when output buffers are too small");
    }

    [Test]
    public void BuildHuffmanTable_WithOffset_WorksCorrectly()
    {
        // Place valid code lengths at offset 3 within a larger array.
        // The offset parameter allows reading from a sub-range. Symbols are numbered
        // 0..count-1 within the sub-range (standard DEFLATE convention).
        byte[] codeLengths = [0, 0, 0, 1, 1, 0, 0];
        int tableBits = 1;
        int tableSize = 1 << tableBits;
        int[] symbols = new int[tableSize];
        int[] lengths = new int[tableSize];

        bool result = PngHuffman.BuildHuffmanTable(codeLengths, 3, 2, tableBits, symbols, lengths);

        Assert.That(result, Is.True);

        // Symbol 0 (first in range) gets code 0 -> reversed(0,1) = 0 -> table index 0
        // Symbol 1 (second in range) gets code 1 -> reversed(1,1) = 1 -> table index 1
        Assert.That(symbols[0], Is.EqualTo(0));
        Assert.That(symbols[1], Is.EqualTo(1));
    }
}
