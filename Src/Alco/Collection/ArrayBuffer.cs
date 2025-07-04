using System;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// A high-performance array buffer that manages memory allocation and provides efficient access to elements.
/// </summary>
/// <typeparam name="T">The type of elements stored in the buffer.</typeparam>
public class ArrayBuffer<T>
{
    public const int DefaultCapacity = 16;

    private T[] _buffer;
    private int _length;

    /// <summary>
    /// Gets the logical size of the buffer based on the maximum size ensured by the user.
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    /// <summary>
    /// Gets the actual capacity of the underlying buffer.
    /// </summary>
    public int Capacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Length;
    }

    /// <summary>
    /// Initializes a new instance of the ArrayBuffer class.
    /// </summary>
    public ArrayBuffer()
    {
        _buffer = Array.Empty<T>();
        _length = 0;
    }

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get or set.</param>
    /// <returns>A reference to the element at the specified index.</returns>
    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _buffer[index];
    }

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>The element at the specified index.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get(int index)
    {
        return _buffer[index];
    }

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>The element at the specified index.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get(uint index)
    {
        return _buffer[index];
    }

    /// <summary>
    /// Gets a reference to the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>A reference to the element at the specified index.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
    {
        return ref _buffer[index];
    }

    /// <summary>
    /// Gets a reference to the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>A reference to the element at the specified index.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(uint index)
    {
        return ref _buffer[index];
    }

    /// <summary>
    /// Sets the element at the specified index to the given value.
    /// </summary>
    /// <param name="index">The zero-based index of the element to set.</param>
    /// <param name="value">The value to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index, T value)
    {
        _buffer[index] = value;
    }

    /// <summary>
    /// Sets the element at the specified index to the given value.
    /// </summary>
    /// <param name="index">The zero-based index of the element to set.</param>
    /// <param name="value">The value to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(uint index, T value)
    {
        _buffer[index] = value;
    }

    /// <summary>
    /// Returns a span that represents the ensured portion of the buffer.
    /// </summary>
    /// <returns>A span over the ensured elements.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan()
    {
        return _buffer.AsSpan(0, _length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan(int length)
    {
        return _buffer.AsSpan(0, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan(int start, int length)
    {
        return _buffer.AsSpan(start, length);
    }

    /// <summary>
    /// Ensures the buffer has at least the specified size. Uses intelligent expansion strategy:
    /// - If buffer is empty, expands to DefaultCapacity first
    /// - If still not enough, doubles capacity until requirement is met
    /// </summary>
    /// <param name="size">The minimum size required.</param>
    public void SetSize(int size)
    {
        if (_buffer.Length < size)
        {
            int newCapacity;
            if (_buffer.Length <= 0)
            {
                // Start with DefaultCapacity if buffer is empty
                newCapacity = DefaultCapacity;
            }
            else
            {
                // Double capacity until we meet the requirement
                newCapacity = _buffer.Length;
            }

            while (newCapacity < size)
            {
                newCapacity *= 2;
            }

            Array.Resize(ref _buffer, newCapacity);
        }

        _length = size;
    }

    /// <summary>
    /// Ensures the buffer has at least the specified size without copying existing data.
    /// Uses the same expansion strategy as EnsureSize.
    /// </summary>
    /// <param name="size">The minimum size required.</param>
    public void SetSizeWithoutCopy(int size)
    {
        if (_buffer.Length < size)
        {
            int newCapacity;
            if (_buffer.Length <= 0)
            {
                // Start with DefaultCapacity if buffer is empty
                newCapacity = DefaultCapacity;
            }
            else
            {
                // Double capacity until we meet the requirement
                newCapacity = _buffer.Length;
            }

            while (newCapacity < size)
            {
                newCapacity *= 2;
            }

            _buffer = new T[newCapacity];
        }

        // Update the ensured size to the maximum size ever requested
        _length = size;
    }

    /// <summary>
    /// Clears the ensured portion of the buffer.
    /// </summary>
    public void Clear()
    {
        _buffer.AsSpan(0, _length).Clear();
    }
}