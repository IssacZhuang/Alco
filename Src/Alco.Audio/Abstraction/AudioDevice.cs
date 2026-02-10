using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Audio;
/// <summary>
/// Represents the abstract audio device interface used by the engine.
/// Implementations provide listener state control and create sources/clips.
/// </summary>
public abstract class AudioDevice
{
    protected readonly IAudioDeviceHost _host;

    /// <summary>
    /// Gets or sets the master output volume for this device.
    /// This is a normalized value in the range [0, 1].
    /// </summary>
    public abstract float Volume { get; set; }

    /// <summary>
    /// Gets or sets the attenuation mode for audio sources.
    /// This determines how audio volume decreases with distance from the listener.
    /// </summary>
    public abstract AudioAttenuationMode AttenuationMode { get; set; }

    /// <summary>
    /// Gets or sets the listener world-space position.
    /// </summary>
    public abstract Vector3 ListenerPosition { get; set; }
    /// <summary>
    /// Gets or sets the listener world-space velocity.
    /// </summary>
    public abstract Vector3 ListenerVelocity { get; set; }
    /// <summary>
    /// Gets the listener orientation consisting of forward and up vectors.
    /// </summary>
    /// <param name="forward">The normalized forward vector of the listener.</param>
    /// <param name="up">The normalized up vector of the listener.</param>
    public abstract void GetListenerOrientation(out Vector3 forward, out Vector3 up);

    /// <summary>
    /// Sets the listener orientation consisting of forward and up vectors.
    /// </summary>
    /// <param name="forward">The normalized forward vector to set.</param>
    /// <param name="up">The normalized up vector to set.</param>
    public abstract void SetListenerOrientation(in Vector3 forward, in Vector3 up);

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDevice"/> class.
    /// </summary>
    /// <param name="host">The device host used for lifecycle and logging.</param>
    public AudioDevice(IAudioDeviceHost host)
    {
        _host = host;
        ListenerPosition = Vector3.Zero;
        ListenerVelocity = Vector3.Zero;
        host.OnDispose += Dispose;
    }

    /// <summary>
    /// Creates an <see cref="AudioClip"/> from interleaved PCM float samples.
    /// </summary>
    /// <param name="data">Interleaved samples in the range [-1, 1].</param>
    /// <param name="channel">Number of channels (1 for mono, 2 for stereo).</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="name">Optional human-readable clip name for logging/debugging.</param>
    /// <returns>A new <see cref="AudioClip"/> owned by the device.</returns>
    public unsafe AudioClip CreateAudioClip(ReadOnlySpan<float> data, int channel, int sampleRate, string name = "unnamed_audio_clip")
    {
        return CreateAudioClipCore(data, channel, sampleRate, name);
    }

    /// <summary>
    /// Creates a new <see cref="AudioSource"/> that can play clips on this device.
    /// </summary>
    /// <returns>A new audio source instance.</returns>
    public AudioSource CreateAudioSource()
    {
        return CreateAudioSourceCore();
    }

    protected abstract AudioSource CreateAudioSourceCore();

    /// <summary>
    /// Core factory for creating an <see cref="AudioClip"/>.
    /// </summary>
    /// <param name="data">Interleaved samples.</param>
    /// <param name="channel">Channel count.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="name">Optional clip name.</param>
    /// <returns>The created clip.</returns>
    protected abstract AudioClip CreateAudioClipCore(ReadOnlySpan<float> data, int channel, int sampleRate, string? name);

    /// <summary>
    /// Called periodically (e.g. once per frame) to allow the device to perform
    /// maintenance such as detecting disconnection and attempting reconnection.
    /// </summary>
    /// <param name="delta">Time in seconds since the last poll.</param>
    public virtual void Poll(float delta) { }

    protected abstract void Dispose(bool disposing);

    private void Dispose()
    {
        Dispose(true);
        _host.OnDispose -= Dispose;
    }
}