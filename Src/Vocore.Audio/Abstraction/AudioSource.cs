using System.Numerics;

namespace Vocore.Audio;

public abstract class AudioSource : BaseAudioObject
{
    public abstract AudioClip? AudioClip { get; set; }

    /// <summary>
    /// The value that affects the volume of the audio source
    /// <br/> This is a normalized value, the range is from 0 to 1
    /// </summary>
    /// <value></value>
    public abstract float Gain { get; set; }

    /// <summary>
    /// The value that affects the pitch of the audio source
    /// <br/> This is a normalized value, the range is from 0 to 1
    /// </summary>
    /// <value></value>
    public abstract float Pitch { get; set; }
    public abstract Vector3 Position { get; set; }
    public abstract Vector3 Velocity { get; set; }

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