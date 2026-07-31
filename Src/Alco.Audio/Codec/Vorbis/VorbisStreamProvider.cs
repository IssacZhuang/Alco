using Alco;
using static StbVorbisSharp.StbVorbis;

namespace Alco.Audio;

/// <summary>
/// An <see cref="IAudioStreamDataProvider"/> that incrementally decodes Vorbis (OGG) data into
/// interleaved float32 PCM using StbVorbisSharp. Inherits lazy background loading and off-thread
/// prefetch decoding from <see cref="AudioStreamDataProvider"/>. Auto-disposable (GC-managed via
/// finalizer).
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
/// Because <see cref="AudioStreamDataProvider"/> drives decoding on a pool thread, the subclass
/// only implements three synchronous methods (<see cref="OpenCore"/>, <see cref="ReadCore"/>,
/// <see cref="ResetCore"/>) and writes no threading code.
/// </para>
/// <para>
/// The returned frame count follows StbVorbisSharp semantics (frames = samples per channel), so
/// the caller multiplies by <see cref="AudioStreamDataProvider.Channel"/> to get the float count.
/// </para>
/// </remarks>
public sealed unsafe class VorbisStreamProvider : AudioStreamDataProvider
{
    // Source bytes owned by this provider, freed on dispose. Null after dispose.
    private byte* _nativeData;
    private int _dataLength;

    // Owned by this provider; null after dispose. Touched only on the pool thread (open/read/reset).
    private stb_vorbis? _vorbis;
    private stb_vorbis_info _info;
    private Stream? _sourceStream;

    /// <summary>
    /// Initializes a new instance that loads the OGG data from <paramref name="stream"/> in the
    /// background. The provider takes ownership of <paramref name="stream"/> and disposes it.
    /// Construction returns immediately; call <see cref="WaitForOpen"/> before reading
    /// <see cref="AudioStreamDataProvider.Channel"/>/<see cref="AudioStreamDataProvider.SampleRate"/>.
    /// </summary>
    /// <param name="stream">A readable stream containing the raw OGG bytes.</param>
    public VorbisStreamProvider(Stream stream)
    {
        _sourceStream = stream;
    }

    /// <summary>
    /// Initializes a new instance from a span of raw OGG bytes, copying them into a native buffer.
    /// The bytes are loaded in the background like the <see cref="VorbisStreamProvider(Stream)"/>
    /// constructor.
    /// </summary>
    /// <param name="oggData">Raw OGG file bytes.</param>
    public VorbisStreamProvider(ReadOnlySpan<byte> oggData)
    {
        _dataLength = oggData.Length;
        _nativeData = (byte*)MemoryUtility.Alloc(oggData.Length);
        fixed (byte* src = oggData)
        {
            MemoryUtility.MemCopy(src, _nativeData, oggData.Length);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="AudioException">
    /// Thrown if the stream is unreadable or the data cannot be opened as Vorbis.
    /// </exception>
    protected override void OpenCore()
    {
        if (_nativeData == null)
        {
            // Stream-backed: read it fully into a native buffer now.
            Stream? stream = _sourceStream;
            if (stream == null || !stream.CanRead)
            {
                throw new AudioException("Vorbis source stream is null or not readable.");
            }

            _dataLength = (int)stream.Length;
            _nativeData = (byte*)MemoryUtility.Alloc(_dataLength);

            int read = 0;
            while (read < _dataLength)
            {
                int n = stream.Read(new Span<byte>(_nativeData + read, _dataLength - read));
                if (n <= 0)
                {
                    break;
                }

                read += n;
            }

            if (read != _dataLength)
            {
                MemoryUtility.Free(_nativeData);
                _nativeData = null;
                throw new AudioException("Vorbis source stream ended before the declared length.");
            }

            stream.Dispose();
            _sourceStream = null;
        }

        int error = 0;
        stb_vorbis vorbis = stb_vorbis_open_memory(_nativeData, _dataLength, &error);
        if (vorbis == null)
        {
            MemoryUtility.Free(_nativeData);
            _nativeData = null;
            throw new AudioException($"Failed to open Vorbis stream (stb_vorbis error {error}).");
        }

        _vorbis = vorbis;
        _info = stb_vorbis_get_info(vorbis);
        Channel = _info.channels;
        SampleRate = (int)_info.sample_rate;
    }

    /// <inheritdoc/>
    protected override int ReadCore(Span<float> buffer)
    {
        stb_vorbis? vorbis = _vorbis;
        if (vorbis == null)
        {
            return 0;
        }

        fixed (float* ptr = buffer)
        {
            int frames = stb_vorbis_get_samples_float_interleaved(vorbis, _info.channels, ptr, buffer.Length);
            return frames < 0 ? 0 : frames;
        }
    }

    /// <inheritdoc/>
    protected override void ResetCore()
    {
        if (_vorbis != null)
        {
            stb_vorbis_seek_start(_vorbis);
        }
    }

    /// <inheritdoc/>
    protected override void Release()
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
            _nativeData = null;
        }

        _sourceStream?.Dispose();
        _sourceStream = null;
    }
}
