using System.Numerics;

namespace Alco.Audio.NoAudio;

/// <summary>
/// No-op backend for <see cref="AudioStream"/>. Used when the audio backend is
/// <see cref="AudioBackend.None"/>. Playback methods only update the logical state and do not
/// produce sound; the provider is still disposed with the stream.
/// </summary>
internal sealed class NoAudioStream : AudioStream
{
    private float _gain = 1f;
    private float _pitch = 1f;
    private bool _isSpatial;
    private Vector3 _position = Vector3.Zero;
    private bool _isLooping = true;

    /// <inheritdoc/>
    public override float Gain
    {
        get => _gain;
        set => _gain = value;
    }

    /// <inheritdoc/>
    public override float Pitch
    {
        get => _pitch;
        set => _pitch = value;
    }

    /// <inheritdoc/>
    public override bool IsSpatial
    {
        get => _isSpatial;
        set => _isSpatial = value;
    }

    /// <inheritdoc/>
    public override Vector3 Position
    {
        get => _position;
        set => _position = value;
    }

    /// <inheritdoc/>
    public override bool IsLooping
    {
        get => _isLooping;
        set => _isLooping = value;
    }

    public NoAudioStream(IAudioStreamDataProvider provider) : base(provider)
    {
    }

    protected override void OnBusVolumeChanged()
    {
    }

    protected override void PlayCore()
    {
        State = AudioStreamState.Playing;
    }

    protected override void PauseCore()
    {
        if (State == AudioStreamState.Playing)
        {
            State = AudioStreamState.Paused;
        }
    }

    protected override void StopCore()
    {
        State = AudioStreamState.Stopped;
        Provider.Reset();
    }

    protected override void Dispose(bool disposing)
    {
        Provider.Dispose();
    }
}
