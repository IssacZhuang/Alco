namespace Alco.Audio;

/// <summary>
/// Supplies interleaved float32 PCM samples to an <see cref="AudioStream"/> on demand.
/// Implementations are owned by the <see cref="AudioStream"/> that consumes them and are
/// disposed together with it. Implementations are auto-disposable (GC-managed via finalizer).
/// </summary>
public interface IAudioStreamDataProvider : IDisposable
{
    /// <summary>
    /// Number of channels (1 = mono, 2 = stereo). The value is <c>0</c> and must not be read
    /// until <see cref="WaitForOpen"/> has returned.
    /// </summary>
    int Channel { get; }

    /// <summary>
    /// Sample rate in Hz. The value is <c>0</c> and must not be read until
    /// <see cref="WaitForOpen"/> has returned.
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    /// Blocks the calling thread until the provider has finished opening and can supply a valid
    /// <see cref="Channel"/>/<see cref="SampleRate"/> and decoded PCM. Providers that are ready
    /// immediately after construction (e.g. constructed from already-loaded bytes) implement this
    /// as a no-op. May throw if opening failed.
    /// </summary>
    void WaitForOpen();

    /// <summary>
    /// Reads up to <paramref name="buffer"/>.Length interleaved float samples into <paramref name="buffer"/>.
    /// Must not be called before <see cref="WaitForOpen"/> has returned.
    /// </summary>
    /// <param name="buffer">Destination span, written as interleaved float samples in the range [-1, 1].</param>
    /// <returns>Number of FRAMES decoded (samples per channel). 0 when the stream is exhausted.</returns>
    int ReadSamples(Span<float> buffer);

    /// <summary>Rewinds the stream to its beginning (used for seamless looping).</summary>
    void Reset();
}
