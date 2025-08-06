using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

/// <summary>
/// Interface for managing a pool of graphics buffers of different sizes for efficient reuse.
/// The thread safety is dependent on the implementation.
/// </summary>
public interface IGraphicsBufferPool : IDisposable
{
    /// <summary>
    /// Gets the sizes of buffers available in the pool.
    /// </summary>
    ReadOnlySpan<uint> BufferSizes { get; }

    /// <summary>
    /// Attempts to get a buffer from the pool that is at least the specified size.
    /// </summary>
    /// <param name="bufferSize">The minimum size of the buffer needed.</param>
    /// <param name="buffer">When this method returns, contains the requested buffer if found; otherwise, null.</param>
    /// <returns>true if a suitable buffer was found; otherwise, false.</returns>
    bool TryGetBuffer(uint bufferSize, [MaybeNullWhen(false)] out GraphicsBuffer buffer);

    /// <summary>
    /// Attempts to return a buffer to the pool.
    /// The buffer must be the exact size of the pool.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    /// <returns>true if the buffer was returned; otherwise, false.</returns>
    bool TryReturnBuffer(GraphicsBuffer buffer);
}