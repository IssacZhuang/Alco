using System.Text;
using Vocore.Unsafe;

namespace Vocore.Audio;

internal unsafe static class FlacDecoder
{
    

    public static float* DecodeWaveAudioToFloat32Unsafe(ReadOnlySpan<byte> data, out int channel, out int sampleCount, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            using FlacFile file = new FlacFile(ptr, (uint)data.Length);
            channel = (int)file.Channels;
            sampleCount = (int)file.TotalSamples * channel;
            sampleRate = (int)file.SampleRate;

            float* buffer = UtilsMemory.Alloc<float>(sampleCount);
            Span<float> tmp = stackalloc float[4096];
            int offset = 0;
            int read = 0;
            while ((read = file.ReadSamples(tmp)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    buffer[offset + i] = tmp[i];
                }
                offset += read;
            }
            return buffer;
        }
        //throw new NotImplementedException();
    }


    


}