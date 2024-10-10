using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using CSCore;
using CSCore.Codecs;
using CSCore.Codecs.AIFF;
using CSCore.Codecs.FLAC;
using CSCore.Codecs.MP3;
using CSCore.Codecs.WAV;
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
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    public static float[] DecodeOgg(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        //todo: performance optimization
        fixed (byte* ptr = data)
        {
            UnsafeStream stream = new UnsafeStream(ptr, data.Length);
            VorbisReader reader = new VorbisReader(stream, false);
            channel = reader.Channels;
            sampleRate = reader.SampleRate;
            int length = (int)reader.TotalSamples * channel;
            float[] buffer = new float[length];
            reader.ReadSamples(buffer, 0, length);
            return buffer;
        }
    }

    /// <summary>
    /// Decode the mp3 data into pcm data
    /// </summary>
    /// <param name="data">The source data of mp3</param>
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    public static float[] DecodeMpge(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            UnsafeStream stream = new UnsafeStream(ptr, data.Length);
            using DmoMp3Decoder decoder = new DmoMp3Decoder(stream);
            channel = decoder.WaveFormat.Channels;
            sampleRate = decoder.WaveFormat.SampleRate;
            int length = (int)decoder.Length / channel;
            float[] buffer = new float[length];
            using var source = decoder.ToSampleSource();
            source.Read(buffer, 0, length);
            return buffer;
        }
    }

    public static float[] DecodeWave(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            float[] buffer = WaveDecoder.DecodeWaveAudioToFloat32(data, out channel, out sampleRate);
            return buffer;
        }
    }

    public static float[] DecodeAiff(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            UnsafeStream stream = new UnsafeStream(ptr, data.Length);
            using AiffReader reader = new AiffReader(stream);
            channel = reader.WaveFormat.Channels;
            sampleRate = reader.WaveFormat.SampleRate;
            int length = (int)reader.Length / channel;
            float[] buffer = new float[length];
            using var source = reader.ToSampleSource();
            source.Read(buffer, 0, length);
            return buffer;
        }
    }

    public static float[] DecodeFlac(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            UnsafeStream stream = new UnsafeStream(ptr, data.Length);
            using FlacFile reader = new FlacFile(stream);
            channel = reader.WaveFormat.Channels;
            sampleRate = reader.WaveFormat.SampleRate;
            int length = (int)reader.Length / channel;
            float[] buffer = new float[length];
            using var source = reader.ToSampleSource();
            source.Read(buffer, 0, length);
            return buffer;
        }
    }

    public static AudioClip CreateAudioClipFromOgg(this AudioDevice device, ReadOnlySpan<byte> data)
    {
        float[] buffer = DecodeOgg(data, out int channel, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(buffer, channel, sampleRate);
        return clip;
    }

    public static AudioClip CreateAudioClipFromMpge(this AudioDevice device, ReadOnlySpan<byte> data)
    {
        float[] buffer = DecodeMpge(data, out int channel, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(buffer, channel, sampleRate);
        return clip;
    }

    public static AudioClip CreateAudioClipFromWave(this AudioDevice device, ReadOnlySpan<byte> data)
    {
        float[] buffer = DecodeWave(data, out int channel, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(buffer, channel, sampleRate);
        return clip;
    }

    public static AudioClip CreateAudioClipFromAiff(this AudioDevice device, ReadOnlySpan<byte> data)
    {
        float[] buffer = DecodeAiff(data, out int channel, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(buffer, channel, sampleRate);
        return clip;
    }

    public static AudioClip CreateAudioClipFromFlac(this AudioDevice device, ReadOnlySpan<byte> data)
    {
        float[] buffer = DecodeFlac(data, out int channel, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(buffer, channel, sampleRate);
        return clip;
    }


}