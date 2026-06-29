using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Audio;

/// <summary>
/// A self-contained streaming audio player. It owns a borrowed backend playback source,
/// a ring of streaming buffers, and an <see cref="IAudioStreamDataProvider"/> that feeds it
/// interleaved float32 PCM on demand. The device refills the buffers each frame; the caller
/// only needs to configure properties and call <see cref="Play"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="AudioSource"/>: GC-managed (extends <see cref="BaseAudioObject"/>),
/// borrows a source from the device pool, shadows state that is pushed to hardware once a
/// source is allocated, and binds to an optional <see cref="AudioBus"/>.
/// </remarks>
public abstract class AudioStream : BaseAudioObject
{
    private AudioBus? _bus;

    /// <summary>
    /// The provider that supplies PCM data to this stream. Owned by the stream and disposed
    /// together with it. Held as a strong reference; since the stream is GC-managed and the
    /// device holds only a weak reference to the stream, the stream and provider are collected
    /// together as a GC island.
    /// </summary>
    protected readonly IAudioStreamDataProvider Provider;

    /// <summary>
    /// Gets or sets the audio bus associated with this stream. When set, the effective bus
    /// volume is applied to the output gain. If null, only the local <see cref="Gain"/> and
    /// <see cref="AudioDevice"/>.Volume affect the output.
    /// </summary>
    public AudioBus? Bus
    {
        get => _bus;
        set
        {
            if (_bus == value) return;

            if (_bus != null)
            {
                _bus.OnVolumeChanged -= OnBusVolumeChanged;
            }

            _bus = value;

            if (_bus != null)
            {
                _bus.OnVolumeChanged += OnBusVolumeChanged;
            }

            OnBusVolumeChanged();
        }
    }

    /// <summary>
    /// The value that affects the output volume of the stream.
    /// <br/>This is a normalized value in the range [0, 1].
    /// </summary>
    public abstract float Gain { get; set; }

    /// <summary>
    /// The value that affects the pitch of the stream.
    /// <br/>This is a normalized value in the range [0, 1].
    /// </summary>
    public abstract float Pitch { get; set; }

    /// <summary>
    /// Indicates whether spatialization is enabled. Defaults to <c>false</c> for music (BGM is
    /// a non-spatial "relative"/direct-channel source). When <c>false</c>, <see cref="Position"/>
    /// has no effect.
    /// </summary>
    public abstract bool IsSpatial { get; set; }

    /// <summary>
    /// World-space position of the stream when spatialized. Ignored when
    /// <see cref="IsSpatial"/> is <c>false</c>.
    /// </summary>
    public abstract Vector3 Position { get; set; }

    /// <summary>
    /// Indicates whether the stream should loop seamlessly. When the provider is exhausted and
    /// this is <c>true</c>, the provider is <see cref="IAudioStreamDataProvider.Reset">reset</see>
    /// and playback continues. Defaults to <c>true</c> (BGM loops).
    /// </summary>
    public abstract bool IsLooping { get; set; }

    /// <summary>
    /// True when the logical playback state is <see cref="AudioStreamState.Playing"/>.
    /// </summary>
    public bool IsPlaying => State == AudioStreamState.Playing;

    /// <summary>The logical playback state of this stream.</summary>
    public AudioStreamState State { get; protected set; } = AudioStreamState.Stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioStream"/> class.
    /// </summary>
    /// <param name="provider">The data provider that feeds PCM to this stream.</param>
    protected AudioStream(IAudioStreamDataProvider provider)
    {
        Provider = provider;
    }

    /// <summary>
    /// Starts playback. On the first call after construction or <see cref="Stop"/>, the source
    /// is allocated, buffers are primed and playback begins from the start. If paused, playback
    /// resumes from the current position.
    /// </summary>
    public void Play()
    {
        PlayCore();
    }

    /// <summary>Pauses playback, retaining the source and queued buffers.</summary>
    public void Pause()
    {
        PauseCore();
    }

    /// <summary>
    /// Stops playback, releases the borrowed source back to the pool, and rewinds the provider
    /// so the next <see cref="Play"/> begins from the start.
    /// </summary>
    public void Stop()
    {
        StopCore();
    }

    /// <summary>Called when the associated bus volume changes or when a new bus is assigned.</summary>
    protected abstract void OnBusVolumeChanged();

    /// <summary>Backend implementation of <see cref="Play"/>.</summary>
    protected abstract void PlayCore();

    /// <summary>Backend implementation of <see cref="Pause"/>.</summary>
    protected abstract void PauseCore();

    /// <summary>Backend implementation of <see cref="Stop"/>.</summary>
    protected abstract void StopCore();
}
