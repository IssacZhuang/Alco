using System;
using System.Runtime.CompilerServices;
using System.Numerics;

namespace Alco;

/// <summary>
/// A dynamic string builder that uses spans for efficient string construction.
/// Provides StringBuilder-like functionality with better performance for span-based operations.
/// </summary>
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

    /// <summary>
    /// Gets the current length of the string being built.
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    /// <summary>
    /// Initializes a new instance of the SpanStringBuilder class with the specified initial capacity.
    /// </summary>
    /// <param name="capacity">The initial capacity of the string builder. Minimum value is MinSize.</param>
    public SpanStringBuilder(int capacity = MinSize)
    {
        capacity = Math.Max(capacity, MinSize);
        _array = new char[capacity];
        _length = 0;
    }

    /// <summary>
    /// Returns the current content as a read-only span of characters.
    /// </summary>
    /// <returns>A read-only span representing the current string content.</returns>
    public ReadOnlySpan<char> AsReadOnlySpan()
    {
        return new ReadOnlySpan<char>(_array, 0, _length);
    }

    /// <summary>
    /// Clears the content of the string builder, resetting its length to 0.
    /// </summary>
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

    /// <summary>
    /// Appends a read-only span of characters to the end of the current content.
    /// </summary>
    /// <param name="value">The character span to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(ReadOnlySpan<char> value)
    {
        if (value.Length == 0) return this;

        EnsureCapacity(_length + value.Length);
        value.CopyTo(RemainingCurrentChunk);
        _length += value.Length;
        return this;
    }

    /// <summary>
    /// Appends a single character to the end of the current content.
    /// </summary>
    /// <param name="value">The character to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(char value)
    {
        EnsureCapacity(_length + 1);
        fixed (char* ptr = _array)
        {
            ptr[_length++] = value;
        }
        return this;
    }

    /// <summary>
    /// Appends a string to the end of the current content.
    /// </summary>
    /// <param name="value">The string to append. If null, no operation is performed.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(string? value)
    {
        if (value == null) return this;
        return Append(value.AsSpan());
    }

    /// <summary>
    /// Appends a formattable value to the end of the current content.
    /// </summary>
    /// <typeparam name="T">The type of the value to append, must implement ISpanFormattable.</typeparam>
    /// <param name="value">The value to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append<T>(T value) where T : unmanaged, ISpanFormattable
    {
        if (value.TryFormat(RemainingCurrentChunk, out int written, default, null))
        {
            _length += written;
            return this;
        }
        return Append(value.ToString());
    }

    /// <summary>
    /// Appends a boolean value to the end of the current content.
    /// </summary>
    /// <param name="value">The boolean value to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(bool value)
    {
        return Append(value ? "True" : "False");
    }

    /// <summary>
    /// Appends a line terminator to the end of the current content.
    /// </summary>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanStringBuilder AppendLine()
    {
        Append("\r\n");
        return this;
    }

    /// <summary>
    /// Appends a read-only span of characters followed by a line terminator.
    /// </summary>
    /// <param name="value">The character span to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder AppendLine(ReadOnlySpan<char> value)
    {
        Append(value);
        return AppendLine();
    }

    /// <summary>
    /// Appends a character followed by a line terminator.
    /// </summary>
    /// <param name="value">The character to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder AppendLine(char value)
    {
        Append(value);
        return AppendLine();
    }

    /// <summary>
    /// Appends a string followed by a line terminator.
    /// </summary>
    /// <param name="value">The string to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder AppendLine(string value)
    {
        Append(value);
        return AppendLine();
    }

    /// <summary>
    /// Appends a formattable value followed by a line terminator.
    /// </summary>
    /// <typeparam name="T">The type of the value to append, must implement ISpanFormattable.</typeparam>
    /// <param name="value">The value to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder AppendLine<T>(T value) where T : unmanaged, ISpanFormattable
    {
        Append(value);
        return AppendLine();
    }

    /// <summary>
    /// Appends a boolean value followed by a line terminator.
    /// </summary>
    /// <param name="value">The boolean value to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder AppendLine(bool value)
    {
        Append(value);
        return AppendLine();
    }

    /// <summary>
    /// Converts the current content to a string.
    /// </summary>
    /// <returns>A string representation of the current content.</returns>
    public override string ToString()
    {
        return new string(_array, 0, _length);
    }

    /// <summary>
    /// Appends a formatted vector representation to the end of the current content.
    /// The vector will be formatted as &lt;x, y, ...&gt;.
    /// </summary>
    /// <typeparam name="T">The type of the vector components, must implement ISpanFormattable.</typeparam>
    /// <param name="vector">The vector span to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append<T>(ReadOnlySpan<T> vector) where T : unmanaged, ISpanFormattable
    {
        Append('<');
        for (int i = 0; i < vector.Length; i++)
        {
            Append(vector[i]);
            if (i < vector.Length - 1)
            {
                Append(',');
                Append(' ');
            }
        }
        Append('>');
        return this;
    }

    /// <summary>
    /// Appends a Vector2 to the end of the current content in the format &lt;x, y&gt;.
    /// </summary>
    /// <param name="vector">The Vector2 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(Vector2 vector)
    {
        float* components = (float*)&vector;
        return Append(new ReadOnlySpan<float>(components, 2));
    }

    /// <summary>
    /// Appends a Vector3 to the end of the current content in the format &lt;x, y, z&gt;.
    /// </summary>
    /// <param name="vector">The Vector3 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(Vector3 vector)
    {
        float* components = (float*)&vector;
        return Append(new ReadOnlySpan<float>(components, 3));
    }

    /// <summary>
    /// Appends a Vector4 to the end of the current content in the format &lt;x, y, z, w&gt;.
    /// </summary>
    /// <param name="vector">The Vector4 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(Vector4 vector)
    {
        float* components = (float*)&vector;
        return Append(new ReadOnlySpan<float>(components, 4));
    }

    /// <summary>
    /// Appends a Half2 to the end of the current content in the format &lt;x, y&gt;.
    /// </summary>
    /// <param name="vector">The Half2 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(Half2 vector)
    {
        Half* components = (Half*)&vector;
        return Append(new ReadOnlySpan<Half>(components, 2));
    }

    /// <summary>
    /// Appends a Half3 to the end of the current content in the format &lt;x, y, z&gt;.
    /// </summary>
    /// <param name="vector">The Half3 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(Half3 vector)
    {
        Half* components = (Half*)&vector;
        return Append(new ReadOnlySpan<Half>(components, 3));
    }

    /// <summary>
    /// Appends a Half4 to the end of the current content in the format &lt;x, y, z, w&gt;.
    /// </summary>
    /// <param name="vector">The Half4 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(Half4 vector)
    {
        Half* components = (Half*)&vector;
        return Append(new ReadOnlySpan<Half>(components, 4));
    }

    /// <summary>
    /// Appends a int2 to the end of the current content in the format &lt;x, y&gt;.
    /// </summary>
    /// <param name="vector">The int2 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(int2 vector)
    {
        int* components = (int*)&vector;
        return Append(new ReadOnlySpan<int>(components, 2));
    }

    /// <summary>
    /// Appends a int3 to the end of the current content in the format &lt;x, y, z&gt;.
    /// </summary>
    /// <param name="vector">The int3 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(int3 vector)
    {
        int* components = (int*)&vector;
        return Append(new ReadOnlySpan<int>(components, 3));
    }

    /// <summary>
    /// Appends a int4 to the end of the current content in the format &lt;x, y, z, w&gt;.
    /// </summary>
    /// <param name="vector">The int4 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(int4 vector)
    {
        int* components = (int*)&vector;
        return Append(new ReadOnlySpan<int>(components, 4));
    }

    /// <summary>
    /// Appends a uint2 to the end of the current content in the format &lt;x, y&gt;.
    /// </summary>
    /// <param name="vector">The uint2 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(uint2 vector)
    {
        uint* components = (uint*)&vector;
        return Append(new ReadOnlySpan<uint>(components, 2));
    }

    /// <summary>
    /// Appends a uint3 to the end of the current content in the format &lt;x, y, z&gt;.
    /// </summary>
    /// <param name="vector">The uint3 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(uint3 vector)
    {
        uint* components = (uint*)&vector;
        return Append(new ReadOnlySpan<uint>(components, 3));
    }

    /// <summary>
    /// Appends a uint4 to the end of the current content in the format &lt;x, y, z, w&gt;.
    /// </summary>
    /// <param name="vector">The uint4 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(uint4 vector)
    {
        uint* components = (uint*)&vector;
        return Append(new ReadOnlySpan<uint>(components, 4));
    }

    /// <summary>
    /// Appends a ColorFloat to the end of the current content in the format &lt;r, g, b, a&gt;.
    /// </summary>
    /// <param name="color">The ColorFloat to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(ColorFloat color)
    {
        float* components = (float*)&color;
        return Append(new ReadOnlySpan<float>(components, 4));
    }

    /// <summary>
    /// Appends a Color32 to the end of the current content in the format &lt;r, g, b, a&gt;.
    /// </summary>
    /// <param name="color">The Color32 to append.</param>
    /// <returns>This SpanStringBuilder instance for method chaining.</returns>
    public SpanStringBuilder Append(Color32 color)
    {
        byte* components = (byte*)&color;
        return Append(new ReadOnlySpan<byte>(components, 4));
    }
}