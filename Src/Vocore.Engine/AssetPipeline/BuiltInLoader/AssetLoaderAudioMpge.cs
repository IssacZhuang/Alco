using System.Diagnostics.CodeAnalysis;
using Vocore.Audio;
using Vocore.IO;

namespace Vocore.Engine;

/// <summary>
/// The loader for ogg audio
/// </summary>
public class AssetLoaderAudioMpge : IAssetLoader<AudioClip>
{
    private AudioDevice _device;

    public string Name => "AssetLoader.Audio.Vorbis";

    public IReadOnlyList<string> FileExtensions { get; } = [FileExt.AudioMp3];

    public AssetLoaderAudioMpge(AudioDevice device)
    {
        _device = device;
    }

    public bool TryCreateAsset(string filename, ReadOnlySpan<byte> data, [NotNullWhen(true)] out AudioClip? asset)
    {
        asset = _device.CreateAudioClipFromMpge(data);
        return true;
    }
}