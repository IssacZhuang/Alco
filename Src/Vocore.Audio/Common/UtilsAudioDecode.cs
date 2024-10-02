using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Hebron.Runtime;
using StbVorbisSharp;

namespace Vocore.Audio;

/// <summary>
/// The utils to decode audio, all the audio will be decoded into float32 format by default
/// </summary>
public unsafe static class UtilsAudioDecode
{
    public const float Int16ToFloat32 = 1 / 32768f;

    /// <summary>
    /// Decode the ogg data into pcm data
    /// </summary>
    /// <param name="data">The source data of ogg</param>
    /// <param name="outData">The decoded data pointer</param>
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    public static void DecodeOgg(ReadOnlySpan<byte> data, out float* outData, out int size, out int channel, out int sampleRate)
    {
        short* result = null;// the result of stb vorbis is interleaved
        var length = 0;

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

        size = length * sizeof(float);
        float* pcmData = (float*)Marshal.AllocHGlobal(size);

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

        CRuntime.free(result);
        outData = pcmData;
    }

    public static AudioClip CreateAudioClipFromOgg(ReadOnlySpan<byte> data)
    {
        DecodeOgg(data, out float* pcm, out int size, out int channel, out int sampleRate);
        return AudioClip.UnsafeCreate(pcm, size, channel, sampleRate);
    }


}