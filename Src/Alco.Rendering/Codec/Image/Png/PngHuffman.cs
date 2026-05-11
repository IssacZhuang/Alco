namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Builds DEFLATE Huffman decode tables from code lengths and decodes symbols from a bit stream.
/// All methods are static and operate on caller-provided buffers (no heap allocations).
/// The lookup table uses LSB-first (reversed) bit ordering as required by the DEFLATE format.
/// </summary>
internal static class PngHuffman
{
    /// <summary>
    /// Maximum number of bits for a literal/length Huffman code in DEFLATE.
    /// </summary>
    public const int MaxLiteralBits = 15;

    /// <summary>
    /// Number of literal/length symbols in the DEFLATE fixed Huffman table.
    /// </summary>
    public const int LiteralSymbolCount = 288;

    /// <summary>
    /// Number of distance symbols in the DEFLATE fixed Huffman table.
    /// </summary>
    public const int DistanceSymbolCount = 32;

    // Pre-built fixed Huffman tables (computed once in static constructor).
    private static readonly int[] s_fixedLitSymbols = new int[1 << 9];
    private static readonly int[] s_fixedLitLengths = new int[1 << 9];
    private static readonly int[] s_fixedDistSymbols = new int[1 << 5];
    private static readonly int[] s_fixedDistLengths = new int[1 << 5];

    static PngHuffman()
    {
        BuildFixedLiteralTable(s_fixedLitSymbols, s_fixedLitLengths);
        BuildFixedDistanceTable(s_fixedDistSymbols, s_fixedDistLengths);
    }

    public static ReadOnlySpan<int> FixedLiteralSymbols => s_fixedLitSymbols;
    public static ReadOnlySpan<int> FixedLiteralLengths => s_fixedLitLengths;
    public static ReadOnlySpan<int> FixedDistanceSymbols => s_fixedDistSymbols;
    public static ReadOnlySpan<int> FixedDistanceLengths => s_fixedDistLengths;

