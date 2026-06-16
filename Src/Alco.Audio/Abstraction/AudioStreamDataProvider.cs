using System.Threading;
using Alco;

namespace Alco.Audio;

/// <summary>
/// An optional base class for <see cref="IAudioStreamDataProvider"/> implementations that opens
/// lazily on a thread-pool thread and decodes each PCM chunk ahead of demand on a thread-pool
/// thread, so neither file I/O nor decoding blocks the main thread.
/// </summary>
/// <remarks>
/// <para>
/// A subclass implements three synchronous methods (<see cref="OpenCore"/>,
/// <see cref="ReadCore"/>, <see cref="ResetCore"/>) and inherits the threading machinery: a
/// reusable open task (Phase 1) and a reusable prefetch-decode task with a 2-slot native float
/// ring (Phase 2). The subclass's three methods all run on the pool thread; the base class
/// guarantees they never run concurrently with each other, so no locking is required inside them.
/// </para>
/// <para>
/// Providers that do not want this machinery may implement <see cref="IAudioStreamDataProvider"/>
/// directly (implementing <see cref="IAudioStreamDataProvider.WaitForOpen"/> as a no-op and
/// decoding synchronously on the caller thread).
/// </para>
/// <para>
/// <b>Threading model.</b> <see cref="WaitForOpen"/>, <see cref="ReadSamples"/>,
/// <see cref="Reset"/>, and <see cref="Dispose"/> are called only from the main (owner) thread.
/// <see cref="OpenCore"/>, <see cref="ReadCore"/>, and <see cref="ResetCore"/> run only on the
/// pool thread, strictly one at a time (the decode task is single-shot and never overlaps a run).
/// Because the pool thread holds the only decoder access, subclass methods need no locking.
/// </para>
/// <para>
/// <b>Prefetch model.</b> Two slots are kept ready: while the main thread consumes slot N, the pool
/// thread decodes the previous slot. <see cref="ReadSamples"/> returns the ready slot immediately
/// (a <see cref="Span{T}"/> copy) and hands the consumed slot back for refill. Decodes are
/// serialized (the decoder is not thread-safe); the 3-buffer OpenAL ring provides ample slack so
/// the serialized refill never blocks the main thread in practice.
/// </para>
/// </remarks>
public abstract unsafe class AudioStreamDataProvider : IAudioStreamDataProvider
{
    private const int SlotCount = 2;
    private const int NoSlot = -1;

    // Phase 1: async open. Owned by this provider; disposed in Dispose.
    private readonly OpenTask _openTask;

    // Phase 2: prefetch decode, re-run per chunk. Owned by this provider; disposed in Dispose.
    private readonly DecodeTask _decodeTask;

    // One float ring per slot, allocated after OpenCore sets the format.
    private readonly float*[] _slots = new float*[SlotCount];
    private readonly VolatileInt[] _framesReady = new VolatileInt[SlotCount];
    private int _slotSize; // floats per slot (Channel * SampleRate).

    // Which slot the main thread consumes next; the other is being (re)filled by the pool.
    private int _consumeIndex;

    // The slot the pool thread fills on its next run; set by the main thread before RunCore().
    private int _requestedSlot = NoSlot;

    // True once the decoder has reported EOF (ReadCore returned 0) and no Reset has happened.
    private volatile bool _eof;

    private volatile int _disposed;

    /// <summary>
    /// Number of channels (1 = mono, 2 = stereo). Set by <see cref="OpenCore"/>; <c>0</c> until
    /// <see cref="WaitForOpen"/> has returned.
    /// </summary>
    public int Channel { get; protected set; }

    /// <summary>
    /// Sample rate in Hz. Set by <see cref="OpenCore"/>; <c>0</c> until <see cref="WaitForOpen"/>
    /// has returned.
    /// </summary>
    public int SampleRate { get; protected set; }

    /// <summary>
    /// Initializes a new instance and queues the background open task. Construction returns
    /// immediately; the provider is not ready until <see cref="WaitForOpen"/> returns.
    /// </summary>
    protected AudioStreamDataProvider()
    {
        _openTask = new OpenTask(this);
        _decodeTask = new DecodeTask(this);
        _openTask.Run();
    }

    /// <summary>
    /// Loads and opens the underlying audio source and sets <see cref="Channel"/> and
    /// <see cref="SampleRate"/>. Runs on a pool thread, exactly once, before any decode.
    /// </summary>
    /// <exception cref="AudioException">Thrown if the source cannot be opened.</exception>
    protected abstract void OpenCore();

    /// <summary>
    /// Decodes up to <paramref name="buffer"/>.Length interleaved float samples into
    /// <paramref name="buffer"/>. Runs on a pool thread; never concurrent with
    /// <see cref="OpenCore"/> or <see cref="ResetCore"/>.
    /// </summary>
    /// <param name="buffer">Destination span, written as interleaved float samples.</param>
    /// <returns>Number of FRAMES decoded (samples per channel). 0 at end of stream.</returns>
    protected abstract int ReadCore(Span<float> buffer);

    /// <summary>Rewinds the underlying decoder to its beginning. Runs on a pool thread.</summary>
    protected abstract void ResetCore();

