using System;
using System.Runtime.CompilerServices;

namespace Vocore;

public class ArrayBuffer<T>
{
    private T[] _buffer;

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Length;

    }

    public ArrayBuffer()
    {
        _buffer = Array.Empty<T>();
    }

    public ArrayBuffer(int capacity)
    {
        _buffer = new T[capacity];
    }


    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _buffer[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get(int index)
    {
        return _buffer[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get(uint index)
    {
        return _buffer[index];
    }

    public T[] Data
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    public ReadOnlySpan<T> Span
    {
        get => _buffer;
    }

    public void EnsureSize(int size)
    {
        if (_buffer.Length < size)
        {
            Array.Resize(ref _buffer, size);
        }
    }

    public void EnsureSizeWithoutCopy(int size)
    {
        if (_buffer.Length < size)
        {
            _buffer = new T[size];
        }
    }

    public ReadOnlySpan<T> Slice(int start, int length)
    {
        return _buffer.AsSpan(start, length);
    }
}