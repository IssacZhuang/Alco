using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using static OggVorbisSharp.VorbisFile;

using NVorbis;
using OggVorbisSharp;
using static OggVorbisSharp.Vorbis;

using static StbVorbisSharp.StbVorbis;

namespace Vocore.Audio;

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

    /// <summary>
    /// Decode the mp3 data into pcm data
    /// </summary>
    /// <param name="data">The source data of mp3</param>
    /// <param name="channel">The channels of audio</param>
    /// <param name="sampleRate">The sample rate of audio</param>
    public static float[] DecodeMpge(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        throw new NotImplementedException();
    }

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
            float[] buffer = WaveDecoder.DecodeWaveAudioToFloat32(data, out channel, out sampleRate);
            return buffer;
        }
    }

    public static float[] DecodeAiff(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        throw new NotImplementedException();
    }

    public static float[] DecodeFlac(ReadOnlySpan<byte> data, out int channel, out int sampleRate)
    {
        throw new NotImplementedException();
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

    public unsafe static AudioClip CreateAudioClipFromWave(this AudioDevice device, ReadOnlySpan<byte> data)
    {
        float* buffer = WaveDecoder.DecodeWaveAudioToFloat32Unsafe(data, out int channel, out int sampleCount, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(new ReadOnlySpan<float>(buffer, sampleCount), channel, sampleRate);
        Marshal.FreeHGlobal((IntPtr)buffer);
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
        float* buffer = FlacDecoder.DecodeWaveAudioToFloat32Unsafe(data, out int channel, out int sampleCount, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(new ReadOnlySpan<float>(buffer, sampleCount), channel, sampleRate);
        Marshal.FreeHGlobal((IntPtr)buffer);
        return clip;
    }


}