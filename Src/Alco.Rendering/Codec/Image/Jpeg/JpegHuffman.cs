using System.Runtime.CompilerServices;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Huffman decoding for JPEG entropy-coded data.
/// Uses a flat lookup table for O(1) symbol decoding: peek N bits, index into table, get symbol + code length.
/// Bits are stored left-aligned in a 64-bit buffer (MSB-first), matching the JPEG bit stream order.
/// Consumption is done via left-shift, which is a single instruction on x86.
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

        /// <summary>
        /// Fast AC lookup table (512 entries) for combined Huffman + magnitude decode.
        /// Only populated for AC Huffman tables. Zero entries mean "not fast" (fall back to normal decode).
        /// Non-zero encoding: bits [15:8] = sign-extended coefficient value,
        /// bits [7:4] = run length (0-15), bits [3:0] = total bits to consume (huffman + magnitude).
        /// </summary>
        public short[] FastAc;
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
        table.Table = new LookupEntry[tableSize];
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

        // Build fast AC lookup table for combined Huffman + magnitude decode
        BuildFastAc(ref table);

        return true;
    }

    /// <summary>
    /// Build a fast AC lookup table that combines Huffman decode + magnitude extraction
    /// into a single table lookup. For each 9-bit peek value where the Huffman code length
    /// plus magnitude bits total ≤ 9, pre-compute the sign-extended value, run, and total
    /// consume bits. This avoids a separate Huffman decode + ReceiveExtend in the AC hot loop.
    /// </summary>
    private static void BuildFastAc(ref HuffmanTable table)
    {
        table.FastAc = new short[512];

        int tableBits = table.TableBits;
        if (tableBits == 0)
            return;

        int tableMask = (1 << tableBits) - 1;
        var lookupTable = table.Table;

        for (int i = 0; i < 512; i++)
        {
            // Map the 9-bit index to the actual table entry
            int tableIdx = i >> (9 - tableBits);
            if (tableIdx > tableMask)
                continue;

            ref readonly var entry = ref lookupTable[tableIdx];

            // For fast AC, the peek must find a valid symbol
            if (entry.Length == 0)
                continue;

            byte symbol = (byte)entry.Symbol;
            int run = (symbol >> 4) & 0x0F;
            int magBits = symbol & 0x0F;
            int codeLen = entry.Length;

            // Only beneficial when code length + magnitude bits ≤ 9
            if (magBits == 0 || codeLen + magBits > 9)
                continue;

            // Extract the magnitude bits from the 9-bit peek value
            int k = ((i << codeLen) & ((1 << 9) - 1)) >> (9 - magBits);

            // Sign-extend
            int m = 1 << (magBits - 1);
            if (k < m)
                k += -(1 << magBits);

            // Only encode if value fits in a byte (value stored in bits [15:8])
            if (k is >= -128 and <= 127)
            {
                // Encoding: bits[15:8] = sign-extended value, bits[7:4] = run, bits[3:0] = total consume bits
                table.FastAc[i] = (short)(k * 256 + run * 16 + codeLen + magBits);
            }
        }
    }

    /// <summary>
    /// Decode one symbol from the JPEG bit stream using flat table lookup.
    /// Peeks TableBits bits from the top of the buffer, indexes into the flat table,
    /// and consumes the actual code length via left-shift.
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

        // Peek tableBits bits from the top of the buffer (MSB-first, left-aligned)
        int peekBits;
        if (bitsAvailable >= tableBits)
        {
            peekBits = (int)(bitBuffer >> (64 - tableBits));
        }
        else
        {
            // Not enough bits: shift what we have to the top positions
            peekBits = (int)((bitBuffer >> (64 - bitsAvailable)) << (tableBits - bitsAvailable));
            peekBits &= (1 << tableBits) - 1;
        }

        ref readonly var entry = ref table.Table[peekBits];

        if (entry.Length == 0)
            return -1;

        // Consume bits via left-shift (single instruction)
        bitBuffer <<= entry.Length;
        bitsAvailable -= entry.Length;
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

        // Extract value from top of buffer
        int value = (int)(bitBuffer >> (64 - bits));
        bitBuffer <<= bits;
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

        int value = (int)(bitBuffer >> (64 - bits));
        bitBuffer <<= bits;
        bitsAvailable -= bits;

        return value;
    }

    /// <summary>
    /// Fill the bit buffer with more data from the source span.
    /// Reads bytes one at a time, handling JPEG byte stuffing (0xFF 0x00 -> 0xFF).
    /// New bytes are shifted into the bottom of the buffer, maintaining left-alignment.
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

            bitBuffer |= (ulong)b << (56 - bitsAvailable);
            bitsAvailable += 8;
        }
    }
}
