using System.Diagnostics.CodeAnalysis;
using Vocore.Audio;
using Vocore.IO;

namespace Vocore.Engine;

/// <summary>
/// The loader for ogg audio
/// </summary>
public class AssetLoaderAudioVorbis : IAssetLoader<AudioClip>
{
    private readonly AudioDevice _device;

    public string Name => "AssetLoader.Audio.Vorbis";

    public IReadOnlyList<string> FileExtensions { get; } = [FileExt.AudioOgg];

    public AssetLoaderAudioVorbis(AudioDevice device)
    {
        _device = device;
    }

    public AudioClip CreateAsset(string filename, ReadOnlySpan<byte> data)
    {
        return _device.CreateAudioClipFromOgg(data);
    }
}