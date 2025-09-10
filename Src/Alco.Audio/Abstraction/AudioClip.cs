using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco.Audio;

/// <summary>
/// Represents decoded PCM audio data owned by an <see cref="AudioDevice"/>.
/// The data is immutable once created and described by its channel count,
/// sample rate and total sample count per channel (interleaved layout).
/// </summary>
public unsafe abstract class AudioClip : BaseAudioObject
{
    /// <summary>
    /// The human-readable clip name for logging and debugging.
    /// </summary>
    public abstract string Name { get; }
    /// <summary>
    /// Number of channels (1 = mono, 2 = stereo).
    /// </summary>
    public abstract int Channel { get; }
    /// <summary>
    /// Sample rate in Hz.
    /// </summary>
    public abstract int SampleRate { get; }
    /// <summary>
    /// Total number of interleaved samples (all channels included).
    /// </summary>
    public abstract int SampleCount { get; }
}