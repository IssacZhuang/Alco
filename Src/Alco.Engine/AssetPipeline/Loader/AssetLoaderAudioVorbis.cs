using System.Diagnostics.CodeAnalysis;
using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// The loader for Vorbis (Ogg) audio.
/// </summary>
public class AssetLoaderAudioVorbis : BaseAssetLoader<AudioClip>
{
    private readonly AudioDevice _device;

    public override string Name => "AssetLoader.Audio.Vorbis";

    public override IReadOnlyList<string> FileExtensions { get; } = [FileExt.AudioOgg];

    /// <summary>
    /// Initializes a new instance of <see cref="AssetLoaderAudioVorbis"/>.
    /// </summary>
    /// <param name="device">Audio device used to decode audio data.</param>
    public AssetLoaderAudioVorbis(AudioDevice device)
    {
        _device = device;
    }

    public override object CreateAsset(in AssetLoadContext context)
    {
        return _device.CreateAudioClipFromOgg(context.GetData(), context.Filename);
    }
}