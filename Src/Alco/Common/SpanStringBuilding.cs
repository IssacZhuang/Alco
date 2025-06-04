using System;
using System.Runtime.CompilerServices;

namespace Alco;

public unsafe class SpanStringBuilder
{
    public const int MinSize = 16;
    private char[] _array;
    private int _length;

    private Span<char> RemainingCurrentChunk
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new Span<char>(_array, _length, _array.Length - _length);
    }

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    public SpanStringBuilder(int capacity = MinSize)
    {
        capacity = Math.Max(capacity, MinSize);
        _array = new char[capacity];
        _length = 0;
    }

    public ReadOnlySpan<char> AsReadOnlySpan()
    {
        return new ReadOnlySpan<char>(_array, 0, _length);
    }

    public void Clear()
    {
        _length = 0;
    }

    private void EnsureCapacity(int required)
    {
        if (_array.Length < required)
        {
            int newCapacity = _array.Length * 2;
            Array.Resize(ref _array, Math.Max(newCapacity, required));
        }
    }

    public SpanStringBuilder Append(ReadOnlySpan<char> value)
    {
        if (value.Length == 0) return this;

        EnsureCapacity(_length + value.Length);
        value.CopyTo(RemainingCurrentChunk);
        _length += value.Length;
        return this;
    }

    public SpanStringBuilder Append(char value)
    {
        EnsureCapacity(_length + 1);
        fixed (char* ptr = _array)
        {
            ptr[_length++] = value;
        }
        return this;
    }

    public SpanStringBuilder Append(string? value)
    {
        if (value == null) return this;
        return Append(value.AsSpan());
    }

    public SpanStringBuilder Append<T>(T value) where T : unmanaged, ISpanFormattable
    {
        if (value.TryFormat(RemainingCurrentChunk, out int written, default, null))
        {
            _length += written;
            return this;
        }
        return Append(value.ToString());
    }

    public SpanStringBuilder Append(bool value)
    {
        return Append(value ? "True" : "False");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanStringBuilder AppendLine()
    {
        Append("\r\n");
        return this;
    }

    public SpanStringBuilder AppendLine(ReadOnlySpan<char> value)
    {
        Append(value);
        return AppendLine();
    }

    public SpanStringBuilder AppendLine(char value)
    {
        Append(value);
        return AppendLine();
    }

    public SpanStringBuilder AppendLine(string value)
    {
        Append(value);
        return AppendLine();
    }

    public SpanStringBuilder AppendLine<T>(T value) where T : unmanaged, ISpanFormattable
    {
        Append(value);
        return AppendLine();
    }

    public SpanStringBuilder AppendLine(bool value)
    {
        Append(value);
        return AppendLine();
    }

    public override string ToString()
    {
        return new string(_array, 0, _length);
    }
}