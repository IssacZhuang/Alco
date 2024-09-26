using System.Runtime.InteropServices;
using Hebron.Runtime;
using StbVorbisSharp;

namespace Vocore.Audio;

public unsafe static class UtilsAudioDecode
{
    public static AudioClip DecodeOgg(ReadOnlySpan<byte> data, bool interleaved = true)
    {
        short* result = null;// the result of stb vorbis is interleaved
        var length = 0;

        int sampleRate, channel;

        fixed (byte* b = data)
        {
            int c, s;
            length = StbVorbis.stb_vorbis_decode_memory(b, data.Length, &c, &s, ref result);
            if (length == -1)
            {
                throw new Exception("Unable to decode ogg");
            }

            channel = c;
            sampleRate = s;
        }

        float* pcmData = (float*)Marshal.AllocHGlobal(length * sizeof(float));
        if (interleaved)
        {
            //use original data
            for (int i = 0; i < length; i++)
            {
                pcmData[i] = result[i];
            }
        }
        else
        {
            //interleave to deinterleave
            for (int i = 0; i < length; i++)
            {
                pcmData[i] = result[i * channel];
            }
        }

        CRuntime.free(result);
        return AudioClip.UnsafeCreate(pcmData, (uint)length, sampleRate);
    }
}