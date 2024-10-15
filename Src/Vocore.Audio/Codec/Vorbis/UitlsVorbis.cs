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

    public static bool IsVorbisHeader(byte* ptr){
        return IsBytesEqual(ptr, MagicVorbis);
    }

    public static byte ReadBit(ref byte* ptr, ref byte bit)
    {
        byte result = (byte)((*ptr >> bit) & 1);
        bit++;
        if (bit == 8){
            bit = 0;
            ptr++;
        }
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