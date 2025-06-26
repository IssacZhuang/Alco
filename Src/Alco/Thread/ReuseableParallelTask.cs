using System;
using System.Collections.Generic;
using System.Threading;

namespace Alco;

/// <summary>
/// Represents a reusable parallel task that can execute work items concurrently.
/// This class manages a pool of TaskItem objects to minimize allocations and provides
/// parallel execution of indexed operations.
/// </summary>
public abstract class ReuseableParallelTask : AutoDisposable
{
    /// <summary>
    /// Represents a work item that can be executed in parallel.
    /// </summary>
    public class TaskItem : IThreadPoolWorkItem
    {
        /// <summary>
        /// The parent task that owns this item.
        /// </summary>
        public ReuseableParallelTask Task { get; set; }

        /// <summary>
        /// The starting index for this work item.
        /// </summary>
        public int IndexStart { get; set; }

        /// <summary>
        /// The number of iterations this work item should process.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Initializes a new instance of the TaskItem class.
        /// </summary>
        /// <param name="task">The parent task.</param>
        /// <param name="indexStart">The starting index.</param>
        /// <param name="count">The number of iterations to process.</param>
        public TaskItem(ReuseableParallelTask task, int indexStart, int count)
        {
            Task = task;
            IndexStart = indexStart;
            Count = count;
        }

        /// <summary>
        /// Executes the work item by processing the assigned range of indices.
        /// </summary>
        public void Execute()
        {
            try
            {
                for (int i = IndexStart; i < IndexStart + Count; i++)
                {
                    Task.ExecuteCore(i);
                }
            }
            finally
            {
                // Signal completion and return this item to the pool
                Task.ReturnItem(this);
                Task._completionCount.Release();
            }
        }
    }

    private readonly SemaphoreSlim _completionCount;
    private readonly ConcurrentPool<TaskItem> _pool;
    private readonly int _maxConcurrency;

    /// <summary>
    /// Gets the maximum number of concurrent tasks that can be executed.
    /// </summary>
    public int MaxConcurrency => _maxConcurrency;

    /// <summary>
    /// Initializes a new instance of the ReuseableParallelTask class.
    /// </summary>
    /// <param name="maxConcurrency">The maximum number of concurrent tasks. Defaults to the processor count.</param>
    public ReuseableParallelTask(int maxConcurrency = 0)
    {
        _maxConcurrency = maxConcurrency <= 0 ? Environment.ProcessorCount : maxConcurrency;
        _completionCount = new SemaphoreSlim(0, int.MaxValue);
        _pool = new ConcurrentPool<TaskItem>(() => new TaskItem(this, 0, 0));
    }

    /// <summary>
    /// Executes the core logic for a specific index. Must be implemented by derived classes.
    /// </summary>
    /// <param name="index">The index to process.</param>
    protected abstract void ExecuteCore(int index);

    /// <summary>
    /// Gets an existing TaskItem from the pool or creates a new one if the pool is empty.
    /// </summary>
    /// <param name="indexStart">The starting index for the task item.</param>
    /// <param name="count">The number of iterations for the task item.</param>
    /// <returns>A TaskItem configured with the specified parameters.</returns>
    public TaskItem GetOrCreateItem(int indexStart, int count)
    {
        var item = _pool.Get();
        item.Task = this;
        item.IndexStart = indexStart;
        item.Count = count;
        return item;
    }

    /// <summary>
    /// Executes the parallel task for the specified range of indices.
    /// </summary>
    /// <param name="totalCount">The total number of iterations to process.</param>
    /// <param name="batchSize">The size of each batch. If null, it will be calculated automatically.</param>
    public void RunParallel(int totalCount, int? batchSize = null)
    {
        if (totalCount <= 0) return;

        int actualBatchSize = batchSize ?? Math.Max(1, totalCount / _maxConcurrency);
        int taskCount = (totalCount + actualBatchSize - 1) / actualBatchSize;

        // Queue all tasks
        for (int i = 0; i < taskCount; i++)
        {
            int start = i * actualBatchSize;
            int count = Math.Min(actualBatchSize, totalCount - start);

            var item = GetOrCreateItem(start, count);
            ThreadPool.UnsafeQueueUserWorkItem(item, false);
        }

        // Wait for all tasks to complete
        for (int i = 0; i < taskCount; i++)
        {
            _completionCount.Wait();
        }
    }

    /// <summary>
    /// Returns a TaskItem to the pool for reuse.
    /// </summary>
    /// <param name="item">The TaskItem to return to the pool.</param>
    private void ReturnItem(TaskItem item)
    {
        _pool.Return(item);
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