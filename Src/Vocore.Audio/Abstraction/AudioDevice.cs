namespace Vocore.Audio;
public abstract class AudioDevice : BaseAudioObject
{
    public const int Frequency44K = 44100;
    public const int Frequency48K = 48000;
    public const int Frequency96K = 96000;
    public const int Frequency192K = 192000;

    public AudioDevice()
    {
    }

    public AudioSource CreateSource()
    {
        return CreateSourceCore();
    }

    protected abstract AudioSource CreateSourceCore();
}