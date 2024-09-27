using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Hebron.Runtime;
using StbVorbisSharp;

namespace Vocore.Audio;

public unsafe static class UtilsAudioDecode
{
    public const float Int16ToFloat32 = 1 / 32768f;
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
            // for (int i = 0; i < length; i++)
            // {
            //     pcmData[i] = result[i] * Int16ToFloat32;
            // }
            int simdLength = length / Vector128<short>.Count;
            for (int i = 0; i < simdLength; i++)
            {
                Vector128<short> inputVec = Vector128<short>.Zero;
                for (int j = 0; j < Vector128<short>.Count; j++)
                {
                    inputVec = inputVec.WithElement(j, result[i * Vector128<short>.Count + j]);
                }

                Vector128<float> outputVec = Vector128<float>.Zero;
                for (int j = 0; j < Vector128<float>.Count; j++)
                {
                    outputVec = outputVec.WithElement(j, inputVec.GetElement(j) * Int16ToFloat32);
                }

                for (int j = 0; j < Vector128<float>.Count; j++)
                {
                    pcmData[i * Vector128<float>.Count + j] = outputVec.GetElement(j);
                }
            }

            // Handle remaining elements
            for (int i = simdLength * Vector128<short>.Count; i < length; i++)
            {
                pcmData[i] = result[i] * Int16ToFloat32;
            }
        }
        else
        {
            //interleave to deinterleave
            int samplesPerChannel = length / channel;
            for (int channelIndex = 0; channelIndex < channel; channelIndex++)
            {
                int channelStart = channelIndex * samplesPerChannel;
                for (int i = 0; i < samplesPerChannel; i++)
                {
                    pcmData[channelStart + i] = result[i * channel] * Int16ToFloat32;
                }
            }
        }

        CRuntime.free(result);
        return AudioClip.UnsafeCreate(pcmData, (uint)length, sampleRate);
    }

    
}