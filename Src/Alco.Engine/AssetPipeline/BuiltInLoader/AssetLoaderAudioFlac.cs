using System.Diagnostics.CodeAnalysis;
using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

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

    public AudioClip CreateAsset(string filename, ReadOnlySpan<byte> data)
    {
        return _device.CreateAudioClipFromFlac(data);
    }
}