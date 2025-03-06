using System.Diagnostics.CodeAnalysis;
using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// The loader for ogg audio
/// </summary>
public class AssetLoaderAudioVorbis : BaseAssetLoader<AudioClip>
{
    private readonly AudioDevice _device;

    public override string Name => "AssetLoader.Audio.Vorbis";

    public override IReadOnlyList<string> FileExtensions { get; } = [FileExt.AudioOgg];

    public AssetLoaderAudioVorbis(AudioDevice device)
    {
        _device = device;
    }

    public override object CreateAsset(in AssetLoadContext context)
    {
        return _device.CreateAudioClipFromOgg(context.Data);
    }
}