using System.Text;

namespace Vocore.Audio;

public static unsafe class WaveDecoder
{
    private const float Inv128 = 1f / 128f;
    private const float Inv32768 = 1f / 32768f;
    private const float Inv8388608 = 1f / 8388608f;
    private const float Inv2147483648 = 1f / 2147483648f;

    public static float[] DecodeWaveAudioToFloat32(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {

        fixed (byte* ptr = data)
        {
            byte* p = ptr;

            WaveChunckRiff riff = *(WaveChunckRiff*)p;
            p += sizeof(WaveChunckRiff);
            Console.WriteLine((nint)p - (nint)ptr);

            WaveChunckFmt fmt = *(WaveChunckFmt*)p;
            p += 8;//skip "fmt " and fmt.FmtSize
            p += fmt.FmtSize;
            Console.WriteLine((nint)p - (nint)ptr);

            WaveChunckUnknown chunck = *(WaveChunckUnknown*)p;
            

            while (!WaveChunckData.IsDataChunk(p)){
                p += 8;//skip "data" and chunck.Size
                p += chunck.Size;
                chunck = *(WaveChunckUnknown*)p;
            }

            WaveChunckData header = *(WaveChunckData*)p;

            float[] result = new float[header.DataSize / sizeof(float)];

            switch (fmt.WaveFormat)
            {
                case WaveFormat.PCM:
                    DecodePCM(new ReadOnlySpan<byte>(p, (int)header.DataSize), new Span<float>(result), fmt.Channels, fmt.BitsPerSample);
                    break;
                case WaveFormat.IEEEFloat:
                    DecodeIEEEFloat(new ReadOnlySpan<byte>(p, (int)header.DataSize), new Span<float>(result), fmt.BitsPerSample);
                    break;
                case WaveFormat.ALaw:
                    DecodeALaw(new ReadOnlySpan<byte>(p, (int)header.DataSize), new Span<float>(result));
                    break;
                case WaveFormat.MuLaw:
                    DecodeMuLaw(new ReadOnlySpan<byte>(p, (int)header.DataSize), new Span<float>(result));
                    break;
                case WaveFormat.Extensible:
                    throw new NotSupportedException("Extensible format is not supported.");
                default:
                    throw new NotSupportedException();
            }

            channel = fmt.Channels;
            sampleRate = (int)fmt.SampleRate;
            return result;
        }
    }

    public static void DecodeIEEEFloat(ReadOnlySpan<byte> input, Span<float> result, ushort bitDepth)
    {
        if (input.Length != result.Length * sizeof(float))
        {
            throw new ArgumentException("The input and result length does not match.");
        }

        switch (bitDepth)
        {
            case 32:
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

    public static void DecodePCM(ReadOnlySpan<byte> input, Span<float> result, ushort channels, ushort bitDepth)
    {
        Console.WriteLine($"PCM: {input.Length} {result.Length} {bitDepth}");
        if (input.Length != result.Length * bitDepth * channels / 8)
        {
            throw new ArgumentException("The input and result length does not match.");
        }

        switch (bitDepth)
        {
            case 8:
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

    public static void DecodeALaw(ReadOnlySpan<byte> input, Span<float> result)
    {
        if (input.Length != result.Length)
        {
            throw new ArgumentException("The input and result length does not match.");
        }

        for (int i = 0; i < result.Length; i++)
        {
            short int16 = ALawDecoder.ALawToLinearSample(input[i]);
            result[i] = int16 * Inv32768;
        }
    }

    public static void DecodeMuLaw(ReadOnlySpan<byte> input, Span<float> result)
    {
        if (input.Length != result.Length)
        {
            throw new ArgumentException("The input and result length does not match.");
        }

        for (int i = 0; i < result.Length; i++)
        {
            short int16 = MuLawDecoder.MuLawToLinearSample(input[i]);
            result[i] = int16 * Inv32768;
        }
    }

}