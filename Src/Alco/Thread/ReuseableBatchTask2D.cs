using System;
using System.Collections.Generic;
using System.Threading;

namespace Alco;

/// <summary>
/// Represents a reusable parallel task that can execute work items concurrently in 2D space.
/// This class provides parallel execution of 2D indexed operations with optimized
/// object reuse to minimize allocations.
/// </summary>
public abstract class ReuseableBatchTask2D : AutoDisposable
{
    /// <summary>
    /// Represents a work item that can be executed in parallel for 2D operations.
    /// </summary>
    private class TaskItem : IThreadPoolWorkItem
    {
        /// <summary>
        /// The starting X position for this work item.
        /// </summary>
        public volatile int StartX;

        /// <summary>
        /// The starting Y position for this work item.
        /// </summary>
        public volatile int StartY;

        /// <summary>
        /// The width of the region this work item should process.
        /// </summary>
        public volatile int SizeX;

        /// <summary>
        /// The height of the region this work item should process.
        /// </summary>
        public volatile int SizeY;

        /// <summary>
        /// Exception that occurred during execution, if any.
        /// </summary>
        public volatile Exception? Exception;

        /// <summary>
        /// The parent task that owns this item.
        /// </summary>
        private readonly ReuseableBatchTask2D _task;

        /// <summary>
        /// Initializes a new instance of the TaskItem class.
        /// </summary>
        /// <param name="task">The parent task.</param>
        public TaskItem(ReuseableBatchTask2D task)
        {
            _task = task;
        }

        /// <summary>
        /// Executes the work item by processing the assigned 2D region.
        /// </summary>
        public void Execute()
        {
            Exception = null;
            try
            {
                for (int y = StartY; y < StartY + SizeY; y++)
                {
                    for (int x = StartX; x < StartX + SizeX; x++)
                    {
                        _task.ExecuteCore(new int2(x, y));
                    }
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
    /// Initializes a new instance of the ReuseableBatchTask2D class.
    /// </summary>
    /// <param name="maxConcurrency">The maximum number of concurrent tasks. Defaults to the processor count.</param>
    public ReuseableBatchTask2D(int maxConcurrency = 0)
    {
        _maxConcurrency = maxConcurrency <= 0 ? Environment.ProcessorCount : maxConcurrency;
        _completionCount = new SemaphoreSlim(0, int.MaxValue);
        _taskItems = new List<TaskItem>();
        _exceptions = new List<Exception>();
    }

    /// <summary>
    /// Executes the core logic for a specific 2D position. Must be implemented by derived classes.
    /// </summary>
    /// <param name="position">The 2D position to process.</param>
    protected abstract void ExecuteCore(int2 position);

    /// <summary>
    /// Executes the parallel task for the specified 2D size.
    /// </summary>
    /// <param name="size">The 2D size of the region to process.</param>
    /// <param name="batchSize">The size of each batch. If null, it will be calculated automatically.</param>
    /// <exception cref="AggregateException">Thrown when one or more exceptions occur during parallel execution.</exception>
    public void RunParallel(int2 size, int2? batchSize = null)
    {
        if (size.X <= 0 || size.Y <= 0) return;

        int totalPixels = size.X * size.Y;

        // Calculate batch size if not provided
        int2 actualBatchSize;
        if (batchSize.HasValue)
        {
            actualBatchSize = batchSize.Value;
        }
        else
        {
            // Try to create square-ish batches
            int pixelsPerBatch = Math.Max(1, totalPixels / _maxConcurrency);
            int batchWidth = Math.Max(1, (int)Math.Sqrt(pixelsPerBatch));
            int batchHeight = Math.Max(1, pixelsPerBatch / batchWidth);
            actualBatchSize = new int2(Math.Min(batchWidth, size.X), Math.Min(batchHeight, size.Y));
        }

        // Calculate number of batches in each dimension
        int batchesX = (size.X + actualBatchSize.X - 1) / actualBatchSize.X;
        int batchesY = (size.Y + actualBatchSize.Y - 1) / actualBatchSize.Y;
        int totalBatches = batchesX * batchesY;

        // Ensure we have enough TaskItem objects
        while (_taskItems.Count < totalBatches)
        {
            _taskItems.Add(new TaskItem(this));
        }

        // Queue all tasks
        int batchIndex = 0;
        for (int by = 0; by < batchesY; by++)
        {
            for (int bx = 0; bx < batchesX; bx++)
            {
                int startX = bx * actualBatchSize.X;
                int startY = by * actualBatchSize.Y;
                int endX = Math.Min(startX + actualBatchSize.X, size.X);
                int endY = Math.Min(startY + actualBatchSize.Y, size.Y);

                var item = _taskItems[batchIndex];
                item.StartX = startX;
                item.StartY = startY;
                item.SizeX = endX - startX;
                item.SizeY = endY - startY;
                item.Exception = null;
                ThreadPool.UnsafeQueueUserWorkItem(item, false);

                batchIndex++;
            }
        }

        // Wait for all tasks to complete
        for (int i = 0; i < totalBatches; i++)
        {
            _completionCount.Wait();
        }

        // Check for exceptions and aggregate them if any occurred
        _exceptions.Clear();
        for (int i = 0; i < totalBatches; i++)
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
    /// Releases the unmanaged resources used by the ReuseableBatchTask2D and optionally releases the managed resources.
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