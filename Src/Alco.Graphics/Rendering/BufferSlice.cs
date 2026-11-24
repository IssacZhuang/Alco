using System.Diagnostics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>A bounded byte slice within a <see cref="Buffer"/>, implicitly convertible from a whole buffer.</summary>
public readonly struct BufferSlice : IEquatable<BufferSlice>
{
    /// <summary>Initializes a slice covering a byte subrange of <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The backing buffer.</param>
    /// <param name="byteOffset">The byte offset relative to the start of <paramref name="buffer"/>.</param>
    /// <param name="sizeBytes">The slice size in bytes.</param>
    public BufferSlice(Buffer buffer, ulong byteOffset, ulong sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Debug.Assert(!buffer.IsDisposed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteOffset, buffer.SizeBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sizeBytes, buffer.SizeBytes - byteOffset);
        Buffer = buffer;
        ByteOffset = byteOffset;
        SizeBytes = sizeBytes;
    }

    /// <summary>The backing buffer, or <see langword="null"/> for an empty slice.</summary>
    public Buffer? Buffer { get; }

    /// <summary>The byte offset within <see cref="Buffer"/>.</summary>
    public ulong ByteOffset { get; }

    /// <summary>The slice length in bytes.</summary>
    public ulong SizeBytes { get; }

    /// <summary>Whether the slice is empty or unbound.</summary>
    public bool IsEmpty => Buffer == null || SizeBytes == 0;

    /// <summary>The shader-visible buffer view for this slice, or <see langword="default"/> when unbound.</summary>
    public GPUBufferView View => Buffer == null ? default : Buffer.GetView(ByteOffset);

    internal GPURange LowLevelRange => Buffer == null ? default : Buffer.LowLevelRange.Slice(ByteOffset, SizeBytes);

    /// <summary>Returns the remaining slice starting at <paramref name="byteOffset"/> bytes into this slice.</summary>
    /// <param name="byteOffset">The offset relative to this slice.</param>
    /// <returns>The sliced subrange.</returns>
    public BufferSlice Slice(ulong byteOffset)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteOffset, SizeBytes);
        return Slice(byteOffset, SizeBytes - byteOffset);
    }

    /// <summary>Returns a sub-slice starting at <paramref name="byteOffset"/> with length <paramref name="sizeBytes"/>.</summary>
    /// <param name="byteOffset">The offset relative to this slice.</param>
    /// <param name="sizeBytes">The sub-slice size in bytes.</param>
    /// <returns>The sliced subrange.</returns>
    public BufferSlice Slice(ulong byteOffset, ulong sizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteOffset, SizeBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sizeBytes, SizeBytes - byteOffset);
        if (Buffer == null)
            return default;
        return new BufferSlice(Buffer, ByteOffset + byteOffset, sizeBytes);
    }

    /// <summary>Creates a slice covering the entire <paramref name="buffer"/>.</summary>
    public static implicit operator BufferSlice(Buffer buffer) => buffer.Slice();

    /// <inheritdoc />
    public bool Equals(BufferSlice other) =>
        ReferenceEquals(Buffer, other.Buffer) && ByteOffset == other.ByteOffset && SizeBytes == other.SizeBytes;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BufferSlice other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Buffer, ByteOffset, SizeBytes);

    /// <summary>Tests whether two slices refer to the exact same buffer byte range.</summary>
    public static bool operator ==(BufferSlice left, BufferSlice right) => left.Equals(right);

    /// <summary>Tests whether two slices refer to different buffer ranges.</summary>
    public static bool operator !=(BufferSlice left, BufferSlice right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Buffer == null ? "BufferSlice(Empty)" : $"BufferSlice(Offset={ByteOffset}, Size={SizeBytes})";
}
