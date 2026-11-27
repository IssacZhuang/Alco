using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private readonly DrawLayout _drawLayout = new();
    private readonly HashSet<int> _dirtyDrawInstances = [];
    private readonly Dictionary<GeometryEntry, int> _stagedDrawLods = [];
    private readonly DrawKey[] _wantedDrawKeys = new DrawKey[MaxGeometryLods];

    private void StageDrawResidency(GeometryEntry geometry, int lod, in ResidentLod replacement)
    {
        _stagedDrawLods[geometry] = _stagedDrawLods.GetValueOrDefault(geometry) | (1 << lod);
        if (!ReferenceEquals(geometry.Resident[lod]?.Allocation.Buffer, replacement.Allocation.Buffer))
            _dirtyDrawInstances.UnionWith(geometry.Instances);
    }

    private void RefreshDrawLayout()
    {
        foreach (int slot in _dirtyDrawInstances)
        {
            InstanceEntry? instance = _instances.GetSlot(slot);
            int count = 0;
            if (instance != null && instance.Material.Route.AlphaMode != World3DAlphaMode.Blend)
            {
                DrawRoute route = GetDrawRoute(instance);
                GeometryEntry geometry = instance.Geometry;
                int staged = _stagedDrawLods.GetValueOrDefault(geometry);
                for (int lod = 0; lod < MaxGeometryLods; ++lod)
                {
                    ResidentLod? entry = (staged & (1 << lod)) != 0
                        ? geometry.Pending[lod]!.Value.Replacement : geometry.Resident[lod];
                    if (entry is not ResidentLod resident)
                        continue;
                    DrawKey key = new(route, resident.Allocation.Buffer, resident.Allocation.PageId);
                    if (!_wantedDrawKeys.AsSpan(0, count).Contains(key))
                        _wantedDrawKeys[count++] = key;
                }
            }
            _drawLayout.SetPages(slot, _wantedDrawKeys.AsSpan(0, count));
            Array.Clear(_wantedDrawKeys, 0, count);
        }
    }

    private static DrawRoute GetDrawRoute(InstanceEntry instance)
    {
        MaterialRoute material = instance.Material.Route;
        Vector3 scale = instance.Description.Transform.Scale;
        bool mirrored = (scale.X < 0f) ^ (scale.Y < 0f) ^ (scale.Z < 0f);
        CullMode cull = material.DoubleSided ? CullMode.None : mirrored ? CullMode.Clockwise : CullMode.CounterClockwise;
        return new DrawRoute(material.SurfaceModule, material.AlphaMode, cull);
    }

    private readonly record struct MaterialRoute(string? SurfaceModule, World3DAlphaMode AlphaMode, bool DoubleSided)
    {
        /// <summary>Extracts only the material fields that affect pipeline routing.</summary>
        public MaterialRoute(in World3DMaterialDesc desc) : this(desc.SurfaceModule, desc.AlphaMode, desc.DoubleSided) { }
    }

    private readonly record struct DrawRoute(string? SurfaceModule, World3DAlphaMode AlphaMode, CullMode Cull);
    private readonly record struct DrawKey(DrawRoute Route, GpuBuffer Page, uint PageId);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DrawCandidate(uint InstanceSlotPlusOne, uint PageId);

    private sealed class DrawLayout : IDisposable
    {
        private const int ChunkSize = 128;
        private const ulong CandidateBytes = 8;
        private readonly Dictionary<DrawKey, DrawBucket> _buckets = [];
        private readonly Dictionary<int, List<Placement>> _placements = [];
        private readonly Stack<DrawChunk> _freeChunks = [];
        private readonly List<DrawCandidate> _rows = [];
        private readonly HashSet<int> _dirtySlots = [];
        private readonly List<int> _orderedSlots = [];
        private GpuBuffer? _buffer;
        private GpuBuffer? _copySource;
        private int _submittedSlots;

        /// <summary>Gets the live batches and their fixed command ranges.</summary>
        public Dictionary<DrawKey, DrawBucket>.ValueCollection Buckets => _buckets.Values;

        /// <summary>Gets the staged candidate table for the current frame.</summary>
        public GpuBuffer Buffer => _buffer!;

        /// <summary>Gets the extent of the candidate table, including holes.</summary>
        public int SlotCount => _buckets.Count == 0 ? 0 : _rows.Count;

        /// <summary>Retains slots whose keys still match and updates only changed memberships.</summary>
        public void SetPages(int instanceSlot, ReadOnlySpan<DrawKey> desired)
        {
            if (!_placements.TryGetValue(instanceSlot, out List<Placement>? placements))
            {
                if (desired.IsEmpty)
                    return;
                _placements.Add(instanceSlot, placements = new List<Placement>(desired.Length));
            }
            for (int i = placements.Count - 1; i >= 0; --i)
            {
                Placement placement = placements[i];
                if (desired.Contains(placement.Bucket.Key))
                    continue;
                Remove(in placement);
                placements.RemoveAt(i);
            }
            if (desired.IsEmpty)
            {
                _placements.Remove(instanceSlot);
                return;
            }
            foreach (DrawKey key in desired)
            {
                bool exists = false;
                for (int i = 0; i < placements.Count && !exists; ++i)
                    exists = placements[i].Bucket.Key == key;
                if (!exists)
                    placements.Add(Add(instanceSlot, in key));
            }
        }

        /// <summary>Stages changed candidates, preserving the previous GPU prefix when growing capacity.</summary>
        public int Stage(RenderContext context, RenderingSystem system, GpuStore gpu)
        {
            int count = _rows.Count;
            if (count == 0 || (count == _submittedSlots && _dirtySlots.Count == 0))
                return 0;
            ulong bytes = (ulong)count * CandidateBytes;
            if (_buffer == null || _buffer.SizeBytes < bytes)
            {
                GpuBuffer next = system.CreateBuffer(Math.Max(bytes, (_buffer?.SizeBytes ?? 0) * 2), MemoryType.GpuOnly);
                if (_copySource == null && _submittedSlots != 0)
                    _copySource = _buffer;
                else
                    _buffer?.Dispose();
                _buffer = next;
            }
            if (count > _submittedSlots)
            {
                ReadOnlySpan<DrawCandidate> tail = CollectionsMarshal.AsSpan(_rows)[_submittedSlots..];
                gpu.AddCopy(context.UploadTransient(tail), _buffer.Slice((ulong)_submittedSlots * CandidateBytes, (ulong)tail.Length * CandidateBytes));
            }

            _orderedSlots.Clear();
            foreach (int slot in _dirtySlots)
            {
                if (slot < _submittedSlots)
                    _orderedSlots.Add(slot);
            }
            _orderedSlots.Sort();
            int cleanStart = 0;
            if (_orderedSlots.Count != 0)
            {
                Span<DrawCandidate> patches = context.AllocateTransient<DrawCandidate>(_orderedSlots.Count, out BufferSlice source);
                for (int i = 0; i < patches.Length; ++i)
                    patches[i] = _rows[_orderedSlots[i]];
                for (int first = 0; first < patches.Length;)
                {
                    int slot = _orderedSlots[first];
                    int end = first + 1;
                    while (end < patches.Length && _orderedSlots[end] == slot + end - first)
                        ++end;
                    // Copies in this pass are not ordered against overlapping writes.
                    StageCleanRange(gpu, cleanStart, slot);
                    ulong rangeBytes = (ulong)(end - first) * CandidateBytes;
                    gpu.AddCopy(source.Slice((ulong)first * CandidateBytes, rangeBytes), _buffer.Slice((ulong)slot * CandidateBytes, rangeBytes));
                    cleanStart = slot + end - first;
                    first = end;
                }
            }
            StageCleanRange(gpu, cleanStart, _submittedSlots);
            return _orderedSlots.Count + count - _submittedSlots;
        }

        private void StageCleanRange(GpuStore gpu, int first, int end)
        {
            if (_copySource == null || first == end)
                return;
            ulong offset = (ulong)first * CandidateBytes;
            ulong bytes = (ulong)(end - first) * CandidateBytes;
            gpu.AddCopy(_copySource.Slice(offset, bytes), _buffer!.Slice(offset, bytes));
        }

        /// <summary>Publishes staged slots and retires superseded storage after submission.</summary>
        public void Commit()
        {
            _submittedSlots = _rows.Count;
            _dirtySlots.Clear();
            _copySource?.Dispose();
            _copySource = null;
        }

        /// <summary>Retires the candidate storage.</summary>
        public void Dispose()
        {
            _copySource?.Dispose();
            _buffer?.Dispose();
        }

        private Placement Add(int instanceSlot, in DrawKey key)
        {
            if (!_buckets.TryGetValue(key, out DrawBucket? bucket))
                _buckets.Add(key, bucket = new DrawBucket(key));
            DrawChunk? chunk = bucket.Available.First?.Value;
            if (chunk == null)
            {
                if (!_freeChunks.TryPop(out chunk))
                {
                    chunk = new DrawChunk(_rows.Count);
                    for (int i = 0; i < ChunkSize; ++i)
                        _rows.Add(default);
                }
                bucket.Chunks.Add(chunk);
                bucket.Available.AddFirst(chunk);
            }
            int local = chunk.Holes.TryPop(out int hole) ? hole : chunk.Length++;
            if (++chunk.Live == ChunkSize)
                bucket.Available.RemoveFirst();
            int slot = chunk.First + local;
            _rows[slot] = new DrawCandidate((uint)instanceSlot + 1, key.PageId);
            _dirtySlots.Add(slot);
            return new Placement(bucket, chunk, local);
        }

        private void Remove(in Placement placement)
        {
            DrawBucket bucket = placement.Bucket;
            DrawChunk chunk = placement.Chunk;
            int slot = chunk.First + placement.Local;
            _rows[slot] = default;
            _dirtySlots.Add(slot);
            chunk.Holes.Push(placement.Local);
            if (chunk.Live-- == ChunkSize)
                bucket.Available.AddFirst(chunk);
            if (chunk.Live != 0)
                return;

            // A chunk may contain holes while another chunk in the same bucket is also available.
            bucket.Available.Remove(chunk);
            bucket.Chunks.Remove(chunk);
            chunk.Length = 0;
            chunk.Holes.Clear();
            _freeChunks.Push(chunk);
            if (bucket.Chunks.Count == 0)
                _buckets.Remove(bucket.Key);
        }

        private readonly record struct Placement(DrawBucket Bucket, DrawChunk Chunk, int Local);
    }

    private sealed class DrawBucket(DrawKey key)
    {
        public readonly DrawKey Key = key;
        public readonly List<DrawChunk> Chunks = [];
        public readonly LinkedList<DrawChunk> Available = new();
    }

    private sealed class DrawChunk(int first)
    {
        public readonly int First = first;
        public readonly Stack<int> Holes = new();
        public int Length;
        public int Live;
    }
}
