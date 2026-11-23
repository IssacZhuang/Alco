using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Persistent GPU-mirrored instance pool supporting lock-free worker slot updates,
/// deferred thread-safe registration, ref-counted bindless texture retention, and
/// compute-driven dirty-scatter incremental uploads.
/// </summary>
/// <typeparam name="TInstance">Unmanaged GPU instance struct whose byte size is a positive multiple of 4.</typeparam>
public sealed unsafe class GpuInstancePool<TInstance> : AutoDisposable, IInstanceSlotSink
    where TInstance : unmanaged
{
    private const int ChunkSize = 4096;
    private const int DefaultInitialSlotCapacity = 1024;
    private const int InitialBucketCapacity = 32;
    private const uint NoBucket = uint.MaxValue;

    private sealed class SlotRecord(
        GpuInstancePool<TInstance> pool,
        uint bucketId,
        in TInstance initialData,
        Texture? primaryTexture,
        Texture? auxTexture)
    {
        public readonly GpuInstancePool<TInstance> Pool = pool;
        public readonly InstanceSlot<TInstance> Slot = new();
        public readonly uint BucketId = bucketId;
        public TInstance InitialData = initialData;
        public Texture? InitialPrimary = primaryTexture;
        public Texture? InitialAux = auxTexture;
        public int PoolSlot = -1;
        public int Disposed;

        public TInstance TakeInitialData(out Texture? primary, out Texture? aux)
        {
            primary = InitialPrimary;
            aux = InitialAux;
            InitialPrimary = null;
            InitialAux = null;
            return InitialData;
        }
    }

    /// <summary>Registration handle keeping a deferred slot active until disposed.</summary>
    public sealed class Registration : IDisposable
    {
        private readonly SlotRecord _record;

        /// <summary>Registers a new deferred instance slot on <paramref name="pool"/>.</summary>
        /// <param name="pool">Target instance pool.</param>
        /// <param name="bucketId">Bucket identifier assigned to this slot.</param>
        /// <param name="initialData">Initial instance payload.</param>
        /// <param name="primaryTexture">Optional primary bindless texture to keep alive while registered.</param>
        /// <param name="auxTexture">Optional auxiliary bindless texture to keep alive while registered.</param>
        public Registration(
            GpuInstancePool<TInstance> pool,
            uint bucketId,
            in TInstance initialData,
            Texture? primaryTexture = null,
            Texture? auxTexture = null)
        {
            ArgumentNullException.ThrowIfNull(pool);
            ObjectDisposedException.ThrowIf(pool.IsDisposed, pool);

            _record = new SlotRecord(pool, bucketId, in initialData, primaryTexture, auxTexture);
            pool._pendingAdds.Enqueue(_record);
        }

        /// <summary>Gets the slot write handle used to publish updated instance data.</summary>
        public InstanceSlot<TInstance> Slot => _record.Slot;

        /// <summary>Gets the bucket identifier assigned at registration.</summary>
        public uint BucketId => _record.BucketId;

        /// <summary>Gets whether this registration handle has been disposed.</summary>
        public bool IsDisposed => Volatile.Read(ref _record.Disposed) != 0;

        /// <summary>Queues a bindless texture retention update for this registration's slot.</summary>
        /// <param name="primary">Primary sampled texture, or null.</param>
        /// <param name="aux">Auxiliary sampled texture, or null.</param>
        public void SetTextures(Texture? primary, Texture? aux = null)
        {
            if (IsDisposed)
            {
                return;
            }

            _record.Pool._pendingTextureUpdates.Enqueue(new TextureUpdate(_record, primary, aux));
        }

        /// <summary>Detaches the slot immediately and schedules slot reclamation on the render thread.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _record.Disposed, 1) != 0)
            {
                return;
            }

            _record.Slot.Detach();
            if (Volatile.Read(ref _record.PoolSlot) >= 0)
            {
                _record.Pool._pendingDetaches.Enqueue(_record);
            }
        }
    }

    private readonly record struct TextureUpdate(SlotRecord Target, Texture? Primary, Texture? Aux);

    private readonly record struct RetiredBuffer(Alco.Graphics.Buffer Buffer, RenderContext? Context, ulong FrameId);

    private readonly record struct RetiredTexture(Texture Texture, RenderContext? Context, ulong FrameId);

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceScatterRoot
    {
        public GPUBufferView Patches;
        public GPUBufferView PatchSlots;
        public GPUBufferView Pool;
        public uint PatchCount;
        public uint WordsPerInstance;
    }

    private readonly RenderingSystem _system;
    private readonly Shader _scatterShader;
    private readonly TransientBufferPool _transientBuffers;

    private readonly List<TInstance[]> _chunks = new();
    private readonly List<(Texture? Primary, Texture? Aux)[]> _slotTextureChunks = new();
    private readonly List<int> _freeSlots = new();
    private readonly ConcurrentQueue<int> _dirtySlots = new();
    private readonly ConcurrentQueue<SlotRecord> _pendingAdds = new();
    private readonly ConcurrentQueue<SlotRecord> _pendingDetaches = new();
    private readonly ConcurrentQueue<TextureUpdate> _pendingTextureUpdates = new();

    private readonly HashSet<int> _dirtyDedupe = new();
    private readonly List<int> _uploadSlots = new();
    private readonly HashSet<int> _bucketDirtyDedupe = new();
    private readonly List<int> _bucketDirtySlots = new();

    private readonly Dictionary<Texture, int> _textureRefCounts = new();
    private readonly List<RetiredBuffer> _retiredBuffers = new();
    private readonly List<RetiredTexture> _retiredTextures = new();

    private uint[] _slotBuckets;
    private int[] _bucketCounts = new int[InitialBucketCapacity];
    private TInstance[] _patchStaging = [];
    private uint[] _slotStaging = [];
    private uint[] _bucketPatchStaging = [];
    private uint[] _bucketSlotStaging = [];

    private Alco.Graphics.Buffer _poolBuffer;
    private Alco.Graphics.Buffer _slotBucketBuffer;
    private int _slotCapacity;
    private int _gpuSlotCapacity;
    private int _totalSlots;
    private int _liveCount;
    private bool _membershipDirty = true;

    private RenderContext? _lastContext;
    private ulong _lastFrameId;

    /// <summary>Creates a persistent GPU instance pool backed by <paramref name="system"/>.</summary>
    /// <param name="system">The rendering system owning GPU buffers.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    public GpuInstancePool(RenderingSystem system, ShaderSystem? shaders = null)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        if (sizeof(TInstance) == 0 || sizeof(TInstance) % 4 != 0)
        {
            throw new InvalidOperationException(
                $"Instance type {typeof(TInstance).Name} size ({sizeof(TInstance)}B) must be a positive multiple of 4 bytes.");
        }

        ShaderSystem resolvedShaders = shaders ?? system.Shaders;
        AlcoSprite2DShaders.EnsureBuiltInModulesLoaded(resolvedShaders);
        _scatterShader = resolvedShaders.GetShader(AlcoSprite2DShaders.GpuInstanceScatterModule, ShaderKind.Compute);

        if ((uint)sizeof(InstanceScatterRoot) != _scatterShader.Root.SizeBytes ||
            !_scatterShader.Reflection.MatchesStructLayout<InstanceScatterRoot>("InstanceScatterRoot"))
        {
            throw new InvalidOperationException(
                "GpuInstanceScatter shader root layout does not match C# InstanceScatterRoot.");
        }

        _transientBuffers = new TransientBufferPool(system);
        _slotCapacity = DefaultInitialSlotCapacity;
        _gpuSlotCapacity = _slotCapacity;
        _slotBuckets = new uint[_slotCapacity];
        Array.Fill(_slotBuckets, NoBucket);

        _poolBuffer = system.CreateBuffer((ulong)_slotCapacity * (ulong)sizeof(TInstance));
        _slotBucketBuffer = system.CreateBuffer((ulong)_slotCapacity * sizeof(uint));
    }

    /// <summary>Gets the GPU buffer storing all instance slots.</summary>
    public Alco.Graphics.Buffer PoolBuffer => _poolBuffer;

    /// <summary>Gets the GPU buffer storing per-slot bucket identifiers.</summary>
    public Alco.Graphics.Buffer SlotBucketBuffer => _slotBucketBuffer;

    /// <summary>Gets the high-water slot count allocated so far.</summary>
    public int TotalSlots => _totalSlots;

    /// <summary>Gets the current number of active live slots.</summary>
    public int LiveCount => _liveCount;

    /// <summary>Gets the current GPU backing buffer slot capacity.</summary>
    public int SlotCapacity => _slotCapacity;

    /// <summary>Gets the number of distinct bindless textures currently retained by live or pending-retirement slots.</summary>
    public int ActiveTextureCount => _textureRefCounts.Count + _retiredTextures.Count;

    /// <summary>Gets the number of dirty instance patches uploaded during the last <see cref="SyncToGpu"/> call.</summary>
    public int LastDirtyPatchCount { get; private set; }

    /// <summary>Gets the live slot count currently assigned to <paramref name="bucketId"/>.</summary>
    /// <param name="bucketId">The bucket identifier to query.</param>
    /// <returns>The number of active slots in <paramref name="bucketId"/>.</returns>
    public int GetBucketCount(uint bucketId)
    {
        return bucketId < (uint)_bucketCounts.Length ? _bucketCounts[bucketId] : 0;
    }

    /// <summary>Ensures backing CPU mirrors and GPU buffers can hold at least <paramref name="minCapacity"/> slots without resizing mid-frame.</summary>
    /// <param name="minCapacity">Minimum slot capacity to reserve.</param>
    public void EnsureSlotCapacity(int minCapacity)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        EnsureCpuCapacity(minCapacity);
        SyncGpuCapacity();
    }

    /// <inheritdoc />
    public void MarkSlotDirty(int slot)
    {
        _dirtySlots.Enqueue(slot);
    }

    /// <summary>
    /// Queues a deferred thread-safe slot registration that becomes visible after the next
    /// <see cref="Flush"/> or <see cref="SyncToGpu"/> call.
    /// </summary>
    /// <param name="bucketId">Bucket identifier assigned to this slot.</param>
    /// <param name="initialData">Initial instance payload.</param>
    /// <param name="primaryTexture">Optional primary bindless texture to keep alive while registered.</param>
    /// <param name="auxTexture">Optional auxiliary bindless texture to keep alive while registered.</param>
    /// <returns>A disposable registration handle owning the slot's lifecycle.</returns>
    public Registration RegisterDeferred(
        uint bucketId,
        in TInstance initialData,
        Texture? primaryTexture = null,
        Texture? auxTexture = null)
    {
        return new Registration(this, bucketId, in initialData, primaryTexture, auxTexture);
    }

    /// <summary>Allocates and writes a slot directly on the render thread.</summary>
    /// <param name="bucketId">Bucket identifier assigned to this slot.</param>
    /// <param name="data">Initial instance payload.</param>
    /// <param name="primaryTexture">Optional primary bindless texture to retain.</param>
    /// <param name="auxTexture">Optional auxiliary bindless texture to retain.</param>
    /// <returns>The allocated global slot index.</returns>
    public int AddDirect(uint bucketId, in TInstance data, Texture? primaryTexture = null, Texture? auxTexture = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        int slot = AllocateSlot(bucketId);
        _chunks[slot / ChunkSize][slot % ChunkSize] = data;
        SetSlotTextures(slot, primaryTexture, auxTexture);
        _dirtySlots.Enqueue(slot);
        return slot;
    }

    /// <summary>Updates a direct slot's payload and optional retained textures on the render thread.</summary>
    /// <param name="slot">Global slot index returned by <see cref="AddDirect"/>.</param>
    /// <param name="data">Updated instance payload.</param>
    /// <param name="primaryTexture">Optional primary bindless texture to retain.</param>
    /// <param name="auxTexture">Optional auxiliary bindless texture to retain.</param>
    public void UpdateDirect(int slot, in TInstance data, Texture? primaryTexture = null, Texture? auxTexture = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Debug.Assert((uint)slot < (uint)_totalSlots);

        _chunks[slot / ChunkSize][slot % ChunkSize] = data;
        SetSlotTextures(slot, primaryTexture, auxTexture);
        _dirtySlots.Enqueue(slot);
    }

    /// <summary>Releases a direct slot on the render thread.</summary>
    /// <param name="slot">Global slot index returned by <see cref="AddDirect"/>.</param>
    public void RemoveDirect(int slot)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ReleaseSlot(slot);
    }

    /// <summary>
    /// Applies pending deferred detachments, additions, and texture updates on the render thread.
    /// Detachments run first so same-frame dispose and re-registration stay consistent.
    /// </summary>
    /// <returns><see langword="true"/> when bucket membership counts changed since the last flush.</returns>
    public bool Flush()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        while (_pendingDetaches.TryDequeue(out SlotRecord? record))
        {
            int slot = Volatile.Read(ref record.PoolSlot);
            if (slot >= 0)
            {
                record.Slot.Clear();
                ReleaseSlot(slot);
                Volatile.Write(ref record.PoolSlot, -1);
            }
        }

        while (_pendingAdds.TryDequeue(out SlotRecord? record))
        {
            if (Volatile.Read(ref record.Disposed) != 0)
            {
                _ = record.TakeInitialData(out _, out _);
                continue;
            }

            TInstance initialData = record.TakeInitialData(out Texture? primary, out Texture? aux);
            int slot = AllocateSlot(record.BucketId);
            TInstance[] chunk = _chunks[slot / ChunkSize];
            int offset = slot % ChunkSize;
            chunk[offset] = initialData;
            SetSlotTextures(slot, primary, aux);

            record.Slot.Attach(chunk, offset, this, slot);
            Volatile.Write(ref record.PoolSlot, slot);
            _dirtySlots.Enqueue(slot);

            if (Volatile.Read(ref record.Disposed) != 0)
            {
                record.Slot.Clear();
                ReleaseSlot(slot);
                Volatile.Write(ref record.PoolSlot, -1);
            }
        }

        while (_pendingTextureUpdates.TryDequeue(out TextureUpdate update))
        {
            if (Volatile.Read(ref update.Target.Disposed) != 0)
            {
                continue;
            }

            int slot = Volatile.Read(ref update.Target.PoolSlot);
            if (slot >= 0)
            {
                SetSlotTextures(slot, update.Primary, update.Aux);
            }
        }

        bool changed = _membershipDirty;
        _membershipDirty = false;
        return changed;
    }

    /// <summary>
    /// Flushes deferred changes, grows GPU backing buffers if needed, and scatters dirty slots
    /// and bucket assignments into the GPU pool via compute without overwriting in-flight GPU buffers.
    /// </summary>
    /// <param name="context">Active recording context.</param>
    /// <param name="compute">Open compute scope for issuing scatter dispatches.</param>
    /// <returns><see langword="true"/> when bucket membership counts changed this sync.</returns>
    public bool SyncToGpu(RenderContext context, in RenderContext.ComputeScope compute)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        ReclaimRetiredResources();

        bool membershipChanged = Flush();
        bool grewBuffers = _slotCapacity > _gpuSlotCapacity;

        LastDirtyPatchCount = 0;
        if (grewBuffers)
        {
            SyncGpuCapacity();
            DrainPendingDirtyLists();
            _lastContext = context;
            _lastFrameId = context.CurrentFrameId;
            return membershipChanged;
        }

        bool dispatchedScatter = false;
        dispatchedScatter |= DispatchInstanceScatter(context, compute);
        dispatchedScatter |= DispatchBucketScatter(context, compute);

        if (dispatchedScatter)
        {
            compute.Barrier(
                Stage.Compute, Access.ShaderWrite,
                Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
        }

        _lastContext = context;
        _lastFrameId = context.CurrentFrameId;
        return membershipChanged;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _transientBuffers.Dispose();

        for (int i = 0; i < _retiredBuffers.Count; i++)
        {
            if (!_retiredBuffers[i].Buffer.IsDestroyed)
            {
                _retiredBuffers[i].Buffer.Destroy();
            }
        }

        _retiredBuffers.Clear();
        _retiredTextures.Clear();
        _textureRefCounts.Clear();

        if (!_poolBuffer.IsDestroyed)
        {
            _poolBuffer.Destroy();
        }

        if (!_slotBucketBuffer.IsDestroyed)
        {
            _slotBucketBuffer.Destroy();
        }
    }

    private int AllocateSlot(uint bucketId)
    {
        EnsureBucketCapacity(bucketId);

        int slot;
        if (_freeSlots.Count > 0)
        {
            slot = _freeSlots[^1];
            _freeSlots.RemoveAt(_freeSlots.Count - 1);
        }
        else
        {
            if (_totalSlots >= _slotCapacity)
            {
                EnsureCpuCapacity(_totalSlots + 1);
            }

            slot = _totalSlots++;
            if (slot % ChunkSize == 0)
            {
                _chunks.Add(new TInstance[ChunkSize]);
                _slotTextureChunks.Add(new (Texture?, Texture?)[ChunkSize]);
            }
        }

        _slotBuckets[slot] = bucketId;
        ++_bucketCounts[bucketId];
        ++_liveCount;
        MarkBucketSlotDirty(slot);
        _membershipDirty = true;
        return slot;
    }

    private void ReleaseSlot(int slot)
    {
        Debug.Assert((uint)slot < (uint)_totalSlots);

        uint oldBucket = _slotBuckets[slot];
        if (oldBucket != NoBucket)
        {
            --_bucketCounts[oldBucket];
            --_liveCount;
            _slotBuckets[slot] = NoBucket;
            MarkBucketSlotDirty(slot);
            _membershipDirty = true;
        }

        _chunks[slot / ChunkSize][slot % ChunkSize] = default;
        SetSlotTextures(slot, null, null);
        _dirtySlots.Enqueue(slot);
        _freeSlots.Add(slot);
    }

    private void EnsureCpuCapacity(int requiredSlots)
    {
        if (requiredSlots <= _slotCapacity)
        {
            return;
        }

        int nextCapacity = _slotCapacity;
        while (nextCapacity < requiredSlots)
        {
            nextCapacity *= 2;
        }

        int oldCapacity = _slotCapacity;
        Array.Resize(ref _slotBuckets, nextCapacity);
        Array.Fill(_slotBuckets, NoBucket, oldCapacity, nextCapacity - oldCapacity);
        _slotCapacity = nextCapacity;
    }

    private void SyncGpuCapacity()
    {
        if (_slotCapacity <= _gpuSlotCapacity)
        {
            return;
        }

        Alco.Graphics.Buffer newPool = _system.CreateBuffer((ulong)_slotCapacity * (ulong)sizeof(TInstance));
        Alco.Graphics.Buffer newBuckets = _system.CreateBuffer((ulong)_slotCapacity * sizeof(uint));

        for (int i = 0; i < _totalSlots; i += ChunkSize)
        {
            int count = Math.Min(ChunkSize, _totalSlots - i);
            newPool.SetData<TInstance>(_chunks[i / ChunkSize].AsSpan(0, count), (ulong)i * (ulong)sizeof(TInstance));
        }

        if (_totalSlots > 0)
        {
            newBuckets.SetData<uint>(_slotBuckets.AsSpan(0, _totalSlots));
        }

        _retiredBuffers.Add(new RetiredBuffer(_poolBuffer, _lastContext, _lastFrameId));
        _retiredBuffers.Add(new RetiredBuffer(_slotBucketBuffer, _lastContext, _lastFrameId));
        _poolBuffer = newPool;
        _slotBucketBuffer = newBuckets;
        _gpuSlotCapacity = _slotCapacity;
    }

    private void EnsureBucketCapacity(uint bucketId)
    {
        if (bucketId < (uint)_bucketCounts.Length)
        {
            return;
        }

        int nextLen = _bucketCounts.Length;
        while ((uint)nextLen <= bucketId)
        {
            nextLen *= 2;
        }

        Array.Resize(ref _bucketCounts, nextLen);
    }

    private void SetSlotTextures(int slot, Texture? nextPrimary, Texture? nextAux)
    {
        (Texture? Primary, Texture? Aux)[] texChunk = _slotTextureChunks[slot / ChunkSize];
        int offset = slot % ChunkSize;
        (Texture? prevPrimary, Texture? prevAux) = texChunk[offset];

        if (!ReferenceEquals(prevPrimary, nextPrimary))
        {
            RetainTexture(nextPrimary);
            ReleaseTexture(prevPrimary);
        }

        if (!ReferenceEquals(prevAux, nextAux))
        {
            RetainTexture(nextAux);
            ReleaseTexture(prevAux);
        }

        texChunk[offset] = (nextPrimary, nextAux);
    }

    private void RetainTexture(Texture? texture)
    {
        if (texture == null)
        {
            return;
        }

        _textureRefCounts.TryGetValue(texture, out int count);
        _textureRefCounts[texture] = count + 1;
    }

    private void ReleaseTexture(Texture? texture)
    {
        if (texture == null || !_textureRefCounts.TryGetValue(texture, out int count))
        {
            return;
        }

        if (count > 1)
        {
            _textureRefCounts[texture] = count - 1;
            return;
        }

        _textureRefCounts.Remove(texture);
        if (_lastContext != null && !_lastContext.IsFrameCompleted(_lastFrameId))
        {
            _retiredTextures.Add(new RetiredTexture(texture, _lastContext, _lastFrameId));
        }
    }

    private void MarkBucketSlotDirty(int slot)
    {
        if (_bucketDirtyDedupe.Add(slot))
        {
            _bucketDirtySlots.Add(slot);
        }
    }

    private void DrainPendingDirtyLists()
    {
        while (_dirtySlots.TryDequeue(out _))
        {
        }

        _dirtyDedupe.Clear();
        _uploadSlots.Clear();
        _bucketDirtyDedupe.Clear();
        _bucketDirtySlots.Clear();
    }

    private bool DispatchInstanceScatter(RenderContext context, in RenderContext.ComputeScope compute)
    {
        if (_dirtySlots.IsEmpty)
        {
            return false;
        }

        _dirtyDedupe.Clear();
        _uploadSlots.Clear();
        while (_dirtySlots.TryDequeue(out int slot))
        {
            if ((uint)slot < (uint)_totalSlots && _dirtyDedupe.Add(slot))
            {
                _uploadSlots.Add(slot);
            }
        }

        int count = _uploadSlots.Count;
        if (count == 0)
        {
            return false;
        }

        if (_patchStaging.Length < count)
        {
            int nextLen = Math.Max(count, Math.Max(64, _patchStaging.Length * 2));
            _patchStaging = new TInstance[nextLen];
            _slotStaging = new uint[nextLen];
        }

        for (int i = 0; i < count; ++i)
        {
            int slot = _uploadSlots[i];
            _patchStaging[i] = _chunks[slot / ChunkSize][slot % ChunkSize];
            _slotStaging[i] = (uint)slot;
        }

        Alco.Graphics.Buffer patchBuf = _transientBuffers.Acquire(context, (ulong)count * (ulong)sizeof(TInstance));
        Alco.Graphics.Buffer slotBuf = _transientBuffers.Acquire(context, (ulong)count * sizeof(uint));
        patchBuf.SetData<TInstance>(_patchStaging.AsSpan(0, count));
        slotBuf.SetData<uint>(_slotStaging.AsSpan(0, count));

        GPURange root = context.AllocateRoot(new InstanceScatterRoot
        {
            Patches = patchBuf.View,
            PatchSlots = slotBuf.View,
            Pool = _poolBuffer.View,
            PatchCount = (uint)count,
            WordsPerInstance = (uint)(sizeof(TInstance) / 4),
        });
        compute.Dispatch(_scatterShader, root, new Extent3D((uint)((count + 63) / 64), 1u, 1u));
        LastDirtyPatchCount = count;
        return true;
    }

    private bool DispatchBucketScatter(RenderContext context, in RenderContext.ComputeScope compute)
    {
        int count = _bucketDirtySlots.Count;
        if (count == 0)
        {
            return false;
        }

        if (_bucketPatchStaging.Length < count)
        {
            int nextLen = Math.Max(count, Math.Max(64, _bucketPatchStaging.Length * 2));
            _bucketPatchStaging = new uint[nextLen];
            _bucketSlotStaging = new uint[nextLen];
        }

        int validCount = 0;
        for (int i = 0; i < count; ++i)
        {
            int slot = _bucketDirtySlots[i];
            if ((uint)slot < (uint)_totalSlots)
            {
                _bucketPatchStaging[validCount] = _slotBuckets[slot];
                _bucketSlotStaging[validCount] = (uint)slot;
                ++validCount;
            }
        }

        _bucketDirtySlots.Clear();
        _bucketDirtyDedupe.Clear();

        if (validCount == 0)
        {
            return false;
        }

        Alco.Graphics.Buffer patchBuf = _transientBuffers.Acquire(context, (ulong)validCount * sizeof(uint));
        Alco.Graphics.Buffer slotBuf = _transientBuffers.Acquire(context, (ulong)validCount * sizeof(uint));
        patchBuf.SetData<uint>(_bucketPatchStaging.AsSpan(0, validCount));
        slotBuf.SetData<uint>(_bucketSlotStaging.AsSpan(0, validCount));

        GPURange root = context.AllocateRoot(new InstanceScatterRoot
        {
            Patches = patchBuf.View,
            PatchSlots = slotBuf.View,
            Pool = _slotBucketBuffer.View,
            PatchCount = (uint)validCount,
            WordsPerInstance = 1u,
        });
        compute.Dispatch(_scatterShader, root, new Extent3D((uint)((validCount + 63) / 64), 1u, 1u));
        return true;
    }

    private void ReclaimRetiredResources()
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

        for (int i = _retiredTextures.Count - 1; i >= 0; --i)
        {
            RetiredTexture entry = _retiredTextures[i];
            if (entry.Context == null || entry.Context.IsFrameCompleted(entry.FrameId))
            {
                _retiredTextures.RemoveAt(i);
            }
        }
    }
}
