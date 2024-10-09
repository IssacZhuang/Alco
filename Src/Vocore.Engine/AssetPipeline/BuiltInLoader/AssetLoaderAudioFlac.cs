using System.Diagnostics.CodeAnalysis;
using Vocore.Audio;
using Vocore.IO;

namespace Vocore.Engine;

/// <summary>
/// The loader for ogg audio
/// </summary>
public class AssetLoaderAudioFlac : IAssetLoader<AudioClip>
{
    private readonly AudioDevice _device;

    public string Name => "AssetLoader.Audio.Vorbis";

    public IReadOnlyList<string> FileExtensions { get; } = [FileExt.AudioFlac];

    public AssetLoaderAudioFlac(AudioDevice device)
    {
        _device = device;
    }

    public bool TryCreateAsset(string filename, ReadOnlySpan<byte> data, [NotNullWhen(true)] out AudioClip? asset)
    {
        asset = _device.CreateAudioClipFromFlac(data);
        return true;
    }
}