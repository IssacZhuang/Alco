using System.IO.Compression;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

[TestFixture]
public class TestPngZlib
{
    #region Adler32 Tests

    [Test]
    public void TestAdler32_Empty()
    {
        // Adler-32 of empty data is defined as 1 (a=1, b=0, result = (0<<16)|1 = 1)
        uint result = PngZlib.Adler32(ReadOnlySpan<byte>.Empty);
        Assert.That(result, Is.EqualTo(1u));
    }

    [Test]
    public void TestAdler32_KnownVector()
    {
        // Known test vector: Adler-32 of "Wikipedia" = 0x11E60398
        byte[] data = System.Text.Encoding.ASCII.GetBytes("Wikipedia");
        uint result = PngZlib.Adler32(data);
        Assert.That(result, Is.EqualTo(0x11E60398u));
    }

    [Test]
    public void TestAdler32_AllZeros()
    {
        // Adler-32 of 100 zero bytes:
        // a = 1 + 0*100 = 1
        // b = sum of a_i where a_0=1, a_i=1 for all i (since adding 0 doesn't change a)
        // b = 1 * 100 = 100
        // result = (100 << 16) | 1 = 0x00640001
        byte[] data = new byte[100];
        uint result = PngZlib.Adler32(data);
        Assert.That(result, Is.EqualTo(0x00640001u));
    }

    [Test]
    public void TestAdler32_LargeInput()
    {
        // 10000 bytes to exercise the SIMD path and batch reduction
        byte[] data = new byte[10000];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i & 0xFF);

        uint result = PngZlib.Adler32(data);

        // Compute expected value using reference scalar implementation
        uint expectedA = 1;
        uint expectedB = 0;
        for (int i = 0; i < data.Length; i++)
        {
            expectedA = (expectedA + data[i]) % 65521;
            expectedB = (expectedB + expectedA) % 65521;
        }
        uint expected = (expectedB << 16) | expectedA;

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Inflate Tests

    [Test]
    public void TestInflate_StoredBlock()
    {
        // Build a stored (type 0) DEFLATE block containing "Hello"
        // Format: BFINAL=1, BTYPE=00, align to byte, LEN (2 LE), NLEN (2 LE), raw data
        byte[] rawData = System.Text.Encoding.ASCII.GetBytes("Hello");

        using var ms = new MemoryStream();
        // Block header: BFINAL=1, BTYPE=00 => bits: 1 00 => byte: 0x01
        ms.WriteByte(0x01);
        // Align to byte boundary (already aligned after 3 bits, need 5 padding bits)
        // Actually, DEFLATE stores the header as: bit0=BFINAL, bits1-2=BTYPE
        // After reading 3 bits, stored block requires byte alignment
        // The remaining 5 bits of byte 0 are padding for alignment
        // Then LEN (2 bytes LE) and NLEN (2 bytes LE)
        int len = rawData.Length;
        int nlen = len ^ 0xFFFF;
        ms.WriteByte((byte)(len & 0xFF));
        ms.WriteByte((byte)((len >> 8) & 0xFF));
        ms.WriteByte((byte)(nlen & 0xFF));
        ms.WriteByte((byte)((nlen >> 8) & 0xFF));
        ms.Write(rawData, 0, rawData.Length);

        byte[] compressed = ms.ToArray();
        byte[] output = new byte[256];
        int bytesWritten = PngZlib.Inflate(compressed, output);

        Assert.That(bytesWritten, Is.EqualTo(5));
        Assert.That(output[..5].ToArray(), Is.EqualTo(rawData));
    }

    [Test]
    public void TestInflate_FixedHuffman()
    {
        // Use DeflateStream to compress known data, then verify our Inflate can decompress it.
        // We test only the raw DEFLATE portion (without zlib wrapper).
        byte[] input = System.Text.Encoding.ASCII.GetBytes("Hello, DEFLATE world!");
        byte[] output = new byte[input.Length + 256];

        // Compress with System.IO.Compression to get a valid DEFLATE stream
        byte[] compressed = CompressRawDeflate(input);

        int bytesWritten = PngZlib.Inflate(compressed, output);

        Assert.That(bytesWritten, Is.EqualTo(input.Length));
        Assert.That(output[..bytesWritten].ToArray(), Is.EqualTo(input));
    }

