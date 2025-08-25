using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using SharpMSDF.Core;

namespace SharpMSDF.IO
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Text;
    using System.Security.Cryptography;

    public static class MinimalPngEncoder
    {
        public static void EncodeToStream(Span<byte> rgba, int width, int height, Stream output)
        {
            using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

                // PNG signature
                writer.Write(new byte[] {
                137, 80, 78, 71, 13, 10, 26, 10
            });

            // Write IHDR
            WriteChunk(writer, "IHDR", stream =>
            {
                stream.Write(Be(width));
                stream.Write(Be(height));
                stream.WriteByte(8);  // Bit depth
                stream.WriteByte(6);  // Color type: RGBA
                stream.WriteByte(0);  // Compression
                stream.WriteByte(0);  // Filter
                stream.WriteByte(0);  // Interlace
            });

            // Prepare raw image data (with filter byte per row)
            using var rawImage = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                rawImage.WriteByte(0); // Filter type: None
                int offset = y * width * 4;
                rawImage.Write(rgba.Slice(offset, width * 4));
            }

            // Compress with zlib wrapper
            using var compressed = new MemoryStream();
            compressed.WriteByte(0x78); // zlib header
            compressed.WriteByte(0x01); // default compression

            using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                rawImage.Position = 0;
                rawImage.CopyTo(deflate);
            }

            // Write Adler-32 checksum
            compressed.Write(Be((int)Adler32(rawImage.ToArray())));

            // Write IDAT chunk
            WriteChunk(writer, "IDAT", stream =>
            {
                compressed.Position = 0;
                compressed.CopyTo(stream);
            });

            // Write IEND chunk
            WriteChunk(writer, "IEND", _ => { });
                
        }

        private static void WriteChunk(BinaryWriter writer, string type, Action<MemoryStream> writeData)
        {
            using var chunkStream = new MemoryStream();
            writeData(chunkStream);
            byte[] data = chunkStream.ToArray();

            writer.Write(Be(data.Length));
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            writer.Write(typeBytes);
            writer.Write(data);
            uint crc = Crc32(typeBytes, data);
            writer.Write(Be((int)crc));
        }

        private static byte[] Be(int value)
        {
            byte[] b = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            return b;
        }

        private static uint Crc32(byte[] typeBytes, byte[] data)
        {
            using var crc32 = new Crc32Algorithm();
            crc32.Initialize();
            crc32.TransformBlock(typeBytes, 0, typeBytes.Length, null, 0);
            crc32.TransformFinalBlock(data, 0, data.Length);
            return BitConverter.ToUInt32(crc32.Hash, 0);
        }

        private static uint Adler32(byte[] data)
        {
            const uint MOD_ADLER = 65521;
            uint a = 1, b = 0;
            foreach (byte d in data)
            {
                a = (a + d) % MOD_ADLER;
                b = (b + a) % MOD_ADLER;
            }
            return (b << 16) | a;
        }
    }

    public class Crc32Algorithm : HashAlgorithm
    {
        public static readonly uint[] Table = new uint[256];

        static Crc32Algorithm()
        {
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                Table[i] = c;
            }
        }

        private uint _crc;

        public override void Initialize() => _crc = 0xFFFFFFFF;

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            for (int i = ibStart; i < ibStart + cbSize; i++)
                _crc = Table[(_crc ^ array[i]) & 0xFF] ^ (_crc >> 8);
        }

        protected override byte[] HashFinal() => BitConverter.GetBytes(~_crc);

        public override int HashSize => 32;
    }

}
