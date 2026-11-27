using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Top-level acceleration strategy used by <see cref="ChunkedBvhAabb2D"/> to index active chunks.
    /// </summary>
    public enum ChunkedTlasMode2D
    {
        /// <summary>
        /// Uses an incremental <see cref="DynamicBvhAabb2D"/> TLAS updated immediately on chunk changes,
        /// avoiding full-TLAS rebuild stalls on streaming chunk loads and unloads.
        /// </summary>
        Dynamic = 0,

        /// <summary>
        /// Builds a compiled 4-wide SIMD 30-bit <see cref="BvhAabb2D"/> TLAS on <see cref="ChunkedBvhAabb2D.CommitDirty"/>
        /// for maximum steady-state query throughput while falling back to the incremental TLAS before commit.
        /// </summary>
        MortonSimd = 1,
    }

    /// <summary>
    /// Generational handle identifying a registered AABB entry inside <see cref="ChunkedBvhAabb2D"/>.
    /// </summary>
    public readonly struct ChunkedEntryHandle2D : IEquatable<ChunkedEntryHandle2D>
    {
        /// <summary>Internal slot index of the entry.</summary>
        public int Index { get; }

        /// <summary>Slot generation used to detect stale handles after removal or chunk unload.</summary>
        public int Generation { get; }

        /// <summary>Returns true if this handle carries a non-zero generation.</summary>
        public bool IsValid => Generation > 0;

        /// <summary>Canonical invalid handle.</summary>
        public static ChunkedEntryHandle2D Invalid => default;

        /// <summary>Initializes an entry handle.</summary>
        public ChunkedEntryHandle2D(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        /// <inheritdoc/>
        public bool Equals(ChunkedEntryHandle2D other) => Index == other.Index && Generation == other.Generation;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ChunkedEntryHandle2D other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Index, Generation);

        /// <summary>Equality operator.</summary>
        public static bool operator ==(ChunkedEntryHandle2D left, ChunkedEntryHandle2D right) => left.Equals(right);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(ChunkedEntryHandle2D left, ChunkedEntryHandle2D right) => !left.Equals(right);
    }

    /// <summary>
    /// Unbounded two-level single-owner chunked AABB spatial index.
    /// Each entry is owned by exactly one signed <c>chunkCoord</c> determined from its AABB center (zero duplicate storage across chunks).
    /// Each chunk maintains tight loose bounds and builds either a <see cref="FlatSimdBucket2D"/> (small chunks) or a
    /// 20-bit <see cref="BvhAabb2D"/> (dense chunks) on <see cref="CommitDirty"/> using one shared <see cref="BvhAabb2DBuilder"/>.
    /// Queries remain immediately accurate before <see cref="CommitDirty"/> is called.
    /// </summary>
    public sealed class ChunkedBvhAabb2D : IDisposable
    {
        private struct Entry
        {
            public BoundingBox2D Bounds;
            public int Payload;
            public int2 ChunkCoord;
            public int ChunkSlot;
            public int NextFree;
            public int Generation;
            public bool Alive;
        }

        private sealed class Chunk : IDisposable
        {
            public int2 Coord;
            public int DenseIndex;
            public int TlasNodeHandle = DynamicBvhAabb2D.NullNode;
            public int[] EntrySlots = new int[4];
            public int Count;
            public BoundingBox2D LooseBounds;
            public bool BoundsDirty;
            public bool AccelerationDirty;
            public bool IsQueuedDirty;
            public FlatSimdBucket2D? FlatBucket;
            public BvhAabb2D? TreeBvh;

            public void AddSlot(int entrySlot, Entry[] entries)
            {
                if (Count == EntrySlots.Length)
                {
                    Array.Resize(ref EntrySlots, EntrySlots.Length * 2);
                }
                entries[entrySlot].ChunkSlot = Count;
                EntrySlots[Count++] = entrySlot;
            }

            public void RemoveSlot(int entrySlot, Entry[] entries)
            {
                int slotIdx = entries[entrySlot].ChunkSlot;
                int lastIdx = --Count;
                if (slotIdx != lastIdx)
                {
                    int movedEntry = EntrySlots[lastIdx];
                    EntrySlots[slotIdx] = movedEntry;
                    entries[movedEntry].ChunkSlot = slotIdx;
                }
            }

            public long NativeMemoryBytes
            {
                get
                {
                    long bytes = 0;
                    if (FlatBucket != null) bytes += FlatBucket.NativeMemoryBytes;
                    if (TreeBvh != null) bytes += TreeBvh.NativeMemoryBytes;
                    return bytes;
                }
            }

            public void Dispose()
            {
                FlatBucket?.Dispose();
                FlatBucket = null;
                TreeBvh?.Dispose();
                TreeBvh = null;
            }
        }

        private struct StatelessLeafAdapter<TVisitor> : IBvhAabb2DLeafVisitor<TVisitor>
            where TVisitor : struct, IBvhAabb2DLeafVisitor
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int payload, ref TVisitor state) => state.OnLeaf(payload);
        }

        private struct StatelessRayClosestAdapter<TVisitor> : IBvhAabb2DRayClosestVisitor<TVisitor>
            where TVisitor : struct, IBvhAabb2DRayClosestVisitor
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float OnLeaf(int payload, float entryT, float currentBestT, ref TVisitor state) =>
                state.OnLeaf(payload, entryT, currentBestT);
        }

        private ref struct InnerLeafForwarder<TVisitor, TState> : IBvhAabb2DLeafVisitor
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            private ref TVisitor _visitor;
            private ref TState _state;
            public bool Stopped;

            public InnerLeafForwarder(ref TVisitor visitor, ref TState state)
            {
                _visitor = ref visitor;
                _state = ref state;
                Stopped = false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int payload)
            {
                if (!_visitor.OnLeaf(payload, ref _state))
                {
                    Stopped = true;
                    return false;
                }
                return true;
            }
        }

        private ref struct InnerRayClosestForwarder<TVisitor, TState> : IBvhAabb2DRayClosestVisitor
            where TVisitor : struct, IBvhAabb2DRayClosestVisitor<TState>
        {
            private ref TVisitor _visitor;
            private ref TState _state;
            public float BestT;

            public InnerRayClosestForwarder(ref TVisitor visitor, ref TState state, float initialBestT)
            {
                _visitor = ref visitor;
                _state = ref state;
                BestT = initialBestT;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float OnLeaf(int payload, float entryT, float currentBestT)
            {
                float nextBest = _visitor.OnLeaf(payload, entryT, currentBestT, ref _state);
                if (nextBest < BestT)
                {
                    BestT = nextBest;
                }
                return nextBest;
            }
        }

        private ref struct ChunkPointVisitor<TVisitor, TState> : IBvhAabb2DLeafVisitor
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            private readonly ChunkedBvhAabb2D _owner;
            private readonly Vector2 _point;
            private ref TVisitor _visitor;
            private ref TState _state;

            public ChunkPointVisitor(ChunkedBvhAabb2D owner, Vector2 point, ref TVisitor visitor, ref TState state)
            {
                _owner = owner;
                _point = point;
                _visitor = ref visitor;
                _state = ref state;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int chunkIndex) => _owner.VisitChunkPoint(chunkIndex, _point, ref _visitor, ref _state);
        }

        private ref struct ChunkOverlapVisitor<TVisitor, TState> : IBvhAabb2DLeafVisitor
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            private readonly ChunkedBvhAabb2D _owner;
            private readonly BoundingBox2D _query;
            private ref TVisitor _visitor;
            private ref TState _state;

            public ChunkOverlapVisitor(ChunkedBvhAabb2D owner, in BoundingBox2D query, ref TVisitor visitor, ref TState state)
            {
                _owner = owner;
                _query = query;
                _visitor = ref visitor;
                _state = ref state;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int chunkIndex) => _owner.VisitChunkOverlap(chunkIndex, _query, ref _visitor, ref _state);
        }

        private ref struct ChunkRayAllVisitor<TVisitor, TState> : IBvhAabb2DLeafVisitor
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            private readonly ChunkedBvhAabb2D _owner;
            private readonly Vector2 _origin;
            private readonly Vector2 _displacement;
            private readonly float _maxT;
            private ref TVisitor _visitor;
            private ref TState _state;

            public ChunkRayAllVisitor(ChunkedBvhAabb2D owner, Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor, ref TState state)
            {
                _owner = owner;
                _origin = origin;
                _displacement = displacement;
                _maxT = maxT;
                _visitor = ref visitor;
                _state = ref state;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int chunkIndex) => _owner.VisitChunkRayAll(chunkIndex, _origin, _displacement, _maxT, ref _visitor, ref _state);
        }

        private ref struct ChunkRayClosestVisitor<TVisitor, TState> : IBvhAabb2DRayClosestVisitor
            where TVisitor : struct, IBvhAabb2DRayClosestVisitor<TState>
        {
            private readonly ChunkedBvhAabb2D _owner;
            private readonly Vector2 _origin;
            private readonly Vector2 _displacement;
            private readonly float _segmentMaxT;
            private ref TVisitor _visitor;
            private ref TState _state;

            public ChunkRayClosestVisitor(ChunkedBvhAabb2D owner, Vector2 origin, Vector2 displacement, float segmentMaxT, ref TVisitor visitor, ref TState state)
            {
                _owner = owner;
                _origin = origin;
                _displacement = displacement;
                _segmentMaxT = segmentMaxT;
                _visitor = ref visitor;
                _state = ref state;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float OnLeaf(int chunkIndex, float entryT, float currentBestT) =>
                _owner.VisitChunkRayClosest(chunkIndex, _origin, _displacement, _segmentMaxT, currentBestT, ref _visitor, ref _state);
        }

        private struct InnerRayClosestBoxAdapter : IBvhAabb2DRayClosestVisitor
        {
            public bool HasHit;
            public float BestT;
            public int BestPayload;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float OnLeaf(int payload, float entryT, float currentBestT)
            {
                if (entryT < currentBestT || (!HasHit && entryT <= currentBestT))
                {
                    HasHit = true;
                    BestT = entryT;
                    BestPayload = payload;
                    return entryT;
                }
                return currentBestT;
            }
        }

        private struct OuterRayClosestBoxAdapter : IBvhAabb2DRayClosestVisitor
        {
            private readonly ChunkedBvhAabb2D _owner;
            private readonly Vector2 _origin;
            private readonly Vector2 _displacement;
            private readonly float _segmentMaxT;
            public bool HasHit;
            public float BestT;
            public int BestPayload;

            public OuterRayClosestBoxAdapter(ChunkedBvhAabb2D owner, Vector2 origin, Vector2 displacement, float initialBestT)
            {
                _owner = owner;
                _origin = origin;
                _displacement = displacement;
                _segmentMaxT = initialBestT;
                HasHit = false;
                BestT = initialBestT;
                BestPayload = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float OnLeaf(int chunkIndex, float entryT, float currentBestT)
            {
                InnerRayClosestBoxAdapter inner = new() { BestT = currentBestT, BestPayload = -1 };
                StatelessRayClosestAdapter<InnerRayClosestBoxAdapter> adapter = default;
                float nextBest = _owner.VisitChunkRayClosest(chunkIndex, _origin, _displacement, _segmentMaxT, currentBestT, ref adapter, ref inner);
                if (inner.HasHit && (inner.BestT < BestT || (!HasHit && inner.BestT <= BestT)))
                {
                    HasHit = true;
                    BestT = inner.BestT;
                    BestPayload = inner.BestPayload;
                    return BestT;
                }
                return nextBest;
            }
        }

        private struct AnyHitStopVisitor : IBvhAabb2DLeafVisitor
        {
            public bool Hit;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int payload)
            {
                Hit = true;
                return false;
            }
        }

        private struct ListLeafCollector : IBvhAabb2DLeafVisitor
        {
            private readonly List<int> _results;

            public ListLeafCollector(List<int> results) => _results = results;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool OnLeaf(int payload)
            {
                _results.Add(payload);
                return true;
            }
        }

        private readonly int _chunkSize;
        private readonly float _invChunkSize;
        private readonly int _treeBuildThreshold;
        private readonly ChunkedTlasMode2D _tlasMode;
        private readonly ChunkTable<Chunk> _chunkTable = new();
        private readonly List<Chunk> _chunks = new();
        private readonly List<Chunk> _dirtyChunks = new();
        private readonly DynamicBvhAabb2D _dynamicTlas = new(initialCapacity: 128, fattenMargin: 0f);
        private readonly BvhAabb2D _simdTlas = new();
        private readonly BvhAabb2DBuilder _sharedBuilder = new();

        private Entry[] _entries = new Entry[128];
        private int _entryCount;
        private int _highWaterMark;
        private int _freeEntryHead = -1;
        private int _liveDirtyChunkCount;
        private bool _simdTlasDirty;
        private BoundingBox2D[] _chunkBoundsScratch = new BoundingBox2D[16];
        private int[] _chunkPayloadsScratch = new int[16];
        private BoundingBox2D[] _tlasBoundsScratch = new BoundingBox2D[64];
        private int[] _tlasPayloadsScratch = new int[64];
        private bool _isDisposed;

        /// <summary>Gets the world-space edge length of each square owner chunk.</summary>
        public int ChunkSize => _chunkSize;

        /// <summary>Gets the entry count threshold above which a chunk builds a 20-bit <see cref="BvhAabb2D"/> instead of a <see cref="FlatSimdBucket2D"/>.</summary>
        public int TreeBuildThreshold => _treeBuildThreshold;

        /// <summary>Gets the configured TLAS strategy.</summary>
        public ChunkedTlasMode2D TlasMode => _tlasMode;

        /// <summary>Gets the number of active AABB entries registered across all loaded chunks.</summary>
        public int EntryCount => _entryCount;

        /// <summary>Gets the number of non-empty chunks currently loaded.</summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>Gets the number of chunks with un-rebuilt local edits pending <see cref="CommitDirty"/>.</summary>
        public int DirtyChunkCount => _liveDirtyChunkCount;

        /// <summary>Gets total unmanaged memory bytes used by the shared builder, TLAS, and all chunk BLAS storage.</summary>
        public long NativeMemoryBytes
        {
            get
            {
                long total = _sharedBuilder.NativeMemoryBytes + _simdTlas.NativeMemoryBytes;
                for (int i = 0; i < _chunks.Count; i++)
                {
                    total += _chunks[i].NativeMemoryBytes;
                }
                return total;
            }
        }

        /// <summary>
        /// Initializes a <see cref="ChunkedBvhAabb2D"/> index with arbitrary signed world chunk coordinates.
        /// </summary>
        public ChunkedBvhAabb2D(
            int chunkSize = ChunkUtility.DefaultChunkSize,
            int treeBuildThreshold = 16,
            ChunkedTlasMode2D tlasMode = ChunkedTlasMode2D.MortonSimd)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
            }
            if (treeBuildThreshold < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(treeBuildThreshold), "Tree build threshold cannot be negative.");
            }

            _chunkSize = chunkSize;
            _invChunkSize = 1f / chunkSize;
            _treeBuildThreshold = treeBuildThreshold;
            _tlasMode = tlasMode;
        }

        /// <summary>
        /// Inserts a new AABB entry with caller <paramref name="payload"/> into its single owner chunk and returns its generational handle.
        /// Immediately visible to queries even before <see cref="CommitDirty"/>.
        /// </summary>
        public ChunkedEntryHandle2D Add(in BoundingBox2D bounds, int payload)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (payload == BvhAabb2D.EmptyLeafPayload)
            {
                throw new ArgumentException("Payload cannot equal BvhAabb2D.EmptyLeafPayload.", nameof(payload));
            }

            int slot = AllocateEntrySlot(out int generation);
            int2 coord = ComputeOwnerChunk(bounds);
            Chunk chunk = GetOrCreateChunk(coord);

            _entries[slot] = new Entry
            {
                Bounds = bounds,
                Payload = payload,
                ChunkCoord = coord,
                ChunkSlot = -1,
                NextFree = -1,
                Generation = generation,
                Alive = true,
            };
            _entryCount++;

            bool isFirst = chunk.Count == 0;
            chunk.AddSlot(slot, _entries);

            if (isFirst)
            {
                chunk.LooseBounds = bounds;
                chunk.BoundsDirty = false;
                chunk.TlasNodeHandle = _dynamicTlas.Insert(chunk.LooseBounds, chunk.DenseIndex);
                MarkSimdTlasDirty();
            }
            else if (!ContainsBox(chunk.LooseBounds, bounds))
            {
                chunk.LooseBounds = BoundingBox2D.Merge(chunk.LooseBounds, bounds);
                _dynamicTlas.UpdateBounds(chunk.TlasNodeHandle, chunk.LooseBounds);
                MarkSimdTlasDirty();
            }

            MarkChunkDirty(chunk);
            return new ChunkedEntryHandle2D(slot, generation);
        }

        /// <summary>
        /// Removes the entry identified by <paramref name="handle"/>.
        /// Returns <c>false</c> if <paramref name="handle"/> is invalid or stale.
        /// </summary>
        public bool Remove(ChunkedEntryHandle2D handle)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!TryResolveAliveEntry(handle, out int slot))
            {
                return false;
            }

            ref Entry entry = ref _entries[slot];
            int2 coord = entry.ChunkCoord;
            BoundingBox2D removedBounds = entry.Bounds;

            if (_chunkTable.TryGet(coord, out Chunk? chunk))
            {
                chunk.RemoveSlot(slot, _entries);
                if (chunk.Count == 0)
                {
                    RemoveEmptyChunk(chunk);
                }
                else
                {
                    if (TouchesBoundary(chunk.LooseBounds, removedBounds))
                    {
                        chunk.BoundsDirty = true;
                        MarkSimdTlasDirty();
                    }
                    MarkChunkDirty(chunk);
                }
            }

            FreeEntrySlot(slot);
            return true;
        }

        /// <summary>
        /// Updates the bounds of an active entry, automatically migrating between owner chunks if needed.
        /// Returns <c>false</c> if <paramref name="handle"/> is invalid or stale.
        /// </summary>
        public bool UpdateBounds(ChunkedEntryHandle2D handle, in BoundingBox2D newBounds)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!TryResolveAliveEntry(handle, out int slot))
            {
                return false;
            }

            ref Entry entry = ref _entries[slot];
            BoundingBox2D oldBounds = entry.Bounds;
            int2 oldCoord = entry.ChunkCoord;
            int2 newCoord = ComputeOwnerChunk(newBounds);

            entry.Bounds = newBounds;

            if (oldCoord == newCoord)
            {
                if (_chunkTable.TryGet(oldCoord, out Chunk? chunk))
                {
                    BoundingBox2D oldLoose = chunk.LooseBounds;
                    if (!ContainsBox(oldLoose, newBounds))
                    {
                        chunk.LooseBounds = BoundingBox2D.Merge(oldLoose, newBounds);
                        _dynamicTlas.UpdateBounds(chunk.TlasNodeHandle, chunk.LooseBounds);
                        MarkSimdTlasDirty();
                    }
                    if (TouchesBoundary(oldLoose, oldBounds))
                    {
                        chunk.BoundsDirty = true;
                        MarkSimdTlasDirty();
                    }
                    MarkChunkDirty(chunk);
                }
                return true;
            }

            if (_chunkTable.TryGet(oldCoord, out Chunk? oldChunk))
            {
                oldChunk.RemoveSlot(slot, _entries);
                if (oldChunk.Count == 0)
                {
                    RemoveEmptyChunk(oldChunk);
                }
                else
                {
                    if (TouchesBoundary(oldChunk.LooseBounds, oldBounds))
                    {
                        oldChunk.BoundsDirty = true;
                        MarkSimdTlasDirty();
                    }
                    MarkChunkDirty(oldChunk);
                }
            }

            Chunk newChunk = GetOrCreateChunk(newCoord);
            entry.ChunkCoord = newCoord;
            bool firstInNew = newChunk.Count == 0;
            newChunk.AddSlot(slot, _entries);

            if (firstInNew)
            {
                newChunk.LooseBounds = newBounds;
                newChunk.BoundsDirty = false;
                newChunk.TlasNodeHandle = _dynamicTlas.Insert(newChunk.LooseBounds, newChunk.DenseIndex);
                MarkSimdTlasDirty();
            }
            else if (!ContainsBox(newChunk.LooseBounds, newBounds))
            {
                newChunk.LooseBounds = BoundingBox2D.Merge(newChunk.LooseBounds, newBounds);
                _dynamicTlas.UpdateBounds(newChunk.TlasNodeHandle, newChunk.LooseBounds);
                MarkSimdTlasDirty();
            }

            MarkChunkDirty(newChunk);
            return true;
        }

        /// <summary>Gets the user payload associated with <paramref name="handle"/> if active.</summary>
        public bool TryGetPayload(ChunkedEntryHandle2D handle, out int payload)
        {
            if (TryResolveAliveEntry(handle, out int slot))
            {
                payload = _entries[slot].Payload;
                return true;
            }
            payload = 0;
            return false;
        }

        /// <summary>Gets the current AABB associated with <paramref name="handle"/> if active.</summary>
        public bool TryGetBounds(ChunkedEntryHandle2D handle, out BoundingBox2D bounds)
        {
            if (TryResolveAliveEntry(handle, out int slot))
            {
                bounds = _entries[slot].Bounds;
                return true;
            }
            bounds = default;
            return false;
        }

        /// <summary>Returns true if the specified chunk coordinate currently has a non-empty chunk loaded.</summary>
        public bool ContainsChunk(int2 chunkCoord) => _chunkTable.TryGet(chunkCoord, out Chunk? chunk) && chunk.Count > 0;

        /// <summary>Gets the tight union AABB of all entries owned by <paramref name="chunkCoord"/>, if loaded.</summary>
        public bool TryGetChunkBounds(int2 chunkCoord, out BoundingBox2D looseBounds)
        {
            if (!_chunkTable.TryGet(chunkCoord, out Chunk? chunk) || chunk.Count == 0)
            {
                looseBounds = default;
                return false;
            }

            if (chunk.BoundsDirty)
            {
                RecomputeChunkBounds(chunk);
            }
            looseBounds = chunk.LooseBounds;
            return true;
        }

        /// <summary>
        /// Unloads an entire owner chunk and all entries belonging to it, freeing its acceleration buffers
        /// and incrementing generations to invalidate any outstanding handles.
        /// Returns the number of entries removed.
        /// </summary>
        public int UnloadChunk(int2 chunkCoord, Action<int>? onEntryRemoved = null)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_chunkTable.TryGet(chunkCoord, out Chunk? chunk))
            {
                return 0;
            }

            int count = chunk.Count;
            RemoveEmptyChunk(chunk);

            for (int i = 0; i < count; i++)
            {
                int slot = chunk.EntrySlots[i];
                int payload = _entries[slot].Payload;
                FreeEntrySlot(slot);
                onEntryRemoved?.Invoke(payload);
            }

            chunk.Count = 0;
            return count;
        }

        /// <summary>
        /// Rebuilds only the chunks whose contents changed since the last commit, and refreshes
        /// the compiled SIMD TLAS if <see cref="TlasMode"/> is <see cref="ChunkedTlasMode2D.MortonSimd"/>.
        /// </summary>
        public void CommitDirty()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            for (int i = 0; i < _dirtyChunks.Count; i++)
            {
                Chunk chunk = _dirtyChunks[i];
                chunk.IsQueuedDirty = false;
                if (chunk.Count == 0)
                {
                    continue;
                }

                if (chunk.BoundsDirty)
                {
                    RecomputeChunkBounds(chunk);
                }

                RebuildChunkBlas(chunk);
                chunk.AccelerationDirty = false;
            }
            _dirtyChunks.Clear();
            _liveDirtyChunkCount = 0;

            if (_tlasMode == ChunkedTlasMode2D.MortonSimd && _simdTlasDirty)
            {
                RebuildSimdTlas();
            }
        }

        /// <summary>
        /// Clears all entries and chunks, disposing chunk acceleration buffers while advancing
        /// active handle generations so stale handles never match future additions.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                _chunks[i].Dispose();
            }

            _chunkTable.Clear();
            _chunks.Clear();
            _dirtyChunks.Clear();
            _liveDirtyChunkCount = 0;
            _dynamicTlas.Clear();
            _simdTlas.Clear();
            _sharedBuilder.Clear();
            _simdTlasDirty = false;

            for (int i = 0; i < _highWaterMark; i++)
            {
                if (_entries[i].Alive)
                {
                    int nextGen = _entries[i].Generation + 1;
                    _entries[i].Generation = nextGen > 0 ? nextGen : 1;
                    _entries[i].Alive = false;
                }
            }
            _entryCount = 0;
            _highWaterMark = 0;
            _freeEntryHead = -1;
        }

        /// <summary>Finds all payloads whose AABB contains <paramref name="point"/>.</summary>
        public void QueryPoint(Vector2 point, List<int> results)
        {
            ListLeafCollector collector = new(results);
            VisitPoint(point, ref collector);
        }

        /// <summary>Visits all payloads whose AABB contains <paramref name="point"/> without heap allocation.</summary>
        public void VisitPoint<TVisitor>(Vector2 point, ref TVisitor visitor)
            where TVisitor : struct, IBvhAabb2DLeafVisitor
        {
            StatelessLeafAdapter<TVisitor> adapter = default;
            VisitPoint(point, ref adapter, ref visitor);
        }

        /// <summary>Visits all payloads whose AABB contains <paramref name="point"/> with mutable caller state passed by reference.</summary>
        public void VisitPoint<TVisitor, TState>(Vector2 point, ref TVisitor visitor, ref TState state)
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            if (_entryCount == 0)
            {
                return;
            }

            ChunkPointVisitor<TVisitor, TState> outer = new(this, point, ref visitor, ref state);
            if (_tlasMode == ChunkedTlasMode2D.MortonSimd && !_simdTlasDirty)
            {
                _simdTlas.VisitPoint(point, ref outer);
            }
            else
            {
                _dynamicTlas.VisitPoint(point, ref outer);
            }
        }

        /// <summary>Finds all payloads whose AABB overlaps <paramref name="query"/>.</summary>
        public void OverlapAabb(in BoundingBox2D query, List<int> results)
        {
            ListLeafCollector collector = new(results);
            VisitOverlapAabb(query, ref collector);
        }

        /// <summary>Visits all payloads whose AABB overlaps <paramref name="query"/> without heap allocation.</summary>
        public void VisitOverlapAabb<TVisitor>(in BoundingBox2D query, ref TVisitor visitor)
            where TVisitor : struct, IBvhAabb2DLeafVisitor
        {
            StatelessLeafAdapter<TVisitor> adapter = default;
            VisitOverlapAabb(query, ref adapter, ref visitor);
        }

        /// <summary>Visits all payloads whose AABB overlaps <paramref name="query"/> with mutable caller state passed by reference.</summary>
        public void VisitOverlapAabb<TVisitor, TState>(in BoundingBox2D query, ref TVisitor visitor, ref TState state)
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            if (_entryCount == 0)
            {
                return;
            }

            ChunkOverlapVisitor<TVisitor, TState> outer = new(this, query, ref visitor, ref state);
            if (_tlasMode == ChunkedTlasMode2D.MortonSimd && !_simdTlasDirty)
            {
                _simdTlas.VisitOverlapAabb(query, ref outer);
            }
            else
            {
                _dynamicTlas.VisitOverlapAabb(query, ref outer);
            }
        }

        /// <summary>Visits all payloads whose AABB intersects the ray segment within <c>[0, maxT]</c>.</summary>
        public void VisitRayAll<TVisitor>(Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor)
            where TVisitor : struct, IBvhAabb2DLeafVisitor
        {
            StatelessLeafAdapter<TVisitor> adapter = default;
            VisitRayAll(origin, displacement, maxT, ref adapter, ref visitor);
        }

        /// <summary>Visits all payloads whose AABB intersects the ray segment within <c>[0, maxT]</c> with mutable caller state passed by reference.</summary>
        public void VisitRayAll<TVisitor, TState>(Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor, ref TState state)
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            if (_entryCount == 0 || maxT < 0f)
            {
                return;
            }

            ChunkRayAllVisitor<TVisitor, TState> outer = new(this, origin, displacement, maxT, ref visitor, ref state);
            if (_tlasMode == ChunkedTlasMode2D.MortonSimd && !_simdTlasDirty)
            {
                _simdTlas.VisitRayAll(origin, displacement, maxT, ref outer);
            }
            else
            {
                _dynamicTlas.VisitRayAll(origin, displacement, maxT, ref outer);
            }
        }

        /// <summary>
        /// Traverses chunks and leaf AABBs front-to-back along the ray up to <paramref name="maxT"/>,
        /// allowing <paramref name="visitor"/> to tighten the active fraction dynamically via exact narrowphase tests.
        /// </summary>
        public void VisitRayClosest<TVisitor>(Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor)
            where TVisitor : struct, IBvhAabb2DRayClosestVisitor
        {
            StatelessRayClosestAdapter<TVisitor> adapter = default;
            VisitRayClosest(origin, displacement, maxT, ref adapter, ref visitor);
        }

        /// <summary>
        /// Traverses chunks and leaf AABBs front-to-back along the ray up to <paramref name="maxT"/> with mutable caller state passed by reference.
        /// </summary>
        public void VisitRayClosest<TVisitor, TState>(Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor, ref TState state)
            where TVisitor : struct, IBvhAabb2DRayClosestVisitor<TState>
        {
            if (_entryCount == 0 || maxT < 0f)
            {
                return;
            }

            ChunkRayClosestVisitor<TVisitor, TState> outer = new(this, origin, displacement, maxT, ref visitor, ref state);
            if (_tlasMode == ChunkedTlasMode2D.MortonSimd && !_simdTlasDirty)
            {
                _simdTlas.VisitRayClosest(origin, displacement, maxT, ref outer);
            }
            else
            {
                _dynamicTlas.VisitRayClosest(origin, displacement, maxT, ref outer);
            }
        }

        /// <summary>Finds the closest payload whose AABB is entered within <c>[0, 1]</c>.</summary>
        public bool RayCastClosest(Vector2 origin, Vector2 displacement, out int hitPayload, out float hitT) =>
            RayCastClosest(origin, displacement, 1f, out hitPayload, out hitT);

        /// <summary>Finds the closest payload whose AABB is entered within <c>[0, maxT]</c>.</summary>
        public bool RayCastClosest(Vector2 origin, Vector2 displacement, float maxT, out int hitPayload, out float hitT)
        {
            hitPayload = -1;
            hitT = 0f;
            if (_entryCount == 0 || maxT < 0f)
            {
                return false;
            }

            OuterRayClosestBoxAdapter outer = new(this, origin, displacement, maxT);
            if (_tlasMode == ChunkedTlasMode2D.MortonSimd && !_simdTlasDirty)
            {
                _simdTlas.VisitRayClosest(origin, displacement, maxT, ref outer);
            }
            else
            {
                _dynamicTlas.VisitRayClosest(origin, displacement, maxT, ref outer);
            }

            if (outer.HasHit)
            {
                hitPayload = outer.BestPayload;
                hitT = outer.BestT;
                return true;
            }
            return false;
        }

        /// <summary>Returns true if any leaf AABB intersects the ray segment within <c>[0, maxT]</c>.</summary>
        public bool RayCastAny(Vector2 origin, Vector2 displacement, float maxT = 1f)
        {
            if (_entryCount == 0 || maxT < 0f)
            {
                return false;
            }

            AnyHitStopVisitor visitor = default;
            VisitRayAll(origin, displacement, maxT, ref visitor);
            return visitor.Hit;
        }

        private bool VisitChunkPoint<TVisitor, TState>(int chunkIndex, Vector2 point, ref TVisitor visitor, ref TState state)
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            Chunk chunk = _chunks[chunkIndex];
            if (!chunk.AccelerationDirty)
            {
                InnerLeafForwarder<TVisitor, TState> forwarder = new(ref visitor, ref state);
                if (chunk.TreeBvh != null && chunk.TreeBvh.LeafCount > 0)
                {
                    chunk.TreeBvh.VisitPoint(point, ref forwarder);
                }
                else if (chunk.FlatBucket != null && chunk.FlatBucket.Count > 0)
                {
                    chunk.FlatBucket.VisitPoint(point, ref forwarder);
                }
                return !forwarder.Stopped;
            }

            for (int i = 0; i < chunk.Count; i++)
            {
                ref readonly Entry entry = ref _entries[chunk.EntrySlots[i]];
                if (ContainsPoint(entry.Bounds, point) && !visitor.OnLeaf(entry.Payload, ref state))
                {
                    return false;
                }
            }
            return true;
        }

        private bool VisitChunkOverlap<TVisitor, TState>(int chunkIndex, in BoundingBox2D query, ref TVisitor visitor, ref TState state)
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            Chunk chunk = _chunks[chunkIndex];
            if (!chunk.AccelerationDirty)
            {
                InnerLeafForwarder<TVisitor, TState> forwarder = new(ref visitor, ref state);
                if (chunk.TreeBvh != null && chunk.TreeBvh.LeafCount > 0)
                {
                    chunk.TreeBvh.VisitOverlapAabb(query, ref forwarder);
                }
                else if (chunk.FlatBucket != null && chunk.FlatBucket.Count > 0)
                {
                    chunk.FlatBucket.VisitOverlapAabb(query, ref forwarder);
                }
                return !forwarder.Stopped;
            }

            for (int i = 0; i < chunk.Count; i++)
            {
                ref readonly Entry entry = ref _entries[chunk.EntrySlots[i]];
                if (Overlaps(entry.Bounds, query) && !visitor.OnLeaf(entry.Payload, ref state))
                {
                    return false;
                }
            }
            return true;
        }

        private bool VisitChunkRayAll<TVisitor, TState>(int chunkIndex, Vector2 origin, Vector2 displacement, float maxT, ref TVisitor visitor, ref TState state)
            where TVisitor : struct, IBvhAabb2DLeafVisitor<TState>
        {
            Chunk chunk = _chunks[chunkIndex];
            if (!chunk.AccelerationDirty)
            {
                InnerLeafForwarder<TVisitor, TState> forwarder = new(ref visitor, ref state);
                if (chunk.TreeBvh != null && chunk.TreeBvh.LeafCount > 0)
                {
                    chunk.TreeBvh.VisitRayAll(origin, displacement, maxT, ref forwarder);
                }
                else if (chunk.FlatBucket != null && chunk.FlatBucket.Count > 0)
                {
                    chunk.FlatBucket.VisitRayAll(origin, displacement, maxT, ref forwarder);
                }
                return !forwarder.Stopped;
            }

            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            for (int i = 0; i < chunk.Count; i++)
            {
                ref readonly Entry entry = ref _entries[chunk.EntrySlots[i]];
                if (TryRaySlab(entry.Bounds, origin.X, origin.Y, invX, invY, maxT, out _) && !visitor.OnLeaf(entry.Payload, ref state))
                {
                    return false;
                }
            }
            return true;
        }

        private float VisitChunkRayClosest<TVisitor, TState>(int chunkIndex, Vector2 origin, Vector2 displacement, float segmentMaxT, float currentBestT, ref TVisitor visitor, ref TState state)
            where TVisitor : struct, IBvhAabb2DRayClosestVisitor<TState>
        {
            Chunk chunk = _chunks[chunkIndex];
            if (!chunk.AccelerationDirty)
            {
                InnerRayClosestForwarder<TVisitor, TState> tracker = new(ref visitor, ref state, currentBestT);
                if (chunk.TreeBvh != null && chunk.TreeBvh.LeafCount > 0)
                {
                    chunk.TreeBvh.VisitRayClosest(origin, displacement, segmentMaxT, currentBestT, ref tracker);
                }
                else if (chunk.FlatBucket != null && chunk.FlatBucket.Count > 0)
                {
                    chunk.FlatBucket.VisitRayClosest(origin, displacement, segmentMaxT, currentBestT, ref tracker);
                }
                return tracker.BestT;
            }

            float bestT = currentBestT;
            float invX = displacement.X != 0f ? 1f / displacement.X : float.MaxValue;
            float invY = displacement.Y != 0f ? 1f / displacement.Y : float.MaxValue;
            for (int i = 0; i < chunk.Count; i++)
            {
                ref readonly Entry entry = ref _entries[chunk.EntrySlots[i]];
                if (TryRaySlab(entry.Bounds, origin.X, origin.Y, invX, invY, segmentMaxT, out float entryT) && entryT <= bestT)
                {
                    float nextBest = visitor.OnLeaf(entry.Payload, entryT, bestT, ref state);
                    if (nextBest < bestT)
                    {
                        bestT = nextBest;
                    }
                }
            }
            return bestT;
        }

        private Chunk GetOrCreateChunk(int2 coord)
        {
            if (_chunkTable.TryGet(coord, out Chunk? existing))
            {
                return existing;
            }

            Chunk created = new()
            {
                Coord = coord,
                DenseIndex = _chunks.Count,
            };
            _chunks.Add(created);
            _chunkTable.TryAdd(coord, created);
            return created;
        }

        private void RemoveEmptyChunk(Chunk chunk)
        {
            _chunkTable.TryRemove(chunk.Coord, out _);

            if (chunk.TlasNodeHandle != DynamicBvhAabb2D.NullNode)
            {
                _dynamicTlas.Remove(chunk.TlasNodeHandle);
                chunk.TlasNodeHandle = DynamicBvhAabb2D.NullNode;
            }

            if (chunk.IsQueuedDirty)
            {
                chunk.IsQueuedDirty = false;
                _liveDirtyChunkCount--;
            }

            int removeIdx = chunk.DenseIndex;
            int lastIdx = _chunks.Count - 1;
            if (removeIdx != lastIdx)
            {
                Chunk moved = _chunks[lastIdx];
                _chunks[removeIdx] = moved;
                moved.DenseIndex = removeIdx;
                if (moved.TlasNodeHandle != DynamicBvhAabb2D.NullNode)
                {
                    _dynamicTlas.SetPayload(moved.TlasNodeHandle, removeIdx);
                }
            }
            _chunks.RemoveAt(lastIdx);

            chunk.Dispose();
            MarkSimdTlasDirty();
        }

        private void RecomputeChunkBounds(Chunk chunk)
        {
            if (chunk.Count == 0)
            {
                chunk.BoundsDirty = false;
                return;
            }

            BoundingBox2D union = _entries[chunk.EntrySlots[0]].Bounds;
            for (int i = 1; i < chunk.Count; i++)
            {
                union = BoundingBox2D.Merge(union, _entries[chunk.EntrySlots[i]].Bounds);
            }
            chunk.LooseBounds = union;
            chunk.BoundsDirty = false;

            if (chunk.TlasNodeHandle != DynamicBvhAabb2D.NullNode)
            {
                _dynamicTlas.UpdateBounds(chunk.TlasNodeHandle, union);
            }
        }

        private void RebuildChunkBlas(Chunk chunk)
        {
            int count = chunk.Count;
            if (_chunkBoundsScratch.Length < count)
            {
                int cap = Math.Max(count, _chunkBoundsScratch.Length * 2);
                _chunkBoundsScratch = new BoundingBox2D[cap];
                _chunkPayloadsScratch = new int[cap];
            }

            for (int i = 0; i < count; i++)
            {
                ref readonly Entry entry = ref _entries[chunk.EntrySlots[i]];
                _chunkBoundsScratch[i] = entry.Bounds;
                _chunkPayloadsScratch[i] = entry.Payload;
            }

            ReadOnlySpan<BoundingBox2D> bounds = _chunkBoundsScratch.AsSpan(0, count);
            ReadOnlySpan<int> payloads = _chunkPayloadsScratch.AsSpan(0, count);

            if (count <= _treeBuildThreshold)
            {
                if (chunk.TreeBvh != null)
                {
                    chunk.TreeBvh.Clear();
                }
                chunk.FlatBucket ??= new FlatSimdBucket2D();
                chunk.FlatBucket.Build(bounds, payloads);
            }
            else
            {
                if (chunk.FlatBucket != null)
                {
                    chunk.FlatBucket.Clear();
                }
                chunk.TreeBvh ??= new BvhAabb2D();
                chunk.TreeBvh.Build(bounds, payloads, _sharedBuilder, use30BitMorton: false);
            }
        }

        private void RebuildSimdTlas()
        {
            int chunkCount = _chunks.Count;
            if (chunkCount == 0)
            {
                _simdTlas.Clear();
                _simdTlasDirty = false;
                return;
            }

            if (_tlasBoundsScratch.Length < chunkCount)
            {
                int cap = Math.Max(chunkCount, _tlasBoundsScratch.Length * 2);
                _tlasBoundsScratch = new BoundingBox2D[cap];
                _tlasPayloadsScratch = new int[cap];
            }

            for (int i = 0; i < chunkCount; i++)
            {
                Chunk chunk = _chunks[i];
                if (chunk.BoundsDirty)
                {
                    RecomputeChunkBounds(chunk);
                }
                _tlasBoundsScratch[i] = chunk.LooseBounds;
                _tlasPayloadsScratch[i] = i;
            }

            _simdTlas.Build(
                _tlasBoundsScratch.AsSpan(0, chunkCount),
                _tlasPayloadsScratch.AsSpan(0, chunkCount),
                _sharedBuilder,
                use30BitMorton: true);
            _simdTlasDirty = false;
        }

        private void MarkChunkDirty(Chunk chunk)
        {
            chunk.AccelerationDirty = true;
            if (!chunk.IsQueuedDirty)
            {
                chunk.IsQueuedDirty = true;
                _dirtyChunks.Add(chunk);
                _liveDirtyChunkCount++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkSimdTlasDirty()
        {
            if (_tlasMode == ChunkedTlasMode2D.MortonSimd)
            {
                _simdTlasDirty = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int2 ComputeOwnerChunk(in BoundingBox2D bounds)
        {
            float cx = (bounds.Min.X + bounds.Max.X) * 0.5f * _invChunkSize;
            float cy = (bounds.Min.Y + bounds.Max.Y) * 0.5f * _invChunkSize;
            return new int2((int)MathF.Floor(cx), (int)MathF.Floor(cy));
        }

        private int AllocateEntrySlot(out int generation)
        {
            if (_freeEntryHead >= 0)
            {
                int slot = _freeEntryHead;
                _freeEntryHead = _entries[slot].NextFree;
                generation = _entries[slot].Generation > 0 ? _entries[slot].Generation : 1;
                return slot;
            }

            if (_highWaterMark == _entries.Length)
            {
                Array.Resize(ref _entries, _entries.Length * 2);
            }

            int idx = _highWaterMark++;
            generation = _entries[idx].Generation > 0 ? _entries[idx].Generation : 1;
            return idx;
        }

        private void FreeEntrySlot(int slot)
        {
            int nextGen = _entries[slot].Generation + 1;
            _entries[slot].Generation = nextGen > 0 ? nextGen : 1;
            _entries[slot].Alive = false;
            _entries[slot].NextFree = _freeEntryHead;
            _freeEntryHead = slot;
            _entryCount--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryResolveAliveEntry(ChunkedEntryHandle2D handle, out int slot)
        {
            slot = handle.Index;
            return (uint)slot < (uint)_highWaterMark &&
                   _entries[slot].Alive &&
                   _entries[slot].Generation == handle.Generation;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsBox(in BoundingBox2D outer, in BoundingBox2D inner) =>
            outer.Min.X <= inner.Min.X && outer.Min.Y <= inner.Min.Y &&
            outer.Max.X >= inner.Max.X && outer.Max.Y >= inner.Max.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TouchesBoundary(in BoundingBox2D outer, in BoundingBox2D inner) =>
            inner.Min.X <= outer.Min.X || inner.Min.Y <= outer.Min.Y ||
            inner.Max.X >= outer.Max.X || inner.Max.Y >= outer.Max.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsPoint(in BoundingBox2D box, Vector2 point) =>
            box.Min.X <= point.X && point.X <= box.Max.X &&
            box.Min.Y <= point.Y && point.Y <= box.Max.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Overlaps(in BoundingBox2D a, in BoundingBox2D b) =>
            a.Min.X <= b.Max.X && b.Min.X <= a.Max.X &&
            a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryRaySlab(
            in BoundingBox2D box,
            float orgX,
            float orgY,
            float invX,
            float invY,
            float maxT,
            out float rawTMin)
        {
            float tx1 = (box.Min.X - orgX) * invX;
            float tx2 = (box.Max.X - orgX) * invX;
            float tMin = tx1 < tx2 ? tx1 : tx2;
            float tMax = tx1 > tx2 ? tx1 : tx2;

            float ty1 = (box.Min.Y - orgY) * invY;
            float ty2 = (box.Max.Y - orgY) * invY;
            float tyMin = ty1 < ty2 ? ty1 : ty2;
            float tyMax = ty1 > ty2 ? ty1 : ty2;

            if (tyMin > tMin) tMin = tyMin;
            if (tyMax < tMax) tMax = tyMax;

            rawTMin = tMin;
            float clampedMin = tMin > 0f ? tMin : 0f;
            float clampedMax = tMax < maxT ? tMax : maxT;
            return clampedMin <= clampedMax;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Clear();
            _simdTlas.Dispose();
            _sharedBuilder.Dispose();
            _isDisposed = true;
        }
    }
}
