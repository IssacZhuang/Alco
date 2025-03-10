using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

/// <summary>
/// Manages a pool of graphics buffers of different sizes for efficient reuse.
/// </summary>
public class GraphicsBufferPool: AutoDisposable
{
    /// <summary>
    /// Represents a pool of graphics buffers of a specific size.
    /// </summary>
    public readonly struct Entry
    {
        public readonly uint BufferSize;
        public readonly ConcurrentPool<GraphicsBuffer> Pool;

        internal Entry(uint size, ConcurrentPool<GraphicsBuffer> pool)
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
    private readonly RenderingSystem _renderingSystem;
    private readonly Entry[] _pools;
    private readonly uint[] _bufferSizes;

    public ReadOnlySpan<uint> BufferSizes => _bufferSizes;

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsBufferPool"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create graphics buffers.</param>
    /// <param name="bufferSizes">The sizes of buffers to pre-allocate in the pool.</param>
    public GraphicsBufferPool(RenderingSystem renderingSystem, params ReadOnlySpan<uint> bufferSizes)
    {
        _renderingSystem = renderingSystem;
        _bufferSizes = bufferSizes.ToArray();
        _pools = new Entry[bufferSizes.Length];
        for (int i = 0; i < bufferSizes.Length; i++)
        {
            uint size = bufferSizes[i];
            _pools[i] = new Entry(
                size,
                new ConcurrentPool<GraphicsBuffer>(() => _renderingSystem.CreateGraphicsBuffer(size))
                );
        }
    }

    /// <summary>
    /// Attempts to get a buffer from the pool that is at least the specified size.
    /// </summary>
    /// <param name="bufferSize">The minimum size of the buffer needed.</param>
    /// <param name="buffer">When this method returns, contains the requested buffer if found; otherwise, null.</param>
    /// <returns>true if a suitable buffer was found; otherwise, false.</returns>
    public bool TryGetBuffer(uint bufferSize, [MaybeNullWhen(false)] out GraphicsBuffer buffer)
    {
        if (TryGetEntry(bufferSize, out var entry))
        {
            buffer = entry.Get();
            return true;
        }

        // If no suitable pool found, create a new buffer with the exact requested size
        buffer = null;
        return false;
    }

    /// <summary>
    /// Attempts to return a buffer to the pool.
    /// The buffer must be the exact size of the pool.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    /// <returns>true if the buffer was returned; otherwise, false.</returns>
    public bool TryReturnBuffer(GraphicsBuffer buffer)
    {
        if (buffer == null)
            return false;

        uint bufferSize = buffer.Size;

        // Binary search to find the exact matching pool
        int left = 0;
        int right = _pools.Length - 1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (_pools[mid].BufferSize == bufferSize)
            {
                // Found the exact matching pool
                _pools[mid].Pool.Return(buffer);
                return true;
            }
            else if (_pools[mid].BufferSize < bufferSize)
            {
                left = mid + 1; // Look in the right half
            }
            else
            {
                right = mid - 1; // Look in the left half
            }
        }

        // No matching pool found
        return false;
    }

    /// <summary>
    /// Attempts to get an entry from the pool that is at least the specified size.
    /// </summary>
    /// <param name="bufferSize">The minimum size of the buffer needed.</param>
    /// <param name="entry">When this method returns, contains the requested entry if found; otherwise, default.</param>
    /// <returns>true if a suitable entry was found; otherwise, false.</returns>
    public bool TryGetEntry(uint bufferSize, out Entry entry)
    {
        //binary search to find the pool
        int left = 0;
        int right = _pools.Length - 1;
        int selectedIndex = -1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (_pools[mid].BufferSize >= bufferSize)
            {
                selectedIndex = mid;
                right = mid - 1; // Look for a smaller buffer that still meets the requirement
            }
            else
            {
                left = mid + 1; // Look for a larger buffer
            }
        }

        if (selectedIndex != -1)
        {
            entry = _pools[selectedIndex];
            return true;
        }

        // If no suitable pool found, create a new buffer with the exact requested size
        entry = default;
        return false;


    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var pool in _pools)
            {
                foreach (var buffer in pool.Pool)
                {
                    buffer.Dispose();
                }
                pool.Pool.Clear();
            }
        }
    }
}