    [Test]
    public void TestInflate_DynamicHuffman()
    {
        // Use a larger input that forces dynamic Huffman encoding
        byte[] input = new byte[1024];
        for (int i = 0; i < input.Length; i++)
            input[i] = (byte)('A' + (i % 26));

        byte[] compressed = CompressRawDeflate(input);
        byte[] output = new byte[input.Length + 256];

        int bytesWritten = PngZlib.Inflate(compressed, output);

        Assert.That(bytesWritten, Is.EqualTo(input.Length));
        Assert.That(output[..bytesWritten].ToArray(), Is.EqualTo(input));
    }

    [Test]
    public void TestInflate_OverlappingBackreference()
    {
        // Test that overlapping back-references (distance < length) produce correct output.
        // This specifically exercises the byte-by-byte copy path for distance 1-7.
        // We test a variety of inputs that force short-distance back-references.
        byte[][] testInputs =
        [
            // Short repeated pattern (forces back-reference with distance 4)
            System.Text.Encoding.ASCII.GetBytes("ABCDABCDABCDABCDABCD"),
            // Single byte repeated (distance 1, RLE)
            System.Text.Encoding.ASCII.GetBytes("AAAAAAAAAAAAAAAAAAAA"),
            // 2-byte repeated pattern (distance 2)
            System.Text.Encoding.ASCII.GetBytes("ABABABABABABABABABAB"),
            // 3-byte repeated pattern (distance 3)
            System.Text.Encoding.ASCII.GetBytes("ABCABCABCABCABCABCAB"),
            // 5-byte repeated pattern (distance 5)
            System.Text.Encoding.ASCII.GetBytes("ABCDEABCDEABCDEABCDE"),
            // 6-byte repeated pattern (distance 6)
            System.Text.Encoding.ASCII.GetBytes("ABCDEFABCDEFABCDEFAB"),
            // 7-byte repeated pattern (distance 7)
            System.Text.Encoding.ASCII.GetBytes("ABCDEFGABCDEFGABCDEF"),
            // Longer data with mixed patterns
            System.Text.Encoding.ASCII.GetBytes("HelloHelloHelloHelloXXXYYYXXXYYYZZZZZZ"),
        ];

        foreach (byte[] input in testInputs)
        {
            byte[] compressed = CompressRawDeflate(input);
            byte[] output = new byte[input.Length + 256];
            int bytesWritten = PngZlib.Inflate(compressed, output);

            Assert.That(bytesWritten, Is.EqualTo(input.Length),
                $"Length mismatch for input '{System.Text.Encoding.ASCII.GetString(input)}'");
            Assert.That(output[..bytesWritten].ToArray(), Is.EqualTo(input),
                $"Data mismatch for input '{System.Text.Encoding.ASCII.GetString(input)}'");
        }
    }

    #endregion

    #region DecompressZlib Tests

    [Test]
    public void TestDecompressZlib_RoundTrip()
    {
        // Compress known data with System.IO.Compression.DeflateStream (zlib format)
        byte[] input = System.Text.Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog.");
        byte[] compressed = CompressZlib(input);
        byte[] output = new byte[input.Length + 256];

        int bytesWritten = PngZlib.DecompressZlib(compressed, output);

        Assert.That(bytesWritten, Is.EqualTo(input.Length));
        Assert.That(output[..bytesWritten].ToArray(), Is.EqualTo(input));
    }

    [Test]
    public void TestDecompressZlib_InvalidHeader()
    {
        // CM field != 8 (deflate)
        byte[] invalidData = new byte[6];
        invalidData[0] = 0x09; // CM=9 (invalid)
        invalidData[1] = 0x01; // FLG (doesn't matter, CM check fails first)

        byte[] output = new byte[256];

        var ex = Assert.Throws<ImageDecodeException>(() =>
            PngZlib.DecompressZlib(invalidData, output));
        Assert.That(ex!.Message, Does.Contain("CM field"));
    }

