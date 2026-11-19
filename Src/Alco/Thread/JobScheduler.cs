using System;
using System.Collections.Generic;
using System.Threading;

namespace Alco;

/// <summary>
/// Dispatches jobs to the shared thread pool using preallocated work items.
/// Use a single producer thread; jobs and callbacks must not call back into this scheduler.
/// </summary>
public sealed class JobScheduler : IDisposable
{
    private const int DefaultCapacity = 256;
    private const int ProducerToken = 1;

    private readonly AutoResetEvent _completed = new(false);
    private readonly int _processorCount = Environment.ProcessorCount;
    private WorkItem[] _workItems = [];
    private BatchState[] _batches = [];
    private int _itemCount;
    private int _pendingJobs = ProducerToken;
    private bool _hasErrors;
    private bool _disposed;

    /// <summary>Creates a scheduler with 256 preallocated work slots.</summary>
    public JobScheduler() => Capacity = DefaultCapacity;

    /// <summary>
    /// Gets or sets the number of preallocated work slots between calls to <see cref="WaitAll"/>.
    /// Change the capacity only when no work has been submitted since the last wait.
    /// </summary>
    public int Capacity
    {
        get => _workItems.Length;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            if (value == _workItems.Length) return;
            if (_itemCount != 0)
                throw new InvalidOperationException("Wait for scheduled work before changing capacity.");

            var workItems = new WorkItem[value];
            int retained = Math.Min(_workItems.Length, value);
            Array.Copy(_workItems, workItems, retained);
            for (int i = retained; i < workItems.Length; i++)
                workItems[i] = new WorkItem(this);

            _batches = new BatchState[value];
            _workItems = workItems;
        }
    }

    /// <summary>Queues a job without growing the scheduler's storage.</summary>
    /// <param name="job">The job. Reuse a reference-type instance to avoid caller-side boxing.</param>
    /// <param name="onJobComplete">A reusable callback invoked on the worker after successful execution.</param>
    /// <exception cref="InvalidOperationException">No preallocated work slot is available.</exception>
    public void Schedule(IJob job, JobDelegate? onJobComplete = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(job);
        var item = _workItems[ReserveWorkItems(1)];
        item.Prepare(job, onJobComplete);
        Interlocked.Increment(ref _pendingJobs);
        ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
    }

    /// <summary>Queues a batch using at most one work slot per logical processor and one per index.</summary>
    /// <param name="job">The batch job. Its count is read on the producer thread.</param>
    /// <param name="onJobComplete">A reusable callback invoked once on a worker after all indices succeed. An empty batch uses one slot if a callback is provided.</param>
    /// <exception cref="InvalidOperationException">The batch does not fit in the preallocated work slots.</exception>
    public void Schedule(IBatchJob job, JobDelegate? onJobComplete = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(job);
        int count = job.Count;
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0 && onJobComplete is null) return;

        int partitions = Math.Max(1, Math.Min(count, _processorCount));
        int first = ReserveWorkItems(partitions);
        _batches[first] = new BatchState(job, onJobComplete, partitions);
        Interlocked.Increment(ref _pendingJobs);

        int size = count / partitions;
        int remainder = count % partitions;
        int start = 0;
        for (int i = 0; i < partitions; i++)
        {
            int end = start + size + (i < remainder ? 1 : 0);
            var item = _workItems[first + i];
            item.Prepare(first, start, end);
            ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
            start = end;
        }
    }

    /// <summary>Waits for this instance's jobs and their callbacks, then makes their work slots reusable.</summary>
    /// <exception cref="AggregateException">One or more jobs or callbacks failed.</exception>
    public void WaitAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WaitAllCore();
    }

    /// <summary>Waits for pending work and disposes the completion signal.</summary>
    /// <exception cref="AggregateException">One or more pending jobs or callbacks failed.</exception>
    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            WaitAllCore();
        }
        finally
        {
            _completed.Dispose();
            _disposed = true;
        }
    }

    private int ReserveWorkItems(int count)
    {
        int first = _itemCount;
        if (count > _workItems.Length - first)
            throw new InvalidOperationException("Not enough preallocated work slots. Increase Capacity before scheduling.");
        _itemCount += count;
        return first;
    }

    private void WaitAllCore()
    {
        int itemCount = _itemCount;
        if (itemCount == 0) return;

        // The producer holds one count until it enters the barrier.
        var spin = new SpinWait();
        while (Volatile.Read(ref _pendingJobs) != ProducerToken && !spin.NextSpinWillYield)
            spin.SpinOnce();
        if (Interlocked.Decrement(ref _pendingJobs) != 0)
            _completed.WaitOne();
        _pendingJobs = ProducerToken;
        _itemCount = 0;

        if (!_hasErrors) return;
        _hasErrors = false;
        var errors = new List<Exception>();
        for (int i = 0; i < itemCount; i++)
        {
            if (_workItems[i].TakeError() is { } error)
                errors.Add(error);
        }
        throw new AggregateException(errors);
    }

    private void CompleteJob()
    {
        if (Interlocked.Decrement(ref _pendingJobs) == 0)
            _completed.Set();
    }

    private struct BatchState(IBatchJob job, JobDelegate? callback, int remaining)
    {
        public readonly IBatchJob Job = job;
        public readonly JobDelegate? Callback = callback;
        public int Remaining = remaining;
        public bool Failed;
    }

    private sealed class WorkItem(JobScheduler scheduler) : IThreadPoolWorkItem
    {
        private IJob? _job;
        private JobDelegate? _callback;
        private int _batchIndex;
        private int _start;
        private int _end;
        private Exception? _error;

        /// <summary>Assigns a single job to the work slot.</summary>
        public void Prepare(IJob job, JobDelegate? callback)
        {
            _job = job;
            _callback = callback;
        }

        /// <summary>Assigns a range of batch indices to the work slot.</summary>
        public void Prepare(int batchIndex, int start, int end)
        {
            _batchIndex = batchIndex;
            _start = start;
            _end = end;
        }

        /// <summary>Returns and clears an error after the barrier.</summary>
        public Exception? TakeError()
        {
            var error = _error;
            _error = null;
            return error;
        }

        void IThreadPoolWorkItem.Execute()
        {
            if (_job is not { } job)
            {
                ExecuteBatchRange();
                return;
            }

            try
            {
                job.Execute();
                _callback?.Invoke();
            }
            catch (Exception error)
            {
                RecordError(error);
            }
            finally
            {
                _job = null;
                _callback = null;
                scheduler.CompleteJob();
            }
        }

        private void ExecuteBatchRange()
        {
            ref var batch = ref scheduler._batches[_batchIndex];
            try
            {
                var job = batch.Job;
                for (int i = _start; i < _end; i++)
                    job.Execute(i);
            }
            catch (Exception error)
            {
                RecordError(error);
                batch.Failed = true;
            }

            if (Interlocked.Decrement(ref batch.Remaining) != 0) return;
            try
            {
                if (!batch.Failed) batch.Callback?.Invoke();
            }
            catch (Exception error)
            {
                RecordError(error);
            }
            finally
            {
                batch = default;
                scheduler.CompleteJob();
            }
        }

        private void RecordError(Exception error)
        {
            _error = error;
            scheduler._hasErrors = true;
        }
    }
}
