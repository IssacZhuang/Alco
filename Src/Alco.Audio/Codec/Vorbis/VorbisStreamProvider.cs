using System.Threading;
using Alco;
using static StbVorbisSharp.StbVorbis;

namespace Alco.Audio;

/// <summary>
/// An <see cref="IAudioStreamDataProvider"/> that incrementally decodes Vorbis (OGG) data into
/// interleaved float32 PCM using StbVorbisSharp. Auto-disposable (GC-managed via finalizer).
/// </summary>
/// <remarks>
/// <para>
/// The OGG bytes are copied into a native buffer (via <see cref="MemoryUtility.Alloc"/>) rather
/// than pinned from a managed array, because <c>stb_vorbis_open_memory</c> stores the raw
/// <c>byte*</c> into the decoder handle for its entire lifetime — a managed array could be
/// relocated by the GC after a <c>fixed</c> block ends. A native allocation is stable and
/// matches the existing <see cref="MemoryUtility"/> usage in the codec layer.
/// </para>
/// <para>
/// The returned frame count from <see cref="ReadSamples"/> follows StbVorbisSharp semantics
/// (frames = samples per channel), so the caller must multiply by <see cref="Channel"/> to get
/// the number of floats actually written.
/// </para>
/// </remarks>
public sealed unsafe class VorbisStreamProvider : IAudioStreamDataProvider
{
    private readonly byte* _nativeData;
    private readonly int _dataLength;
    private readonly stb_vorbis_info _info;
    private stb_vorbis? _vorbis;
    private volatile int _disposed;

    /// <summary>Number of channels (1 = mono, 2 = stereo).</summary>
    public int Channel => _info.channels;

    /// <summary>Sample rate in Hz.</summary>
    public int SampleRate => (int)_info.sample_rate;

    /// <summary>
    /// Initializes a new instance from a span of raw OGG bytes. The bytes are copied into a
    /// native buffer owned by this provider.
    /// </summary>
    /// <param name="oggData">Raw OGG file bytes.</param>
    /// <exception cref="AudioException">Thrown if the data cannot be opened as Vorbis.</exception>
    public VorbisStreamProvider(ReadOnlySpan<byte> oggData)
    {
        _dataLength = oggData.Length;
        _nativeData = (byte*)MemoryUtility.Alloc(oggData.Length);
        fixed (byte* src = oggData)
        {
            MemoryUtility.MemCopy(src, _nativeData, oggData.Length);
        }

        int error = 0;
        _vorbis = stb_vorbis_open_memory(_nativeData, _dataLength, &error);
        if (_vorbis == null)
        {
            MemoryUtility.Free(_nativeData);
            throw new AudioException($"Failed to open Vorbis stream (stb_vorbis error {error}).");
        }

        _info = stb_vorbis_get_info(_vorbis);
    }

    /// <summary>
    /// Reads up to <paramref name="buffer"/>.Length interleaved float samples into <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">Destination span, written as interleaved float samples in the range [-1, 1].</param>
    /// <returns>Number of FRAMES decoded (samples per channel). 0 when the stream is exhausted.</returns>
    public int ReadSamples(Span<float> buffer)
    {
        stb_vorbis? vorbis = _vorbis;
        if (vorbis == null) return 0;

        fixed (float* ptr = buffer)
        {
            int frames = stb_vorbis_get_samples_float_interleaved(vorbis, _info.channels, ptr, buffer.Length);
            if (frames < 0)
            {
                return 0;
            }

            return frames;
        }
    }

    /// <summary>Rewinds the stream to its beginning (used for seamless looping).</summary>
    public void Reset()
    {
        if (_vorbis != null)
        {
            stb_vorbis_seek_start(_vorbis);
        }
    }

    /// <summary>
    /// Releases the Vorbis decoder and the native OGG buffer. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DisposeCore();
            GC.SuppressFinalize(this);
        }
    }

    private void DisposeCore()
    {
        stb_vorbis? vorbis = _vorbis;
        _vorbis = null;
        if (vorbis != null)
        {
            stb_vorbis_close(vorbis);
        }

        if (_nativeData != null)
        {
            MemoryUtility.Free(_nativeData);
        }
    }

    ~VorbisStreamProvider()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DisposeCore();
        }
    }
}
