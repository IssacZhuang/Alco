using Alco.Audio;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Lightweight audio asset loader for NoAudio mode.
/// Skips file reading and PCM decoding, returning a minimal dummy clip produced
/// by the device. Mirrors <see cref="AssetLoaderTexture2DNoGPU"/>: when there is
/// no real audio device the asset pipeline still returns a valid, non-null
/// <see cref="AudioClip"/> without paying for decode.
/// </summary>
public class AssetLoaderAudioNoLoad : BaseAssetLoader<AudioClip>
{
    private static readonly string[] Extensions =
    [
        FileExt.AudioOgg,
        FileExt.AudioWav,
        FileExt.AudioFlac
    ];

    private readonly AudioDevice _device;

    /// <inheritdoc/>
    public override string Name => "AssetLoader.Audio.NoLoad";

    /// <inheritdoc/>
    public override IReadOnlyList<string> FileExtensions => Extensions;

    /// <summary>
    /// Initializes a new instance that produces dummy clips for the supplied device.
    /// </summary>
    /// <param name="device">The audio device used to create dummy clips.</param>
    public AssetLoaderAudioNoLoad(AudioDevice device)
    {
        _device = device;
    }

    /// <inheritdoc/>
    public override object CreateAsset(in AssetLoadContext context)
    {
        // Do not call context.GetData() — skip file I/O and decoding entirely.
        // The device (NoAudioDevice in this branch) ignores the sample data and
        // returns a minimal NoAudioClip, mirroring the 1x1 dummy texture path.
        return _device.CreateAudioClip(ReadOnlySpan<float>.Empty, channel: 1, sampleRate: 44100, context.Filename);
    }
}