    [Test]
    public void TestDecompressZlib_Empty()
    {
        // Construct a valid zlib stream for empty data manually.
        // DeflateStream produces no output for empty input, so we build a minimal DEFLATE stream:
        // A single final block with fixed Huffman codes containing only the end-of-block symbol (256).
        // In the fixed Huffman table, symbol 256 has a 7-bit code.
        // BFINAL=1, BTYPE=01 (fixed Huffman), then symbol 256 encoded.
        // The 7-bit reversed code for 256 in fixed Huffman: code 0000000 (7 bits), reversed = 0000000.
        // So the bit stream is: 1 (BFINAL) 10 (BTYPE=01, reversed bits) 0000000 (symbol 256, 7 bits)
        // = 1 10 0000000 = bits: 1 0 1 0000000 (LSB first in bytes)
        // Byte 0: bits 0-7: 1 (BFINAL) 01 (BTYPE) 0000000 (start of sym 256)
        //         = 0b00000011 = 0x03
        // Wait, DEFLATE is LSB-first. Let me be more careful.
        // Bit stream (in order of reading):
        //   Bit 0: BFINAL = 1
        //   Bits 1-2: BTYPE = 01 (fixed Huffman)
        //   Then decode symbol 256 from fixed Huffman.
        //   In fixed Huffman, symbol 256 has code length 7. Its Huffman code (MSB) is 0000000.
        //   Reversed for DEFLATE (LSB-first): 0000000.
        //   So bits 3-9: 0000000
        // Full bit stream: 1 10 0000000 = 9 bits
        // Pack into bytes (LSB first):
        //   Byte 0 (bits 0-7): bit0=1, bit1=1, bit2=0, bit3=0, bit4=0, bit5=0, bit6=0, bit7=0 = 0x03
        //   Byte 1 (bit 8): bit0=0, rest padding = 0x00
        byte[] deflateData = [0x03, 0x00];

        // Build full zlib stream: header + deflate + Adler-32
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);
        ms.Write(deflateData, 0, deflateData.Length);
        // Adler-32 of empty data = 1
        uint adler = 1;
        ms.WriteByte((byte)((adler >> 24) & 0xFF));
        ms.WriteByte((byte)((adler >> 16) & 0xFF));
        ms.WriteByte((byte)((adler >> 8) & 0xFF));
        ms.WriteByte((byte)(adler & 0xFF));
        byte[] compressed = ms.ToArray();

        byte[] output = new byte[256];
        int bytesWritten = PngZlib.DecompressZlib(compressed, output);

        Assert.That(bytesWritten, Is.EqualTo(0));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Compress data using the zlib format (2-byte header + DEFLATE + 4-byte Adler-32 checksum).
    /// Uses System.IO.Compression.DeflateStream internally.
    /// </summary>
    private static byte[] CompressZlib(byte[] input)
    {
        using var ms = new MemoryStream();

        // Write zlib header: CMF=0x78 (CM=8, CINFO=7 => 32K window), FLG=0x9C (FCHECK so that 0x78FF % 31 == 0... actually compute)
        // (0x78 * 256 + FLG) % 31 == 0 => 30720 + FLG must be % 31 == 0
        // 30720 % 31 = 30720 / 31 = 990 * 31 = 30690, 30720 - 30690 = 30 => FLG must be 31-30 = 1 => but we want level hint too
        // Standard: 0x78 0x9C (default compression), 0x78 0x01 (no compression)
        // Verify: (0x78 * 256 + 0x9C) = 30876, 30876 / 31 = 996, 996*31 = 30876 ✓
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);

        // Compress with DeflateStream
        using (var ds = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            ds.Write(input, 0, input.Length);
        }

        // Write Adler-32 checksum (big-endian)
        uint adler = ComputeAdler32(input);
        ms.WriteByte((byte)((adler >> 24) & 0xFF));
        ms.WriteByte((byte)((adler >> 16) & 0xFF));
        ms.WriteByte((byte)((adler >> 8) & 0xFF));
        ms.WriteByte((byte)(adler & 0xFF));

        return ms.ToArray();
    }

    /// <summary>
    /// Compress data as raw DEFLATE (no zlib header/checksum).
    /// </summary>
    private static byte[] CompressRawDeflate(byte[] input)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            ds.Write(input, 0, input.Length);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Reference Adler-32 implementation for test data generation.
    /// </summary>
    private static uint ComputeAdler32(byte[] data)
    {
        uint a = 1;
        uint b = 0;
        for (int i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    #endregion
}
