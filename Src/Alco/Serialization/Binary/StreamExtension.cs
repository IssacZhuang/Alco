using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Alco
{
    internal static class StreamExtension
    {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void WriteInt32(this Stream stream, int value)
        {
            int* ptr = &value;
            byte* bptr = (byte*)ptr;
            stream.Write(new ReadOnlySpan<byte>(bptr, 4));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static int ReadInt32(this Stream stream)
        {
            // int value = 0;
            // int* ptr = &value;
            // byte* bptr = (byte*)ptr;
            // *bptr = (byte)stream.ReadByte();
            // bptr++;
            // *bptr = (byte)stream.ReadByte();
            // bptr++;
            // *bptr = (byte)stream.ReadByte();
            // bptr++;
            // *bptr = (byte)stream.ReadByte();
            // return value;
            byte* data = stackalloc byte[4];
            int readLength = stream.Read(new Span<byte>(data, 4));
            if (readLength != 4)
            {
                throw new EndOfStreamException($"Stream ended before reading the expected number of bytes. Expected: 4, Read: {readLength}");
            }
            return *(int*)data;
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

