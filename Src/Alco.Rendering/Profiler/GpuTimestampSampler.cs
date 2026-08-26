using System.Diagnostics;
using Alco;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A throttled GPU timestamp sampler: timestamps are recorded, resolved, and read
/// back only once per interval (default 1s) — no GPU timestamp work in between — and
/// the readback always returns the previous sample (≥ one interval old), so its GPU
/// work is guaranteed complete. Per frame: gate on <see cref="ShouldRecord"/>, record
/// into <see cref="QuerySet"/>, resolve into <see cref="ResolveBuffer"/>, then call
/// <see cref="EndSample"/>; <see cref="TryReadback"/> returns the previous sample's
/// slots (indexed by logical slot) or null.
/// <para>
/// Two resolve layouts: contiguous, slots packed tightly and resolved together via
/// <see cref="ResolveAll"/> once every slot has been written; padded-pair, each slot
/// pair resolved into its own stride-aligned region (<see cref="PairStrideBytes"/>)
/// via <see cref="ResolvePair(RenderPassScope, int)"/> right after it is written.
/// Resolving slots that were not yet written (mid-frame, partial set) can lose the
/// device on some backends.
/// </para>
/// </summary>
public sealed class GpuTimestampSampler : AutoDisposable
{
    /// <summary>
    /// The stride of a slot pair in the padded-pair resolve layout: query-resolve
    /// destination offsets are aligned per backend (256 bytes on Vulkan), so this
    /// stride covers the strictest known backend.
    /// See <see cref="ResolvePair(RenderPassScope, int)"/>.
    /// </summary>
    public const int PairStrideBytes = 256;

    private readonly GPUDevice _device;
    private readonly GPUTimestampQuerySet _querySet;
    private readonly GPUBuffer _resolveBuffer;
    private readonly ulong[] _stagingArray;
    private readonly ulong[]? _paddedReadbackArray;
    private readonly int _pairStrideBytes;
    private readonly double _intervalSeconds;
    private readonly Stopwatch _timer = new();
    private bool _hasPending;
    private bool _recordThisFrame;

    /// <summary>The shared timestamp query set.</summary>
    public GPUTimestampQuerySet QuerySet => _querySet;

    /// <summary>The resolve buffer for the current sample frame.</summary>
    public GPUBuffer ResolveBuffer => _resolveBuffer;

    /// <summary>The number of timestamp query slots.</summary>
    public int SlotCount => _stagingArray.Length;

    /// <summary>Whether consecutive slot pairs resolve into padded, stride-aligned
    /// buffer regions (<see cref="ResolvePair(RenderPassScope, int)"/>); when
    /// false, the slots pack contiguously (<see cref="ResolveAll"/>).</summary>
    public bool UsesPaddedPairs => _pairStrideBytes > 0;

    /// <summary>Whether the device supports in-pass timestamp writes.</summary>
    public bool SupportsInPassTimestamps => _device.TimestampQueryInsidePassesSupported;

    /// <summary>
    /// True when this frame is a sample frame (the interval has elapsed). Once
    /// true, stays true for all callers within the same frame until
    /// <see cref="EndSample"/> resets it. Gate all timestamp recording and
    /// resolving on this property.
    /// </summary>
    public bool ShouldRecord
    {
        get
        {
            if (!_recordThisFrame && _timer.Elapsed.TotalSeconds >= _intervalSeconds)
            {
                _recordThisFrame = true;
            }
            return _recordThisFrame;
        }
    }

    /// <summary>
    /// Create a throttled GPU timestamp sampler with the contiguous resolve layout.
    /// </summary>
    /// <param name="device">The GPU device (must support timestamp queries).</param>
    /// <param name="slotCount">The number of timestamp query slots.</param>
    /// <param name="name">A diagnostic name used for GPU resource labels.</param>
    /// <param name="intervalSeconds">The minimum time between samples (default 1s).</param>
    public GpuTimestampSampler(GPUDevice device, int slotCount, string name, double intervalSeconds = 1.0)
        : this(device, slotCount, name, pairStrideBytes: 0, intervalSeconds)
    {
    }

