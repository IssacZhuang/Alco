using System.Numerics;

namespace Vocore.Audio;
public abstract class AudioDevice : BaseAudioObject
{
    public const int Frequency44K = 44100;
    public const int Frequency48K = 48000;
    public const int Frequency96K = 96000;
    public const int Frequency192K = 192000;

    public abstract Vector3 ListenerPosition { get; set; }
    public abstract Vector3 ListenerVelocity { get; set; }

    public AudioDevice()
    {
        ListenerPosition = Vector3.Zero;
        ListenerVelocity = Vector3.Zero;
    }

    public AudioClip CreateClip(ReadOnlySpan<float> data, int channel, int sampleRate)
    {
        return CreateClipCore(data, channel, sampleRate);
    }

    public AudioSource CreateSource()
    {
        return CreateSourceCore();
    }

    protected abstract AudioSource CreateSourceCore();

    protected abstract AudioClip CreateClipCore(ReadOnlySpan<float> data, int channel, int sampleRate);
}