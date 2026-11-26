using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Alco;

/// <summary>
/// Dual-lane, dependency-aware job scheduler with cache-padded lock-free ticket queues,
/// dynamic batch chunking, and main-thread work-helping.
/// Use a single producer thread; jobs and callbacks must not call back into this scheduler.
/// </summary>
public sealed partial class JobScheduler : IDisposable
{
    private const int DefaultCapacity = 1024;
    private const int DefaultWorkerSpinCount = 512;
    private const int MainThreadSpinCount = 512;
    private const int BackgroundSpinCount = 16;
    private const int MaxWorkersPerLane = 63;
    private const int EmptyDependentHead = -1;
    private const int CompletedSentinel = -2;
    private const int MinQueueCapacity = 4096;
    private const int EdgeMultiplier = 8;
    private const int MinEdgeCapacity = 64;

    private const long ClosedBit = 1L << 31;
    private const long ActiveMask = (1L << 31) - 1;

    private enum JobKind : byte
    {
        Single = 0,
        Batch = 1,
        BatchRange = 2,
        CallbackOnly = 3,
        Fence = 4,
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct NodeHot
    {
        [FieldOffset(0)] public long ClaimState;
        [FieldOffset(8)] public int NextIndex;
        [FieldOffset(12)] public int RemainingItems;
        [FieldOffset(16)] public int UnresolvedDeps;
        [FieldOffset(20)] public int DependentHead;
        [FieldOffset(24)] public int Done;
        [FieldOffset(28)] public int Failed;
        [FieldOffset(32)] public int InheritedError;
    }

    private struct NodeData
    {
        public IJob? SingleJob;
        public IBatchJob? BatchJob;
        public IBatchRangeJob? BatchRangeJob;
        public JobDelegate? OnComplete;
        public int BatchCount;
        public int ChunkSize;
        public int GuidedDivisor;
        public int CompletedEdgeHead;
        public JobLane Lane;
        public JobKind Kind;
        public ushort ProfileTypeId;
        public List<Exception>? Errors;
        public List<JobHandle>? FailedAncestors;
    }

    private struct DependencyEdge
    {
        public int ChildSlot;
        public int Next;
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedInt32
    {
        [FieldOffset(64)] public int Value;
    }

    private sealed class BoundedTicketQueue
    {
        private struct Cell
        {
            public long Sequence;
            public ulong Ticket;
        }

        [StructLayout(LayoutKind.Explicit, Size = 192)]
        private struct PaddedPositions
        {
            [FieldOffset(64)] public long EnqueuePos;
            [FieldOffset(128)] public long DequeuePos;
        }

        private readonly Cell[] _cells;
        private readonly int _mask;
        private PaddedPositions _pos;

        public int Capacity => _cells.Length;

        public int ApproximateCount => (int)Math.Max(0L, Volatile.Read(ref _pos.EnqueuePos) - Volatile.Read(ref _pos.DequeuePos));

        public bool IsEmpty => Volatile.Read(ref _pos.EnqueuePos) <= Volatile.Read(ref _pos.DequeuePos);

