using System.Text;
using Alco.Unsafe;

namespace Alco.Audio;

internal unsafe static class FlacDecoder
{


    public static float* DecodeToFloat32Unsafe(ReadOnlySpan<byte> data, out int channel, out int sampleCount, out int sampleRate)
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
            return buffer;//need to be freed outside
        }

    }

    public static float[] DecodeToFloat32(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        //for less GC
        fixed (byte* ptr = data)
        {
            using FlacFile file = new FlacFile(ptr, (uint)data.Length);
            channel = (int)file.Channels;
            sampleRate = (int)file.SampleRate;

            float[] buffer = new float[(int)file.TotalSamples * channel];
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
    }





}