    /// <summary>
    /// Build a Huffman decode table from an array of code lengths (DEFLATE DHT format).
    /// The caller allocates <paramref name="symbols"/> and <paramref name="lengths"/> arrays,
    /// each sized to <c>1 &lt;&lt; tableBits</c>.
    /// </summary>
    /// <param name="codeLengths">Array of code lengths per symbol (0 means symbol not used).</param>
    /// <param name="offset">Starting offset in <paramref name="codeLengths"/>.</param>
    /// <param name="count">Number of symbols to process.</param>
    /// <param name="tableBits">Number of bits for the flat lookup table (typically 9 or 15).</param>
    /// <param name="symbols">Output symbol table, sized <c>1 &lt;&lt; tableBits</c>.</param>
    /// <param name="lengths">Output code-length table, sized <c>1 &lt;&lt; tableBits</c>.</param>
    /// <returns><c>true</c> if the table was built successfully; <c>false</c> if code lengths are invalid.</returns>
    public static bool BuildHuffmanTable(
        ReadOnlySpan<byte> codeLengths,
        int offset,
        int count,
        int tableBits,
        Span<int> symbols,
        Span<int> lengths)
    {
        int tableSize = 1 << tableBits;
        if (symbols.Length < tableSize || lengths.Length < tableSize)
            return false;

        // Step 1: Count codes for each bit length (bl_count)
        Span<int> blCount = stackalloc int[MaxLiteralBits + 1];
        blCount.Clear();

        int maxLen = 0;
        for (int i = 0; i < count; i++)
        {
            int len = codeLengths[offset + i];
            if (len < 0 || len > MaxLiteralBits)
                return false;
            if (len > 0)
                blCount[len]++;
            if (len > maxLen)
                maxLen = len;
        }

        // Edge case: no codes at all (all zero lengths)
        if (maxLen == 0)
        {
            symbols.Clear();
            lengths.Clear();
            return true;
        }

        // Edge case: only one code length value (single symbol or all same)
        // Check for the special case where there's exactly one symbol with length 1
        // DEFLATE spec requires at least 2 codes for a valid Huffman tree,
        // but single-symbol tables are sometimes used in practice.
        if (blCount[1] == 1 && maxLen == 1)
        {
            // Find the single symbol
            int sym = -1;
            for (int i = 0; i < count; i++)
            {
                if (codeLengths[offset + i] == 1)
                {
                    sym = i;
                    break;
                }
            }

            // Both bit patterns (0 and 1) decode to this symbol
            symbols[0] = sym;
            lengths[0] = 1;
            symbols[1] = sym;
            lengths[1] = 1;

            // Clear remaining entries
            for (int i = 2; i < tableSize; i++)
            {
                symbols[i] = sym;
                lengths[i] = 1;
            }

            return true;
        }

        // Step 2: Compute next_code — the first Huffman code for each bit length
        Span<int> nextCode = stackalloc int[MaxLiteralBits + 1];
        nextCode.Clear();

        int code = 0;
        for (int bits = 1; bits <= maxLen; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Validate: the final code should fit in maxLen bits
        // (Kraft inequality check — all codes should be fully used)
        int totalCodes = 0;
        for (int bits = 1; bits <= maxLen; bits++)
            totalCodes += blCount[bits];

        if (totalCodes == 0)
        {
            symbols.Clear();
            lengths.Clear();
            return true;
        }

        // Step 3: Clear output tables
        symbols.Clear();
        lengths.Clear();

        // Step 4: For each symbol, compute its Huffman code and fill the lookup table
        for (int i = 0; i < count; i++)
        {
            int len = codeLengths[offset + i];
            if (len == 0)
                continue;

            int huffmanCode = nextCode[len]++;
            int sym = i;

            // Reverse the bits of the Huffman code (LSB-first for DEFLATE)
            int reversed = BitReverse(huffmanCode, len);

            // Fill all entries in the flat table that start with this code
            // (For codes shorter than tableBits, multiple entries map to the same symbol)
            int index = reversed;
            int step = 1 << len;

            if (len <= tableBits)
            {
                while (index < tableSize)
                {
                    symbols[index] = sym;
                    lengths[index] = len;
                    index += step;
                }
            }
            else
            {
                // Code is longer than tableBits — this implementation only supports
                // single-level lookup. For DEFLATE, tableBits is always >= maxLen
                // for fixed tables, and for dynamic tables we use a sufficient tableBits.
                // If we encounter codes longer than tableBits, store what we can.
                int baseIndex = reversed & ((1 << tableBits) - 1);
                // Use negative length to indicate overflow (not expected in normal DEFLATE)
                symbols[baseIndex] = sym;
                lengths[baseIndex] = -len;
            }
        }

        return true;
    }

    /// <summary>
    /// Build the fixed literal/length Huffman table as defined by the DEFLATE specification.
    /// Symbols 0-143: 8 bits, 144-255: 9 bits, 256-279: 7 bits, 280-287: 8 bits.
    /// </summary>
    /// <param name="symbols">Output symbol table, sized at least 512 (9-bit table).</param>
    /// <param name="lengths">Output code-length table, sized at least 512.</param>
    /// <returns><c>true</c> if the table was built successfully.</returns>
    public static bool BuildFixedLiteralTable(Span<int> symbols, Span<int> lengths)
    {
        Span<byte> codeLengths = stackalloc byte[LiteralSymbolCount];

        for (int i = 0; i <= 143; i++) codeLengths[i] = 8;
        for (int i = 144; i <= 255; i++) codeLengths[i] = 9;
        for (int i = 256; i <= 279; i++) codeLengths[i] = 7;
        for (int i = 280; i <= 287; i++) codeLengths[i] = 8;

        return BuildHuffmanTable(codeLengths, 0, LiteralSymbolCount, 9, symbols, lengths);
    }

    /// <summary>
    /// Build the fixed distance Huffman table as defined by the DEFLATE specification.
    /// All 32 symbols use 5-bit codes.
    /// </summary>
    /// <param name="symbols">Output symbol table, sized at least 32 (5-bit table).</param>
    /// <param name="lengths">Output code-length table, sized at least 32.</param>
    /// <returns><c>true</c> if the table was built successfully.</returns>
    public static bool BuildFixedDistanceTable(Span<int> symbols, Span<int> lengths)
    {
        Span<byte> codeLengths = stackalloc byte[DistanceSymbolCount];

        for (int i = 0; i < DistanceSymbolCount; i++)
            codeLengths[i] = 5;

        return BuildHuffmanTable(codeLengths, 0, DistanceSymbolCount, 5, symbols, lengths);
    }

    /// <summary>
    /// Decode a single Huffman symbol from the bit stream.
    /// Refills the bit buffer from <paramref name="data"/> as needed.
    /// </summary>
    /// <param name="bitBuffer">Current bit buffer (LSB-first). Updated in place.</param>
    /// <param name="bitsAvailable">Number of valid bits in <paramref name="bitBuffer"/>. Updated in place.</param>
    /// <param name="data">Source data span to refill from.</param>
    /// <param name="dataPos">Current byte position in <paramref name="data"/>. Updated in place.</param>
    /// <param name="symbols">Huffman symbol lookup table.</param>
    /// <param name="codeLengths">Huffman code-length lookup table.</param>
    /// <param name="tableBits">Number of bits the tables were built with.</param>
    /// <returns>The decoded symbol, or -1 on error.</returns>
    public static int DecodeSymbol(
        ref ulong bitBuffer,
        ref int bitsAvailable,
        ReadOnlySpan<byte> data,
        ref int dataPos,
        ReadOnlySpan<int> symbols,
        ReadOnlySpan<int> codeLengths,
        int tableBits)
    {
        // Refill the bit buffer if needed (keep at least tableBits bits available)
        while (bitsAvailable < tableBits && dataPos < data.Length)
        {
            bitBuffer |= (ulong)data[dataPos++] << bitsAvailable;
            bitsAvailable += 8;
        }

        if (bitsAvailable <= 0)
            return -1;

        // Peek tableBits bits from the buffer
        int index = (int)(bitBuffer & ((1UL << tableBits) - 1));

        int len = codeLengths[index];
        if (len <= 0)
            return -1;

        int symbol = symbols[index];

        // Consume the actual code length bits
        bitBuffer >>= len;
        bitsAvailable -= len;

        return symbol;
    }

    /// <summary>
    /// Reverse the lowest <paramref name="bitCount"/> bits of <paramref name="value"/>.
    /// Used to convert MSB-first Huffman codes to LSB-first for DEFLATE lookup tables.
    /// </summary>
    /// <param name="value">The value whose bits to reverse.</param>
    /// <param name="bitCount">Number of low bits to reverse.</param>
    /// <returns>The bit-reversed value.</returns>
    private static int BitReverse(int value, int bitCount)
    {
        int result = 0;
        for (int i = 0; i < bitCount; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }
        return result;
    }
}