        public BoundedTicketQueue(int requestedCapacity)
        {
            int cap = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(2, requestedCapacity));
            _cells = new Cell[cap];
            _mask = cap - 1;
            for (int i = 0; i < cap; i++)
            {
                _cells[i].Sequence = i;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnqueue(ulong ticket)
        {
            Cell[] cells = _cells;
            int mask = _mask;
            long pos = Volatile.Read(ref _pos.EnqueuePos);
            while (true)
            {
                ref Cell cell = ref cells[(int)pos & mask];
                long seq = Volatile.Read(ref cell.Sequence);
                long dif = seq - pos;
                if (dif == 0)
                {
                    if (Interlocked.CompareExchange(ref _pos.EnqueuePos, pos + 1, pos) == pos)
                    {
                        cell.Ticket = ticket;
                        Volatile.Write(ref cell.Sequence, pos + 1);
                        return true;
                    }
                }
                else if (dif < 0)
                {
                    return false;
                }
                else
                {
                    pos = Volatile.Read(ref _pos.EnqueuePos);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue(out ulong ticket)
        {
            Cell[] cells = _cells;
            int mask = _mask;
            long pos = Volatile.Read(ref _pos.DequeuePos);
            while (true)
            {
                ref Cell cell = ref cells[(int)pos & mask];
                long seq = Volatile.Read(ref cell.Sequence);
                long dif = seq - (pos + 1);
                if (dif == 0)
                {
                    if (Interlocked.CompareExchange(ref _pos.DequeuePos, pos + 1, pos) == pos)
                    {
                        ticket = cell.Ticket;
                        Volatile.Write(ref cell.Sequence, pos + mask + 1);
                        return true;
                    }
                }
                else if (dif < 0)
                {
                    ticket = 0;
                    return false;
                }
                else
                {
                    pos = Volatile.Read(ref _pos.DequeuePos);
                }
            }
        }
    }

    private sealed class WorkerLane
    {
        private readonly JobScheduler _scheduler;
        private readonly bool _isFrameLane;
        private readonly BoundedTicketQueue _queue;
        private readonly AutoResetEvent[] _wakeEvents;
        private readonly Thread[] _threads;
        private long _sleepingMask;
        private volatile bool _stopping;

        public int WorkerCount => _threads.Length;
        public int QueueCapacity => _queue.Capacity;
        public int ApproximateQueueDepth => _queue.ApproximateCount;
        public int SleepingWorkerCount => BitOperations.PopCount((ulong)Volatile.Read(ref _sleepingMask));
        public bool IsQueueEmpty => _queue.IsEmpty;

        public WorkerLane(JobScheduler scheduler, bool isFrameLane, int workerCount, int queueCapacity)
        {
            _scheduler = scheduler;
            _isFrameLane = isFrameLane;
            _queue = new BoundedTicketQueue(queueCapacity);
            _wakeEvents = new AutoResetEvent[workerCount];
            _threads = new Thread[workerCount];

            string prefix = isFrameLane ? "Alco.Job.Frame." : "Alco.Job.Bg.";
            ThreadPriority priority = isFrameLane ? ThreadPriority.Normal : ThreadPriority.BelowNormal;

            for (int i = 0; i < workerCount; i++)
            {
                int workerIndex = i;
                _wakeEvents[i] = new AutoResetEvent(false);
                var thread = new Thread(() => WorkerLoop(workerIndex))
                {
                    Name = $"{prefix}{i}",
                    IsBackground = true,
                    Priority = priority,
                };
                _threads[i] = thread;
                thread.Start();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue(out ulong ticket) => _queue.TryDequeue(out ticket);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueNoWake(ulong ticket, int count, int callerTrackIndex)
        {
            for (int i = 0; i < count; i++)
            {
                while (!_queue.TryEnqueue(ticket))
                {
                    _scheduler.EvictOrHelpOneQueuedTicket(this, _queue, callerTrackIndex);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueAndWake(ulong ticket, int count, int callerTrackIndex)
        {
            EnqueueNoWake(ticket, count, callerTrackIndex);
            Wake(count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Wake(int requestedCount)
        {
            if (requestedCount <= 0)
            {
                return;
            }

            Thread.MemoryBarrier();
            long mask = Volatile.Read(ref _sleepingMask);
            if (mask == 0)
            {
                return;
            }

            int woken = 0;
            while (mask != 0 && woken < requestedCount)
            {
                int workerIndex = BitOperations.TrailingZeroCount((ulong)mask);
                long bit = 1L << workerIndex;
                long prev = Interlocked.CompareExchange(ref _sleepingMask, mask & ~bit, mask);
                if (prev == mask)
                {
                    _wakeEvents[workerIndex].Set();
                    if (_scheduler._profilingEnabled)
                    {
                        _scheduler._profiler?.OnWorkerWoken(_isFrameLane, workerIndex);
                    }
                    woken++;
                    mask &= ~bit;
                }
                else
                {
                    mask = prev;
                }
            }
        }

        public void StopAndDispose()
        {
            _stopping = true;
            Thread.MemoryBarrier();

            for (int i = 0; i < _wakeEvents.Length; i++)
            {
                _wakeEvents[i].Set();
            }

            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i].Join();
            }

            for (int i = 0; i < _wakeEvents.Length; i++)
            {
                _wakeEvents[i].Dispose();
            }
        }

        private void WorkerLoop(int workerIndex)
        {
            long bit = 1L << workerIndex;
            JobLane lane = _isFrameLane ? JobLane.Frame : JobLane.Background;
            int trackIndex = _isFrameLane ? (1 + workerIndex) : (1 + _scheduler._frameLane.WorkerCount + workerIndex);
            BoundedTicketQueue queue = _queue;
            AutoResetEvent wakeEvent = _wakeEvents[workerIndex];

            while (!_stopping)
            {
                if (queue.TryDequeue(out ulong ticket))
                {
                    while (ticket != 0)
                    {
                        ticket = _scheduler.ExecuteTicketWithContinuation(ticket, lane, trackIndex);
                    }
                    continue;
                }

                int spinBudget = _isFrameLane
                    ? Volatile.Read(ref _scheduler._workerSpinCount)
                    : BackgroundSpinCount;

                long spinStart = _scheduler._profilingEnabled ? Stopwatch.GetTimestamp() : 0;
                bool executed = false;
                for (int i = 0; i < spinBudget; i++)
                {
                    if (_stopping)
                    {
                        return;
                    }

                    if (queue.TryDequeue(out ticket))
                    {
                        if (spinStart != 0)
                        {
                            long spinElapsed = Stopwatch.GetTimestamp() - spinStart;
                            if (spinElapsed > 0)
                            {
                                _scheduler._profiler?.AddTrackSpinTicks(trackIndex, spinElapsed);
                            }
                            spinStart = 0;
                        }

                        while (ticket != 0)
                        {
                            ticket = _scheduler.ExecuteTicketWithContinuation(ticket, lane, trackIndex);
                        }
                        executed = true;
                        break;
                    }

                    Thread.SpinWait(1);
                }

                if (spinStart != 0)
                {
                    long spinElapsed = Stopwatch.GetTimestamp() - spinStart;
                    if (spinElapsed > 0)
                    {
                        _scheduler._profiler?.AddTrackSpinTicks(trackIndex, spinElapsed);
                    }
                }

                if (executed)
                {
                    continue;
                }

                if (_stopping)
                {
                    break;
                }

                Interlocked.Or(ref _sleepingMask, bit);
                if (_stopping || !queue.IsEmpty)
                {
                    Interlocked.And(ref _sleepingMask, ~bit);
                    continue;
                }

                wakeEvent.WaitOne();
                if ((Volatile.Read(ref _sleepingMask) & bit) != 0)
                {
                    Interlocked.And(ref _sleepingMask, ~bit);
                }
            }
        }
    }

    private readonly AutoResetEvent _mainWakeSignal = new(false);
    private readonly Lock _errorLock = new();

    private WorkerLane _frameLane;
    private WorkerLane _backgroundLane;

    private NodeHot[] _hot = [];
    private NodeData[] _data = [];
    private int[] _slotGenerations = [];
    private byte[] _slotInUse = [];
    private int[] _freeSlots = [];
    private int _freeSlotCount;
    private int[] _activeSlots = [];
    private int[] _slotActivePos = [];
    private int _activeSlotCount;

    private DependencyEdge[] _edges = [];
    private int[] _freeEdges = [];
    private int _freeEdgeCount;

    private PaddedInt32 _activeFrameJobs;
    private PaddedInt32 _activeBackgroundJobs;
    private PaddedInt32 _mainWaiting;

    private int _workerSpinCount = DefaultWorkerSpinCount;
    private bool _enableMainThreadHelp = true;
    private bool _disposed;

    /// <summary>Creates a scheduler with default Frame and Background worker counts.</summary>
    public JobScheduler()
        : this(Math.Clamp(Environment.ProcessorCount - 1, 1, MaxWorkersPerLane),
               Math.Clamp(Environment.ProcessorCount / 4, 1, 4))
    {
    }

    /// <summary>Creates a scheduler with explicit worker counts for the Frame and Background lanes.</summary>
    /// <param name="frameWorkerCount">Number of high-priority Frame worker threads (1 to 63).</param>
    /// <param name="backgroundWorkerCount">Number of low-priority Background worker threads (1 to 63).</param>
    public JobScheduler(int frameWorkerCount, int backgroundWorkerCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameWorkerCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameWorkerCount, MaxWorkersPerLane);
        ArgumentOutOfRangeException.ThrowIfLessThan(backgroundWorkerCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(backgroundWorkerCount, MaxWorkersPerLane);

        AllocateStorage(DefaultCapacity);
        _frameLane = new WorkerLane(this, isFrameLane: true, frameWorkerCount, ComputeQueueCapacity(DefaultCapacity, frameWorkerCount));
        _backgroundLane = new WorkerLane(this, isFrameLane: false, backgroundWorkerCount, ComputeQueueCapacity(DefaultCapacity, backgroundWorkerCount));
    }

    /// <summary>
    /// Gets or sets the number of preallocated job slots.
    /// Change only when no active jobs or unconsumed errors remain.
    /// </summary>
    public int Capacity
    {
        get => _hot.Length;
        set => SetCapacity(value);
    }

    /// <summary>Gets the number of dedicated worker threads serving <see cref="JobLane.Frame"/>.</summary>
    public int FrameWorkerCount => _frameLane.WorkerCount;

    /// <summary>Gets the number of dedicated worker threads serving <see cref="JobLane.Background"/>.</summary>
    public int BackgroundWorkerCount => _backgroundLane.WorkerCount;

    /// <summary>Gets whether this scheduler has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Gets or sets the spin iterations performed by idle Frame workers before parking.</summary>
    public int WorkerSpinCount
    {
        get => Volatile.Read(ref _workerSpinCount);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Volatile.Write(ref _workerSpinCount, value);
        }
    }

    /// <summary>Gets or sets whether the producer thread executes Frame work items while waiting in <see cref="Complete"/> or <see cref="WaitAll()"/>.</summary>
    public bool EnableMainThreadHelp
    {
        get => _enableMainThreadHelp;
        set => _enableMainThreadHelp = value;
    }

    /// <summary>Schedules a single job.</summary>
    /// <param name="job">The job to execute. Reuse a reference-type instance to avoid boxing.</param>
    /// <param name="dependsOn">Optional dependency handle that must finish before this job begins.</param>
    /// <param name="onJobComplete">Optional callback invoked on a worker after successful execution.</param>
    /// <param name="lane">The execution lane for the job.</param>
    /// <returns>A handle identifying the scheduled job.</returns>
    public JobHandle Schedule(
        IJob job,
        JobHandle dependsOn = default,
        JobDelegate? onJobComplete = null,
        JobLane lane = JobLane.Frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(job);
        ValidateLane(lane);

        int slot = AllocateSlot();
        int gen = _slotGenerations[slot];
        ushort profileTypeId = _profilingEnabled ? _profiler!.RegisterJobType(job.GetType()) : (ushort)0;

        ref NodeData data = ref _data[slot];
        data.Kind = JobKind.Single;
        data.Lane = lane;
        data.ProfileTypeId = profileTypeId;
        data.SingleJob = job;
        data.BatchJob = null;
        data.BatchRangeJob = null;
        data.OnComplete = onJobComplete;
        data.BatchCount = 1;
        data.ChunkSize = 1;
        data.CompletedEdgeHead = EmptyDependentHead;
        data.Errors = null;
        data.FailedAncestors = null;

        ref NodeHot hot = ref _hot[slot];
        hot.NextIndex = 0;
        hot.RemainingItems = 1;
        hot.DependentHead = EmptyDependentHead;
        hot.Done = 0;
        hot.Failed = 0;
        hot.InheritedError = 0;

        bool hasDep = IsHandleRelevantForDependency(dependsOn);
        hot.UnresolvedDeps = hasDep ? 2 : 0;
        Volatile.Write(ref hot.ClaimState, (long)(uint)gen << 32);

        IncrementActiveLaneJobs(lane);
        if (_profilingEnabled)
        {
            _profiler!.OnJobScheduled(lane, _activeSlotCount);
        }

        if (!hasDep)
        {
            EnqueueAndWake(slot, gen, lane, ticketCount: 1);
            return new JobHandle(slot, gen);
        }

        if (AttachSingleDependency(dependsOn, slot))
        {
            if (Interlocked.Add(ref hot.UnresolvedDeps, -1) == 0)
            {
                EnqueueAndWake(slot, gen, lane, ticketCount: 1);
            }
        }
        else
        {
            if (Interlocked.Add(ref hot.UnresolvedDeps, -2) == 0)
            {
                EnqueueAndWake(slot, gen, lane, ticketCount: 1);
            }
        }

        return new JobHandle(slot, gen);
    }

    /// <summary>Schedules an indexed batch job with dynamic chunk stealing.</summary>
    /// <param name="job">The batch job whose <see cref="IBatchJob.Count"/> is read on the producer thread.</param>
    /// <param name="batchSize">Items per stolen chunk, or 0 for automatic sizing.</param>
    /// <param name="dependsOn">Optional dependency handle that must finish before this batch begins.</param>
    /// <param name="onJobComplete">Optional callback invoked once after all items succeed.</param>
    /// <param name="lane">The execution lane for the batch.</param>
    /// <returns>A handle identifying the scheduled batch.</returns>
    public JobHandle ScheduleBatch(
        IBatchJob job,
        int batchSize = 0,
        JobHandle dependsOn = default,
        JobDelegate? onJobComplete = null,
        JobLane lane = JobLane.Frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfNegative(batchSize);
        ValidateLane(lane);

        int count = job.Count;
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        return ScheduleBatchCore(JobKind.Batch, job, null, count, batchSize, dependsOn, onJobComplete, lane);
    }

    /// <summary>Schedules a range-based batch job with dynamic chunk stealing.</summary>
    /// <param name="job">The range batch job whose <see cref="IBatchRangeJob.Count"/> is read on the producer thread.</param>
    /// <param name="batchSize">Items per stolen range, or 0 for automatic sizing.</param>
    /// <param name="dependsOn">Optional dependency handle that must finish before this batch begins.</param>
    /// <param name="onJobComplete">Optional callback invoked once after all items succeed.</param>
    /// <param name="lane">The execution lane for the batch.</param>
    /// <returns>A handle identifying the scheduled batch.</returns>
    public JobHandle ScheduleBatch(
        IBatchRangeJob job,
        int batchSize = 0,
        JobHandle dependsOn = default,
        JobDelegate? onJobComplete = null,
        JobLane lane = JobLane.Frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfNegative(batchSize);
        ValidateLane(lane);

        int count = job.Count;
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        return ScheduleBatchCore(JobKind.BatchRange, null, job, count, batchSize, dependsOn, onJobComplete, lane);
    }

    /// <summary>Combines two dependency handles into a single handle without heap allocation.</summary>
    public JobHandle CombineDependencies(JobHandle first, JobHandle second)
    {
        ReadOnlySpan<JobHandle> handles = [first, second];
        return CombineDependencies(handles);
    }

    /// <summary>Combines three dependency handles into a single handle without heap allocation.</summary>
    public JobHandle CombineDependencies(JobHandle first, JobHandle second, JobHandle third)
    {
        ReadOnlySpan<JobHandle> handles = [first, second, third];
        return CombineDependencies(handles);
    }

    /// <summary>Combines multiple dependency handles into a single fence handle.</summary>
    /// <param name="handles">The handles that must all complete before the returned handle completes.</param>
    /// <returns>A combined handle, a single active handle, or <see langword="default"/> when all inputs are already done.</returns>
    public JobHandle CombineDependencies(ReadOnlySpan<JobHandle> handles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (handles.IsEmpty)
        {
            return default;
        }

        JobHandle[]? rented = handles.Length > 64 ? new JobHandle[handles.Length] : null;
        Span<JobHandle> selected = rented != null ? rented.AsSpan() : stackalloc JobHandle[64];

        int relevantCount = 0;
        for (int i = 0; i < handles.Length; i++)
        {
            JobHandle candidate = handles[i];
            if (IsHandleRelevantForDependency(candidate))
            {
                selected[relevantCount++] = candidate;
            }
        }

        if (relevantCount == 0)
        {
            return default;
        }

        if (relevantCount == 1)
        {
            return selected[0];
        }

        int slot = AllocateSlot(reservedAdditionalEdges: relevantCount);
        int gen = _slotGenerations[slot];
        if (_profilingEnabled)
        {
            _profiler!.OnFenceCreated(_activeSlotCount);
        }

        ref NodeData data = ref _data[slot];
        data.Kind = JobKind.Fence;
        data.Lane = JobLane.Frame;
        data.ProfileTypeId = 0;
        data.SingleJob = null;
        data.BatchJob = null;
        data.BatchRangeJob = null;
        data.OnComplete = null;
        data.BatchCount = 0;
        data.ChunkSize = 0;
        data.CompletedEdgeHead = EmptyDependentHead;
        data.Errors = null;
        data.FailedAncestors = null;

        ref NodeHot hot = ref _hot[slot];
        hot.NextIndex = 0;
        hot.RemainingItems = 0;
        hot.DependentHead = EmptyDependentHead;
        hot.Done = 0;
        hot.Failed = 0;
        hot.InheritedError = 0;
        hot.UnresolvedDeps = 1 + relevantCount;
        Volatile.Write(ref hot.ClaimState, (long)(uint)gen << 32);

        int alreadyResolved = 1;
        for (int i = 0; i < relevantCount; i++)
        {
            if (!AttachSingleDependency(selected[i], slot))
            {
                alreadyResolved++;
            }
        }

        if (Interlocked.Add(ref hot.UnresolvedDeps, -alreadyResolved) == 0)
        {
            CompleteFenceInline(slot);
        }

        return new JobHandle(slot, gen);
    }

    /// <summary>Returns whether the job identified by <paramref name="handle"/> has finished without mutating or reclaiming it.</summary>
    public bool IsCompleted(JobHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!handle.IsValid || (uint)handle.Index >= (uint)_hot.Length)
        {
            return true;
        }

        if (_slotInUse[handle.Index] == 0 || _slotGenerations[handle.Index] != handle.Generation)
        {
            return true;
        }

        return Volatile.Read(ref _hot[handle.Index].Done) != 0;
    }

    /// <summary>Waits until <paramref name="handle"/> completes, reclaims its slot, and throws any recorded errors.</summary>
    /// <param name="handle">The handle to complete.</param>
    /// <exception cref="AggregateException">One or more jobs, callbacks, or unconsumed ancestors failed.</exception>
    public void Complete(JobHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!handle.IsValid || (uint)handle.Index >= (uint)_hot.Length)
        {
            return;
        }

        int slot = handle.Index;
        int gen = handle.Generation;
        if (_slotInUse[slot] == 0 || _slotGenerations[slot] != gen)
        {
            return;
        }

        if (Volatile.Read(ref _hot[slot].Done) == 0)
        {
            if (_enableMainThreadHelp &&
                _data[slot].Lane == JobLane.Frame &&
                _data[slot].Kind != JobKind.Fence &&
                Volatile.Read(ref _hot[slot].UnresolvedDeps) == 0)
            {
                TryExecuteSlot(slot, gen, JobLane.Frame, allowContinuation: false, trackIndex: 0);
            }

            if (Volatile.Read(ref _hot[slot].Done) == 0)
            {
                WaitUntilSlotDone(slot);
            }
        }

        List<Exception>? errors = ConsumeErrorsForSubtree(slot);
        ReclaimSlot(slot);

        if (errors != null)
        {
            throw new AggregateException(errors);
        }
    }

    /// <summary>Waits for all scheduled jobs in both lanes, reclaims completed slots, and reports any errors.</summary>
    /// <exception cref="AggregateException">One or more jobs or callbacks failed.</exception>
    public void WaitAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WaitLaneCore(JobLane.Frame);
        WaitLaneCore(JobLane.Background);

        List<Exception>? errors = CollectAndReclaimAllSlots();
        if (errors != null)
        {
            throw new AggregateException(errors);
        }
    }

    /// <summary>Waits for all scheduled jobs in <paramref name="lane"/>, reclaims their slots, and reports any errors in that lane.</summary>
    /// <param name="lane">The lane to synchronize.</param>
    /// <exception cref="AggregateException">One or more jobs or callbacks in <paramref name="lane"/> failed.</exception>
    public void WaitAll(JobLane lane)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateLane(lane);

        WaitLaneCore(lane);

        List<Exception>? errors = CollectAndReclaimLaneSlots(lane);
        if (errors != null)
        {
            throw new AggregateException(errors);
        }
    }

    /// <summary>Waits for pending work across all lanes, stops worker threads, and disposes synchronization primitives.</summary>
    /// <exception cref="AggregateException">One or more pending jobs or callbacks failed.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Exception? waitError = null;
        try
        {
            WaitAll();
        }
        catch (Exception ex)
        {
            waitError = ex;
        }
        finally
        {
            _disposed = true;
            _frameLane.StopAndDispose();
            _backgroundLane.StopAndDispose();
            _mainWakeSignal.Dispose();
        }

        if (waitError != null)
        {
            ExceptionDispatchInfo.Throw(waitError);
        }
    }

    private JobHandle ScheduleBatchCore(
        JobKind kind,
        IBatchJob? batchJob,
        IBatchRangeJob? batchRangeJob,
        int count,
        int requestedBatchSize,
        JobHandle dependsOn,
        JobDelegate? onJobComplete,
        JobLane lane)
    {
        if (count == 0)
        {
            if (onJobComplete is null)
            {
                return IsHandleRelevantForDependency(dependsOn) ? dependsOn : default;
            }

            int cbSlot = AllocateSlot();
            int cbGen = _slotGenerations[cbSlot];
            ushort cbTypeId = _profilingEnabled ? _profiler!.RegisterCallback(onJobComplete) : (ushort)0;
            ref NodeData cbData = ref _data[cbSlot];
            cbData.Kind = JobKind.CallbackOnly;
            cbData.Lane = lane;
            cbData.ProfileTypeId = cbTypeId;
            cbData.SingleJob = null;
            cbData.BatchJob = null;
            cbData.BatchRangeJob = null;
            cbData.OnComplete = onJobComplete;
            cbData.BatchCount = 0;
            cbData.ChunkSize = 1;
            cbData.CompletedEdgeHead = EmptyDependentHead;
            cbData.Errors = null;
            cbData.FailedAncestors = null;

            ref NodeHot cbHot = ref _hot[cbSlot];
            cbHot.NextIndex = 0;
            cbHot.RemainingItems = 1;
            cbHot.DependentHead = EmptyDependentHead;
            cbHot.Done = 0;
            cbHot.Failed = 0;
            cbHot.InheritedError = 0;

            bool cbHasDep = IsHandleRelevantForDependency(dependsOn);
            cbHot.UnresolvedDeps = cbHasDep ? 2 : 0;
            Volatile.Write(ref cbHot.ClaimState, (long)(uint)cbGen << 32);
            IncrementActiveLaneJobs(lane);
            if (_profilingEnabled)
            {
                _profiler!.OnJobScheduled(lane, _activeSlotCount);
            }

            if (!cbHasDep)
            {
                EnqueueAndWake(cbSlot, cbGen, lane, ticketCount: 1);
                return new JobHandle(cbSlot, cbGen);
            }

            if (AttachSingleDependency(dependsOn, cbSlot))
            {
                if (Interlocked.Add(ref cbHot.UnresolvedDeps, -1) == 0)
                {
                    EnqueueAndWake(cbSlot, cbGen, lane, ticketCount: 1);
                }
            }
            else if (Interlocked.Add(ref cbHot.UnresolvedDeps, -2) == 0)
            {
                EnqueueAndWake(cbSlot, cbGen, lane, ticketCount: 1);
            }

            return new JobHandle(cbSlot, cbGen);
        }

        int chunkSize = requestedBatchSize > 0 ? Math.Min(requestedBatchSize, count) : 0;
        int concurrency = lane == JobLane.Frame
            ? _frameLane.WorkerCount + (_enableMainThreadHelp ? 1 : 0)
            : _backgroundLane.WorkerCount;
        int guidedDivisor = Math.Max(1, concurrency);
        int laneWorkers = lane == JobLane.Frame ? _frameLane.WorkerCount : _backgroundLane.WorkerCount;
        int maxConcurrency = chunkSize > 0 ? (count + chunkSize - 1) / chunkSize : count;
        int ticketCount = Math.Min(maxConcurrency, laneWorkers);

        int slot = AllocateSlot();
        int gen = _slotGenerations[slot];
        ushort profileTypeId = _profilingEnabled
            ? _profiler!.RegisterJobType((batchJob ?? (object)batchRangeJob!).GetType())
            : (ushort)0;

        ref NodeData data = ref _data[slot];
        data.Kind = kind;
        data.Lane = lane;
        data.ProfileTypeId = profileTypeId;
        data.SingleJob = null;
        data.BatchJob = batchJob;
        data.BatchRangeJob = batchRangeJob;
        data.OnComplete = onJobComplete;
        data.BatchCount = count;
        data.ChunkSize = chunkSize;
        data.GuidedDivisor = guidedDivisor;
        data.CompletedEdgeHead = EmptyDependentHead;
        data.Errors = null;
        data.FailedAncestors = null;

        ref NodeHot hot = ref _hot[slot];
        hot.NextIndex = 0;
        hot.RemainingItems = count;
        hot.DependentHead = EmptyDependentHead;
        hot.Done = 0;
        hot.Failed = 0;
        hot.InheritedError = 0;

        bool hasDep = IsHandleRelevantForDependency(dependsOn);
        hot.UnresolvedDeps = hasDep ? 2 : 0;
        Volatile.Write(ref hot.ClaimState, (long)(uint)gen << 32);

        IncrementActiveLaneJobs(lane);
        if (_profilingEnabled)
        {
            _profiler!.OnJobScheduled(lane, _activeSlotCount);
        }

        if (!hasDep)
        {
            EnqueueAndWake(slot, gen, lane, ticketCount);
            return new JobHandle(slot, gen);
        }

        if (AttachSingleDependency(dependsOn, slot))
        {
            if (Interlocked.Add(ref hot.UnresolvedDeps, -1) == 0)
            {
                EnqueueAndWake(slot, gen, lane, ticketCount);
            }
        }
        else if (Interlocked.Add(ref hot.UnresolvedDeps, -2) == 0)
        {
            EnqueueAndWake(slot, gen, lane, ticketCount);
        }

        return new JobHandle(slot, gen);
    }

    private void SetCapacity(int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        if (value == _hot.Length)
        {
            return;
        }

        if (Volatile.Read(ref _activeFrameJobs.Value) != 0 || Volatile.Read(ref _activeBackgroundJobs.Value) != 0)
        {
            throw new InvalidOperationException("Wait for scheduled work before changing capacity.");
        }

        ReclaimCompletedErrorFreeSlots();
        if (_activeSlotCount != 0)
        {
            throw new InvalidOperationException("Wait for scheduled work and consume any errors before changing capacity.");
        }

        AllocateStorage(value);

        int neededFrameQueue = ComputeQueueCapacity(value, _frameLane.WorkerCount);
        if (neededFrameQueue > _frameLane.QueueCapacity)
        {
            int frameCount = _frameLane.WorkerCount;
            _frameLane.StopAndDispose();
            _frameLane = new WorkerLane(this, isFrameLane: true, frameCount, neededFrameQueue);
        }

        int neededBgQueue = ComputeQueueCapacity(value, _backgroundLane.WorkerCount);
        if (neededBgQueue > _backgroundLane.QueueCapacity)
        {
            int bgCount = _backgroundLane.WorkerCount;
            _backgroundLane.StopAndDispose();
            _backgroundLane = new WorkerLane(this, isFrameLane: false, bgCount, neededBgQueue);
        }
    }

    private void AllocateStorage(int capacity)
    {
        _hot = new NodeHot[capacity];
        _data = new NodeData[capacity];
        _slotGenerations = new int[capacity];
        _slotInUse = new byte[capacity];
        _freeSlots = new int[capacity];
        _freeSlotCount = capacity;
        _activeSlots = new int[capacity];
        _slotActivePos = new int[capacity];
        _activeSlotCount = 0;

        for (int i = 0; i < capacity; i++)
        {
            _slotGenerations[i] = 1;
            _freeSlots[i] = capacity - 1 - i;
        }

        int edgeCapacity = Math.Max(MinEdgeCapacity, capacity * EdgeMultiplier);
        _edges = new DependencyEdge[edgeCapacity];
        _freeEdges = new int[edgeCapacity];
        _freeEdgeCount = edgeCapacity;
        for (int i = 0; i < edgeCapacity; i++)
        {
            _freeEdges[i] = edgeCapacity - 1 - i;
        }
    }

    private static int ComputeQueueCapacity(int slotCapacity, int workerCount)
    {
        long target = Math.Max((long)MinQueueCapacity, (long)slotCapacity * Math.Max(1, workerCount) * 2L);
        return (int)Math.Min(target, 1 << 24);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateLane(JobLane lane)
    {
        if (lane != JobLane.Frame && lane != JobLane.Background)
        {
            throw new ArgumentOutOfRangeException(nameof(lane));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void IncrementActiveLaneJobs(JobLane lane)
    {
        if (lane == JobLane.Frame)
        {
            Interlocked.Increment(ref _activeFrameJobs.Value);
        }
        else
        {
            Interlocked.Increment(ref _activeBackgroundJobs.Value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DecrementActiveLaneJobs(JobLane lane)
    {
        if (lane == JobLane.Frame)
        {
            Interlocked.Decrement(ref _activeFrameJobs.Value);
        }
        else
        {
            Interlocked.Decrement(ref _activeBackgroundJobs.Value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetActiveLaneJobs(JobLane lane)
    {
        return lane == JobLane.Frame
            ? Volatile.Read(ref _activeFrameJobs.Value)
            : Volatile.Read(ref _activeBackgroundJobs.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int AllocateSlot(int reservedAdditionalEdges = 0)
    {
        if (_freeSlotCount == 0 || _freeEdgeCount < reservedAdditionalEdges)
        {
            ReclaimCompletedErrorFreeSlots();
        }

        if (_freeSlotCount == 0)
        {
            throw new InvalidOperationException("Not enough preallocated work slots. Increase Capacity before scheduling.");
        }

        if (_freeEdgeCount < reservedAdditionalEdges)
        {
            throw new InvalidOperationException("Not enough preallocated dependency edges. Increase Capacity before scheduling.");
        }

        int slot = _freeSlots[--_freeSlotCount];
        _slotInUse[slot] = 1;
        int activePos = _activeSlotCount++;
        _activeSlots[activePos] = slot;
        _slotActivePos[slot] = activePos;
        return slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int AllocateEdge()
    {
        if (_freeEdgeCount == 0)
        {
            ReclaimCompletedErrorFreeSlots();
            if (_freeEdgeCount == 0)
            {
                throw new InvalidOperationException("Not enough preallocated dependency edges. Increase Capacity before scheduling.");
            }
        }

        return _freeEdges[--_freeEdgeCount];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FreeEdge(int edgeIndex)
    {
        _freeEdges[_freeEdgeCount++] = edgeIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsHandleRelevantForDependency(JobHandle handle)
    {
        if (!handle.IsValid || (uint)handle.Index >= (uint)_hot.Length)
        {
            return false;
        }

        int slot = handle.Index;
        if (_slotInUse[slot] == 0 || _slotGenerations[slot] != handle.Generation)
        {
            return false;
        }

        return Volatile.Read(ref _hot[slot].Done) == 0 ||
               Volatile.Read(ref _hot[slot].Failed) != 0 ||
               Volatile.Read(ref _hot[slot].InheritedError) != 0;
    }

    private bool AttachSingleDependency(JobHandle parent, int childSlot)
    {
        int parentSlot = parent.Index;
        int parentGen = parent.Generation;
        if (_slotInUse[parentSlot] == 0 || _slotGenerations[parentSlot] != parentGen)
        {
            return false;
        }

        if (Volatile.Read(ref _hot[parentSlot].Done) != 0)
        {
            PropagateCompletedParentErrorOnProducer(parentSlot, parentGen, childSlot);
            return false;
        }

        int edgeIndex = AllocateEdge();
        _edges[edgeIndex].ChildSlot = childSlot;

        ref int dependentHead = ref _hot[parentSlot].DependentHead;
        while (true)
        {
            int head = Volatile.Read(ref dependentHead);
            if (head == CompletedSentinel)
            {
                FreeEdge(edgeIndex);
                PropagateCompletedParentErrorOnProducer(parentSlot, parentGen, childSlot);
                return false;
            }

            _edges[edgeIndex].Next = head;
            if (Interlocked.CompareExchange(ref dependentHead, edgeIndex, head) == head)
            {
                return true;
            }
        }
    }

    private void PropagateCompletedParentErrorOnProducer(int parentSlot, int parentGen, int childSlot)
    {
        if (Volatile.Read(ref _hot[parentSlot].Failed) == 0 &&
            Volatile.Read(ref _hot[parentSlot].InheritedError) == 0)
        {
            return;
        }

        RecordFailedAncestor(childSlot, new JobHandle(parentSlot, parentGen));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong PackTicket(int slot, int generation) => ((ulong)(uint)generation << 32) | (uint)slot;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnqueueAndWake(int slot, int generation, JobLane lane, int ticketCount)
    {
        ulong ticket = PackTicket(slot, generation);
        WorkerLane targetLane = lane == JobLane.Frame ? _frameLane : _backgroundLane;
        targetLane.EnqueueAndWake(ticket, ticketCount, callerTrackIndex: 0);
    }

    private void EvictOrHelpOneQueuedTicket(WorkerLane lane, BoundedTicketQueue queue, int callerTrackIndex)
    {
        if (lane == _frameLane)
        {
            if (queue.TryDequeue(out ulong frameTicket))
            {
                ExecuteTicket(frameTicket, callerTrackIndex);
            }
            return;
        }

        const int MaxRotate = 32;
        Span<ulong> live = stackalloc ulong[MaxRotate];
        int liveCount = 0;

        for (int i = 0; i < MaxRotate; i++)
        {
            if (!queue.TryDequeue(out ulong ticket))
            {
                break;
            }

            int slot = (int)(uint)ticket;
            int gen = (int)(uint)(ticket >> 32);
            long state = Volatile.Read(ref _hot[slot].ClaimState);
            long expectedPrefix = (long)(uint)gen << 32;
            if ((state & ~ActiveMask) == expectedPrefix && Volatile.Read(ref _hot[slot].Done) == 0)
            {
                live[liveCount++] = ticket;
            }
            else
            {
                // Stale ticket discarded, opening space for live tickets and the caller's enqueue.
                break;
            }
        }

        for (int i = 0; i < liveCount; i++)
        {
            while (!queue.TryEnqueue(live[i]))
            {
                Thread.SpinWait(1);
            }
        }

        if (liveCount == MaxRotate)
        {
            Thread.Yield();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteTicket(ulong ticket, int callerTrackIndex)
    {
        int slot = (int)(uint)ticket;
        int gen = (int)(uint)(ticket >> 32);
        TryExecuteSlot(slot, gen, JobLane.Frame, allowContinuation: false, callerTrackIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong ExecuteTicketWithContinuation(ulong ticket, JobLane callerLane, int trackIndex)
    {
        int slot = (int)(uint)ticket;
        int gen = (int)(uint)(ticket >> 32);
        return TryExecuteSlot(slot, gen, callerLane, allowContinuation: true, trackIndex);
    }

    private ulong TryExecuteSlot(int slot, int expectedGen, JobLane callerLane, bool allowContinuation, int trackIndex)
    {
        ref NodeHot hot = ref _hot[slot];
        if (Volatile.Read(ref hot.UnresolvedDeps) != 0)
        {
            return 0;
        }

        long expectedPrefix = (long)(uint)expectedGen << 32;
        long current = Volatile.Read(ref hot.ClaimState);
        while (true)
        {
            if ((current & ~ActiveMask) != expectedPrefix)
            {
                return 0;
            }

            long next = Interlocked.CompareExchange(ref hot.ClaimState, current + 1, current);
            if (next == current)
            {
                break;
            }
            current = next;
        }

        ref NodeData data = ref _data[slot];
        JobLane lane = data.Lane;
        JobKind kind = data.Kind;
        bool profile = _profilingEnabled;
        JobProfiler? profiler = profile ? _profiler : null;
        ushort typeId = profiler != null ? data.ProfileTypeId : (ushort)0;
        JobProfileEventKind eventKind = trackIndex == 0 ? JobProfileEventKind.MainHelp : JobProfileEventKind.JobExecute;
        JobProfileWorkKind workKind = kind switch
        {
            JobKind.Single => JobProfileWorkKind.Single,
            JobKind.CallbackOnly => JobProfileWorkKind.CallbackOnly,
            JobKind.Batch => JobProfileWorkKind.Batch,
            JobKind.BatchRange => JobProfileWorkKind.BatchRange,
            _ => JobProfileWorkKind.None,
        };
        long execStart = 0;
        int localChunks = 0;
        int localItems = 0;
        bool spanRecorded = false;
        bool finishedLast = false;
        ulong continuationTicket = 0;

        try
        {
            if (kind == JobKind.Single)
            {
                if (Interlocked.Exchange(ref hot.NextIndex, 1) == 0)
                {
                    if (profiler != null)
                    {
                        execStart = Stopwatch.GetTimestamp();
                        profiler.BeginActiveSpan(trackIndex, execStart, typeId, lane, JobProfileWorkKind.Single, eventKind);
                    }
                    localChunks = 1;
                    localItems = 1;
                    try
                    {
                        data.SingleJob!.Execute();
                    }
                    catch (Exception ex)
                    {
                        RecordError(slot, ex);
                    }
                    finishedLast = Interlocked.Decrement(ref hot.RemainingItems) == 0;
                }
            }
            else if (kind == JobKind.CallbackOnly)
            {
                if (Interlocked.Exchange(ref hot.NextIndex, 1) == 0)
                {
                    if (profiler != null)
                    {
                        execStart = Stopwatch.GetTimestamp();
                        profiler.BeginActiveSpan(trackIndex, execStart, typeId, lane, JobProfileWorkKind.CallbackOnly, eventKind);
                    }
                    localChunks = 1;
                    localItems = 1;
                    try
                    {
                        data.OnComplete?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        RecordError(slot, ex);
                    }
                    finishedLast = Interlocked.Decrement(ref hot.RemainingItems) == 0;
                }
            }
            else
            {
                int totalCount = data.BatchCount;
                int chunkSize = data.ChunkSize;
                int localCompletedItems = 0;

                if (chunkSize > 0)
                {
                    while (true)
                    {
                        int start = Interlocked.Add(ref hot.NextIndex, chunkSize) - chunkSize;
                        if ((uint)start >= (uint)totalCount)
                        {
                            break;
                        }

                        if (profiler != null && execStart == 0)
                        {
                            execStart = Stopwatch.GetTimestamp();
                            profiler.BeginActiveSpan(trackIndex, execStart, typeId, lane, workKind, eventKind);
                        }

                        int itemCount = Math.Min(chunkSize, totalCount - start);
                        ExecuteBatchRange(ref data, slot, kind, start, itemCount);
                        localCompletedItems += itemCount;
                        localChunks++;
                    }
                }
                else
                {
                    int stageChunks = data.GuidedDivisor;
                    int minChunk = Math.Max(1, totalCount >> 10);
                    while (true)
                    {
                        int chunkId = Interlocked.Increment(ref hot.NextIndex) - 1;
                        if (!TryGetHalvingChunkRange(chunkId, totalCount, stageChunks, minChunk, out int start, out int itemCount))
                        {
                            break;
                        }

                        if (profiler != null && execStart == 0)
                        {
                            execStart = Stopwatch.GetTimestamp();
                            profiler.BeginActiveSpan(trackIndex, execStart, typeId, lane, workKind, eventKind);
                        }

                        ExecuteBatchRange(ref data, slot, kind, start, itemCount);
                        localCompletedItems += itemCount;
                        localChunks++;
                    }
                }

                if (localCompletedItems > 0)
                {
                    localItems = localCompletedItems;
                    finishedLast = Interlocked.Add(ref hot.RemainingItems, -localCompletedItems) == 0;
                }
            }

            if (finishedLast &&
                kind != JobKind.CallbackOnly &&
                Volatile.Read(ref hot.Failed) == 0 &&
                data.OnComplete is { } callback)
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    RecordError(slot, ex);
                }
            }

            if (execStart != 0 && !spanRecorded)
            {
                spanRecorded = true;
                profiler!.EndActiveSpan(
                    trackIndex,
                    execStart,
                    Stopwatch.GetTimestamp(),
                    typeId,
                    lane,
                    workKind,
                    eventKind,
                    localItems,
                    localChunks,
                    failed: Volatile.Read(ref hot.Failed) != 0);
            }

            if (finishedLast)
            {
                int edgeHead = Interlocked.Exchange(ref hot.DependentHead, CompletedSentinel);
                data.CompletedEdgeHead = edgeHead;
                if (edgeHead != EmptyDependentHead)
                {
                    continuationTicket = ReleaseDependents(slot, callerLane, allowContinuation, trackIndex);
                }

                Interlocked.Or(ref hot.ClaimState, ClosedBit);
            }
        }
        finally
        {
            if (execStart != 0 && !spanRecorded)
            {
                spanRecorded = true;
                profiler!.EndActiveSpan(
                    trackIndex,
                    execStart,
                    Stopwatch.GetTimestamp(),
                    typeId,
                    lane,
                    workKind,
                    eventKind,
                    localItems,
                    localChunks,
                    failed: Volatile.Read(ref hot.Failed) != 0);
            }

            long after = Interlocked.Decrement(ref hot.ClaimState);
            if ((after & ClosedBit) != 0 && (after & ActiveMask) == 0)
            {
                MarkSlotDone(slot, lane);
            }
        }

        return continuationTicket;
    }

    private void CompleteFenceInline(int fenceSlot)
    {
        int edgeHead = Interlocked.Exchange(ref _hot[fenceSlot].DependentHead, CompletedSentinel);
        _data[fenceSlot].CompletedEdgeHead = edgeHead;
        if (edgeHead != EmptyDependentHead)
        {
            ReleaseDependents(fenceSlot, JobLane.Frame, allowContinuation: false, callerTrackIndex: 0);
        }

        Interlocked.Exchange(ref _hot[fenceSlot].Done, 1);
        NotifyMainIfWaiting();
    }

    private ulong ReleaseDependents(int rootCompletedSlot, JobLane callerLane, bool allowContinuation, int callerTrackIndex)
    {
        const int StackLimit = 64;
        Span<int> localStack = stackalloc int[StackLimit];
        int localTop = 0;
        Stack<int>? overflowStack = null;

        ulong localContinuation = 0;
        int frameWakeTickets = 0;
        int bgWakeTickets = 0;
        bool notifyMainFrame = false;

        localStack[localTop++] = rootCompletedSlot;

        while (localTop > 0 || (overflowStack != null && overflowStack.Count > 0))
        {
            int completedSlot = overflowStack != null && overflowStack.Count > 0
                ? overflowStack.Pop()
                : localStack[--localTop];

            ref NodeHot completedHot = ref _hot[completedSlot];
            bool parentFailed = Volatile.Read(ref completedHot.Failed) != 0 ||
                                Volatile.Read(ref completedHot.InheritedError) != 0;
            int completedGen = (int)(uint)(Volatile.Read(ref completedHot.ClaimState) >> 32);

            int edge = _data[completedSlot].CompletedEdgeHead;
            while (edge != EmptyDependentHead)
            {
                int childSlot = _edges[edge].ChildSlot;
                edge = _edges[edge].Next;

                if (parentFailed)
                {
                    RecordFailedAncestor(childSlot, new JobHandle(completedSlot, completedGen));
                }

                if (Interlocked.Decrement(ref _hot[childSlot].UnresolvedDeps) != 0)
                {
                    continue;
                }

                ref NodeData childData = ref _data[childSlot];
                if (childData.Kind == JobKind.Fence)
                {
                    int fenceEdgeHead = Interlocked.Exchange(ref _hot[childSlot].DependentHead, CompletedSentinel);
                    childData.CompletedEdgeHead = fenceEdgeHead;
                    if (fenceEdgeHead != EmptyDependentHead)
                    {
                        if (localTop < StackLimit)
                        {
                            localStack[localTop++] = childSlot;
                        }
                        else
                        {
                            overflowStack ??= new Stack<int>();
                            overflowStack.Push(childSlot);
                        }
                    }
                    else
                    {
                        Interlocked.Exchange(ref _hot[childSlot].Done, 1);
                        NotifyMainIfWaiting();
                    }
                }
                else
                {
                    int childGen = (int)(uint)(Volatile.Read(ref _hot[childSlot].ClaimState) >> 32);
                    int ticketCount = CalculateTicketCount(ref childData);
                    ulong childTicket = PackTicket(childSlot, childGen);
                    JobLane childLane = childData.Lane;

                    int queuedTickets = ticketCount;
                    if (allowContinuation && localContinuation == 0 && childLane == callerLane)
                    {
                        localContinuation = childTicket;
                        queuedTickets = ticketCount - 1;
                    }

                    if (queuedTickets > 0)
                    {
                        WorkerLane targetLane = childLane == JobLane.Frame ? _frameLane : _backgroundLane;
                        targetLane.EnqueueNoWake(childTicket, queuedTickets, callerTrackIndex);
                        if (childLane == JobLane.Frame)
                        {
                            frameWakeTickets += queuedTickets;
                        }
                        else
                        {
                            bgWakeTickets += queuedTickets;
                        }
                    }

                    if (childLane == JobLane.Frame)
                    {
                        notifyMainFrame = true;
                    }
                }
            }

            if (completedSlot != rootCompletedSlot)
            {
                Interlocked.Exchange(ref _hot[completedSlot].Done, 1);
                NotifyMainIfWaiting();
            }
        }

        if (frameWakeTickets > 0)
        {
            _frameLane.Wake(frameWakeTickets);
        }

        if (bgWakeTickets > 0)
        {
            _backgroundLane.Wake(bgWakeTickets);
        }

        if (notifyMainFrame)
        {
            if (frameWakeTickets == 0)
            {
                Thread.MemoryBarrier();
            }
            NotifyMainIfWaiting();
        }

        return localContinuation;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetHalvingChunkRange(
        int chunkId,
        int totalCount,
        int stageChunks,
        int minChunk,
        out int start,
        out int itemCount)
    {
        int curStart = 0;
        int remTotal = totalCount;
        int id = chunkId;

        while (true)
        {
            int stageTotal = (int)(((uint)remTotal + 1u) >> 1);
            int chunk = stageTotal / stageChunks;
            if (chunk <= minChunk)
            {
                long tailStart = (long)curStart + (long)id * minChunk;
                if (tailStart >= totalCount)
                {
                    start = 0;
                    itemCount = 0;
                    return false;
                }

                start = (int)tailStart;
                itemCount = (int)Math.Min(minChunk, totalCount - tailStart);
                return true;
            }

            if (id < stageChunks)
            {
                start = curStart + id * chunk;
                itemCount = Math.Min(chunk, totalCount - start);
                return true;
            }

            int covered = stageChunks * chunk;
            curStart += covered;
            remTotal -= covered;
            id -= stageChunks;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteBatchRange(ref NodeData data, int slot, JobKind kind, int start, int itemCount)
    {
        try
        {
            if (kind == JobKind.Batch)
            {
                IBatchJob batchJob = data.BatchJob!;
                int end = start + itemCount;
                for (int i = start; i < end; i++)
                {
                    batchJob.Execute(i);
                }
            }
            else
            {
                data.BatchRangeJob!.Execute(start, itemCount);
            }
        }
        catch (Exception ex)
        {
            RecordError(slot, ex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculateTicketCount(ref NodeData data)
    {
        if (data.Kind == JobKind.Single || data.Kind == JobKind.CallbackOnly)
        {
            return 1;
        }

        int laneWorkers = data.Lane == JobLane.Frame ? _frameLane.WorkerCount : _backgroundLane.WorkerCount;
        int chunkSize = data.ChunkSize;
        int maxConcurrency = chunkSize > 0
            ? (data.BatchCount + chunkSize - 1) / chunkSize
            : data.BatchCount;
        return Math.Min(maxConcurrency, laneWorkers);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkSlotDone(int slot, JobLane lane)
    {
        Volatile.Write(ref _hot[slot].Done, 1);
        DecrementActiveLaneJobs(lane);
        NotifyMainIfWaiting();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NotifyMainIfWaiting()
    {
        if (Volatile.Read(ref _mainWaiting.Value) != 0)
        {
            _mainWakeSignal.Set();
        }
    }

    private void WaitUntilSlotDone(int slot)
    {
        while (Volatile.Read(ref _hot[slot].Done) == 0)
        {
            if (_enableMainThreadHelp && _frameLane.TryDequeue(out ulong ticket))
            {
                while (ticket != 0)
                {
                    ticket = ExecuteTicketWithContinuation(ticket, JobLane.Frame, trackIndex: 0);
                    if (Volatile.Read(ref _hot[slot].Done) != 0)
                    {
                        if (ticket != 0)
                        {
                            _frameLane.EnqueueAndWake(ticket, 1, callerTrackIndex: 0);
                        }
                        return;
                    }
                }
                continue;
            }

            int spins = 0;
            long spinStart = _profilingEnabled ? Stopwatch.GetTimestamp() : 0;
            while (spins < MainThreadSpinCount && Volatile.Read(ref _hot[slot].Done) == 0)
            {
                if (_enableMainThreadHelp && !_frameLane.IsQueueEmpty)
                {
                    break;
                }
                Thread.SpinWait(1);
                spins++;
            }

            if (spinStart != 0 && spins > 0)
            {
                _profiler?.RecordMainWait(JobProfileEventKind.MainWaitSpin, spinStart, Stopwatch.GetTimestamp());
            }

            if (Volatile.Read(ref _hot[slot].Done) != 0)
            {
                break;
            }

            if (_enableMainThreadHelp && !_frameLane.IsQueueEmpty)
            {
                continue;
            }

            Interlocked.Exchange(ref _mainWaiting.Value, 1);
            if (Volatile.Read(ref _hot[slot].Done) == 0 &&
                (!_enableMainThreadHelp || _frameLane.IsQueueEmpty))
            {
                long sleepStart = _profilingEnabled ? Stopwatch.GetTimestamp() : 0;
                _mainWakeSignal.WaitOne();
                if (sleepStart != 0)
                {
                    _profiler?.RecordMainWait(JobProfileEventKind.MainWaitSleep, sleepStart, Stopwatch.GetTimestamp());
                }
            }
            Volatile.Write(ref _mainWaiting.Value, 0);
        }
    }

    private void WaitLaneCore(JobLane lane)
    {
        while (GetActiveLaneJobs(lane) != 0)
        {
            if (_enableMainThreadHelp && _frameLane.TryDequeue(out ulong ticket))
            {
                while (ticket != 0)
                {
                    ticket = ExecuteTicketWithContinuation(ticket, JobLane.Frame, trackIndex: 0);
                    if (GetActiveLaneJobs(lane) == 0)
                    {
                        if (ticket != 0)
                        {
                            _frameLane.EnqueueAndWake(ticket, 1, callerTrackIndex: 0);
                        }
                        return;
                    }
                }
                continue;
            }

            int spins = 0;
            long spinStart = _profilingEnabled ? Stopwatch.GetTimestamp() : 0;
            while (spins < MainThreadSpinCount && GetActiveLaneJobs(lane) != 0)
            {
                if (_enableMainThreadHelp && !_frameLane.IsQueueEmpty)
                {
                    break;
                }
                Thread.SpinWait(1);
                spins++;
            }

            if (spinStart != 0 && spins > 0)
            {
                _profiler?.RecordMainWait(JobProfileEventKind.MainWaitSpin, spinStart, Stopwatch.GetTimestamp());
            }

            if (GetActiveLaneJobs(lane) == 0)
            {
                break;
            }

            if (_enableMainThreadHelp && !_frameLane.IsQueueEmpty)
            {
                continue;
            }

            Interlocked.Exchange(ref _mainWaiting.Value, 1);
            if (GetActiveLaneJobs(lane) != 0 &&
                (!_enableMainThreadHelp || _frameLane.IsQueueEmpty))
            {
                long sleepStart = _profilingEnabled ? Stopwatch.GetTimestamp() : 0;
                _mainWakeSignal.WaitOne();
                if (sleepStart != 0)
                {
                    _profiler?.RecordMainWait(JobProfileEventKind.MainWaitSleep, sleepStart, Stopwatch.GetTimestamp());
                }
            }
            Volatile.Write(ref _mainWaiting.Value, 0);
        }
    }

    private void RecordError(int slot, Exception error)
    {
        lock (_errorLock)
        {
            List<Exception>? errors = _data[slot].Errors;
            if (errors == null)
            {
                errors = new List<Exception>(1);
                _data[slot].Errors = errors;
            }
            errors.Add(error);
        }

        Volatile.Write(ref _hot[slot].Failed, 1);
    }

    private void RecordFailedAncestor(int childSlot, JobHandle parent)
    {
        lock (_errorLock)
        {
            List<JobHandle>? ancestors = _data[childSlot].FailedAncestors;
            if (ancestors == null)
            {
                ancestors = new List<JobHandle>(1);
                _data[childSlot].FailedAncestors = ancestors;
            }
            ancestors.Add(parent);
        }

        Volatile.Write(ref _hot[childSlot].InheritedError, 1);
    }

    private List<Exception>? ConsumeErrorsForSubtree(int rootSlot)
    {
        if (Volatile.Read(ref _hot[rootSlot].Failed) == 0 &&
            Volatile.Read(ref _hot[rootSlot].InheritedError) == 0)
        {
            return null;
        }

        List<Exception>? collected = null;
        Stack<int> stack = new();
        stack.Push(rootSlot);

        while (stack.Count > 0)
        {
            int slot = stack.Pop();
            List<Exception>? ownErrors;
            List<JobHandle>? ancestors;

            lock (_errorLock)
            {
                ownErrors = _data[slot].Errors;
                _data[slot].Errors = null;
                ancestors = _data[slot].FailedAncestors;
                _data[slot].FailedAncestors = null;
            }

            Volatile.Write(ref _hot[slot].Failed, 0);
            Volatile.Write(ref _hot[slot].InheritedError, 0);

            if (ownErrors != null)
            {
                collected ??= new List<Exception>(ownErrors.Count);
                collected.AddRange(ownErrors);
            }

            if (ancestors != null)
            {
                for (int i = 0; i < ancestors.Count; i++)
                {
                    JobHandle anc = ancestors[i];
                    if ((uint)anc.Index >= (uint)_hot.Length)
                    {
                        continue;
                    }

                    int ancSlot = anc.Index;
                    if (_slotInUse[ancSlot] == 0 ||
                        _slotGenerations[ancSlot] != anc.Generation ||
                        Volatile.Read(ref _hot[ancSlot].Done) == 0)
                    {
                        continue;
                    }

                    stack.Push(ancSlot);
                }
            }
        }

        return collected;
    }

    private List<Exception>? CollectAndReclaimAllSlots()
    {
        List<Exception>? errors = null;
        for (int idx = _activeSlotCount - 1; idx >= 0; idx--)
        {
            int i = _activeSlots[idx];
            if (Volatile.Read(ref _hot[i].Done) == 0)
            {
                continue;
            }

            if (Volatile.Read(ref _hot[i].Failed) != 0)
            {
                lock (_errorLock)
                {
                    if (_data[i].Errors is { } slotErrors)
                    {
                        errors ??= new List<Exception>(slotErrors.Count);
                        errors.AddRange(slotErrors);
                        _data[i].Errors = null;
                    }
                }
            }

            ReclaimSlot(i);
        }

        return errors;
    }

    private List<Exception>? CollectAndReclaimLaneSlots(JobLane lane)
    {
        List<Exception>? errors = null;
        for (int idx = _activeSlotCount - 1; idx >= 0; idx--)
        {
            int i = _activeSlots[idx];
            if (Volatile.Read(ref _hot[i].Done) == 0)
            {
                continue;
            }

            ref NodeData data = ref _data[i];
            if (data.Kind == JobKind.Fence)
            {
                if (Volatile.Read(ref _hot[i].InheritedError) == 0)
                {
                    ReclaimSlot(i);
                }
                continue;
            }

            if (data.Lane != lane)
            {
                continue;
            }

            List<Exception>? slotErrors = ConsumeErrorsForSubtree(i);
            if (slotErrors != null)
            {
                errors ??= new List<Exception>(slotErrors.Count);
                errors.AddRange(slotErrors);
            }

            ReclaimSlot(i);
        }

        return errors;
    }

    private void ReclaimCompletedErrorFreeSlots()
    {
        for (int idx = _activeSlotCount - 1; idx >= 0; idx--)
        {
            int i = _activeSlots[idx];
            ref NodeHot hot = ref _hot[i];
            if (Volatile.Read(ref hot.Done) == 0)
            {
                continue;
            }

            if (Volatile.Read(ref hot.Failed) != 0 || Volatile.Read(ref hot.InheritedError) != 0)
            {
                continue;
            }

            ReclaimSlot(i);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReclaimSlot(int slot)
    {
        ref NodeData data = ref _data[slot];
        int edge = data.CompletedEdgeHead;
        while (edge != EmptyDependentHead)
        {
            int next = _edges[edge].Next;
            FreeEdge(edge);
            edge = next;
        }

        data.CompletedEdgeHead = EmptyDependentHead;
        data.ProfileTypeId = 0;
        data.SingleJob = null;
        data.BatchJob = null;
        data.BatchRangeJob = null;
        data.OnComplete = null;
        data.Errors = null;
        data.FailedAncestors = null;

        int nextGen = _slotGenerations[slot] + 1;
        _slotGenerations[slot] = nextGen <= 0 ? 1 : nextGen;

        Volatile.Write(ref _hot[slot].ClaimState, 0);
        int activePos = _slotActivePos[slot];
        int lastPos = --_activeSlotCount;
        if (activePos != lastPos)
        {
            int swappedSlot = _activeSlots[lastPos];
            _activeSlots[activePos] = swappedSlot;
            _slotActivePos[swappedSlot] = activePos;
        }
        _slotInUse[slot] = 0;
        _freeSlots[_freeSlotCount++] = slot;
    }
}
