using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

public class AssetHotReloaderAudioVorbis : BaseAssetHotReloader<AudioClip>
{
    private readonly AudioDevice _device;

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
