using System.Diagnostics.CodeAnalysis;
using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// The loader for ogg audio
/// </summary>
public class AssetLoaderAudioFlac : BaseAssetLoader<AudioClip>
{
    private readonly AudioDevice _device;

    public override string Name => "AssetLoader.Audio.Flac";

    public override IReadOnlyList<string> FileExtensions => [FileExt.AudioFlac];

    public AssetLoaderAudioFlac(AudioDevice device)
    {
        _device = device;
    }

    public override object CreateAsset(in AssetLoadContext context)
    {
        return _device.CreateAudioClipFromFlac(context.Data);
    }
}