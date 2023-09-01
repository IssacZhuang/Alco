using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static class StreamExtension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void WriteInt32(this Stream stream, int value)
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
    }
}

