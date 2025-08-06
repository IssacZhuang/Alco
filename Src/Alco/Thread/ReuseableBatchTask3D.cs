using System;
using System.Collections.Generic;
using System.Threading;

namespace Alco;

/// <summary>
/// Represents a reusable parallel task that can execute work items concurrently in 3D space.
/// This class provides parallel execution of 3D indexed operations with optimized
/// object reuse to minimize allocations.
/// </summary>
public abstract class ReuseableBatchTask3D : AutoDisposable
{
    /// <summary>
    /// Represents a work item that can be executed in parallel for 3D operations.
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
        /// The starting Z position for this work item.
        /// </summary>
        public volatile int StartZ;

        /// <summary>
        /// The width of the region this work item should process.
        /// </summary>
        public volatile int SizeX;

        /// <summary>
        /// The height of the region this work item should process.
        /// </summary>
        public volatile int SizeY;

        /// <summary>
        /// The depth of the region this work item should process.
        /// </summary>
        public volatile int SizeZ;

        /// <summary>
        /// Exception that occurred during execution, if any.
        /// </summary>
        public volatile Exception? Exception;

        /// <summary>
        /// The parent task that owns this item.
        /// </summary>
        private readonly ReuseableBatchTask3D _task;

        /// <summary>
        /// Initializes a new instance of the TaskItem class.
        /// </summary>
        /// <param name="task">The parent task.</param>
        public TaskItem(ReuseableBatchTask3D task)
        {
            _task = task;
        }

        /// <summary>
        /// Executes the work item by processing the assigned 3D region.
        /// </summary>
        public void Execute()
        {
            Exception = null;
            try
            {
                for (int z = StartZ; z < StartZ + SizeZ; z++)
                {
                    for (int y = StartY; y < StartY + SizeY; y++)
                    {
                        for (int x = StartX; x < StartX + SizeX; x++)
                        {
                            _task.ExecuteCore(x, y, z);
                        }
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
    /// Initializes a new instance of the ReuseableBatchTask3D class.
    /// </summary>
    /// <param name="maxConcurrency">The maximum number of concurrent tasks. Defaults to the processor count.</param>
    public ReuseableBatchTask3D(int maxConcurrency = 0)
    {
        _maxConcurrency = maxConcurrency <= 0 ? Environment.ProcessorCount : maxConcurrency;
        _completionCount = new SemaphoreSlim(0, int.MaxValue);
        _taskItems = new List<TaskItem>();
        _exceptions = new List<Exception>();
    }

    /// <summary>
    /// Executes the core logic for a specific 3D position. Must be implemented by derived classes.
    /// </summary>
    /// <param name="x">The X coordinate to process.</param>
    /// <param name="y">The Y coordinate to process.</param>
    /// <param name="z">The Z coordinate to process.</param>
    protected abstract void ExecuteCore(int x, int y, int z);

    /// <summary>
    /// Executes the parallel task for the specified 3D size.
    /// </summary>
    /// <param name="x">The width of the region to process.</param>
    /// <param name="y">The height of the region to process.</param>
    /// <param name="z">The depth of the region to process.</param>
    /// <param name="batchSizeX">The width of each batch. If null, it will be calculated automatically.</param>
    /// <param name="batchSizeY">The height of each batch. If null, it will be calculated automatically.</param>
    /// <param name="batchSizeZ">The depth of each batch. If null, it will be calculated automatically.</param>
    /// <exception cref="AggregateException">Thrown when one or more exceptions occur during parallel execution.</exception>
    public void RunParallel(int x, int y, int z, int? batchSizeX = null, int? batchSizeY = null, int? batchSizeZ = null)
    {
        if (x <= 0 || y <= 0 || z <= 0) return;

        int totalVoxels = x * y * z;

        // Calculate batch size if not provided
        int actualBatchSizeX;
        int actualBatchSizeY;
        int actualBatchSizeZ;
        if (batchSizeX.HasValue && batchSizeY.HasValue && batchSizeZ.HasValue)
        {
            actualBatchSizeX = batchSizeX.Value;
            actualBatchSizeY = batchSizeY.Value;
            actualBatchSizeZ = batchSizeZ.Value;
        }
        else
        {
            // Try to create cube-ish batches
            int voxelsPerBatch = Math.Max(1, totalVoxels / _maxConcurrency);
            actualBatchSizeX = Math.Max(1, (int)Math.Round(Math.Pow(voxelsPerBatch, 1.0 / 3.0)));
            actualBatchSizeY = Math.Max(1, (int)Math.Sqrt(voxelsPerBatch / actualBatchSizeX));
            actualBatchSizeZ = Math.Max(1, voxelsPerBatch / (actualBatchSizeX * actualBatchSizeY));

            actualBatchSizeX = Math.Min(actualBatchSizeX, x);
            actualBatchSizeY = Math.Min(actualBatchSizeY, y);
            actualBatchSizeZ = Math.Min(actualBatchSizeZ, z);
        }

        // Calculate number of batches in each dimension
        int batchesX = (x + actualBatchSizeX - 1) / actualBatchSizeX;
        int batchesY = (y + actualBatchSizeY - 1) / actualBatchSizeY;
        int batchesZ = (z + actualBatchSizeZ - 1) / actualBatchSizeZ;
        int totalBatches = batchesX * batchesY * batchesZ;

        // Ensure we have enough TaskItem objects
        while (_taskItems.Count < totalBatches)
        {
            _taskItems.Add(new TaskItem(this));
        }

        // Queue all tasks
        int batchIndex = 0;
        for (int bz = 0; bz < batchesZ; bz++)
        {
            for (int by = 0; by < batchesY; by++)
            {
                for (int bx = 0; bx < batchesX; bx++)
                {
                    int startX = bx * actualBatchSizeX;
                    int startY = by * actualBatchSizeY;
                    int startZ = bz * actualBatchSizeZ;
                    int endX = Math.Min(startX + actualBatchSizeX, x);
                    int endY = Math.Min(startY + actualBatchSizeY, y);
                    int endZ = Math.Min(startZ + actualBatchSizeZ, z);

                    var item = _taskItems[batchIndex];
                    item.StartX = startX;
                    item.StartY = startY;
                    item.StartZ = startZ;
                    item.SizeX = endX - startX;
                    item.SizeY = endY - startY;
                    item.SizeZ = endZ - startZ;
                    item.Exception = null;
                    ThreadPool.UnsafeQueueUserWorkItem(item, false);

                    batchIndex++;
                }
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
    /// Releases the unmanaged resources used by the ReuseableBatchTask3D and optionally releases the managed resources.
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