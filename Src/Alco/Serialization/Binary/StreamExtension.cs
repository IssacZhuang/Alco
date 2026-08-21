using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;

namespace Alco
{
    internal static class StreamExtension
    {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void WriteInt32(this Stream stream, int value)
        {
            byte* data = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(data, 4), value);
            stream.Write(new ReadOnlySpan<byte>(data, 4));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static int ReadInt32(this Stream stream)
        {
            byte* data = stackalloc byte[4];
            int readLength = stream.Read(new Span<byte>(data, 4));
            if (readLength != 4)
            {
                throw new EndOfStreamException($"Stream ended before reading the expected number of bytes. Expected: 4, Read: {readLength}");
            }
            return BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(data, 4));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] ReadBytes(this Stream stream, int length)
        {
            byte[] bytes = new byte[length];
            int readLength = stream.Read(bytes, 0, length);
            if (readLength != length)
            {
                throw new EndOfStreamException($"Stream ended before reading the expected number of bytes. Expected: {length}, Read: {readLength}");
            }
            return bytes;
        }
    }
}
