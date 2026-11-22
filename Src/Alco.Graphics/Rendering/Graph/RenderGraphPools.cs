// Physical texture/buffer pooling, persistent per-resource synchronization state, and timeline-deferred retirement.

using System.Diagnostics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

internal enum PhysicalKind : byte
{
    None,
    PooledTexture,
    PooledBuffer,
    External,
    SwapchainOutput,
}

internal readonly struct PhysicalRef
{
    internal readonly PhysicalKind Kind;
    internal readonly int Index;

    internal PhysicalRef(PhysicalKind kind, int index)
    {
        Kind = kind;
        Index = index;
    }

    internal bool IsValid => Kind != PhysicalKind.None;
}

internal struct PhysicalSyncState
{
    internal bool HasWrite;
    internal Stage WriteStage;
    internal Access WriteAccess;
    internal RGUsage SyncedReads;
    internal Stage ReadStages;

    internal void Reset()
    {
        HasWrite = false;
        WriteStage = Stage.None;
        WriteAccess = Access.None;
        SyncedReads = RGUsage.None;
        ReadStages = Stage.None;
    }
}

internal readonly struct FrameUse
{
    internal readonly RenderContext? Context;
    internal readonly ulong FrameId;

    internal FrameUse(RenderContext? context, ulong frameId)
    {
        Context = context;
        FrameId = frameId;
    }

    internal bool IsCompleted => Context == null || Context.IsFrameCompleted(FrameId);
}

internal sealed class RenderGraphPools
{
    private const ulong MinimumBufferBucketBytes = 4096;

    internal struct PooledTextureEntry
    {
        internal Texture? Texture;
        internal TextureDesc Key;
        internal PhysicalSyncState Sync;
        internal FrameUse LastUsed;
        internal ulong LastUsedFrame;
        internal bool InUseThisFrame;
        internal bool AvailableForReuse;
        internal bool ViewportRelative;
    }

    internal struct PooledBufferEntry
    {
        internal Buffer? Buffer;
        internal ulong CapacityBytes;
        internal PhysicalSyncState Sync;
        internal FrameUse LastUsed;
        internal ulong LastUsedFrame;
        internal bool InUseThisFrame;
        internal bool AvailableForReuse;
    }

    internal struct ExternalSyncEntry
    {
        internal object Key;
        internal PhysicalSyncState Sync;
        internal FrameUse LastUsed;
        internal ulong LastUsedFrame;
        internal bool InUseThisFrame;
    }

    private struct RetiredResource
    {
        internal RenderingResource Resource;
        internal FrameUse LastUsed;
    }

    private readonly RenderingSystem? _system;
    private PooledTextureEntry[] _textures = new PooledTextureEntry[8];
    private int _textureCount;
    private int[] _freedTextures = new int[8];
    private int _freedTextureCount;

    private PooledBufferEntry[] _buffers = new PooledBufferEntry[4];
    private int _bufferCount;
    private int[] _freedBuffers = new int[4];
    private int _freedBufferCount;

    private ExternalSyncEntry[] _externals = new ExternalSyncEntry[8];
    private int _externalCount;

    private RetiredResource[] _retired = new RetiredResource[8];
    private int _retiredCount;

    private PhysicalSyncState _swapchainSync;

    internal RenderGraphPools(RenderingSystem? system)
    {
        _system = system;
    }

    internal int TotalPooledTextureCount => _textureCount;

    internal int TotalPooledBufferCount => _bufferCount;

    internal int RetiredResourceCount => _retiredCount;

