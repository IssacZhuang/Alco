using System.Runtime.InteropServices;
using System.Text;

namespace Vocore.Audio;

public static unsafe class WaveDecoder
{
    private const float Inv128 = 1f / 128f;
    private const float Inv32768 = 1f / 32768f;
    private const float Inv8388608 = 1f / 8388608f;
    private const float Inv2147483648 = 1f / 2147483648f;

    /// <summary>
    /// Decode wave audio data to float32 array.
    /// </summary>
    /// <param name="data">The wave audio file data</param>
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    /// <returns></returns>
    public static float[] DecodeWaveAudioToFloat32(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {

        fixed (byte* ptr = data)
        {
            byte* p = ptr;

            ReadHeader(ref p, out WaveChunckRiff chunckRiff, out WaveChunckFmt chunckFmt, out WaveChunckData chunckData);

            int smapleCount = (int)chunckData.DataSize / (chunckFmt.BitsPerSample / 8);
            float[] result = new float[smapleCount];

            DecodeData(new ReadOnlySpan<byte>(p, (int)chunckData.DataSize), result, chunckFmt.WaveFormat, chunckFmt.BitsPerSample);

            channel = chunckFmt.Channels;
            sampleRate = (int)chunckFmt.SampleRate;
            return result;
        }
    }

    /// <summary>
    /// Decode wave audio data to float32 memory. The memory is not managed by the GC, so you need to free it manually.
    /// </summary>
    /// <param name="data">The wave audio file data</param>
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    /// <returns></returns>
    public static float* DecodeWaveAudioToFloat32Unsafe(ReadOnlySpan<byte> data, out int channel, out int sampleCount, out int sampleRate){
        fixed (byte* ptr = data)
        {
            byte* p = ptr;

            ReadHeader(ref p, out WaveChunckRiff chunckRiff, out WaveChunckFmt chunckFmt, out WaveChunckData chunckData);

            sampleCount = (int)chunckData.DataSize / (chunckFmt.BitsPerSample / 8);
            float* result = (float*)Marshal.AllocHGlobal(sampleCount * sizeof(float));
            Span<float> resultSpan = new(result, sampleCount);

            DecodeData(new ReadOnlySpan<byte>(p, (int)chunckData.DataSize), resultSpan, chunckFmt.WaveFormat, chunckFmt.BitsPerSample);

            channel = chunckFmt.Channels;
            sampleRate = (int)chunckFmt.SampleRate;
            return result;
        }
    }

    private static void ReadHeader(ref byte* p, out WaveChunckRiff chunckRiff, out WaveChunckFmt chunckFmt, out WaveChunckData chunckData)
    {
        chunckRiff = *(WaveChunckRiff*)p;
        p += sizeof(WaveChunckRiff);

        chunckFmt = *(WaveChunckFmt*)p;
        p += 8;//skip "fmt " and fmt.FmtSize
        p += chunckFmt.FmtSize;

        WaveChunckUnknown chunckUnknown = *(WaveChunckUnknown*)p;

        while (!WaveChunckData.IsDataChunk(p))
        {
            p += 8;//skip "data" and chunck.Size
            p += chunckUnknown.Size;
            chunckUnknown = *(WaveChunckUnknown*)p;
        }

        chunckData = *(WaveChunckData*)p;
        p += sizeof(WaveChunckData);
    }

    private static void DecodeData(ReadOnlySpan<byte> input, Span<float> result, WaveFormat format, ushort bitDepth)
    {
        switch (format)
        {
            case WaveFormat.PCM:
                DecodePCM(input, result, bitDepth);
                break;
            case WaveFormat.IEEEFloat:
                DecodeIEEEFloat(input, result, bitDepth);
                break;
            case WaveFormat.ALaw:
                DecodeALaw(input, result);
                break;
            case WaveFormat.MuLaw:
                DecodeMuLaw(input, result);
                break;
        }
    }

    private static void DecodeIEEEFloat(ReadOnlySpan<byte> input, Span<float> result, ushort bitDepth)
    {
        switch (bitDepth)
        {
            case 32:
                if (input.Length < result.Length * 4)
                {
                    throw new ArgumentException($"Invalid data length on decoding 32-bit IEEE float data.");
                }

                fixed (byte* p = input)
                {
                    float* src = (float*)p;
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = src[i];
                    }
                }
                break;
            case 64:
                if (input.Length < result.Length * 8)
                {
                    throw new ArgumentException($"Invalid data length on decoding 64-bit IEEE float data.");
                }

                fixed (byte* p = input)
                {
                    double* src = (double*)p;
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = (float)src[i];
                    }
                }
                break;
            default:
                throw new NotSupportedException($"Bit depth {bitDepth} is not supported for IEEE float format.");
        }
    }

    private static void DecodePCM(ReadOnlySpan<byte> input, Span<float> result, ushort bitDepth)
    {
        switch (bitDepth)
        {
            case 8:
                //validate the length to prevent pointer overflow
                if (input.Length < result.Length)
                {
                    throw new ArgumentException($"Invalid data length on decoding 8-bit PCM data.");
                }

                fixed (byte* p = input)
                {
                    byte* src = p;
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = src[i] * Inv128 - 1f;
                    }
                }
                break;
            case 16:
                if (input.Length < result.Length * 2)
                {
                    throw new ArgumentException($"Invalid data length on decoding 16-bit PCM data.");
                }

                fixed (byte* p = input)
                {
                    short* src = (short*)p;
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = src[i] * Inv32768;
                    }
                }
                break;
            case 24:
                if (input.Length < result.Length * 3)
                {
                    throw new ArgumentException($"Invalid data length on decoding 24-bit PCM data.");
                }

                fixed (byte* p = input)
                {
                    Int24* src = (Int24*)p;
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = UtilsBitConvert.Int24ToInt32(src[i]) * Inv8388608;
                    }
                }
                break;
            case 32:
                if (input.Length < result.Length * 4)
                {
                    throw new ArgumentException($"Invalid data length on decoding 32-bit PCM data.");
                }

                fixed (byte* p = input)
                {
                    int* src = (int*)p;
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = src[i] * Inv2147483648;
                    }
                }
                break;
            default:
                throw new NotSupportedException($"Bit depth {bitDepth} is not supported for PCM format.");
        }
    }

    private static void DecodeALaw(ReadOnlySpan<byte> input, Span<float> result)
    {
        if (input.Length < result.Length)
        {
            throw new ArgumentException($"Invalid data length on decoding A-Law data.");
        }

        for (int i = 0; i < result.Length; i++)
        {
            short int16 = ALawDecoder.ALawToLinearSample(input[i]);
            result[i] = int16 * Inv32768;
        }
    }

    private static void DecodeMuLaw(ReadOnlySpan<byte> input, Span<float> result)
    {
        if (input.Length < result.Length)
        {
            throw new ArgumentException($"Invalid data length on decoding Mu-Law data.");
        }

        for (int i = 0; i < result.Length; i++)
        {
            short int16 = MuLawDecoder.MuLawToLinearSample(input[i]);
            result[i] = int16 * Inv32768;
        }
    }

}