using System.Diagnostics.CodeAnalysis;
using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// The loader for wave audio
/// </summary>
public class AssetLoaderAudioWave : BaseAssetLoader<AudioClip>
{
    private readonly AudioDevice _device;

    public override string Name => "AssetLoader.Audio.Wave";

    public override IReadOnlyList<string> FileExtensions { get; } = [FileExt.AudioWav];

    public AssetLoaderAudioWave(AudioDevice device)
    {
        _device = device;
    }

    public override object CreateAsset(string filename, ReadOnlySpan<byte> data, Type targetType)
    {
        return _device.CreateAudioClipFromWave(data);
    }
}