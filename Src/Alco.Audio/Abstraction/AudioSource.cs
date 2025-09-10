using System.Numerics;

namespace Alco.Audio;

/// <summary>
/// Represents a playable audio source instance. It holds state like gain, pitch,
/// spatialization, and the clip to be played, and exposes playback controls.
/// </summary>
public abstract class AudioSource : BaseAudioObject
{
    /// <summary>
    /// The clip to play on this source. Set to null to clear.
    /// </summary>
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
    /// <summary>
    /// World-space position of the source when spatialized.
    /// </summary>
    public abstract Vector3 Position { get; set; }
    /// <summary>
    /// World-space velocity of the source when spatialized.
    /// </summary>
    public abstract Vector3 Velocity { get; set; }
    /// <summary>
    /// Indicates whether spatialization is enabled for this source.
    /// Non-spatial sources will be treated as UI/music (relative) sources.
    /// </summary>
    public abstract bool IsSpatial { get; set; }
    /// <summary>
    /// Indicates whether the source should loop the assigned clip.
    /// </summary>
    public abstract bool IsLooping { get; set; }
    /// <summary>
    /// True if the source is currently playing.
    /// </summary>
    public abstract bool IsPlaying { get; }

    /// <summary>
    /// Attempts to start playback if a clip is assigned.
    /// </summary>
    /// <returns>True if playback started; false if no clip was assigned.</returns>
    public bool TryPlay()
    {
        if (AudioClip == null)
        {
            return false;
        }

        PlayCore();
        return true;
    }

    /// <summary>
    /// Starts playback. Throws if no clip is assigned.
    /// </summary>
    public void Play()
    {
        if (AudioClip == null)
        {
            throw new AudioException("The audio clip is not setted");
        }

        PlayCore();
    }

    /// <summary>
    /// Stops playback if playing.
    /// </summary>
    public void Stop()
    {
        StopCore();
    }

    protected abstract void PlayCore();
    protected abstract void StopCore();

}