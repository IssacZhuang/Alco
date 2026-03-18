using System.Numerics;

namespace Alco.Audio.NoAudio;

internal class NoAudioSource : AudioSource
{
    private AudioClip? _clip;
    private float _gain = 1f;
    private float _pitch = 1f;
    private float _rolloff = 1f;
    private Vector3 _position;
    private Vector3 _velocity;
    private bool _isSpatial;
    private bool _isLooping;

    public override AudioClip? AudioClip
    {
        get => _clip;
        set => _clip = value;
    }

    public override float Gain
    {
        get => _gain;
        set => _gain = value;
    }

    public override float Pitch
    {
        get => _pitch;
        set => _pitch = value;
    }

    public override float Rolloff
    {
        get => _rolloff;
        set => _rolloff = value;
    }

    public override Vector3 Position
    {
        get => _position;
        set => _position = value;
    }

    public override Vector3 Velocity
    {
        get => _velocity;
        set => _velocity = value;
    }

    public override bool IsSpatial
    {
        get => _isSpatial;
        set => _isSpatial = value;
    }

    public override bool IsLooping
    {
        get => _isLooping;
        set => _isLooping = value;
    }

    public override bool IsPlaying => false;

    protected override void PlayCore()
    {
    }

    protected override void StopCore()
    {
    }

    protected override void OnBusVolumeChanged()
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}
