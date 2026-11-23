using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Per-view GPU culling output buffers (<see cref="GpuDrawIndexedIndirectArgs"/> and compacted
/// visible slot indices) owned independently so a single <see cref="MeshScene3D"/> can be culled
/// for multiple views (such as main camera and shadow passes) within the same frame.
/// </summary>
public sealed unsafe class MeshScene3DCullOutput : AutoDisposable
{
    private readonly record struct RetiredBuffer(Alco.Graphics.Buffer Buffer, RenderContext? Context, ulong FrameId);

    private readonly RenderingSystem _system;
    private readonly List<RetiredBuffer> _retiredBuffers = new();

    private Alco.Graphics.Buffer _drawArgsBuffer;
    private Alco.Graphics.Buffer _visibleSlotsBuffer;
    private int _bucketCapacity;
    private int _slotCapacity;

    internal MeshScene3DCullOutput(RenderingSystem system, int initialBucketCapacity, int initialSlotCapacity)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _bucketCapacity = Math.Max(1, initialBucketCapacity);
        _slotCapacity = Math.Max(1, initialSlotCapacity);
        _drawArgsBuffer = system.CreateBuffer((ulong)_bucketCapacity * (ulong)sizeof(GpuDrawIndexedIndirectArgs));
        _visibleSlotsBuffer = system.CreateBuffer((ulong)_slotCapacity * sizeof(uint));
    }

    /// <summary>Gets the GPU buffer containing per-bucket <see cref="GpuDrawIndexedIndirectArgs"/> records.</summary>
    public Alco.Graphics.Buffer DrawArgsBuffer => _drawArgsBuffer;

    /// <summary>Gets the GPU buffer containing compacted visible pool slot indices.</summary>
    public Alco.Graphics.Buffer VisibleSlotsBuffer => _visibleSlotsBuffer;

    internal RenderContext? LastContext { get; private set; }

    internal ulong LastFrameId { get; private set; }

    internal void MarkRecorded(RenderContext context)
    {
        LastContext = context;
        LastFrameId = context.CurrentFrameId;
    }

    internal void EnsureCapacity(int requiredBuckets, int requiredSlots)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        ReclaimRetiredBuffers();

        if (requiredBuckets > _bucketCapacity)
        {
            int nextBuckets = _bucketCapacity;
            while (nextBuckets < requiredBuckets)
            {
                nextBuckets *= 2;
            }

            _retiredBuffers.Add(new RetiredBuffer(_drawArgsBuffer, LastContext, LastFrameId));
            _drawArgsBuffer = _system.CreateBuffer((ulong)nextBuckets * (ulong)sizeof(GpuDrawIndexedIndirectArgs));
            _bucketCapacity = nextBuckets;
        }

        if (requiredSlots > _slotCapacity)
        {
            int nextSlots = _slotCapacity;
            while (nextSlots < requiredSlots)
            {
                nextSlots *= 2;
            }

            _retiredBuffers.Add(new RetiredBuffer(_visibleSlotsBuffer, LastContext, LastFrameId));
            _visibleSlotsBuffer = _system.CreateBuffer((ulong)nextSlots * sizeof(uint));
            _slotCapacity = nextSlots;
        }
    }

    internal void ReclaimRetiredBuffers()
    {
        for (int i = _retiredBuffers.Count - 1; i >= 0; --i)
        {
            RetiredBuffer entry = _retiredBuffers[i];
            if (entry.Context != null && !entry.Context.IsFrameCompleted(entry.FrameId))
            {
                continue;
            }

            if (!entry.Buffer.IsDestroyed)
            {
                entry.Buffer.Destroy();
            }

            _retiredBuffers.RemoveAt(i);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        for (int i = 0; i < _retiredBuffers.Count; ++i)
        {
            if (!_retiredBuffers[i].Buffer.IsDestroyed)
            {
                _retiredBuffers[i].Buffer.Destroy();
            }
        }

        _retiredBuffers.Clear();
        if (!_drawArgsBuffer.IsDestroyed)
        {
            _drawArgsBuffer.Destroy();
        }

        if (!_visibleSlotsBuffer.IsDestroyed)
        {
            _visibleSlotsBuffer.Destroy();
        }
    }
}
