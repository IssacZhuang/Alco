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
}