using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

internal sealed class SegmentHandleCache(IRandomAccessStorage storage, Guid worldGuid) : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<uint, SegmentHandleRef> _handles = new();
    private readonly HashSet<uint> _retiredSegmentIds = [];
    private bool _disposed;

    public readonly struct Lease : IDisposable
    {
        private readonly SegmentHandleCache _owner;
        private readonly SegmentHandleRef _handleRef;

        public Lease(SegmentHandleCache owner, SegmentHandleRef handleRef)
        {
            _owner = owner;
            _handleRef = handleRef;
        }

        public IRandomAccessHandle Handle => _handleRef.Handle;

        public void Dispose() => _owner.Release(_handleRef);
    }

    public bool TryAcquire(
        uint segmentId,
        out Lease lease,
        [NotNullWhen(false)] out string? failureReason)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                lease = default;
                failureReason = "Segment store was disposed.";
                return false;
            }

            if (_retiredSegmentIds.Contains(segmentId))
            {
                lease = default;
                failureReason = $"Segment {segmentId} has been retired.";
                return false;
            }

            if (_handles.TryGetValue(segmentId, out SegmentHandleRef? cached) && !cached.Retired)
            {
                cached.RefCount++;
                lease = new Lease(this, cached);
                failureReason = null;
                return true;
            }
        }

        string relativePath = StreamingSavePaths.SegmentFilePath(worldGuid, segmentId);
        if (!storage.TryOpenRead(relativePath, out IRandomAccessHandle? openedHandle))
        {
            lease = default;
            failureReason = $"Segment file '{relativePath}' does not exist.";
            return false;
        }

        if (!ChunkRecordCodec.TryReadAndValidateSegmentHeader(
                openedHandle,
                worldGuid,
                segmentId,
                expectedSpill: false,
                out _,
                out failureReason))
        {
            openedHandle.Dispose();
            lease = default;
            return false;
        }

        var newRef = new SegmentHandleRef(openedHandle, relativePath);
        bool keepNewRef = false;
        lock (_lock)
        {
            if (_disposed)
            {
                openedHandle.Dispose();
                lease = default;
                failureReason = "Segment store was disposed.";
                return false;
            }

            if (_retiredSegmentIds.Contains(segmentId))
            {
                // Segment was retired while opening; let this in-flight read finish without caching.
                newRef.Retired = true;
                newRef.PendingDelete = false;
                newRef.RefCount = 1;
                lease = new Lease(this, newRef);
                keepNewRef = true;
            }
            else if (_handles.TryGetValue(segmentId, out SegmentHandleRef? existing) && !existing.Retired)
            {
                existing.RefCount++;
                lease = new Lease(this, existing);
            }
            else
            {
                newRef.RefCount = 2; // 1 for cache + 1 for caller.
                _handles[segmentId] = newRef;
                lease = new Lease(this, newRef);
                keepNewRef = true;
            }
        }

        if (!keepNewRef)
            openedHandle.Dispose();

        failureReason = null;
        return true;
    }

    public void RetireAndDelete(uint segmentId)
    {
        IRandomAccessHandle? toDispose = null;
        string? pathToDelete = null;
        bool hadCachedHandle = false;

        lock (_lock)
        {
            _retiredSegmentIds.Add(segmentId);
            if (_handles.Remove(segmentId, out SegmentHandleRef? cached))
            {
                hadCachedHandle = true;
                cached.Retired = true;
                cached.PendingDelete = true;
                cached.RefCount--;
                if (cached.RefCount == 0)
                {
                    toDispose = cached.Handle;
                    pathToDelete = cached.RelativePath;
                }
            }
        }

        toDispose?.Dispose();
        if (pathToDelete is not null)
        {
            storage.DeleteFile(pathToDelete);
        }
        else if (!hadCachedHandle)
        {
            storage.DeleteFile(StreamingSavePaths.SegmentFilePath(worldGuid, segmentId));
        }
    }

    public void Dispose()
    {
        List<IRandomAccessHandle>? toDispose = null;
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (SegmentHandleRef handleRef in _handles.Values)
            {
                handleRef.Retired = true;
                handleRef.PendingDelete = false;
                handleRef.RefCount--;
                if (handleRef.RefCount == 0)
                    (toDispose ??= []).Add(handleRef.Handle);
            }

            _handles.Clear();
        }

        if (toDispose is null)
            return;

        for (int i = 0; i < toDispose.Count; i++)
            toDispose[i].Dispose();
    }

    private void Release(SegmentHandleRef handleRef)
    {
        IRandomAccessHandle? toDispose = null;
        string? pathToDelete = null;
        lock (_lock)
        {
            handleRef.RefCount--;
            if (handleRef.Retired && handleRef.RefCount == 0)
            {
                toDispose = handleRef.Handle;
                if (handleRef.PendingDelete)
                    pathToDelete = handleRef.RelativePath;
            }
        }

        toDispose?.Dispose();
        if (pathToDelete is not null)
            storage.DeleteFile(pathToDelete);
    }

    internal sealed class SegmentHandleRef(IRandomAccessHandle handle, string relativePath)
    {
        public IRandomAccessHandle Handle { get; } = handle;
        public string RelativePath { get; } = relativePath;
        public int RefCount;
        public bool Retired;
        public bool PendingDelete;
    }
}
