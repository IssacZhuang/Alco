using System.Diagnostics.CodeAnalysis;
using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// The loader for ogg audio
/// </summary>
public class AssetLoaderAudioVorbis : IAssetLoader
{
    private readonly AudioDevice _device;

    public string Name => "AssetLoader.Audio.Vorbis";

    public IReadOnlyList<string> FileExtensions { get; } = [FileExt.AudioOgg];

    public AssetLoaderAudioVorbis(AudioDevice device)
    {
        _device = device;
    }

    public bool CanHandleType(Type type)
    {
        return type == typeof(AudioClip);
    }

    public object CreateAsset(string filename, ReadOnlySpan<byte> data, Type targetType)
    {
        return _device.CreateAudioClipFromOgg(data);
    }
}