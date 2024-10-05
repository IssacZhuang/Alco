using System.Numerics;

namespace Vocore.Audio;

public abstract class AudioSource : BaseAudioObject
{
    public abstract AudioClip? AudioClip { get; set; }
    public abstract float Volume { get; set; }
    public abstract Vector3 Position { get; set; }
    public abstract Vector3 Velocity { get; set; }
    public AudioSource()
    {
        Volume = 1f;
        Position = Vector3.Zero;
        Velocity = Vector3.Zero;
    }

    public bool TryPlay()
    {
        if (AudioClip == null)
        {
            return false;
        }

        PlayCore();
        return true;
    }

    public void Play()
    {
        if (AudioClip == null)
        {
            throw new AudioException("The audio clip is not setted");
        }

        this.PlayCore();
    }

    protected abstract void PlayCore();

}