    internal int ActivePhysicalTextureCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < _textureCount; ++index)
            {
                if (_textures[index].InUseThisFrame)
                    ++count;
            }
            return count;
        }
    }

    internal int ActivePhysicalBufferCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < _bufferCount; ++index)
            {
                if (_buffers[index].InUseThisFrame)
                    ++count;
            }
            return count;
        }
    }

    internal void BeginFrame()
    {
        ReclaimRetired();
        _freedTextureCount = 0;
        for (int index = 0; index < _textureCount; ++index)
        {
            _textures[index].InUseThisFrame = false;
            _textures[index].AvailableForReuse = false;
        }
        _freedBufferCount = 0;
        for (int index = 0; index < _bufferCount; ++index)
        {
            _buffers[index].InUseThisFrame = false;
            _buffers[index].AvailableForReuse = false;
        }
        for (int index = 0; index < _externalCount; ++index)
            _externals[index].InUseThisFrame = false;
        // Swapchain Acquire synchronizes presentation image availability each frame.
        _swapchainSync.Reset();
    }

    internal int AllocateTexture(in TextureDesc key, int stickyIndex, bool viewportRelative, ulong frameNumber, bool dryRun,
        in FrameUse frameUse = default)
    {
        // 1. Sticky reassignment from this frame's freed candidates (preserves stable backing across aliases).
        if (stickyIndex >= 0 && stickyIndex < _textureCount && _textures[stickyIndex].AvailableForReuse &&
            _textures[stickyIndex].Key == key)
        {
            RemoveFreed(_freedTextures, ref _freedTextureCount, stickyIndex);
            ClaimTexture(stickyIndex, viewportRelative, frameNumber, in frameUse);
            return stickyIndex;
        }

        // 2. Most recently released compatible entry (LIFO aliasing of non-overlapping lifetimes).
        for (int stackIndex = _freedTextureCount - 1; stackIndex >= 0; --stackIndex)
        {
            int candidate = _freedTextures[stackIndex];
            if (_textures[candidate].Key == key)
            {
                RemoveAtOrdered(_freedTextures, ref _freedTextureCount, stackIndex);
                ClaimTexture(candidate, viewportRelative, frameNumber, in frameUse);
                return candidate;
            }
        }

        // 3. Sticky reassignment from idle entries not yet used this frame.
        if (stickyIndex >= 0 && stickyIndex < _textureCount && !_textures[stickyIndex].InUseThisFrame &&
            _textures[stickyIndex].Key == key)
        {
            ClaimTexture(stickyIndex, viewportRelative, frameNumber, in frameUse);
            return stickyIndex;
        }

        // 4. Deterministic lowest-index idle entry.
        for (int index = 0; index < _textureCount; ++index)
        {
            if (!_textures[index].InUseThisFrame && _textures[index].Key == key)
            {
                ClaimTexture(index, viewportRelative, frameNumber, in frameUse);
                return index;
            }
        }

        // 5. Cache miss: create a new pooled texture (or virtual slot in dry-run tests).
        Texture? texture = dryRun ? null : _system!.CreateTexture(in key);
        if (_textureCount == _textures.Length)
            Array.Resize(ref _textures, _textures.Length * 2);
        int created = _textureCount++;
        _textures[created] = new PooledTextureEntry
        {
            Texture = texture,
            Key = key,
            LastUsed = frameUse,
            LastUsedFrame = frameNumber,
            InUseThisFrame = true,
            AvailableForReuse = false,
            ViewportRelative = viewportRelative,
        };
        return created;
    }

    internal void ReleaseTextureForReuse(int poolIndex)
    {
        Debug.Assert(poolIndex >= 0 && poolIndex < _textureCount && !_textures[poolIndex].AvailableForReuse);
        _textures[poolIndex].AvailableForReuse = true;
        if (_freedTextureCount == _freedTextures.Length)
            Array.Resize(ref _freedTextures, _freedTextures.Length * 2);
        _freedTextures[_freedTextureCount++] = poolIndex;
    }

    internal int AllocateBuffer(ulong requiredBytes, int stickyIndex, ulong frameNumber, bool dryRun,
        in FrameUse frameUse = default)
    {
        ulong bucketBytes = RoundBufferSize(requiredBytes);

        if (stickyIndex >= 0 && stickyIndex < _bufferCount && _buffers[stickyIndex].AvailableForReuse &&
            _buffers[stickyIndex].CapacityBytes == bucketBytes)
        {
            RemoveFreed(_freedBuffers, ref _freedBufferCount, stickyIndex);
            ClaimBuffer(stickyIndex, frameNumber, in frameUse);
            return stickyIndex;
        }

        for (int stackIndex = _freedBufferCount - 1; stackIndex >= 0; --stackIndex)
        {
            int candidate = _freedBuffers[stackIndex];
            if (_buffers[candidate].CapacityBytes == bucketBytes)
            {
                RemoveAtOrdered(_freedBuffers, ref _freedBufferCount, stackIndex);
                ClaimBuffer(candidate, frameNumber, in frameUse);
                return candidate;
            }
        }

        if (stickyIndex >= 0 && stickyIndex < _bufferCount && !_buffers[stickyIndex].InUseThisFrame &&
            _buffers[stickyIndex].CapacityBytes == bucketBytes)
        {
            ClaimBuffer(stickyIndex, frameNumber, in frameUse);
            return stickyIndex;
        }

        for (int index = 0; index < _bufferCount; ++index)
        {
            if (!_buffers[index].InUseThisFrame && _buffers[index].CapacityBytes == bucketBytes)
            {
                ClaimBuffer(index, frameNumber, in frameUse);
                return index;
            }
        }

        Buffer? buffer = dryRun ? null : _system!.CreateBuffer(bucketBytes);
        if (_bufferCount == _buffers.Length)
            Array.Resize(ref _buffers, _buffers.Length * 2);
        int created = _bufferCount++;
        _buffers[created] = new PooledBufferEntry
        {
            Buffer = buffer,
            CapacityBytes = bucketBytes,
            LastUsed = frameUse,
            LastUsedFrame = frameNumber,
            InUseThisFrame = true,
            AvailableForReuse = false,
        };
        return created;
    }

    internal void ReleaseBufferForReuse(int poolIndex)
    {
        Debug.Assert(poolIndex >= 0 && poolIndex < _bufferCount && !_buffers[poolIndex].AvailableForReuse);
        _buffers[poolIndex].AvailableForReuse = true;
        if (_freedBufferCount == _freedBuffers.Length)
            Array.Resize(ref _freedBuffers, _freedBuffers.Length * 2);
        _freedBuffers[_freedBufferCount++] = poolIndex;
    }

    internal int GetOrAddExternal(object key, ulong frameNumber, in FrameUse frameUse = default)
    {
        for (int index = 0; index < _externalCount; ++index)
        {
            if (ReferenceEquals(_externals[index].Key, key))
            {
                _externals[index].InUseThisFrame = true;
                _externals[index].LastUsed = frameUse;
                _externals[index].LastUsedFrame = frameNumber;
                return index;
            }
        }
        if (_externalCount == _externals.Length)
            Array.Resize(ref _externals, _externals.Length * 2);
        int created = _externalCount++;
        _externals[created] = new ExternalSyncEntry
        {
            Key = key,
            LastUsed = frameUse,
            LastUsedFrame = frameNumber,
            InUseThisFrame = true,
        };
        return created;
    }

    internal Texture? GetPooledTexture(int poolIndex) => _textures[poolIndex].Texture;

    internal Buffer? GetPooledBuffer(int poolIndex) => _buffers[poolIndex].Buffer;

    internal ref PhysicalSyncState GetSyncState(PhysicalRef reference)
    {
        switch (reference.Kind)
        {
            case PhysicalKind.PooledTexture:
                return ref _textures[reference.Index].Sync;
            case PhysicalKind.PooledBuffer:
                return ref _buffers[reference.Index].Sync;
            case PhysicalKind.External:
                return ref _externals[reference.Index].Sync;
            case PhysicalKind.SwapchainOutput:
                return ref _swapchainSync;
            default:
                throw new InvalidOperationException("Invalid physical resource reference.");
        }
    }

    // Because Alco.Graphics barriers are global, any physical resource whose write scope is covered by this barrier gains visibility for covered reads.
    internal void PropagateGlobalBarrier(Stage before, Access beforeAccess, Stage after, Access afterAccess)
    {
        RGUsage coveredReads = RGUsageInfo.CoveredReadUsages(after, afterAccess);
        if (coveredReads == RGUsage.None)
            return;

        for (int index = 0; index < _textureCount; ++index)
            PropagateEntry(ref _textures[index].Sync, before, beforeAccess, coveredReads);
        for (int index = 0; index < _bufferCount; ++index)
            PropagateEntry(ref _buffers[index].Sync, before, beforeAccess, coveredReads);
        for (int index = 0; index < _externalCount; ++index)
            PropagateEntry(ref _externals[index].Sync, before, beforeAccess, coveredReads);
        PropagateEntry(ref _swapchainSync, before, beforeAccess, coveredReads);
    }

    internal void ClearSyncedReads()
    {
        for (int index = 0; index < _textureCount; ++index)
            _textures[index].Sync.SyncedReads = RGUsage.None;
        for (int index = 0; index < _bufferCount; ++index)
            _buffers[index].Sync.SyncedReads = RGUsage.None;
        for (int index = 0; index < _externalCount; ++index)
            _externals[index].Sync.SyncedReads = RGUsage.None;
        _swapchainSync.SyncedReads = RGUsage.None;
    }

    internal void MarkCompleted()
    {
        for (int index = 0; index < _textureCount; ++index)
        {
            _textures[index].LastUsed = default;
            _textures[index].Sync.Reset();
        }
        for (int index = 0; index < _bufferCount; ++index)
        {
            _buffers[index].LastUsed = default;
            _buffers[index].Sync.Reset();
        }
        for (int index = 0; index < _externalCount; ++index)
        {
            _externals[index].LastUsed = default;
            _externals[index].Sync.Reset();
        }
        _swapchainSync.Reset();
        DestroyAllRetired();
    }

    internal void RetireViewportRelativeTextures(RenderGraph owner)
    {
        for (int index = _textureCount - 1; index >= 0; --index)
        {
            if (!_textures[index].ViewportRelative)
                continue;
            RetireTextureAt(index, owner);
        }
        ReclaimRetired();
    }

    internal void EvictIdleResources(ulong frameNumber, int idleThreshold, RenderGraph owner)
    {
        ulong threshold = idleThreshold > 0 ? (ulong)idleThreshold : 0;
        for (int index = _textureCount - 1; index >= 0; --index)
        {
            if (_textures[index].InUseThisFrame)
                continue;
            if (threshold > 0 && frameNumber >= _textures[index].LastUsedFrame + threshold)
                RetireTextureAt(index, owner);
            else if (_textures[index].LastUsed.Context != null && _textures[index].LastUsed.IsCompleted)
                _textures[index].LastUsed = default;
        }
        for (int index = _bufferCount - 1; index >= 0; --index)
        {
            if (_buffers[index].InUseThisFrame)
                continue;
            if (threshold > 0 && frameNumber >= _buffers[index].LastUsedFrame + threshold)
                RetireBufferAt(index, owner);
            else if (_buffers[index].LastUsed.Context != null && _buffers[index].LastUsed.IsCompleted)
                _buffers[index].LastUsed = default;
        }

        // Evict completed or destroyed external sync entries so the graph never pins dead caller resources.
        for (int index = _externalCount - 1; index >= 0; --index)
        {
            if (_externals[index].InUseThisFrame)
                continue;
            bool destroyed = _externals[index].Key is RenderingResource resource && resource.IsDestroyed;
            bool completed = _externals[index].LastUsed.IsCompleted;
            if (destroyed || (frameNumber > _externals[index].LastUsedFrame + 1 && completed))
            {
                int last = --_externalCount;
                if (index != last)
                    _externals[index] = _externals[last];
                _externals[last] = default;
                owner.OnExternalRetired(index, last);
            }
            else if (completed && _externals[index].LastUsed.Context != null)
            {
                _externals[index].LastUsed = default;
            }
        }
        ReclaimRetired();
    }

    internal void ReclaimRetired()
    {
        int keep = 0;
        for (int index = 0; index < _retiredCount; ++index)
        {
            RetiredResource entry = _retired[index];
            if (entry.LastUsed.IsCompleted)
            {
                if (!entry.Resource.IsDestroyed)
                    entry.Resource.Destroy();
            }
            else
            {
                _retired[keep++] = entry;
            }
        }
        for (int index = keep; index < _retiredCount; ++index)
            _retired[index] = default;
        _retiredCount = keep;
    }

    internal void Destroy()
    {
        DestroyAllRetired();
        for (int index = 0; index < _textureCount; ++index)
        {
            Texture? texture = _textures[index].Texture;
            if (texture != null && !texture.IsDestroyed)
                texture.Destroy();
            _textures[index] = default;
        }
        _textureCount = 0;
        for (int index = 0; index < _bufferCount; ++index)
        {
            Buffer? buffer = _buffers[index].Buffer;
            if (buffer != null && !buffer.IsDestroyed)
                buffer.Destroy();
            _buffers[index] = default;
        }
        _bufferCount = 0;
        for (int index = 0; index < _externalCount; ++index)
            _externals[index] = default;
        _externalCount = 0;
        _swapchainSync.Reset();
    }

    private void RetireTextureAt(int index, RenderGraph owner)
    {
        PooledTextureEntry entry = _textures[index];
        if (entry.Texture != null)
            EnqueueRetired(entry.Texture, in entry.LastUsed);
        int last = --_textureCount;
        if (index != last)
            _textures[index] = _textures[last];
        _textures[last] = default;
        owner.OnPooledTextureRetired(index, last);
    }

    private void RetireBufferAt(int index, RenderGraph owner)
    {
        PooledBufferEntry entry = _buffers[index];
        if (entry.Buffer != null)
            EnqueueRetired(entry.Buffer, in entry.LastUsed);
        int last = --_bufferCount;
        if (index != last)
            _buffers[index] = _buffers[last];
        _buffers[last] = default;
        owner.OnPooledBufferRetired(index, last);
    }

    private void EnqueueRetired(RenderingResource resource, in FrameUse lastUsed)
    {
        if (lastUsed.IsCompleted)
        {
            if (!resource.IsDestroyed)
                resource.Destroy();
            return;
        }
        if (_retiredCount == _retired.Length)
            Array.Resize(ref _retired, _retired.Length * 2);
        _retired[_retiredCount++] = new RetiredResource { Resource = resource, LastUsed = lastUsed };
    }

    private void DestroyAllRetired()
    {
        for (int index = 0; index < _retiredCount; ++index)
        {
            RenderingResource resource = _retired[index].Resource;
            if (!resource.IsDestroyed)
                resource.Destroy();
            _retired[index] = default;
        }
        _retiredCount = 0;
    }

    private void ClaimTexture(int index, bool viewportRelative, ulong frameNumber, in FrameUse frameUse)
    {
        bool firstUseThisFrame = !_textures[index].InUseThisFrame;
        _textures[index].InUseThisFrame = true;
        _textures[index].AvailableForReuse = false;
        _textures[index].LastUsed = frameUse;
        _textures[index].LastUsedFrame = frameNumber;
        _textures[index].ViewportRelative = firstUseThisFrame ? viewportRelative : (_textures[index].ViewportRelative | viewportRelative);
    }

    private void ClaimBuffer(int index, ulong frameNumber, in FrameUse frameUse)
    {
        _buffers[index].InUseThisFrame = true;
        _buffers[index].AvailableForReuse = false;
        _buffers[index].LastUsed = frameUse;
        _buffers[index].LastUsedFrame = frameNumber;
    }

    private static void PropagateEntry(ref PhysicalSyncState sync, Stage before, Access beforeAccess, RGUsage coveredReads)
    {
        if (!sync.HasWrite)
            return;
        bool stageCovered = (before & Stage.AllCommands) != 0 || (sync.WriteStage & before) == sync.WriteStage;
        if (stageCovered && (sync.WriteAccess & beforeAccess) == sync.WriteAccess)
            sync.SyncedReads |= coveredReads;
    }

    private static void RemoveFreed(int[] stack, ref int count, int target)
    {
        for (int index = count - 1; index >= 0; --index)
        {
            if (stack[index] == target)
            {
                RemoveAtOrdered(stack, ref count, index);
                return;
            }
        }
    }

    private static void RemoveAtOrdered(int[] array, ref int count, int index)
    {
        --count;
        if (index < count)
            Array.Copy(array, index + 1, array, index, count - index);
    }

    private static ulong RoundBufferSize(ulong requiredBytes)
    {
        if (requiredBytes <= MinimumBufferBucketBytes)
            return MinimumBufferBucketBytes;
        ulong rounded = System.Numerics.BitOperations.RoundUpToPowerOf2(requiredBytes);
        return rounded != 0 ? rounded : requiredBytes;
    }
}
