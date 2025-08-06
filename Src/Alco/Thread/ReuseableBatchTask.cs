using System;
using System.Collections.Generic;
using System.Threading;

namespace Alco;

/// <summary>
/// Represents a reusable parallel task that can execute work items concurrently.
/// This class provides parallel execution of indexed operations with optimized
/// object reuse to minimize allocations.
/// </summary>
public abstract class ReuseableBatchTask : AutoDisposable
{
    /// <summary>
    /// Represents a work item that can be executed in parallel.
    /// </summary>
    private class TaskItem : IThreadPoolWorkItem
    {
        /// <summary>
        /// The starting index for this work item.
        /// </summary>
        public volatile int IndexStart;

        /// <summary>
        /// The number of iterations this work item should process.
        /// </summary>
        public volatile int Count;

        /// <summary>
        /// Exception that occurred during execution, if any.
        /// </summary>
        public volatile Exception? Exception;

        /// <summary>
        /// The parent task that owns this item.
        /// </summary>
        private readonly ReuseableBatchTask _task;

        /// <summary>
        /// Initializes a new instance of the TaskItem class.
        /// </summary>
        /// <param name="task">The parent task.</param>
        public TaskItem(ReuseableBatchTask task)
        {
            _task = task;
        }

        /// <summary>
        /// Executes the work item by processing the assigned range of indices.
        /// </summary>
        public void Execute()
        {
            Exception = null;
            try
            {
                for (int i = IndexStart; i < IndexStart + Count; i++)
                {
                    _task.ExecuteCore(i);
                }
            }
            catch (Exception e)
            {
                Exception = e;
            }
            finally
            {
                // Signal completion
                _task._completionCount.Release();
            }
        }
    }

    private readonly SemaphoreSlim _completionCount;
    private readonly int _maxConcurrency;
    private readonly List<TaskItem> _taskItems;
    private readonly List<Exception> _exceptions;

    /// <summary>
    /// Initializes a new instance of the ReuseableParallelTask class.
    /// </summary>
    /// <param name="maxConcurrency">The maximum number of concurrent tasks. Defaults to the processor count.</param>
    public ReuseableBatchTask(int maxConcurrency = 0)
    {
        _maxConcurrency = maxConcurrency <= 0 ? Environment.ProcessorCount : maxConcurrency;
        _completionCount = new SemaphoreSlim(0, int.MaxValue);
        _taskItems = new List<TaskItem>();
        _exceptions = new List<Exception>();
    }

    /// <summary>
    /// Executes the core logic for a specific index. Must be implemented by derived classes.
    /// </summary>
    /// <param name="index">The index to process.</param>
    protected abstract void ExecuteCore(int index);

    /// <summary>
    /// Executes the parallel task for the specified range of indices.
    /// </summary>
    /// <param name="totalCount">The total number of iterations to process.</param>
    /// <param name="batchSize">The size of each batch. If null, it will be calculated automatically.</param>
    /// <exception cref="AggregateException">Thrown when one or more exceptions occur during parallel execution.</exception>
    public void RunParallel(int totalCount, int? batchSize = null)
    {
        if (totalCount <= 0) return;

        int actualBatchSize = batchSize ?? Math.Max(1, totalCount / _maxConcurrency);
        int taskCount = (totalCount + actualBatchSize - 1) / actualBatchSize;

        // Ensure we have enough TaskItem objects
        while (_taskItems.Count < taskCount)
        {
            _taskItems.Add(new TaskItem(this));
        }

        // Queue all tasks
        for (int i = 0; i < taskCount; i++)
        {
            int start = i * actualBatchSize;
            int count = Math.Min(actualBatchSize, totalCount - start);

            var item = _taskItems[i];
            item.IndexStart = start;
            item.Count = count;
            item.Exception = null;
            ThreadPool.UnsafeQueueUserWorkItem(item, false);
        }

        // Wait for all tasks to complete
        for (int i = 0; i < taskCount; i++)
        {
            _completionCount.Wait();
        }

        // Check for exceptions and aggregate them if any occurred
        _exceptions.Clear();
        for (int i = 0; i < taskCount; i++)
        {
            var item = _taskItems[i];
            if (item.Exception != null)
            {
                _exceptions.Add(item.Exception);
            }
        }

        if (_exceptions.Count > 0)
        {
            throw new AggregateException("One or more exceptions occurred during parallel execution.", _exceptions);
        }
    }

    /// <summary>
    /// Releases the unmanaged resources used by the ReuseableParallelTask and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _completionCount?.Dispose();
        }
    }
}