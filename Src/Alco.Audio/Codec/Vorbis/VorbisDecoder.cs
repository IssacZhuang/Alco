using static StbVorbisSharp.StbVorbis;

namespace Alco.Audio;

internal unsafe static class VorbisDecoder
{
    public static float[] DecodeToFloat32(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            //stb vorbis
            int error = 0;
            stb_vorbis vorbis = stb_vorbis_open_memory(ptr, data.Length, &error);
            if (vorbis == null)
            {
                throw new Exception("Error in stb vorbis open");
            }

            stb_vorbis_info info = stb_vorbis_get_info(vorbis);
            channel = info.channels;
            sampleRate = (int)info.sample_rate;

            List<float> pcmData = new List<float>();

            float* pcm = stackalloc float[4096];

            while (true)
            {
                int samples = stb_vorbis_get_samples_float_interleaved(vorbis, channel, pcm, 4096);
                if (samples == 0)
                {
                    break;
                }
                else if (samples < 0)
                {
                    throw new Exception("Error in decoding ogg");
                }
                else
                {
                    for (int i = 0; i < samples * channel; i++)
                    {
                        pcmData.Add(pcm[i]);
                    }
                }
            }

            stb_vorbis_close(vorbis);

            float[] buffer = pcmData.ToArray();

            return buffer;
        }
    }

    public static float* DecodeToFloat32Unsafe(ReadOnlySpan<byte> data, out int channel, out int sampleCount, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            //stb vorbis
            int error = 0;
            stb_vorbis vorbis = stb_vorbis_open_memory(ptr, data.Length, &error);
            if (vorbis == null)
            {
                throw new Exception("Error in stb vorbis open");
            }

            stb_vorbis_info info = stb_vorbis_get_info(vorbis);
            channel = info.channels;
            sampleRate = (int)info.sample_rate;

            NativeArrayList<float> pcmData = new NativeArrayList<float>();

            float* pcm = stackalloc float[4096];

            while (true)
            {
                int samples = stb_vorbis_get_samples_float_interleaved(vorbis, channel, pcm, 4096);
                if (samples == 0)
                {
                    break;
                }
                else if (samples < 0)
                {
                    throw new Exception("Error in decoding ogg");
                }
                else
                {
                    for (int i = 0; i < samples * channel; i++)
                    {
                        pcmData.Add(pcm[i]);
                    }
                }
            }

            stb_vorbis_close(vorbis);
            sampleCount = pcmData.Length;
            return pcmData.UnsafePointer;//need to be freed outside
        }
    }
}