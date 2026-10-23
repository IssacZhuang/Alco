using Alco.Audio.NoAudio;
using Alco.Audio.OpenAL;

namespace Alco.Audio;

/// <summary>
/// Creates audio devices for the available audio backends.
/// </summary>
public static class AudioDeviceFactory
{
    /// <summary>
    /// Gets a silent no-op audio device.
    /// </summary>
    /// <returns>A device that produces no sound and accepts all audio calls.</returns>
    public static AudioDevice GetNoAudioDevice()
    {
        return new NoAudioDevice();
    }

    /// <summary>
    /// Creates an OpenAL audio device.
    /// </summary>
    /// <param name="host">The host providing platform integration for the device.</param>
    /// <returns>The created OpenAL device.</returns>
    public static AudioDevice CreateOpenALDevice(IAudioDeviceHost host)
    {
        return new OpenALDevice(host);
    }
}