    /// <summary>
    /// Releases subclass-owned unmanaged resources (e.g. the decoder and source buffer). Called
    /// after the pool thread has been stopped, so the decoder is safe to touch. Must be idempotent.
    /// </summary>
    protected virtual void Release()
    {
    }

    /// <inheritdoc/>
    /// <remarks>Blocks until <see cref="OpenCore"/> has completed and the format is valid.</remarks>
    public void WaitForOpen()
    {
        _openTask.Wait();
    }

    /// <inheritdoc/>
    public int ReadSamples(Span<float> buffer)
    {
        WaitForOpen();

        if (_eof)
        {
            return 0;
        }

        int index = _consumeIndex;

        // The slot we are about to consume was prefetched while we consumed the previous one. If it
        // is not ready yet (decode still in flight) this is correct backpressure.
        if (_framesReady[index].Value == 0)
        {
            _decodeTask.Wait();
        }

        int frames = _framesReady[index].Value;
        if (frames <= 0)
        {
            // The decode produced nothing: end of stream.
            _eof = true;
            return 0;
        }

        int floats = Math.Min(frames * Channel, buffer.Length);
        float* src = _slots[index];
        for (int i = 0; i < floats; i++)
        {
            buffer[i] = src[i];
        }

        // Move consumption to the other slot and refill this one on the pool thread.
        _consumeIndex = index ^ 1;
        RequestFill(index);

        return frames;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        WaitForOpen();

        // The decoder lives on the pool thread: stop any in-flight decode, rewind there, then
        // re-prime both slots from the start.
        _decodeTask.Wait();
        ResetCore();

        for (int i = 0; i < SlotCount; i++)
        {
            _framesReady[i].Value = 0;
        }

        _consumeIndex = 0;
        _eof = false;

        // Re-prime sequentially on the pool thread so slot 0 (consumed first) is ready first.
        FillSlot(0);
        FillSlot(1);
    }

    /// <summary>
    /// Blocks until both background tasks are idle, then releases the native slot buffers. Safe to
    /// call multiple times.
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
        _decodeTask.Wait();
        _openTask.Wait();

        // The pool thread is stopped; let the subclass release the decoder and its source buffer.
        Release();

        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i] != null)
            {
                MemoryUtility.Free(_slots[i]);
                _slots[i] = null;
            }
        }

        _openTask.Dispose();
        _decodeTask.Dispose();
    }

    /// <summary>
    /// Refills slot <paramref name="index"/> on the pool thread. <see cref="RunCore"/> waits for
    /// any in-flight decode first, so decodes are strictly serialized.
    /// </summary>
    private void RequestFill(int index)
    {
        _framesReady[index].Value = 0;
        _requestedSlot = index;
        _decodeTask.Run();
    }

    /// <summary>
    /// Called by the open task on the pool thread once <see cref="OpenCore"/> has set the format:
    /// allocates the slot buffers and primes both.
    /// </summary>
    private void OnOpened()
    {
        _slotSize = Channel * SampleRate;
        if (_slotSize <= 0)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            _slots[i] = MemoryUtility.Alloc<float>(_slotSize);
        }

        // Prime slot 0 (consumed first) then slot 1, sequentially on the pool thread.
        FillSlot(0);
        FillSlot(1);
    }

    /// <summary>Decodes one chunk into slot <paramref name="index"/>. Pool thread only.</summary>
    private void FillSlot(int index)
    {
        Span<float> span = new(_slots[index], _slotSize);
        _framesReady[index].Value = ReadCore(span);
    }

    ~AudioStreamDataProvider()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DisposeCore();
        }
    }

    /// <summary>Background open task: runs <see cref="OpenCore"/>, then <see cref="OnOpened"/>.</summary>
    private sealed class OpenTask : ReusableTask
    {
        private readonly AudioStreamDataProvider _owner;

        public OpenTask(AudioStreamDataProvider owner)
        {
            _owner = owner;
        }

        /// <summary>Starts the open task (waits for any prior run first).</summary>
        public void Run() => RunCore();

        protected override void ExecuteCore()
        {
            _owner.OpenCore();
            _owner.OnOpened();
        }
    }

    /// <summary>
    /// Background decode task. Each run fills <see cref="_requestedSlot"/> (one chunk). The task is
    /// reused (no per-chunk allocation); single-shot per run per <see cref="ReusableTask"/> semantics.
    /// </summary>
    private sealed class DecodeTask : ReusableTask
    {
        private readonly AudioStreamDataProvider _owner;

        public DecodeTask(AudioStreamDataProvider owner)
        {
            _owner = owner;
        }

        /// <summary>Starts a decode of <see cref="_requestedSlot"/> (waits for any prior run first).</summary>
        public void Run() => RunCore();

        protected override void ExecuteCore()
        {
            int index = _owner._requestedSlot;
            if (index < 0 || index >= SlotCount)
            {
                return;
            }

            _owner._requestedSlot = NoSlot;
            _owner.FillSlot(index);
        }
    }

    /// <summary>An int slot with volatile semantics, to keep slot frame counts race-free.</summary>
    private struct VolatileInt
    {
        private int _value;

        public int Value
        {
            get => Volatile.Read(ref _value);
            set => Volatile.Write(ref _value, value);
        }
    }
}
