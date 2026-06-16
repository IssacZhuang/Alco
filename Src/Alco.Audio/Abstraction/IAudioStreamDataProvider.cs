namespace Alco.Audio;

/// <summary>
/// Supplies interleaved float32 PCM samples to an <see cref="AudioStream"/> on demand.
/// Implementations are owned by the <see cref="AudioStream"/> that consumes them and are
/// disposed together with it. Implementations are auto-disposable (GC-managed via finalizer).
/// </summary>
public interface IAudioStreamDataProvider : IDisposable
{
    /// <summary>Number of channels (1 = mono, 2 = stereo).</summary>
    int Channel { get; }

    /// <summary>Sample rate in Hz.</summary>
    int SampleRate { get; }

    /// <summary>
    /// Reads up to <paramref name="buffer"/>.Length interleaved float samples into <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">Destination span, written as interleaved float samples in the range [-1, 1].</param>
    /// <returns>Number of FRAMES decoded (samples per channel). 0 when the stream is exhausted.</returns>
    int ReadSamples(Span<float> buffer);

    /// <summary>Rewinds the stream to its beginning (used for seamless looping).</summary>
    void Reset();
}
