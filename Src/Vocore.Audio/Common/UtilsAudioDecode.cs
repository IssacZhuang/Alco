using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using NLayer;
using NVorbis;

namespace Vocore.Audio;

/// <summary>
/// The utils to decode audio, all the audio will be decoded into float32 format by default
/// </summary>
public unsafe static class UtilsAudioDecode
{
    /// <summary>
    /// Decode the ogg data into pcm data
    /// </summary>
    /// <param name="data">The source data of ogg</param>
    /// <param name="outData">The decoded data pointer</param>
    /// <param name="size">The size in bytes of decoded data</param>
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    public static void DecodeOgg(ReadOnlySpan<byte> data, out float* outData, out int size, out int channel, out int sampleRate)
    {
        //todo: performance optimization
        fixed (byte* ptr = data)
        {
            UnsafeStream stream = new UnsafeStream(ptr, data.Length);
            VorbisReader reader = new VorbisReader(stream, false);
            channel = reader.Channels;
            sampleRate = reader.SampleRate;
            int length = (int)reader.TotalSamples * channel;
            size = length * sizeof(float);
            float* pcmData = (float*)Marshal.AllocHGlobal(size);
            Span<float> pcmSpan = new Span<float>(pcmData, length);
            reader.ReadSamples(pcmSpan);
            outData = pcmData;
        }
    }

    public static void DecodeMpge(ReadOnlySpan<byte> data, out float* outData, out int size, out int channel, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            UnsafeStream stream = new UnsafeStream(ptr, data.Length);
            MpegFile reader = new MpegFile(stream);
            channel = reader.Channels;
            sampleRate = reader.SampleRate;
            int length = (int)reader.SampleCount * channel;
            Console.WriteLine($"sample rete {sampleRate} channel {channel} length {length}");
            size = length * sizeof(float);
            float* pcmData = (float*)Marshal.AllocHGlobal(size);
            Span<float> pcmSpan = new Span<float>(pcmData, length);
            reader.ReadSamples(pcmSpan);
            outData = pcmData;
        }
    }

    public static AudioClip CreateAudioClipFromOgg(this AudioDevice device, ReadOnlySpan<byte> data)
    {
        DecodeOgg(data, out float* pcm, out int size, out int channel, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(new ReadOnlySpan<float>(pcm, size / sizeof(float)), channel, sampleRate);
        Marshal.FreeHGlobal((IntPtr)pcm);
        return clip;
    }

    public static AudioClip CreateAudioClipFromMpge(this AudioDevice device, ReadOnlySpan<byte> data)
    {
        DecodeMpge(data, out float* pcm, out int size, out int channel, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(new ReadOnlySpan<float>(pcm, size / sizeof(float)), channel, sampleRate);
        Marshal.FreeHGlobal((IntPtr)pcm);
        return clip;
    }


}