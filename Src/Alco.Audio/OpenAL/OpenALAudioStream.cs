using System.Numerics;
using System.Runtime.CompilerServices;
using Alco;
using Silk.NET.OpenAL;

namespace Alco.Audio.OpenAL;

/// <summary>
/// OpenAL backend for <see cref="AudioStream"/>. Uses a source borrowed from the device pool and a
/// ring of streaming buffers queued with <c>alSourceQueueBuffers</c>; the device refills processed
/// buffers each frame via <see cref="Refill"/>.
/// </summary>
internal sealed unsafe class OpenALAudioStream : AudioStream, IOpenALSourceOwner
{
    private const int BufferCount = 3;

    private static readonly AL AL = AL.GetApi(true);
    private static readonly int AL_DIRECT_CHANNELS_SOFT = AL.GetEnumValue("AL_DIRECT_CHANNELS_SOFT");
    private static readonly int AL_SOURCE_SPATIALIZE_SOFT = AL.GetEnumValue("AL_SOURCE_SPATIALIZE_SOFT");
    private static readonly int AL_TRUE = AL.GetEnumValue("AL_TRUE");
    private static readonly int AL_FALSE = AL.GetEnumValue("AL_FALSE");

    private readonly OpenALDevice _device;

    // ~1 second of audio per buffer (frames per channel * channels).
    private readonly int _samplesPerBuffer;
    private readonly float* _fillBuffer;

    // Streaming buffer ring, allocated lazily on first Play.
    private readonly uint[] _buffers = new uint[BufferCount];
    private bool _buffersCreated;

    private uint _sourceId;
    private bool _primed; // distinguishes first Play (prime) from resume.

    // Shadow state, applied to the source after allocation (mirrors OpenALSource).
    private float _gain = 1f;
    private float _pitch = 1f;
    private bool _isSpatial;
    private Vector3 _position = Vector3.Zero;
    private bool _isLooping = true;

    private readonly BufferFormat _format;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenALAudioStream"/> class.
    /// </summary>
    /// <param name="device">The owning OpenAL device and source pool provider.</param>
    /// <param name="provider">The data provider that supplies PCM to this stream.</param>
    public OpenALAudioStream(OpenALDevice device, IAudioStreamDataProvider provider) : base(provider)
    {
        _device = device;
        _samplesPerBuffer = provider.Channel * provider.SampleRate;
        _fillBuffer = MemoryUtility.Alloc<float>(_samplesPerBuffer);
        _format = OpenALUtility.GetBufferFormat(provider.Channel);
    }

