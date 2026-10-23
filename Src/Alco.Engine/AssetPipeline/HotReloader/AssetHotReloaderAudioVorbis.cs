using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Hot reloads Vorbis audio clips in place.
/// </summary>
public class AssetHotReloaderAudioVorbis : BaseAssetHotReloader<AudioClip>
{
    private readonly AudioDevice _device;

    /// <summary>
    /// Initializes a new instance of <see cref="AssetHotReloaderAudioVorbis"/>.
    /// </summary>
    /// <param name="device">Audio device used to reload clip data.</param>
    public AssetHotReloaderAudioVorbis(AudioDevice device)
    {
        _device = device;
    }

    public override void HotReload(object asset, ReadOnlySpan<byte> data)
    {
        AudioClip clip = (AudioClip)asset;
        _device.UnsafeHotReloadFromOgg(clip, data);
    }
}
