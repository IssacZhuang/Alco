using System.Runtime.CompilerServices;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Huffman decoding for JPEG entropy-coded data.
/// Uses a flat lookup table for O(1) symbol decoding: peek N bits, index into table, get symbol + code length.
/// Bits are read MSB-first as required by the JPEG bit stream.
/// </summary>
internal static class JpegHuffman
{
    /// <summary>
    /// Maximum Huffman code length in baseline JPEG.
    /// </summary>
    public const int MaxCodeLength = 16;

    /// <summary>
    /// Huffman decode table for one JPEG component.
    /// Uses a flat lookup approach: table[peek_bits] directly gives symbol + actual code length.
    /// The table size is 2^MaxBits where MaxBits is the maximum code length in this table.
    /// For most JPEG Huffman tables, MaxBits is 9-12, giving table sizes of 512-4096 entries.
    /// </summary>
    public struct HuffmanTable
    {
        /// <summary>Flat lookup table. Entry[i] = {Symbol, Length} for peeked bits.</summary>
        public LookupEntry[] Table;

        /// <summary>Number of bits to peek for table lookup (= max code length in this table).</summary>
        public int TableBits;
    }

    /// <summary>
    /// Single entry in the Huffman lookup table.
    /// </summary>
    public struct LookupEntry
    {
        /// <summary>Decoded symbol value (0-255).</summary>
        public ushort Symbol;

        /// <summary>Code length in bits (1-16).</summary>
        public byte Length;
    }

    /// <summary>
    /// Build a Huffman table from DHT marker data.
    /// Creates a flat lookup table for O(1) decoding.
    /// </summary>
    public static bool BuildTable(ReadOnlySpan<byte> codeLengths, ReadOnlySpan<byte> values, ref HuffmanTable table)
    {
        if (codeLengths.Length != MaxCodeLength)
            return false;

        // Count total symbols and find max code length
        int totalSymbols = 0;
        int maxBits = 0;
        for (int i = 0; i < MaxCodeLength; i++)
        {
            totalSymbols += codeLengths[i];
            if (codeLengths[i] > 0)
                maxBits = i + 1;
        }

        if (values.Length < totalSymbols)
            return false;

        // Edge case: empty table
        if (totalSymbols == 0 || maxBits == 0)
        {
            table.TableBits = 0;
            table.Table = null!;
            return true;
        }

        int tableSize = 1 << maxBits;
        table.Table ??= new LookupEntry[tableSize];
        if (table.Table.Length < tableSize)
            table.Table = new LookupEntry[tableSize];

        table.Table.AsSpan().Clear();
        table.TableBits = maxBits;

        // Compute Huffman codes per length (JPEG spec Annex C, Figure C.1)
        Span<int> nextCode = stackalloc int[MaxCodeLength + 1];
        nextCode.Clear();

        int code = 0;
        for (int l = 1; l <= MaxCodeLength; l++)
        {
            nextCode[l] = code;
            code = (code + codeLengths[l - 1]) << 1;
        }

        // Fill the flat lookup table
        int symbolIndex = 0;
        for (int l = 1; l <= MaxCodeLength; l++)
        {
            int count = codeLengths[l - 1];
            if (count == 0)
                continue;

            // Each code of length l fills 2^(maxBits - l) entries
            int fillStep = 1 << (maxBits - l);

            for (int i = 0; i < count; i++)
            {
                int startIndex = nextCode[l] << (maxBits - l);

                var entry = new LookupEntry
                {
                    Symbol = values[symbolIndex],
                    Length = (byte)l
                };

                for (int j = startIndex; j < startIndex + fillStep; j++)
                    table.Table[j] = entry;

                nextCode[l]++;
                symbolIndex++;
            }
        }

        return true;
    }

    /// <summary>
    /// Decode one symbol from the JPEG bit stream using flat table lookup.
    /// Peeks TableBits bits, indexes into the flat table, and consumes the actual code length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DecodeSymbol(
        ref ulong bitBuffer,
        ref int bitsAvailable,
        ReadOnlySpan<byte> data,
        ref int dataPos,
        in HuffmanTable table)
    {
        FillBuffer(ref bitBuffer, ref bitsAvailable, data, ref dataPos);

        if (bitsAvailable <= 0)
            return -1;

        int tableBits = table.TableBits;
        int tableSize = 1 << tableBits;

        // Peek tableBits bits (MSB-first)
        int peekBits = bitsAvailable >= tableBits
            ? (int)((bitBuffer >> (bitsAvailable - tableBits)) & (ulong)(tableSize - 1))
            : (int)((bitBuffer << (tableBits - bitsAvailable)) & (ulong)(tableSize - 1));

        ref readonly var entry = ref table.Table[peekBits];

        if (entry.Length == 0)
            return -1;

        bitsAvailable -= entry.Length;
        bitBuffer &= (1UL << bitsAvailable) - 1;
        return entry.Symbol;
    }

    /// <summary>
    /// Receive and extend a value of the given bit count.
    /// JPEG spec section F.2.2.1: magnitude value decoding.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReceiveExtend(
        int bits,
        ref ulong bitBuffer,
        ref int bitsAvailable,
        ReadOnlySpan<byte> data,
        ref int dataPos)
    {
        if (bits == 0)
            return 0;

        FillBuffer(ref bitBuffer, ref bitsAvailable, data, ref dataPos);

        if (bitsAvailable < bits)
            return 0;

        int value = (int)((bitBuffer >> (bitsAvailable - bits)) & ((1UL << bits) - 1));
        bitBuffer &= (1UL << (bitsAvailable - bits)) - 1;
        bitsAvailable -= bits;

        if (value < (1 << (bits - 1)))
            value -= (1 << bits) - 1;

        return value;
    }

    /// <summary>
    /// Read raw unsigned bits from the bit stream without sign extension.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Receive(
        int bits,
        ref ulong bitBuffer,
        ref int bitsAvailable,
        ReadOnlySpan<byte> data,
        ref int dataPos)
    {
        if (bits == 0)
            return 0;

        FillBuffer(ref bitBuffer, ref bitsAvailable, data, ref dataPos);

        if (bitsAvailable < bits)
            return 0;

        int value = (int)((bitBuffer >> (bitsAvailable - bits)) & ((1UL << bits) - 1));
        bitBuffer &= (1UL << (bitsAvailable - bits)) - 1;
        bitsAvailable -= bits;

        return value;
    }

    /// <summary>
    /// Fill the bit buffer with more data from the source span.
    /// Reads bytes one at a time, handling JPEG byte stuffing (0xFF 0x00 -> 0xFF).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillBuffer(ref ulong bitBuffer, ref int bitsAvailable, ReadOnlySpan<byte> data, ref int dataPos)
    {
        while (bitsAvailable <= 56 && dataPos < data.Length)
        {
            byte b = data[dataPos++];

            if (b == 0xFF)
            {
                if (dataPos < data.Length && data[dataPos] == 0x00)
                    dataPos++;
            }

            bitBuffer = (bitBuffer << 8) | b;
            bitsAvailable += 8;
        }
    }
}
