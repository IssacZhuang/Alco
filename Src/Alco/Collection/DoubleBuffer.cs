using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// Represents a double-buffering data structure that maintains two instances of type T,
/// allowing for concurrent read and write operations through front and back buffers.
/// </summary>
/// <typeparam name="T">The type of elements stored in the double buffer.</typeparam>
public class DoubleBuffer<T>
{
    private T _item1;
    private T _item2;
    private bool _isSwapped;

    /// <summary>
    /// Initializes a new instance of the <see cref="DoubleBuffer{T}"/> struct.
    /// </summary>
    /// <param name="item1">The first buffer item.</param>
    /// <param name="item2">The second buffer item.</param>
    public DoubleBuffer(T item1, T item2)
    {
        _item1 = item1;
        _item2 = item2;
        _isSwapped = false;
    }

    /// <summary>
    /// Gets the current front buffer. This is typically used for reading operations.
    /// </summary>
    public T Front
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isSwapped ? _item2 : _item1;
    }

    /// <summary>
    /// Gets the current back buffer. This is typically used for writing operations.
    /// </summary>
    public T Back
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isSwapped ? _item1 : _item2;
    }

    /// <summary>
    /// Swaps the front and back buffers, making the previous back buffer the new front buffer
    /// and vice versa. This operation is designed to be very fast as it only toggles a boolean flag.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Swap()
    {
        _isSwapped = !_isSwapped;
    }

    /// <summary>
    /// Resets the double buffer to its initial state, where the front and back buffers are the same.
    /// </summary>
    public void Reset()
    {
        _isSwapped = false;
    }
}
