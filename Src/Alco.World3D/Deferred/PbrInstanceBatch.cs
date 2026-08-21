using System.Numerics;
using System.Runtime.InteropServices;

using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// One instanced draw segment: a material and mesh pair with a contiguous
/// instance range in the owning batch's buffer.
/// </summary>
internal readonly struct PbrInstanceSegment
{
    /// <summary>The material shared by all instances of the segment.</summary>
    public readonly Material Material;
    /// <summary>The mesh shared by all instances of the segment.</summary>
    public readonly Mesh Mesh;
    /// <summary>The first instance index of the segment in the instance buffer.</summary>
    public readonly uint Start;
    /// <summary>The number of instances of the segment.</summary>
    public readonly uint Count;

    /// <summary>Create a segment.</summary>
    /// <param name="material">The material shared by all instances of the segment.</param>
    /// <param name="mesh">The mesh shared by all instances of the segment.</param>
    /// <param name="start">The first instance index of the segment in the instance buffer.</param>
    /// <param name="count">The number of instances of the segment.</param>
    public PbrInstanceSegment(Material material, Mesh mesh, uint start, uint count)
    {
        Material = material;
        Mesh = mesh;
        Start = start;
        Count = count;
    }
}

/// <summary>
/// Accumulates per-instance draw data and groups it into instanced draw
/// segments for the G-buffer / CSM shadow / RSM passes. Consecutive instances
/// that share a material and mesh merge into one segment;
/// <see cref="Flush"/> uploads the CPU array into a single GPU storage
/// buffer. The GPU buffer object stays identical between rebuilds unless the
/// required capacity grows, so render bundles recorded against it stay valid
/// until the next rebuild.
/// </summary>
internal sealed class PbrInstanceBatch : AutoDisposable
{
    // Lower bound for recreated buffers so tiny scenes do not recreate the
    // buffer (and invalidate recorded bundles) on every small change.
    private static readonly uint MinCapacityBytes = 64 * (uint)sizeof(PbrInstanceData);

    private readonly ArrayBuffer<PbrInstanceData> _data = new();
    private readonly List<PbrInstanceSegment> _segments = new();
    private GraphicsBuffer? _buffer;

    /// <summary>
    /// The instance draw segments built by the last <see cref="Flush"/>. Empty
    /// when the batch holds no instances.
    /// </summary>
    public ReadOnlySpan<PbrInstanceSegment> Segments => CollectionsMarshal.AsSpan(_segments);

    /// <summary>
    /// The GPU instance storage buffer of the last <see cref="Flush"/>, or null
    /// when the batch has never held instances.
    /// </summary>
    public GraphicsBuffer? Buffer => _buffer;

    /// <summary>Clear all accumulated instances and segments (call before re-filling).</summary>
    public void BeginBatch()
    {
        _data.SetSize(0);
        _segments.Clear();
    }

    /// <summary>
    /// Append one instance. Consecutive instances sharing a material and mesh
    /// merge into the previous segment; other occurrences start a new segment.
    /// </summary>
    /// <param name="instance">The instance data to append.</param>
    /// <param name="material">The material the instance is drawn with.</param>
    /// <param name="mesh">The mesh the instance is drawn with.</param>
    /// <returns>The instance index assigned to the added instance.</returns>
    public uint AddInstance(in PbrInstanceData instance, Material material, Mesh mesh)
    {
        int index = _data.Length;
        _data.SetSize(index + 1);
        _data[index] = instance;

        if (_segments.Count > 0)
        {
            PbrInstanceSegment last = _segments[^1];
            if (ReferenceEquals(last.Material, material) && ReferenceEquals(last.Mesh, mesh) &&
                last.Start + last.Count == (uint)index)
            {
                _segments[^1] = new PbrInstanceSegment(material, mesh, last.Start, last.Count + 1);
                return (uint)index;
            }
        }

        _segments.Add(new PbrInstanceSegment(material, mesh, (uint)index, 1));
        return (uint)index;
    }

    /// <summary>
    /// Upload the accumulated instances into the GPU storage buffer (growing it
    /// when needed) so <see cref="Buffer"/> and <see cref="Segments"/> are ready
    /// for draw recording. Does nothing when the batch is empty.
    /// </summary>
    /// <param name="rendering">The rendering system used to create the GPU buffer.</param>
    /// <param name="bufferName">The name of the (re)created GPU buffer.</param>
    public void Flush(RenderingSystem rendering, string bufferName)
    {
        if (_data.Length == 0)
        {
            return;
        }

        uint requiredBytes = (uint)(_data.Length * sizeof(PbrInstanceData));
        if (_buffer == null || _buffer.Size < requiredBytes)
        {
            _buffer?.Dispose();
            uint capacityBytes = Math.Max(MinCapacityBytes, BitOperations.RoundUpToPowerOf2(requiredBytes));
            _buffer = rendering.CreateGraphicsBuffer(capacityBytes, bufferName);
        }

        _buffer.UpdateBuffer(_data.AsSpan());
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer?.Dispose();
            _buffer = null;
        }
    }
}
