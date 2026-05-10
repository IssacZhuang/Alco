namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Huffman decoding for JPEG entropy-coded data.
/// Uses the classic MinCode/MaxCode/ValPtr approach per code length (1-16)
/// as described in the JPEG specification (ITU-T T.81, Annex C).
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
    /// Built once per DHT marker and reused for all MCUs.
    /// </summary>
    public struct HuffmanTable
    {
        /// <summary>Decoded symbol values, indexed via ValPtr.</summary>
        public ushort[] Values;

        /// <summary>Code size for each symbol in <see cref="Values"/>.</summary>
        public byte[] CodeSizes;

        /// <summary>Minimum code value for each code length (index 1-16). Index 0 is unused (-1).</summary>
        public int[] MinCode;

        /// <summary>Maximum code value for each code length (index 1-16). Index 0 is unused (-1).</summary>
        public int[] MaxCode;

        /// <summary>Offset into <see cref="Values"/> for each code length (index 1-16). Index 0 is unused.</summary>
        public int[] ValPtr;
    }

    /// <summary>
    /// Build a Huffman table from DHT marker data.
    /// </summary>
    /// <param name="codeLengths">
    /// Array of 16 values: count of codes of each length (1-16).
    /// This is the BITS field from the JPEG DHT marker.
    /// </param>
    /// <param name="values">
    /// Symbol values following the code length counts (the HUFFVAL field).
    /// Length must equal the sum of <paramref name="codeLengths"/>.
    /// </param>
    /// <param name="table">
    /// The Huffman table to populate. Caller must ensure the arrays are allocated.
    /// <see cref="HuffmanTable.Values"/> and <see cref="HuffmanTable.CodeSizes"/> must have
    /// length equal to the sum of <paramref name="codeLengths"/>.
    /// <see cref="HuffmanTable.MinCode"/>, <see cref="HuffmanTable.MaxCode"/>,
    /// and <see cref="HuffmanTable.ValPtr"/> must have length at least 17.
    /// </param>
    /// <returns><c>true</c> if the table was built successfully; <c>false</c> if the input is invalid.</returns>
    public static bool BuildTable(ReadOnlySpan<byte> codeLengths, ReadOnlySpan<byte> values, ref HuffmanTable table)
    {
        if (codeLengths.Length != MaxCodeLength)
            return false;

        // Count total symbols
        int totalSymbols = 0;
        for (int i = 0; i < MaxCodeLength; i++)
            totalSymbols += codeLengths[i];

        if (values.Length < totalSymbols)
            return false;

        // Ensure arrays are allocated and large enough
        table.Values ??= new ushort[totalSymbols];
        table.CodeSizes ??= new byte[totalSymbols];
        table.MinCode ??= new int[MaxCodeLength + 1];
        table.MaxCode ??= new int[MaxCodeLength + 1];
        table.ValPtr ??= new int[MaxCodeLength + 1];

        if (table.Values.Length < totalSymbols ||
            table.CodeSizes.Length < totalSymbols ||
            table.MinCode.Length < MaxCodeLength + 1 ||
            table.MaxCode.Length < MaxCodeLength + 1 ||
            table.ValPtr.Length < MaxCodeLength + 1)
        {
            return false;
        }

        // Initialize MinCode/MaxCode to -1 (unused)
        for (int i = 0; i <= MaxCodeLength; i++)
        {
            table.MinCode[i] = -1;
            table.MaxCode[i] = -1;
            table.ValPtr[i] = 0;
        }

        // Edge case: empty table (no symbols)
        if (totalSymbols == 0)
            return true;

        // Compute Huffman codes per length (JPEG spec Annex C, Figure C.1).
        // nextCode[l] = the first Huffman code value for code length l.
        Span<int> nextCode = stackalloc int[MaxCodeLength + 1];
        nextCode.Clear();

        int code = 0;
        for (int l = 1; l <= MaxCodeLength; l++)
        {
            nextCode[l] = code;
            code = (code + codeLengths[l - 1]) << 1;
        }

        // Fill the table arrays per code length
        int symbolIndex = 0;
        for (int l = 1; l <= MaxCodeLength; l++)
        {
            int count = codeLengths[l - 1];
            if (count == 0)
                continue;

            table.MinCode[l] = nextCode[l];
            table.MaxCode[l] = nextCode[l] + count - 1;
            table.ValPtr[l] = symbolIndex;

            for (int i = 0; i < count; i++)
            {
                table.Values[symbolIndex] = values[symbolIndex];
                table.CodeSizes[symbolIndex] = (byte)l;
                symbolIndex++;
            }

            // Advance nextCode for the next length
            nextCode[l] += count;
        }

        return true;
    }

    /// <summary>
    /// Decode one symbol from the JPEG bit stream.
    /// Bits are consumed MSB-first as required by JPEG.
    /// </summary>
    /// <param name="bitBuffer">Current bit buffer (MSB-first). Updated in place.</param>
    /// <param name="bitsAvailable">Number of valid bits in <paramref name="bitBuffer"/>. Updated in place.</param>
    /// <param name="data">Source data span to refill from (entropy-coded segment).</param>
    /// <param name="dataPos">Current byte position in <paramref name="data"/>. Updated in place.</param>
    /// <param name="table">The Huffman table to use for decoding.</param>
    /// <returns>The decoded symbol value, or -1 on error.</returns>
    public static int DecodeSymbol(
        ref ulong bitBuffer,
        ref int bitsAvailable,
        ReadOnlySpan<byte> data,
        ref int dataPos,
        in HuffmanTable table)
    {
        // Ensure at least 16 bits are available
        FillBuffer(ref bitBuffer, ref bitsAvailable, data, ref dataPos);

        if (bitsAvailable <= 0)
            return -1;

        // Peek 16 bits MSB-first. The bitBuffer stores bits MSB-aligned:
        // the most recently read bits are in the lowest positions,
        // but JPEG reads MSB-first so we need to extract from the top.
        // We maintain bitBuffer with bits filled LSB-first (same as PngHuffman),
        // so to peek l bits MSB-first, we shift right by (bitsAvailable - l).
        int code = 0;

        for (int l = 1; l <= MaxCodeLength; l++)
        {
            if (bitsAvailable < l)
                return -1;

            // Extract the top l bits of the available bits (MSB-first read)
            code = (code << 1) | (int)((bitBuffer >> (bitsAvailable - l)) & 1);

            int minCode = table.MinCode[l];
            int maxCode = table.MaxCode[l];

            if (minCode != -1 && code >= minCode && code <= maxCode)
            {
                // Found a match: consume l bits and return the symbol
                bitBuffer &= (1UL << (bitsAvailable - l)) - 1;
                bitsAvailable -= l;
                return table.Values[table.ValPtr[l] + code - minCode];
            }
        }

        // No valid code found
        return -1;
    }

    /// <summary>
    /// Receive and extend a value of the given bit count.
    /// JPEG spec section F.2.2.1: magnitude value decoding.
    /// </summary>
    /// <param name="bits">Number of bits to read (0-16).</param>
    /// <param name="bitBuffer">Current bit buffer. Updated in place.</param>
    /// <param name="bitsAvailable">Number of valid bits. Updated in place.</param>
    /// <param name="data">Source data span.</param>
    /// <param name="dataPos">Current byte position. Updated in place.</param>
    /// <returns>The sign-extended value.</returns>
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

        // Read 'bits' bits MSB-first from the buffer
        int value = (int)((bitBuffer >> (bitsAvailable - bits)) & ((1UL << bits) - 1));
        bitBuffer &= (1UL << (bitsAvailable - bits)) - 1;
        bitsAvailable -= bits;

        // Sign extend: if the MSB is not set, the value is negative
        if (value < (1 << (bits - 1)))
            value -= (1 << bits) - 1;

        return value;
    }

    /// <summary>
    /// Fill the bit buffer with more data from the source span.
    /// Reads bytes one at a time, handling JPEG byte stuffing (0xFF 0x00 -> 0xFF).
    /// </summary>
    private static void FillBuffer(ref ulong bitBuffer, ref int bitsAvailable, ReadOnlySpan<byte> data, ref int dataPos)
    {
        while (bitsAvailable <= 56 && dataPos < data.Length)
        {
            byte b = data[dataPos++];

            // JPEG byte stuffing: 0xFF followed by 0x00 represents literal 0xFF
            if (b == 0xFF)
            {
                if (dataPos < data.Length && data[dataPos] == 0x00)
                {
                    dataPos++; // skip the 0x00 stuffing byte
                }
                // If 0xFF is followed by a non-zero marker byte, we stop reading
                // (markers are handled by the caller). Fall through to add the 0xFF.
            }

            bitBuffer = (bitBuffer << 8) | b;
            bitsAvailable += 8;
        }
    }
}
