using System.Text;

namespace Vocore.Audio;

public unsafe static class FlacDecoder
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("fLaC");

    public static void ReadHeader(ref byte* p)
    {
        for (int i = 0; i < 4; i++)
        {
            if (p[i] != Magic[i])
            {
                throw new Exception("Invalid FLAC header.");
            }
        }
        p += 4;

        // Skip the metadata block until we reach the audio data
        
    }
}