using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;


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
            //VorbisDecoder.DecodeVorbisAudioToFloat32(data, out channel, out sampleRate);
            UnsafeStream stream = new UnsafeStream(ptr, data.Length);
            VorbisReader reader = new VorbisReader(stream, false);
            channel = reader.Channels;
            sampleRate = reader.SampleRate;
            int length = (int)reader.TotalSamples * channel;
            float[] buffer = new float[length];
            reader.ReadSamples(buffer, 0, length);
            return buffer;
            // OggVorbis_File vf = new OggVorbis_File();
            // int openResult = ov_open(stream, ref vf, null, 0);
            
            // if (openResult < 0)
            // {
            //     throw new Exception("Error in ov_open ogg");
            // }

            // vorbis_info info = ov_info(ref vf, -1);
            // channel = info.channels;

            // List<float> interleaved = new List<float>();

            // int bitStream = 0;
            // while (true)
            // {
            //     float** pcm = null;
            //     long samples = ov_read_float(ref vf, ref pcm, 1024, ref bitStream);
            //     if (samples == 0)
            //     {
            //         //EOF
            //         break;
            //     }
            //     else if (samples < 0)
            //     {
            //         throw new Exception("Error in decoding ogg");
            //     }
            //     else
            //     {
            //         for (int i = 0; i < samples; i++)
            //         {
            //             for (int j = 0; j < channel; j++)
            //             {
            //                 interleaved.Add(pcm[j][i]);
            //             }
            //         }
            //     }
            // }

            // ov_clear(ref vf);

            // float[] buffer = interleaved.ToArray();
            // sampleRate = info.rate;
            // return buffer;
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