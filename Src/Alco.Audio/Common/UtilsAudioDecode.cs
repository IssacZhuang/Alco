using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using static StbVorbisSharp.StbVorbis;
using Alco;

namespace Alco.Audio;

/// <summary>
/// The utils to decode audio, all the audio will be decoded into float32 format by default
/// </summary>
public unsafe static class UtilsAudioDecode
{
    public static void StereoToMono(ReadOnlySpan<float> stereoData, Span<float> monoData)
    {
        Debug.Assert(stereoData.Length % 2 == 0);
        Debug.Assert(monoData.Length == stereoData.Length / 2);

        for (int i = 0; i < monoData.Length; i++)
        {
            monoData[i] = (stereoData[i * 2] + stereoData[i * 2 + 1]) * 0.5f;
        }
    }

    /// <summary>
    /// Decode the ogg data into pcm data
    /// </summary>
    /// <param name="data">The source data of ogg</param>
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    public static float[] DecodeOgg(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        return VorbisDecoder.DecodeToFloat32(data, out channel, out sampleRate);
    }

    // /// <summary>
    // /// Decode the mp3 data into pcm data
    // /// </summary>
    // /// <param name="data">The source data of mp3</param>
    // /// <param name="channel">The channels of audio</param>
    // /// <param name="sampleRate">The sample rate of audio</param>
    // public static float[] DecodeMpge(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    // {
    //     throw new NotImplementedException();
    // }

    /// <summary>
    /// Decode the wave data into pcm data
    /// </summary>
    /// <param name="data">The source data of wave</param>
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    /// <returns></returns>
    public static float[] DecodeWave(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            float[] buffer = WaveDecoder.DecodeToFloat32(data, out channel, out sampleRate);
            return buffer;
        }
    }

    /// <summary>
    /// Decode the flac data into pcm data
    /// </summary>
    /// <param name="data">The source data of flac</param>
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    /// <returns></returns>
    public static float[] DecodeFlac(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        return FlacDecoder.DecodeToFloat32(data, out channel, out sampleRate);
    }

    /// <summary>
    /// Create an audio clip from the ogg data
    /// </summary>
    /// <param name="device">The audio device</param>
    /// <param name="data">The source data of ogg</param>
    /// <param name="name">Optional clip name</param>
    /// <returns></returns>
    public static AudioClip CreateAudioClipFromOgg(this AudioDevice device, ReadOnlySpan<byte> data, string name = "unnamed_audio_clip")
    {
        float* buffer = null;
        try
        {
            buffer = VorbisDecoder.DecodeToFloat32Unsafe(data, out int channel, out int sampleCount, out int sampleRate);
            AudioClip clip = device.CreateAudioClip(new ReadOnlySpan<float>(buffer, sampleCount), channel, sampleRate, name);
            return clip;
        }
        finally
        {
            if (buffer != null)
            {
                UtilsMemory.Free(buffer);
            }
        }
    }

    // public static AudioClip CreateAudioClipFromMpge(this AudioDevice device, ReadOnlySpan<byte> data)
    // {
    //     float[] buffer = DecodeMpge(data, out int channel, out int sampleRate);
    //     AudioClip clip = device.CreateAudioClip(buffer, channel, sampleRate);
    //     return clip;
    // }

    /// <summary>
    /// Create an audio clip from the wave data
    /// </summary>
    /// <param name="device">The audio device</param>
    /// <param name="data">The source data of wave</param>
    /// <param name="name">Optional clip name</param>
    /// <returns></returns>
    public unsafe static AudioClip CreateAudioClipFromWave(this AudioDevice device, ReadOnlySpan<byte> data, string name = "unnamed_audio_clip")
    {
        float* buffer = null;
        try
        {
            buffer = WaveDecoder.DecodeFloat32Unsafe(data, out int channel, out int sampleCount, out int sampleRate);
            AudioClip clip = device.CreateAudioClip(new ReadOnlySpan<float>(buffer, sampleCount), channel, sampleRate, name);
            return clip;
        }
        finally
        {
            if (buffer != null)
            {
                UtilsMemory.Free(buffer);
            }
        }

    }

    /// <summary>
    /// Create an audio clip from the flac data
    /// </summary>
    /// <param name="device">The audio device</param>
    /// <param name="data">The source data of flac</param>
    /// <param name="name">Optional clip name</param>
    /// <returns></returns>
    public static AudioClip CreateAudioClipFromFlac(this AudioDevice device, ReadOnlySpan<byte> data, string name = "unnamed_audio_clip")
    {
        float* buffer = null;
        try
        {
            buffer = FlacDecoder.DecodeToFloat32Unsafe(data, out int channel, out int sampleCount, out int sampleRate);
            AudioClip clip = device.CreateAudioClip(new ReadOnlySpan<float>(buffer, sampleCount), channel, sampleRate, name);
            return clip;
        }
        finally
        {
            if (buffer != null)
            {
                UtilsMemory.Free(buffer);
            }
        }


    }


}