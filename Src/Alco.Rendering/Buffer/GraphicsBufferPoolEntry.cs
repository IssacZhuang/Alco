using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;


/// <summary>
/// Represents a pool of graphics buffers of a specific size.
/// </summary>
public readonly struct GraphicsBufferPoolEntry
{
    public readonly uint BufferSize;
    public readonly ConcurrentPool<GraphicsBuffer> Pool;

    internal GraphicsBufferPoolEntry(uint size, ConcurrentPool<GraphicsBuffer> pool)
    {
        BufferSize = size;
        Pool = pool;
    }

    /// <summary>
    /// Gets a buffer from the pool.
    /// </summary>
    /// <returns>A buffer from the pool.</returns>
    public GraphicsBuffer Get() => Pool.Get();

    /// <summary>
    /// Tries to return a buffer to the pool. The buffer must be the exact size of the pool.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    /// <returns>true if the buffer was returned; otherwise, false.</returns>
    public bool TryReturn(GraphicsBuffer buffer)
    {
        if (buffer.Size != BufferSize)
        {
            return false;
        }

        Pool.Return(buffer);
        return true;
    }

    /// <summary>
    /// Reduces memory usage by removing and disposing the specified amount of memory from the pool.
    /// </summary>
    /// <param name="memoryToReduce">The amount of memory to reduce in bytes.</param>
    /// <returns>The actual amount of memory reduced in bytes.</returns>
    public uint ReduceMemory(uint memoryToReduce)
    {
        // Calculate how many buffers we need to remove
        int buffersToRemove = (int)Math.Ceiling((double)memoryToReduce / BufferSize);

        uint actualMemoryReduced = 0;

        // Remove buffers from the pool using Get method
        // Note: Get will create new buffers if pool is empty, but we only want to remove existing ones
        // So we check the pool count to avoid creating new buffers unnecessarily
        for (int i = 0; i < buffersToRemove && Pool.Count > 0; i++)
        {
            var buffer = Pool.Get();
            buffer.Dispose();
            actualMemoryReduced += BufferSize;
        }

        return actualMemoryReduced;
    }
}