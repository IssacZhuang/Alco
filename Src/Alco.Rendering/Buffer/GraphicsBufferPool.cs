using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

/// <summary>
/// Manages a pool of graphics buffers of different sizes for efficient reuse.
/// </summary>
public class GraphicsBufferPool
{
    private struct PoolEntry
    {
        public uint Size;
        public ConcurrentPool<GraphicsBuffer> Pool;
    }
    private readonly RenderingSystem _renderingSystem;
    private readonly PoolEntry[] _pools;

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsBufferPool"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create graphics buffers.</param>
    /// <param name="bufferSizes">The sizes of buffers to pre-allocate in the pool.</param>
    public GraphicsBufferPool(RenderingSystem renderingSystem, params ReadOnlySpan<uint> bufferSizes)
    {
        _renderingSystem = renderingSystem;
        _pools = new PoolEntry[bufferSizes.Length];
        for (int i = 0; i < bufferSizes.Length; i++)
        {
            uint size = bufferSizes[i];
            _pools[i] = new PoolEntry
            {
                Size = size,
                Pool = new ConcurrentPool<GraphicsBuffer>(() => _renderingSystem.CreateGraphicsBuffer(size))
            };
        }
    }

    /// <summary>
    /// Attempts to get a buffer from the pool that is at least the specified size.
    /// </summary>
    /// <param name="size">The minimum size of the buffer needed.</param>
    /// <param name="buffer">When this method returns, contains the requested buffer if found; otherwise, null.</param>
    /// <returns>true if a suitable buffer was found; otherwise, false.</returns>
    public bool TryGetBuffer(uint size, [MaybeNullWhen(false)] out GraphicsBuffer buffer)
    {
        //binary search to find the pool
        int left = 0;
        int right = _pools.Length - 1;
        int selectedIndex = -1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (_pools[mid].Size >= size)
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
            buffer = _pools[selectedIndex].Pool.Get();
            return true;
        }

        // If no suitable pool found, create a new buffer with the exact requested size
        buffer = null;
        return false;
    }

}
