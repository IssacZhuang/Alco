using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Decompresses zlib-wrapped DEFLATE streams as used in PNG IDAT chunks.
/// Includes a bit reader for LSB-first DEFLATE parsing, a full Inflate implementation
/// supporting all three DEFLATE block types, and an Adler-32 checksum with SIMD acceleration.
/// </summary>
internal static class PngZlib
{
    /// <summary>Modulus base for Adler-32.</summary>
    private const uint AdlerMod = 65521;

    /// <summary>Maximum number of bytes to accumulate before reducing mod 65521 (prevents uint32 overflow).</summary>
    private const int AdlerNMax = 5552;

    /// <summary>Weight vector for SIMD Adler-32: weights 16,15,...,9 for bytes 0-7.</summary>
    private static ReadOnlySpan<short> WeightLo => [16, 15, 14, 13, 12, 11, 10, 9];

    /// <summary>Weight vector for SIMD Adler-32: weights 8,7,...,1 for bytes 8-15.</summary>
    private static ReadOnlySpan<short> WeightHi => [8, 7, 6, 5, 4, 3, 2, 1];

    /// <summary>DEFLATE length code base values for codes 257-285.</summary>
    private static ReadOnlySpan<ushort> LengthBase =>
    [
        3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
        35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258
    ];

    /// <summary>Extra bits for DEFLATE length codes 257-285.</summary>
    private static ReadOnlySpan<byte> LengthExtra =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0
    ];

    /// <summary>DEFLATE distance code base values for codes 0-29.</summary>
    private static ReadOnlySpan<ushort> DistanceBase =>
    [
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
        257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577
    ];

    /// <summary>Extra bits for DEFLATE distance codes 0-29.</summary>
    private static ReadOnlySpan<byte> DistanceExtra =>
    [
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
    ];

    /// <summary>
    /// Order in which code length alphabet lengths are transmitted in dynamic Huffman blocks.
    /// Defined by DEFLATE specification (RFC 1951 section 3.2.7).
    /// </summary>
    private static ReadOnlySpan<byte> CodeLengthOrder =>
        [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    /// <summary>
    /// Decompress a zlib-wrapped DEFLATE stream into the output buffer.
    /// Verifies the 2-byte zlib header and the trailing Adler-32 checksum.
    /// </summary>
    /// <param name="data">Zlib-compressed data (header + compressed stream + checksum).</param>
    /// <param name="output">Destination buffer for decompressed data.</param>
    /// <returns>Number of bytes written to <paramref name="output"/>.</returns>
    /// <exception cref="ImageDecodeException">Thrown on invalid zlib header, corrupt data, or checksum mismatch.</exception>
    public static int DecompressZlib(ReadOnlySpan<byte> data, Span<byte> output)
    {
        if (data.Length < 6)
            throw new ImageDecodeException("Zlib data too short for header and checksum.");

        // Parse zlib header (2 bytes)
        byte cmf = data[0];
        byte flg = data[1];

        // CMF: low 4 bits = CM (compression method), must be 8 (deflate)
        // CMF: high 4 bits = CINFO (window size), must be <= 7
        int cm = cmf & 0x0F;
        int cinfo = (cmf >> 4) & 0x0F;

        if (cm != 8)
            throw new ImageDecodeException($"Invalid zlib CM field: expected 8 (deflate), got {cm}.");

        if (cinfo > 7)
            throw new ImageDecodeException($"Invalid zlib CINFO field: expected <= 7, got {cinfo}.");

        // FLG: FCHECK must satisfy (CMF*256 + FLG) % 31 == 0
        if (((cmf * 256 + flg) % 31) != 0)
            throw new ImageDecodeException("Invalid zlib header: FCHECK failed.");

        // Inflate the compressed data (skip 2-byte header, exclude last 4 bytes for checksum)
        ReadOnlySpan<byte> compressedData = data.Slice(2, data.Length - 6);
        int bytesWritten = Inflate(compressedData, output);

        // Read and verify Adler-32 checksum (big-endian, last 4 bytes)
        uint expectedChecksum = (uint)((data[data.Length - 4] << 24) |
                                       (data[data.Length - 3] << 16) |
                                       (data[data.Length - 2] << 8) |
                                       data[data.Length - 1]);
        uint actualChecksum = Adler32(output.Slice(0, bytesWritten));

        if (actualChecksum != expectedChecksum)
            throw new ImageDecodeException($"Adler-32 checksum mismatch: expected 0x{expectedChecksum:X8}, got 0x{actualChecksum:X8}.");

        return bytesWritten;
    }

    /// <summary>
    /// Inflate (decompress) a raw DEFLATE stream.
    /// Handles all three block types: stored (0), fixed Huffman (1), and dynamic Huffman (2).
    /// </summary>
    /// <param name="compressed">Raw DEFLATE compressed data.</param>
    /// <param name="output">Destination buffer for decompressed data.</param>
    /// <returns>Number of bytes written to <paramref name="output"/>.</returns>
    /// <exception cref="ImageDecodeException">Thrown on corrupt DEFLATE data.</exception>
    public static int Inflate(ReadOnlySpan<byte> compressed, Span<byte> output)
    {
        var reader = new BitReader(compressed);
        int outputPos = 0;

        // Build fixed Huffman tables (reused across all fixed blocks)
        int[] fixedLitSymbols = new int[1 << 9];
        int[] fixedLitLengths = new int[1 << 9];
        int[] fixedDistSymbols = new int[1 << 5];
        int[] fixedDistLengths = new int[1 << 5];

        bool fixedTablesBuilt = false;

        while (true)
        {
            // Read block header: BFINAL (1 bit), BTYPE (2 bits)
            int bfinal = reader.ReadBits(1);
            int btype = reader.ReadBits(2);

            switch (btype)
            {
                case 0: // Stored / uncompressed
                    reader.AlignToByte();
                    InflateStoredBlock(ref reader, output, ref outputPos);
                    break;

                case 1: // Fixed Huffman
                    if (!fixedTablesBuilt)
                    {
                        if (!PngHuffman.BuildFixedLiteralTable(fixedLitSymbols, fixedLitLengths))
                            throw new ImageDecodeException("Failed to build fixed literal Huffman table.");
                        if (!PngHuffman.BuildFixedDistanceTable(fixedDistSymbols, fixedDistLengths))
                            throw new ImageDecodeException("Failed to build fixed distance Huffman table.");
                        fixedTablesBuilt = true;
                    }
                    InflateHuffmanBlock(ref reader, output, ref outputPos,
                        fixedLitSymbols, fixedLitLengths, 9,
                        fixedDistSymbols, fixedDistLengths, 5);
                    break;

                case 2: // Dynamic Huffman
                    InflateDynamicHuffmanBlock(ref reader, output, ref outputPos);
                    break;

                default:
                    throw new ImageDecodeException($"Invalid DEFLATE block type: {btype}.");
            }

            if (bfinal != 0)
                break;
        }

        return outputPos;
    }

    /// <summary>
    /// Compute the Adler-32 checksum of the given data.
    /// Uses SIMD (SSE2/AVX2) acceleration when available for large inputs.
    /// </summary>
    /// <param name="data">Input data to checksum.</param>
    /// <returns>The Adler-32 checksum value.</returns>
    public static uint Adler32(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return 1;

        uint a = 1;
        uint b = 0;

        // Use SIMD path for large inputs
        if (Sse2.IsSupported && data.Length >= 32)
        {
            Adler32Simd(data, ref a, ref b);
        }
        else
        {
            Adler32Scalar(data, ref a, ref b);
        }

        return (b << 16) | a;
    }

    /// <summary>
    /// Inflate a stored (uncompressed) DEFLATE block.
    /// Reads LEN/NLEN, copies raw bytes.
    /// </summary>
    private static void InflateStoredBlock(ref BitReader reader, Span<byte> output, ref int outputPos)
    {
        int len = reader.ReadBits(16);
        int nlen = reader.ReadBits(16);

        if ((len ^ 0xFFFF) != nlen)
            throw new ImageDecodeException($"Invalid stored block: LEN={len}, NLEN={nlen} (complement check failed).");

        if (outputPos + len > output.Length)
            throw new ImageDecodeException("Stored block exceeds output buffer.");

        for (int i = 0; i < len; i++)
            output[outputPos++] = (byte)reader.ReadBits(8);
    }

    /// <summary>
    /// Inflate a Huffman-coded DEFLATE block (used for both fixed and dynamic tables).
    /// </summary>
    private static void InflateHuffmanBlock(
        ref BitReader reader,
        Span<byte> output,
        ref int outputPos,
        int[] litSymbols, int[] litLengths, int litTableBits,
        int[] distSymbols, int[] distLengths, int distTableBits)
    {
        // We need to pass individual ref fields to DecodeSymbol because BitReader is a ref struct
        // and cannot be captured by lambda or passed by ref-through-ref-struct.
        // Instead, we extract the fields and work with them directly.
        ulong bitBuffer = reader._bitBuffer;
        int bitsAvailable = reader._bitsAvailable;
        ReadOnlySpan<byte> data = reader._data;
        int dataPos = reader._dataPos;

        while (true)
        {
            int symbol = PngHuffman.DecodeSymbol(
                ref bitBuffer, ref bitsAvailable, data, ref dataPos,
                litSymbols, litLengths, litTableBits);

            if (symbol < 0)
                throw new ImageDecodeException("Failed to decode literal/length symbol.");

            if (symbol < 256)
            {
                // Literal byte
                if (outputPos >= output.Length)
                    throw new ImageDecodeException("Output buffer overflow during literal decode.");
                output[outputPos++] = (byte)symbol;
            }
            else if (symbol == 256)
            {
                // End of block
                break;
            }
            else
            {
                // Length/distance pair
                int lengthCode = symbol - 257;
                if (lengthCode >= LengthBase.Length)
                    throw new ImageDecodeException($"Invalid length code: {symbol}.");

                int length = LengthBase[lengthCode];
                int extraBits = LengthExtra[lengthCode];
                if (extraBits > 0)
                    length += reader.ReadBitsFromFields(ref bitBuffer, ref bitsAvailable, data, ref dataPos, extraBits);

                // Decode distance
                int distCode = PngHuffman.DecodeSymbol(
                    ref bitBuffer, ref bitsAvailable, data, ref dataPos,
                    distSymbols, distLengths, distTableBits);

                if (distCode < 0 || distCode >= DistanceBase.Length)
                    throw new ImageDecodeException($"Invalid distance code: {distCode}.");

                int distance = DistanceBase[distCode];
                int distExtra = DistanceExtra[distCode];
                if (distExtra > 0)
                    distance += reader.ReadBitsFromFields(ref bitBuffer, ref bitsAvailable, data, ref dataPos, distExtra);

                if (distance > outputPos)
                    throw new ImageDecodeException($"Distance {distance} exceeds available output ({outputPos} bytes).");

                if (outputPos + length > output.Length)
                    throw new ImageDecodeException("Output buffer overflow during length/distance copy.");

                // Copy from back-reference, handling overlapping copies (distance < length)
                int srcPos = outputPos - distance;
                for (int i = 0; i < length; i++)
                    output[outputPos++] = output[srcPos++];
            }
        }

        // Write back the bit reader state
        reader._bitBuffer = bitBuffer;
        reader._bitsAvailable = bitsAvailable;
        reader._dataPos = dataPos;
    }

    /// <summary>
    /// Inflate a dynamic Huffman DEFLATE block.
    /// Reads the table definitions from the stream, builds Huffman tables, then decodes.
    /// </summary>
    private static void InflateDynamicHuffmanBlock(ref BitReader reader, Span<byte> output, ref int outputPos)
    {
        // Read table parameters
        int hlit = reader.ReadBits(5) + 257;   // number of literal/length codes (257-286)
        int hdist = reader.ReadBits(5) + 1;     // number of distance codes (1-32)
        int hclen = reader.ReadBits(4) + 4;     // number of code length codes (4-19)

        // Read code length code lengths in permuted order
        Span<byte> codeLengthCodeLengths = stackalloc byte[19];
        codeLengthCodeLengths.Clear();
        for (int i = 0; i < hclen; i++)
            codeLengthCodeLengths[CodeLengthOrder[i]] = (byte)reader.ReadBits(3);

        // Build code length Huffman table
        const int ClTableBits = 7;
        int clTableSize = 1 << ClTableBits;
        Span<int> clSymbols = stackalloc int[clTableSize];
        Span<int> clLengths = stackalloc int[clTableSize];

        if (!PngHuffman.BuildHuffmanTable(codeLengthCodeLengths, 0, 19, ClTableBits, clSymbols, clLengths))
            throw new ImageDecodeException("Failed to build code length Huffman table.");

        // Decode literal/length and distance code lengths
        int totalCodes = hlit + hdist;
        Span<byte> allCodeLengths = stackalloc byte[totalCodes];
        allCodeLengths.Clear();

        int decodedCount = 0;
        while (decodedCount < totalCodes)
        {
            ulong bitBuffer = reader._bitBuffer;
            int bitsAvailable = reader._bitsAvailable;
            ReadOnlySpan<byte> data = reader._data;
            int dataPos = reader._dataPos;

            int sym = PngHuffman.DecodeSymbol(
                ref bitBuffer, ref bitsAvailable, data, ref dataPos,
                clSymbols, clLengths, ClTableBits);

            reader._bitBuffer = bitBuffer;
            reader._bitsAvailable = bitsAvailable;
            reader._dataPos = dataPos;

            if (sym < 0)
                throw new ImageDecodeException("Failed to decode code length symbol.");

            if (sym < 16)
            {
                allCodeLengths[decodedCount++] = (byte)sym;
            }
            else
            {
                int repeatCount;
                byte repeatValue;

                switch (sym)
                {
                    case 16: // Repeat previous code length 3-6 times
                        repeatCount = reader.ReadBits(2) + 3;
                        if (decodedCount == 0)
                            throw new ImageDecodeException("Code length repeat (16) with no previous symbol.");
                        repeatValue = allCodeLengths[decodedCount - 1];
                        break;

                    case 17: // Repeat zero 3-10 times
                        repeatCount = reader.ReadBits(3) + 3;
                        repeatValue = 0;
                        break;

                    case 18: // Repeat zero 11-138 times
                        repeatCount = reader.ReadBits(7) + 11;
                        repeatValue = 0;
                        break;

                    default:
                        throw new ImageDecodeException($"Invalid code length symbol: {sym}.");
                }

                if (decodedCount + repeatCount > totalCodes)
                    throw new ImageDecodeException("Code length repeat exceeds total code count.");

                for (int i = 0; i < repeatCount; i++)
                    allCodeLengths[decodedCount++] = repeatValue;
            }
        }

        // Build literal/length Huffman table from first hlit code lengths
        const int LitTableBits = 15;
        int litTableSize = 1 << LitTableBits;
        int[] litSymbols = new int[litTableSize];
        int[] litLengths = new int[litTableSize];

        if (!PngHuffman.BuildHuffmanTable(allCodeLengths, 0, hlit, LitTableBits, litSymbols, litLengths))
            throw new ImageDecodeException("Failed to build dynamic literal/length Huffman table.");

        // Build distance Huffman table from last hdist code lengths
        const int DistTableBits = 15;
        int distTableSize = 1 << DistTableBits;
        int[] distSymbols = new int[distTableSize];
        int[] distLengths = new int[distTableSize];

        if (!PngHuffman.BuildHuffmanTable(allCodeLengths, hlit, hdist, DistTableBits, distSymbols, distLengths))
            throw new ImageDecodeException("Failed to build dynamic distance Huffman table.");

        // Decode data using the dynamic tables
        InflateHuffmanBlock(ref reader, output, ref outputPos,
            litSymbols, litLengths, LitTableBits,
            distSymbols, distLengths, DistTableBits);
    }

    /// <summary>
    /// Scalar Adler-32 computation, processing in NMAX-sized batches to prevent overflow.
    /// </summary>
    private static void Adler32Scalar(ReadOnlySpan<byte> data, ref uint a, ref uint b)
    {
        int offset = 0;
        int remaining = data.Length;

        while (remaining > 0)
        {
            int batch = Math.Min(remaining, AdlerNMax);

            for (int i = 0; i < batch; i++)
            {
                a += data[offset + i];
                b += a;
            }

            a %= AdlerMod;
            b %= AdlerMod;

            offset += batch;
            remaining -= batch;
        }
    }

    /// <summary>
    /// SIMD-accelerated Adler-32 computation using SSE2.
    /// Processes 16 bytes at a time, computing both the byte sum and weighted sum efficiently.
    /// The formula for a group of 16 bytes is: b += 16*a + weighted_sum where
    /// weighted_sum = 16*byte[0] + 15*byte[1] + ... + 1*byte[15].
    /// </summary>
    private static void Adler32Simd(ReadOnlySpan<byte> data, ref uint a, ref uint b)
    {
        int offset = 0;
        int remaining = data.Length;

        // Weight vectors for computing weighted sum:
        // wLo multiplies bytes 0-7 with weights 16,15,...,9
        // wHi multiplies bytes 8-15 with weights 8,7,...,1
        Vector128<short> wLo = Unsafe.ReadUnaligned<Vector128<short>>(
            ref Unsafe.As<short, byte>(ref Unsafe.AsRef(in WeightLo[0])));
        Vector128<short> wHi = Unsafe.ReadUnaligned<Vector128<short>>(
            ref Unsafe.As<short, byte>(ref Unsafe.AsRef(in WeightHi[0])));
        Vector128<short> ones = Vector128.Create((short)1);

        while (remaining > 0)
        {
            int batch = Math.Min(remaining, AdlerNMax);
            int remainingInBatch = batch;

            int i = 0;
            int simdLimit = remainingInBatch - 15;

            while (i < simdLimit)
            {
                // Load 16 bytes
                Vector128<byte> chunk = Unsafe.ReadUnaligned<Vector128<byte>>(
                    ref Unsafe.AsRef(in data[offset + i]));

                // Widen bytes to two 8-element uint16 vectors
                Vector128<ushort> lo = Sse2.UnpackLow(chunk, Vector128<byte>.Zero).AsUInt16();
                Vector128<ushort> hi = Sse2.UnpackHigh(chunk, Vector128<byte>.Zero).AsUInt16();

                // Compute plain sum of all 16 bytes (for updating a)
                Vector128<int> sumLo = Sse2.MultiplyAddAdjacent(lo.AsInt16(), ones);
                Vector128<int> sumHi = Sse2.MultiplyAddAdjacent(hi.AsInt16(), ones);
                int chunkSum = (int)HorizontalSum(Sse2.Add(sumLo, sumHi));

                // Compute weighted sum (for updating b):
                // Multiply each widened byte by its weight and sum
                Vector128<int> weightedLo = Sse2.MultiplyAddAdjacent(lo.AsInt16(), wLo);
                Vector128<int> weightedHi = Sse2.MultiplyAddAdjacent(hi.AsInt16(), wHi);
                int weightedSum = (int)HorizontalSum(Sse2.Add(weightedLo, weightedHi));

                // Update: b += 16*a + weighted_sum, a += plain_sum
                b += (uint)(16 * (int)a + weightedSum);
                a += (uint)chunkSum;

                i += 16;
            }

            a %= AdlerMod;
            b %= AdlerMod;

            // Process remaining bytes with scalar loop
            for (; i < remainingInBatch; i++)
            {
                a += data[offset + i];
                b += a;
            }

            a %= AdlerMod;
            b %= AdlerMod;

            offset += batch;
            remaining -= batch;
        }
    }

    /// <summary>
    /// Horizontal sum of a 128-bit vector of 4 int32 values.
    /// </summary>
    private static uint HorizontalSum(Vector128<int> v)
    {
        Vector128<int> shuffled = Sse2.Shuffle(v, 0x0E); // _MM_SHUFFLE(0,0,3,2)
        Vector128<int> summed = Sse2.Add(v, shuffled);
        Vector128<int> shuffled2 = Sse2.Shuffle(summed, 0x01); // _MM_SHUFFLE(0,0,0,1)
        Vector128<int> result = Sse2.Add(summed, shuffled2);
        return (uint)result.GetElement(0);
    }

    /// <summary>
    /// Bit reader for LSB-first DEFLATE streams.
    /// Maintains a bit buffer filled from a byte span on demand.
    /// </summary>
    private ref struct BitReader
    {
        public ulong _bitBuffer;
        public int _bitsAvailable;
        public ReadOnlySpan<byte> _data;
        public int _dataPos;

        public BitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bitBuffer = 0;
            _bitsAvailable = 0;
            _dataPos = 0;
        }

        /// <summary>
        /// Read <paramref name="count"/> bits from the stream, LSB-first.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBits(int count)
        {
            EnsureBits(count);
            int value = (int)(_bitBuffer & ((1UL << count) - 1));
            _bitBuffer >>= count;
            _bitsAvailable -= count;
            return value;
        }

        /// <summary>
        /// Read <paramref name="count"/> bits from explicitly-provided bit buffer fields.
        /// Used when the caller is managing the bit buffer state directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBitsFromFields(ref ulong bitBuffer, ref int bitsAvailable, ReadOnlySpan<byte> data, ref int dataPos, int count)
        {
            while (bitsAvailable < count && dataPos < data.Length)
            {
                bitBuffer |= (ulong)data[dataPos++] << bitsAvailable;
                bitsAvailable += 8;
            }

            int value = (int)(bitBuffer & ((1UL << count) - 1));
            bitBuffer >>= count;
            bitsAvailable -= count;
            return value;
        }

        /// <summary>
        /// Peek at the next <paramref name="count"/> bits without consuming them.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int PeekBits(int count)
        {
            EnsureBits(count);
            return (int)(_bitBuffer & ((1UL << count) - 1));
        }

        /// <summary>
        /// Consume <paramref name="count"/> bits from the buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DropBits(int count)
        {
            _bitBuffer >>= count;
            _bitsAvailable -= count;
        }

        /// <summary>
        /// Align the bit cursor to the next byte boundary, discarding remaining bits in the current byte.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AlignToByte()
        {
            int discard = _bitsAvailable & 7;
            if (discard > 0)
            {
                _bitBuffer >>= discard;
                _bitsAvailable -= discard;
            }
        }

        /// <summary>
        /// Ensure at least <paramref name="count"/> bits are available in the buffer, refilling from data if needed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureBits(int count)
        {
            while (_bitsAvailable < count && _dataPos < _data.Length)
            {
                _bitBuffer |= (ulong)_data[_dataPos++] << _bitsAvailable;
                _bitsAvailable += 8;
            }
        }
    }
}
