using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Alco
{
    internal static class StreamExtension
    {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void WriteInt32(this MemoryStream stream, int value)
        {
            int* ptr = &value;
            byte* bptr = (byte*)ptr;
            stream.WriteByte(*bptr);
            bptr++;
            stream.WriteByte(*bptr);
            bptr++;
            stream.WriteByte(*bptr);
            bptr++;
            stream.WriteByte(*bptr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static int ReadInt32(this MemoryStream stream)
        {
            int value = 0;
            int* ptr = &value;
            byte* bptr = (byte*)ptr;
            *bptr = (byte)stream.ReadByte();
            bptr++;
            *bptr = (byte)stream.ReadByte();
            bptr++;
            *bptr = (byte)stream.ReadByte();
            bptr++;
            *bptr = (byte)stream.ReadByte();
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] ReadBytes(this MemoryStream stream, int length)
        {
            byte[] bytes = new byte[length];
            stream.Read(bytes, 0, length);
            return bytes;
        }
    }
}

