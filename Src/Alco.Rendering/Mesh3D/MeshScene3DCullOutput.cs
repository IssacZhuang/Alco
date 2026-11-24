using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Per-view GPU culling output buffers (<see cref="GpuDrawIndexedIndirectArgs"/> and compacted
/// visible slot indices) owned independently so a single <see cref="MeshScene3D"/> can be culled
/// for multiple views (such as main camera and shadow passes) within the same frame.
/// </summary>
public sealed unsafe class MeshScene3DCullOutput : AutoDisposable
{
    private readonly RenderingSystem _system;

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

    internal void EnsureCapacity(int requiredBuckets, int requiredSlots)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (requiredBuckets > _bucketCapacity)
        {
            int nextBuckets = _bucketCapacity;
            while (nextBuckets < requiredBuckets)
            {
                nextBuckets *= 2;
            }

            _drawArgsBuffer.Dispose();
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

            _visibleSlotsBuffer.Dispose();
            _visibleSlotsBuffer = _system.CreateBuffer((ulong)nextSlots * sizeof(uint));
            _slotCapacity = nextSlots;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _drawArgsBuffer.Dispose();
        _visibleSlotsBuffer.Dispose();
    }
}
