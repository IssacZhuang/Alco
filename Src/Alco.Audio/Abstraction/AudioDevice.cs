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
    /// Gets or sets the listener world-space position.
    /// </summary>
    public abstract Vector3 ListenerPosition { get; set; }
    /// <summary>
    /// Gets or sets the listener world-space velocity.
    /// </summary>
    public abstract Vector3 ListenerVelocity { get; set; }
    /// <summary>
    /// Gets or sets the listener forward direction (the up vector is implementation-defined).
    /// </summary>
    public abstract Vector3 ListenerDirection { get; set; }

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
    /// <returns>A new <see cref="AudioClip"/> owned by the device.</returns>
    public unsafe AudioClip CreateAudioClip(ReadOnlySpan<float> data, int channel, int sampleRate)
    {
        return CreateAudioClipCore(data, channel, sampleRate);
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

    protected abstract AudioClip CreateAudioClipCore(ReadOnlySpan<float> data, int channel, int sampleRate);

    protected abstract void Dispose(bool disposing);

    private void Dispose()
    {
        Dispose(true);
        _host.OnDispose -= Dispose;
    }
}