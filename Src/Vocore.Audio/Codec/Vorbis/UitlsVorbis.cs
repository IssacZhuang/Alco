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

    public static bool IsIdentificationHeader(byte* ptr)
    {
        VorbisHeaderType type = (VorbisHeaderType)ptr[0];
        return type == VorbisHeaderType.Identification;
    }

    public static bool IsCommentHeader(byte* ptr)
    {
        VorbisHeaderType type = (VorbisHeaderType)ptr[0];
        return type == VorbisHeaderType.Comment;
    }

    public static bool IsSetupHeader(byte* ptr)
    {
        VorbisHeaderType type = (VorbisHeaderType)ptr[0];
        return type == VorbisHeaderType.Setup;
    }

    public static bool IsCodebook(byte* ptr)
    {
        return IsBytesEqual(ptr, MagicCodebook);
    }

    public static bool IsVorbisHeader(byte* ptr){
        return IsBytesEqual(ptr, MagicVorbis);
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