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
    public abstract Vector3 ListenerDirection { get; set; }

    public AudioDevice()
    {
        ListenerPosition = Vector3.Zero;
        ListenerVelocity = Vector3.Zero;
    }

    public AudioClip CreateAudioClip(ReadOnlySpan<float> data, int channel, int sampleRate)
    {
        return CreateAudioClipCore(data, channel, sampleRate);
    }

    public AudioSource CreateAudioSource()
    {
        return CreateAudioSourceCore();
    }

    protected abstract AudioSource CreateAudioSourceCore();

    protected abstract AudioClip CreateAudioClipCore(ReadOnlySpan<float> data, int channel, int sampleRate);
}