    /// <summary>
    /// Create a throttled GPU timestamp sampler with an explicit resolve layout.
    /// </summary>
    /// <param name="device">The GPU device (must support timestamp queries).</param>
    /// <param name="slotCount">The number of timestamp query slots.</param>
    /// <param name="name">A diagnostic name used for GPU resource labels.</param>
    /// <param name="pairStrideBytes">When positive, the padded-pair layout: each
    /// consecutive slot pair resolves into its own region of this size (must be
    /// at least 16 and aligned to the backend's query-resolve buffer alignment —
    /// use <see cref="PairStrideBytes"/>). Zero selects the contiguous layout.</param>
    /// <param name="intervalSeconds">The minimum time between samples (default 1s).</param>
    public GpuTimestampSampler(GPUDevice device, int slotCount, string name, int pairStrideBytes, double intervalSeconds = 1.0)
    {
        _device = device;
        _intervalSeconds = intervalSeconds;
        _pairStrideBytes = pairStrideBytes;
        _querySet = device.CreateTimestampQuerySet((uint)slotCount, name + "_timestamps");
        uint resolveSize = pairStrideBytes > 0
            ? (uint)(slotCount / 2 * pairStrideBytes)
            : sizeof(ulong) * (uint)slotCount;
        _resolveBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = BufferUsage.QueryResolve | BufferUsage.CopySrc,
            Size = resolveSize,
            Name = name + "_resolve",
        });
        _stagingArray = new ulong[slotCount];
        if (pairStrideBytes > 0)
        {
            _paddedReadbackArray = new ulong[resolveSize / sizeof(ulong)];
        }
        _timer.Start();
    }

    /// <summary>
    /// Read back timestamps from the previous sample. Returns null if this isn't
    /// a sample frame or no previous data exists. Call on sample frames
    /// (<see cref="ShouldRecord"/> == true); the returned array is indexed by
    /// logical slot regardless of the resolve layout.
    /// </summary>
    /// <returns>The timestamp array, or null.</returns>
    public ulong[]? TryReadback()
    {
        if (!_hasPending || !_recordThisFrame)
        {
            return null;
        }

        if (_paddedReadbackArray != null)
        {
            // Compaction: pair i lives at buffer offset i * stride, but consumers
            // index the staging array by logical slot.
            _device.ReadBuffer(_resolveBuffer, _paddedReadbackArray);
            int strideUlongs = _pairStrideBytes / sizeof(ulong);
            for (int i = 0; i < SlotCount / 2; i++)
            {
                _stagingArray[i * 2] = _paddedReadbackArray[i * strideUlongs];
                _stagingArray[i * 2 + 1] = _paddedReadbackArray[i * strideUlongs + 1];
            }
        }
        else
        {
            _device.ReadBuffer(_resolveBuffer, _stagingArray);
        }
        _hasPending = false;
        return _stagingArray;
    }

    /// <summary>
    /// Mark the current sample frame as complete (timestamps recorded + resolved).
    /// Resets the sample flag and restarts the interval timer. Call once per
    /// sample frame after all resolves are done.
    /// </summary>
    public void EndSample()
    {
        if (_recordThisFrame)
        {
            _hasPending = true;
            _recordThisFrame = false;
            _timer.Restart();
        }
    }

    /// <summary>
    /// Compute the duration in milliseconds between two timestamp slots.
    /// </summary>
    public double DeltaMilliseconds(ulong[] timestamps, int beginSlot, int endSlot)
    {
        if (endSlot >= timestamps.Length)
        {
            return 0.0;
        }
        if (timestamps[endSlot] >= timestamps[beginSlot] && timestamps[endSlot] > 0)
        {
            return (timestamps[endSlot] - timestamps[beginSlot])
                * _device.TimestampPeriodNanoseconds / 1_000_000.0;
        }
        return 0.0;
    }

    /// <summary>
    /// Schedule (at <paramref name="pass"/> close) a resolve of the sampler's
    /// whole query set into the resolve buffer at offset 0 — the layout
    /// <see cref="DeltaMilliseconds"/> reads back. Contiguous layout only, and
    /// only valid once every slot has been written this frame. A repeated
    /// identical resolve is harmless.
    /// </summary>
    /// <param name="pass">An open pass scope; the resolve runs when it closes.</param>
    public void ResolveAll(RenderPassScope pass)
    {
        pass.ResolveTimestampsOnEnd(QuerySet, 0, (uint)SlotCount, ResolveBuffer);
    }

    /// <summary>
    /// Record a resolve of the sampler's whole query set into its resolve buffer
    /// at offset 0. Contiguous layout only; every slot must have been written
    /// before the resolve executes. Call outside any render/compute pass while
    /// the command buffer is recording.
    /// </summary>
    /// <param name="command">The recording command buffer.</param>
    public void ResolveAll(GPUCommandBuffer command)
    {
        command.ResolveTimestamps(QuerySet, 0, (uint)SlotCount, ResolveBuffer);
    }

    /// <summary>
    /// Schedule (at <paramref name="pass"/> close) a resolve of one slot pair
    /// into its own stride-aligned region of the resolve buffer, so passes
    /// sharing the sampler never overwrite each other's timings. Padded-pair
    /// layout only; call on the pass that writes the pair's end timestamp, after
    /// both of its slots have been written.
    /// </summary>
    /// <param name="pass">An open pass scope; the resolve runs when it closes.</param>
    /// <param name="pairIndex">The pair to resolve; pair <c>i</c> covers slots
    /// <c>2i</c> and <c>2i+1</c>.</param>
    public void ResolvePair(RenderPassScope pass, int pairIndex)
    {
        pass.ResolveTimestampsOnEnd(QuerySet, (uint)(pairIndex * 2), 2, ResolveBuffer,
            (ulong)(pairIndex * _pairStrideBytes));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _querySet.Dispose();
            _resolveBuffer.Dispose();
        }
    }
}
