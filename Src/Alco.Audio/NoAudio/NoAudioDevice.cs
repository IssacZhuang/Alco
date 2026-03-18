using System.Numerics;

namespace Alco.Audio.NoAudio;

internal class NoAudioDevice : AudioDevice
{
    private class DummyHost : IAudioDeviceHost
    {
        event Action IAudioDeviceHost.OnDispose
        {
            add { }
            remove { }
        }

        public void LogInfo(ReadOnlySpan<char> message)
        {
        }

        public void LogWarning(ReadOnlySpan<char> message)
        {
        }

        public void LogError(ReadOnlySpan<char> message)
        {
        }

        public void LogSuccess(ReadOnlySpan<char> message)
        {
        }
    }

    private Vector3 _listenerPosition;
    private Vector3 _listenerVelocity;
    private Vector3 _listenerForward = -Vector3.UnitZ;
    private Vector3 _listenerUp = Vector3.UnitY;
    private float _volume = 1f;
    private AudioAttenuationMode _attenuationMode = AudioAttenuationMode.Inverse;

    public override Vector3 ListenerPosition
    {
        get => _listenerPosition;
        set => _listenerPosition = value;
    }

    public override Vector3 ListenerVelocity
    {
        get => _listenerVelocity;
        set => _listenerVelocity = value;
    }

    public override float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public override AudioAttenuationMode AttenuationMode
    {
        get => _attenuationMode;
        set => _attenuationMode = value;
    }

    public NoAudioDevice() : base(new DummyHost())
    {
    }

    public override void GetListenerOrientation(out Vector3 forward, out Vector3 up)
    {
        forward = _listenerForward;
        up = _listenerUp;
    }

    public override void SetListenerOrientation(in Vector3 forward, in Vector3 up)
    {
        _listenerForward = forward;
        _listenerUp = up;
    }

    protected override AudioSource CreateAudioSourceCore()
    {
        return new NoAudioSource();
    }

    protected override AudioClip CreateAudioClipCore(ReadOnlySpan<float> data, int channel, int sampleRate, string? name)
    {
        return new NoAudioClip(name);
    }

    protected override void Dispose(bool disposing)
    {
    }
}
