using System;
using System.Runtime.CompilerServices;

namespace Vocore;

public class SpanStringBuilder : AutoDisposable
{
    public const int MinSize = 16;
    private NativeArrayList<char> _buffer;

    public ReadOnlySpan<char> Buffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.ReadOnlySpan;
    }

    public unsafe char* UnsafePointer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.UnsafePointer;
    }

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Length;
    }

    public SpanStringBuilder(int capacity = MinSize)
    {
        if (capacity < MinSize)
        {
            capacity = MinSize;
        }

        _buffer = new NativeArrayList<char>(capacity);
    }

    public void Clear()
    {
        _buffer.Clear();
    }

    public void Append(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return;
        }

        for (int i = 0; i < value.Length; i++)
        {
            _buffer.Add(value[i]);
        }
    }

    public void Append(char value)
    {
        _buffer.Add(value);
    }

    public void Append(string value)
    {
        if (value == null)
        {
            return;
        }

        for (int i = 0; i < value.Length; i++)
        {
            _buffer.Add(value[i]);
        }
    }

    public void Append<T>(T value) where T : unmanaged, ISpanFormattable
    {
        Span<char> formatBuffer = stackalloc char[32];
        value.TryFormat(formatBuffer, out int written, ReadOnlySpan<char>.Empty, null);
        for (int i = 0; i < written; i++)
        {
            _buffer.Add(formatBuffer[i]);
        }
    }

    public void Append(bool value)
    {
        if (value)
        {
            Append("True");
        }
        else
        {
            Append("False");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLine()
    {
        _buffer.Add('\n');
    }

    public void AppendLine(ReadOnlySpan<char> value)
    {
        Append(value);
        AppendLine();
    }

    public void AppendLine(char value)
    {
        Append(value);
        AppendLine();
    }

    public void AppendLine(string value)
    {
        Append(value);
        AppendLine();
    }

    public void AppendLine<T>(T value) where T : unmanaged, ISpanFormattable
    {
        Append(value);
        AppendLine();
    }

    public void AppendLine(bool value)
    {
        Append(value);
        AppendLine();
    }

    public override string ToString()
    {
        return new string(_buffer.ReadOnlySpan);
    }

    protected override void Dispose(bool disposing)
    {
        _buffer.Dispose();
    }
}