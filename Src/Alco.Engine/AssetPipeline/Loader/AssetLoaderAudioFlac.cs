using System.Diagnostics.CodeAnalysis;
using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// The loader for FLAC audio.
/// </summary>
public class AssetLoaderAudioFlac : BaseAssetLoader<AudioClip>
{
    private readonly AudioDevice _device;

    public override string Name => "AssetLoader.Audio.Flac";

    public override IReadOnlyList<string> FileExtensions => [FileExt.AudioFlac];

    /// <summary>
    /// Initializes a new instance of <see cref="AssetLoaderAudioFlac"/>.
    /// </summary>
    /// <param name="device">Audio device used to decode audio data.</param>
    public AssetLoaderAudioFlac(AudioDevice device)
    {
        _device = device;
    }

    public override object CreateAsset(in AssetLoadContext context)
    {
        return _device.CreateAudioClipFromFlac(context.GetData(), context.Filename);
    }
}