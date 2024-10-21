using System.Runtime.CompilerServices;
using System.Text;

namespace Vocore.Audio;

internal unsafe class UtilsVorbis
{
    private static readonly byte[] MagicPage = Encoding.ASCII.GetBytes("OggS");
    private static readonly byte[] MagicVorbis = Encoding.ASCII.GetBytes("vorbis");
    private static readonly byte[] MagicCodebook = [0x42, 0x43, 0x56];

    public static bool IsOggHeader(byte* ptr)
    {
        return IsBytesEqual(ptr, MagicPage);
    }

    public static bool IsCodebook(byte* ptr)
    {
        return IsBytesEqual(ptr, MagicCodebook);
    }

    public static bool IsCodebook(uint check)
    {
        return check == 0x564342u;
    }

    public static bool IsVorbisHeader(byte* ptr){
        return IsBytesEqual(ptr, MagicVorbis);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Read<T>(ref byte* ptr) where T : unmanaged
    {
        T result = *(T*)ptr;
        ptr += sizeof(T);
        return result;
    }

    public static uint IntLog(uint x)
    {
        uint result = 0;
        while (x > 0)
        {
            result++;
            x >>= 1;
        }
        return result;
    }

    public static byte IntLog(byte x)
    {
        byte result = 0;
        while (x > 0)
        {
            result++;
            x >>= 1;
        }
        return result;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBytesEqual(byte* ptr, byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (ptr[i] != bytes[i])
            {
                return false;
            }
        }

        return true;
    }
}