    /// <inheritdoc/>
    public override float Gain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gain;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _gain = value;
            UpdateHardwareGain();
        }
    }

    /// <inheritdoc/>
    public override float Pitch
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pitch;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _pitch = value;
            if (_sourceId != 0)
            {
                AL.SetSourceProperty(_sourceId, SourceFloat.Pitch, value);
            }
        }
    }

    /// <inheritdoc/>
    public override bool IsSpatial
    {
        get => _isSpatial;
        set
        {
            if (_isSpatial == value) return;
            _isSpatial = value;
            SetSpatialSetting(_isSpatial);
        }
    }

    /// <inheritdoc/>
    public override Vector3 Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _position;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _position = value;
            if (_sourceId != 0)
            {
                // Convert from engine LH to OpenAL RH by flipping Z.
                AL.SetSourceProperty(_sourceId, SourceVector3.Position, new Vector3(value.X, value.Y, -value.Z));
            }
        }
    }

    /// <inheritdoc/>
    public override bool IsLooping
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isLooping;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _isLooping = value;
    }

    protected override void OnBusVolumeChanged()
    {
        UpdateHardwareGain();
    }

    private void UpdateHardwareGain()
    {
        if (_sourceId != 0)
        {
            float busVolume = Bus?.EffectiveVolume ?? 1f;
            AL.SetSourceProperty(_sourceId, SourceFloat.Gain, _gain * busVolume);
        }
    }

    protected override void PlayCore()
    {
        if (_sourceId == 0)
        {
            _sourceId = _device.AllocateSource(this);
        }

        if (_sourceId == 0) return;

        RestoreState();

        if (!_primed)
        {
            PrimeBuffers();
            _primed = true;
        }

        State = AudioStreamState.Playing;
        AL.SourcePlay(_sourceId);
    }

    protected override void PauseCore()
    {
        if (_sourceId == 0 || State != AudioStreamState.Playing) return;

        AL.SourcePause(_sourceId);
        State = AudioStreamState.Paused;
    }

    protected override void StopCore()
    {
        ReleaseSource();
        _primed = false;
        State = AudioStreamState.Stopped;
        Provider.Reset();
    }

    /// <inheritdoc/>
    /// <remarks>Called by the <c>SourcePool</c> when this stream's borrowed source is reclaimed.</remarks>
    void IOpenALSourceOwner.OnSourceReclaimed()
    {
        // The pool has handed our source id to another borrower. Forget it and stop logically;
        // the next Play() will borrow a fresh source.
        if (_sourceId != 0)
        {
            AL.SourceStop(_sourceId);
            _sourceId = 0;
        }

        _primed = false;
        State = AudioStreamState.Stopped;
    }

    /// <summary>
    /// Stops and returns the borrowed source to the pool, clearing the queue. Does not reset state
    /// or the provider (callers do that as appropriate).
    /// </summary>
    private void ReleaseSource()
    {
        if (_sourceId == 0) return;

        AL.SourceStop(_sourceId);
        DrainQueuedBuffers();
        _device.FreeSource(this, _sourceId);
        _sourceId = 0;
    }

    /// <summary>
    /// Called by the device each frame to unqueue processed buffers and refill/requeue them.
    /// Also detects and recovers from buffer underrun.
    /// </summary>
    internal void Refill()
    {
        if (_sourceId == 0 || State != AudioStreamState.Playing) return;

        // Unqueue and refill all processed buffers.
        AL.GetSourceProperty(_sourceId, GetSourceInteger.BuffersProcessed, out int processed);
        for (int i = 0; i < processed; i++)
        {
            uint buffer = 0;
            AL.SourceUnqueueBuffers(_sourceId, 1, &buffer);
            if (!TryFillAndQueue(buffer))
            {
                // Provider exhausted without looping: finish playback.
                FinishPlayback();
                return;
            }
        }

        DetectUnderrun();
    }

    private void PrimeBuffers()
    {
        EnsureBuffersCreated();
        for (int i = 0; i < BufferCount; i++)
        {
            if (!TryFillAndQueue(_buffers[i]))
            {
                // Track shorter than the ring; remaining buffers stay empty, playback ends naturally.
                break;
            }
        }
    }

    private bool TryFillAndQueue(uint buffer)
    {
        int frames = FillBufferFromProvider();
        if (frames <= 0)
        {
            return false;
        }

        int floats = frames * Provider.Channel;
        int sizeBytes = floats * sizeof(float);
        AL.BufferData(buffer, _format, _fillBuffer, sizeBytes, Provider.SampleRate);

        uint handle = buffer;
        AL.SourceQueueBuffers(_sourceId, 1, &handle);
        return true;
    }

    private int FillBufferFromProvider()
    {
        // Read into the full fill span, then report how many frames were produced.
        Span<float> span = new(_fillBuffer, _samplesPerBuffer);
        int frames = Provider.ReadSamples(span);
        if (frames <= 0)
        {
            if (_isLooping)
            {
                Provider.Reset();
                frames = Provider.ReadSamples(span);
            }
        }

        return frames;
    }

    private void DetectUnderrun()
    {
        // The source may have stopped (e.g. after a frame stall drained the queue). If we are
        // logically playing but OpenAL is not, resume: re-prime when the queue is empty, or just
        // restart playback of the still-queued buffers otherwise.
        AL.GetSourceProperty(_sourceId, GetSourceInteger.SourceState, out int state);
        if (state == (int)SourceState.Playing) return;

        AL.GetSourceProperty(_sourceId, GetSourceInteger.BuffersQueued, out int queued);
        if (queued == 0)
        {
            DrainQueuedBuffers();
            PrimeBuffers();
        }

        AL.SourcePlay(_sourceId);
    }

    private void FinishPlayback()
    {
        ReleaseSource();
        _primed = false;
        State = AudioStreamState.Stopped;
        Provider.Reset();
    }

    private void DrainQueuedBuffers()
    {
        if (_sourceId == 0) return;

        AL.GetSourceProperty(_sourceId, GetSourceInteger.BuffersQueued, out int queued);
        for (int i = 0; i < queued; i++)
        {
            uint buffer = 0;
            AL.SourceUnqueueBuffers(_sourceId, 1, &buffer);
        }
    }

    private void EnsureBuffersCreated()
    {
        if (_buffersCreated) return;

        fixed (uint* ptr = _buffers)
        {
            AL.GenBuffers(BufferCount, ptr);
        }

        _buffersCreated = true;
    }

    private void RestoreState()
    {
        if (_sourceId == 0) return;

        UpdateHardwareGain();
        AL.SetSourceProperty(_sourceId, SourceFloat.Pitch, _pitch);
        AL.SetSourceProperty(_sourceId, SourceVector3.Position, new Vector3(_position.X, _position.Y, -_position.Z));
        SetSpatialSetting(_isSpatial);
    }

    private void SetSpatialSetting(bool isSpatial)
    {
        if (_sourceId == 0) return;

        if (_device.SupportsSpatialize)
        {
            int value = isSpatial ? AL_TRUE : AL_FALSE;
            AL.SetSourceProperty(_sourceId, (SourceInteger)AL_SOURCE_SPATIALIZE_SOFT, value);
        }

        AL.SetSourceProperty(_sourceId, SourceBoolean.SourceRelative, !isSpatial);
        if (_device.SupportsDirectChannels)
        {
            AL.SetSourceProperty(_sourceId, (SourceBoolean)AL_DIRECT_CHANNELS_SOFT, !isSpatial);
        }
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseSource();

        if (_buffersCreated)
        {
            fixed (uint* ptr = _buffers)
            {
                AL.DeleteBuffers(BufferCount, ptr);
            }

            _buffersCreated = false;
        }

        if (_fillBuffer != null)
        {
            MemoryUtility.Free(_fillBuffer);
        }

        Provider.Dispose();
    }
}
