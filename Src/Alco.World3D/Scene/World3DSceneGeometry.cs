using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private readonly LinkedList<PendingLod> _pendingLods = new();
    private readonly List<RecordedLod> _recordedLods = [];

    /// <summary>Queues a copy of a LOD's vertices and 32-bit triangle indices.</summary>
    /// <param name="handle">The geometry receiving this LOD.</param>
    /// <param name="lod">The zero-based LOD index.</param>
    /// <param name="vertices">The PBR vertices, all within the registered geometry bounds.</param>
    /// <param name="indices">The geometry-local triangle indices.</param>
    /// <param name="minimumDistance">The nonnegative camera distance to the instance bound's center at which this LOD qualifies.</param>
    /// <returns>Whether the LOD was valid, not already pending, and had storage available.</returns>
    public bool TryQueueGeometryLod(WorldGeometryHandle handle, int lod, ReadOnlySpan<VertexPBR> vertices,
        ReadOnlySpan<uint> indices, float minimumDistance = 0)
    {
        BeginEdit();
        if (!TryGetGeometry(handle, out GeometryEntry? geometry) || !geometry.CanQueue(lod, minimumDistance) ||
            vertices.IsEmpty || indices.IsEmpty || indices.Length % 3 != 0)
            return false;

        Vector3 min = vertices[0].Position;
        Vector3 max = min;
        for (int i = 0; i < vertices.Length; ++i)
        {
            Vector3 v = vertices[i].Position;
            if (!Contains(geometry.LocalMin, geometry.LocalMax, v))
                return false;
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        for (int i = 0; i < indices.Length; ++i)
        {
            if (indices[i] >= (uint)vertices.Length)
                return false;
        }

        ulong vertexBytes = (ulong)vertices.Length * (uint)VertexPBR.SizeInBytes;
        ulong totalBytes = vertexBytes + (ulong)indices.Length * sizeof(uint);
        if (totalBytes > int.MaxValue || !_gpu.TryAllocateGeometry(totalBytes, out GeometryAllocation allocation))
            return false;
        try
        {
            byte[] data = GC.AllocateUninitializedArray<byte>((int)totalBytes);
            MemoryMarshal.AsBytes(vertices).CopyTo(data);
            MemoryMarshal.AsBytes(indices).CopyTo(data.AsSpan((int)vertexBytes));
            QueueLod(handle.Slot, geometry, lod, allocation, (uint)vertices.Length, (uint)indices.Length,
                minimumDistance, min, max, data, null);
        }
        catch
        {
            _gpu.ReleaseGeometry(allocation);
            throw;
        }
        return true;
    }

    /// <summary>Queues a LOD from existing GPU geometry, taking ownership of the source on success.</summary>
    /// <param name="handle">The geometry receiving this LOD.</param>
    /// <param name="lod">The zero-based LOD index.</param>
    /// <param name="source">Immutable, synchronized <see cref="VertexPBR"/> and UInt32 buffers from this scene's rendering system.</param>
    /// <param name="minimumDistance">The nonnegative camera distance to the instance bound's center at which this LOD qualifies.</param>
    /// <returns>Whether the LOD was accepted and ownership transferred.</returns>
    public bool TryQueueGeometryLod(WorldGeometryHandle handle, int lod, ModelGeometry source, float minimumDistance = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        BeginEdit();
        if (!TryGetGeometry(handle, out GeometryEntry? geometry) || !geometry.CanQueue(lod, minimumDistance) ||
            source.IsDisposed || source.VertexBuffer.IsDisposed || source.IndexBuffer.IsDisposed || source.IndexType != IndexType.UInt32 ||
            source.VertexCount == 0 || source.IndexCount == 0 || source.IndexCount % 3 != 0 ||
            !IsBound(source.LocalBoundsMin, source.LocalBoundsMax) ||
            !Contains(geometry.LocalMin, geometry.LocalMax, source.LocalBoundsMin) ||
            !Contains(geometry.LocalMin, geometry.LocalMax, source.LocalBoundsMax))
            return false;
        ulong vertexBytes = (ulong)source.VertexCount * (uint)VertexPBR.SizeInBytes;
        ulong indexBytes = (ulong)source.IndexCount * sizeof(uint);
        if (vertexBytes != source.VertexBuffer.SizeBytes || indexBytes != source.IndexBuffer.SizeBytes ||
            vertexBytes + indexBytes > uint.MaxValue || !_gpu.TryAllocateGeometry(vertexBytes + indexBytes, out GeometryAllocation allocation))
            return false;
        try
        {
            QueueLod(handle.Slot, geometry, lod, allocation, source.VertexCount, source.IndexCount,
                minimumDistance, source.LocalBoundsMin, source.LocalBoundsMax, null, source);
        }
        catch
        {
            _gpu.ReleaseGeometry(allocation);
            throw;
        }
        return true;
    }

    /// <summary>Reports whether a LOD was fully uploaded and published on the scene's submission stream.</summary>
    /// <param name="handle">The geometry to query.</param>
    /// <param name="lod">The zero-based LOD index.</param>
    /// <returns>Whether a published LOD is available, including while a replacement is pending.</returns>
    public bool IsGeometryLodResident(WorldGeometryHandle handle, int lod)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReconcileFrame();
        return (uint)lod < MaxGeometryLods && TryGetGeometry(handle, out GeometryEntry? geometry) && geometry.Resident[lod].HasValue;
    }

    private void QueueLod(int slot, GeometryEntry geometry, int lod, in GeometryAllocation allocation,
        uint vertexCount, uint indexCount, float minimumDistance, Vector3 min, Vector3 max, byte[]? data, ModelGeometry? source)
    {
        uint vertexOffset = checked((uint)allocation.Offset);
        uint indexOffset = checked(vertexOffset + vertexCount * (uint)VertexPBR.SizeInBytes);
        GeometryLodGpu gpu = new(allocation.PageId, vertexOffset, indexOffset, vertexCount, indexCount,
            minimumDistance, new Vector4(min, 0), new Vector4(max, 0));
        PendingLod pending = new(geometry, slot, lod, new ResidentLod(allocation, gpu), data, source);
        geometry.Pending[lod] = _pendingLods.AddLast(pending);
    }

    private ulong PrepareGeometryCopies(RenderContext context)
    {
        ulong remaining = GeometryUploadBudgetBytes & ~3ul;
        ulong uploaded = 0;
        for (LinkedListNode<PendingLod>? node = _pendingLods.First; node != null && remaining != 0; node = node.Next)
        {
            PendingLod lod = node.Value;
            ulong end = lod.Uploaded + Math.Min(lod.ByteCount - lod.Uploaded, remaining);
            ulong size = end - lod.Uploaded;
            ResidentLod resident = lod.Replacement;
            BufferSlice destination = resident.Allocation.Buffer.Slice(resident.Allocation.Offset + lod.Uploaded, size);
            if (lod.Data != null)
                _gpu.AddCopy(context.UploadTransient<byte>(lod.Data.AsSpan((int)lod.Uploaded, (int)size)), destination);
            else
                StageModelGeometryCopy(lod, destination, end);
            _recordedLods.Add(new RecordedLod(node, end));
            if (end == lod.ByteCount)
            {
                _gpu.SetGeometryLod(lod.Slot, lod.Lod, resident.Gpu);
                StageDrawResidency(lod.Geometry, lod.Lod, resident);
            }
            uploaded += size;
            remaining -= size;
        }
        return uploaded;
    }

    private void StageModelGeometryCopy(PendingLod lod, BufferSlice destination, ulong end)
    {
        ModelGeometry source = lod.Source!;
        ulong start = lod.Uploaded;
        ulong vertexBytes = source.VertexBuffer.SizeBytes;
        if (start < vertexBytes)
        {
            ulong vertexEnd = Math.Min(end, vertexBytes);
            ulong bytes = vertexEnd - start;
            _gpu.AddCopy(source.VertexBuffer.Slice(start, bytes), destination.Slice(0, bytes));
        }
        if (end > vertexBytes)
        {
            ulong indexStart = Math.Max(vertexBytes, start);
            ulong bytes = end - indexStart;
            _gpu.AddCopy(source.IndexBuffer.Slice(indexStart - vertexBytes, bytes), destination.Slice(indexStart - start, bytes));
        }
    }

    private void CompleteGeometryCopies(bool submitted)
    {
        bool anyLodCompleted = false;
        foreach (RecordedLod recorded in _recordedLods)
        {
            PendingLod lod = recorded.Node.Value;
            if (!submitted)
            {
                if (recorded.End == lod.ByteCount)
                    _gpu.SetGeometryLod(lod.Slot, lod.Lod, lod.Geometry.Resident[lod.Lod]?.Gpu ?? default);
                continue;
            }
            lod.Uploaded = recorded.End;
            if (lod.Uploaded != lod.ByteCount)
                continue;
            if (lod.Geometry.Resident[lod.Lod] is ResidentLod old)
                _retireOnSubmit.Add(() => _gpu.ReleaseGeometry(old.Allocation));
            lod.Geometry.Resident[lod.Lod] = lod.Replacement;
            lod.Geometry.Pending[lod.Lod] = null;
            _pendingLods.Remove(recorded.Node);
            lod.Source?.Dispose();
            anyLodCompleted = true;
        }
        _recordedLods.Clear();
        if (anyLodCompleted)
            MarkShadowSceneDirty();
    }

    private void RemoveGeometryLods(GeometryEntry geometry)
    {
        for (int lod = 0; lod < MaxGeometryLods; ++lod)
        {
            if (geometry.Pending[lod] is LinkedListNode<PendingLod> node)
            {
                PendingLod pending = node.Value;
                _pendingLods.Remove(node);
                pending.Source?.Dispose();
                _retireOnSubmit.Add(() => _gpu.ReleaseGeometry(pending.Replacement.Allocation));
            }
            if (geometry.Resident[lod] is ResidentLod resident)
                _retireOnSubmit.Add(() => _gpu.ReleaseGeometry(resident.Allocation));
        }
    }

    private sealed class GeometryEntry(Vector3 localMin, Vector3 localMax)
    {
        public readonly Vector3 LocalMin = localMin;
        public readonly Vector3 LocalMax = localMax;
        public readonly ResidentLod?[] Resident = new ResidentLod?[MaxGeometryLods];
        public readonly LinkedListNode<PendingLod>?[] Pending = new LinkedListNode<PendingLod>?[MaxGeometryLods];
        public readonly HashSet<int> Instances = [];

        public bool CanQueue(int lod, float minimumDistance) =>
            (uint)lod < MaxGeometryLods && Pending[lod] == null && minimumDistance >= 0 && float.IsFinite(minimumDistance);
    }

    private readonly record struct ResidentLod(GeometryAllocation Allocation, GeometryLodGpu Gpu);

    private sealed class PendingLod(GeometryEntry geometry, int slot, int lod, ResidentLod replacement, byte[]? data, ModelGeometry? source)
    {
        public readonly GeometryEntry Geometry = geometry;
        public readonly int Slot = slot;
        public readonly int Lod = lod;
        public readonly ResidentLod Replacement = replacement;
        public readonly byte[]? Data = data;
        public readonly ModelGeometry? Source = source;
        public readonly ulong ByteCount = replacement.Gpu.IndexByteOffset - replacement.Gpu.VertexByteOffset + (ulong)replacement.Gpu.IndexCount * sizeof(uint);
        public ulong Uploaded;
    }

    private readonly record struct RecordedLod(LinkedListNode<PendingLod> Node, ulong End);
}
