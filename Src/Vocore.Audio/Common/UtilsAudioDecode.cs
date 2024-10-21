using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using static OggVorbisSharp.VorbisFile;

using NVorbis;
using OggVorbisSharp;
using static OggVorbisSharp.Vorbis;

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
        //todo: performance optimization
        fixed (byte* ptr = data)
        {
            //VorbisDecoder.DecodeVorbisAudioToFloat32(data, out channel, out sampleRate);
            UnsafeStream stream = new UnsafeStream(ptr, data.Length);
            // VorbisReader reader = new VorbisReader(stream, false);
            // channel = reader.Channels;
            // sampleRate = reader.SampleRate;
            // int length = (int)reader.TotalSamples * channel;
            // float[] buffer = new float[length];
            // reader.ReadSamples(buffer, 0, length);
            // return buffer;
            OggVorbis_File vf = new OggVorbis_File();
            int openResult = ov_open(stream, ref vf, null, 0);

            if (openResult < 0)
            {
                throw new Exception("Error in ov_open ogg");
            }

            vorbis_info info = ov_info(ref vf, -1);
            channel = info.channels;
            sampleRate = info.rate;

            List<float> pcmData = new List<float>();

            int bitStream = 0;
            while (true)
            {
                float** pcm = null;
                long samples = ov_read_float(ref vf, &pcm, 4096, ref bitStream);
                if (samples == 0)
                {
                    //EOF
                    break;
                }
                else if (samples < 0)
                {
                    throw new Exception("Error in decoding ogg");
                }
                else
                {
                    // for (int i = 0; i < channel; i++)
                    // {
                    //     for (int j = 0; j < samples; j++)
                    //     {
                    //         pcmData.Add(pcm[i][j]);
                    //     }
                    // }

                    //interleave the channels   
                    for (int i = 0; i < samples; i++)
                    {
                        for (int j = 0; j < channel; j++)
                        {
                            pcmData.Add(pcm[j][i]);
                        }
                    }
                }
            }
            // short* pcm = stackalloc short[4096];
            // while (true){
            //     int bytes = ov_read(ref vf, (byte*)pcm, 4096 * sizeof(short), 0, 2, 1, ref bitStream);
            //     if (bytes == 0)
            //     {
            //         //EOF
            //         break;
            //     }
            //     else if (bytes < 0)
            //     {
            //         throw new Exception("Error in decoding ogg");
            //     }
            //     else
            //     {
            //         for (int i = 0; i < bytes/2; i++)
            //         {
            //             pcmData.Add(pcm[i] / 32768.0f);
            //         }
            //     }

            // }

            ov_clear(ref vf);

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
        float[] buffer = DecodeFlac(data, out int channel, out int sampleRate);
        AudioClip clip = device.CreateAudioClip(buffer, channel, sampleRate);
        return clip;
    }


}