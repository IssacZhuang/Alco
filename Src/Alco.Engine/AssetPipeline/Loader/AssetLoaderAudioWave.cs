using System.Diagnostics.CodeAnalysis;
using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// The loader for WAV audio.
/// </summary>
public class AssetLoaderAudioWave : BaseAssetLoader<AudioClip>
{
    private readonly AudioDevice _device;

    public override string Name => "AssetLoader.Audio.Wave";

    public override IReadOnlyList<string> FileExtensions { get; } = [FileExt.AudioWav];

    /// <summary>
    /// Initializes a new instance of <see cref="AssetLoaderAudioWave"/>.
    /// </summary>
    /// <param name="device">Audio device used to decode audio data.</param>
    public AssetLoaderAudioWave(AudioDevice device)
    {
        _device = device;
    }

    public override object CreateAsset(in AssetLoadContext context)
    {
        return _device.CreateAudioClipFromWave(context.GetData(), context.Filename);
    }
}