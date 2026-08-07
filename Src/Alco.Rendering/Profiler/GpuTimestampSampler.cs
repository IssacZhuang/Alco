using System.Diagnostics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A throttled GPU timestamp sampler that records timestamps, resolves, and
/// readbacks only once per configurable interval (default 0.5s). Between samples,
/// zero GPU timestamp work is performed — no command-buffer overhead, no CPU
/// readback stalls. The readback always reads data from the previous sample
/// (≥ interval seconds ago), so the GPU work is guaranteed complete.
/// <para>
/// Call <see cref="ShouldRecord"/> at the start of each frame to check whether
/// this is a sample frame. If true, record timestamps into <see cref="QuerySet"/>,
/// resolve into <see cref="ResolveBuffer"/>, then call <see cref="EndSample"/>.
/// Call <see cref="TryReadback"/> to get the previous sample's timestamps (returns
/// null if none are available or this isn't a sample frame).
/// </para>
/// </summary>
public sealed class GpuTimestampSampler : IDisposable
{
    private readonly GPUDevice _device;
    private readonly GPUTimestampQuerySet _querySet;
    private readonly GPUBuffer _resolveBuffer;
    private readonly ulong[] _stagingArray;
    private readonly double _intervalSeconds;
    private readonly Stopwatch _timer = new();
    private bool _hasPending;
    private bool _recordThisFrame;

    /// <summary>The shared timestamp query set.</summary>
    public GPUTimestampQuerySet QuerySet => _querySet;

    /// <summary>The resolve buffer for the current sample frame.</summary>
    public GPUBuffer ResolveBuffer => _resolveBuffer;

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
    /// Create a throttled GPU timestamp sampler.
    /// </summary>
    /// <param name="device">The GPU device (must support timestamp queries).</param>
    /// <param name="slotCount">The number of timestamp query slots.</param>
    /// <param name="name">A diagnostic name used for GPU resource labels.</param>
    /// <param name="intervalSeconds">The minimum time between samples (default 0.5s).</param>
    public GpuTimestampSampler(GPUDevice device, int slotCount, string name, double intervalSeconds = 0.5)
    {
        _device = device;
        _intervalSeconds = intervalSeconds;
        _querySet = device.CreateTimestampQuerySet((uint)slotCount, name + "_timestamps");
        _resolveBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = BufferUsage.QueryResolve | BufferUsage.CopySrc,
            Size = sizeof(ulong) * (uint)slotCount,
            Name = name + "_resolve",
        });
        _stagingArray = new ulong[slotCount];
        _timer.Start();
    }

    /// <summary>
    /// Read back timestamps from the previous sample. Returns null if this isn't
    /// a sample frame or no previous data exists. Call on sample frames
    /// (<see cref="ShouldRecord"/> == true) before recording new timestamps.
    /// </summary>
    /// <returns>The timestamp array, or null.</returns>
    public ulong[]? TryReadback()
    {
        if (!_hasPending || !_recordThisFrame)
        {
            return null;
        }

        _device.ReadBuffer(_resolveBuffer, _stagingArray);
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

    /// <inheritdoc />
    public void Dispose()
    {
        _querySet.Dispose();
        _resolveBuffer.Dispose();
    }
}
