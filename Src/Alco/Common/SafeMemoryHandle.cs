using System;
using Alco.Unsafe;

namespace Alco;

/// <summary>
/// Provides a safe wrapper for managing memory allocations, supporting both managed and unmanaged memory.
/// This class automatically handles memory cleanup through the IDisposable pattern.
/// </summary>
public unsafe class SafeMemoryHandle : AutoDisposable
{
    //both unmanaged or managed are supported

    private readonly byte* _unsafePointer;
    private readonly int _length;

    private readonly byte[]? _buffer;

    public static readonly SafeMemoryHandle Empty = new SafeMemoryHandle(Array.Empty<byte>());

    /// <summary>
    /// Gets a span representing the memory managed by this handle.
    /// If using unmanaged memory, returns a span over the unsafe pointer.
    /// If using managed memory, returns a span over the managed buffer.
    /// Returns an empty span if no memory is allocated.
    /// </summary>
    public Span<byte> Span
    {
        get
        {
            if (_unsafePointer != null)
            {
                return new Span<byte>(_unsafePointer, _length);
            }
            if (_buffer != null)
            {
                return _buffer;
            }

            return Span<byte>.Empty;
        }
    }

    /// <summary>
    /// Initializes a new instance of the SafeMemoryHandle class by allocating unmanaged memory of the specified size.
    /// </summary>
    /// <param name="byteCount">The number of bytes to allocate. Must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when byteCount is negative.</exception>
    public SafeMemoryHandle(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        _unsafePointer = (byte*)UtilsMemory.Alloc(byteCount);
        _length = byteCount;
    }

    /// <summary>
    /// Initializes a new instance of the SafeMemoryHandle class by allocating unmanaged memory of the specified size.
    /// </summary>
    /// <param name="byteCount">The number of bytes to allocate. Must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when byteCount is negative.</exception>
    public SafeMemoryHandle(long byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        _unsafePointer = (byte*)UtilsMemory.Alloc(byteCount);
        _length = (int)byteCount;
    }

    /// <summary>
    /// Initializes a new instance of the SafeMemoryHandle class using an existing unmanaged memory pointer.
    /// </summary>
    /// <param name="unsafePointer">Pointer to the unmanaged memory. Must not be null.</param>
    /// <param name="length">The length of the memory block in bytes. Must be non-negative.</param>
    /// <exception cref="ArgumentNullException">Thrown when unsafePointer is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when length is negative.</exception>
    public SafeMemoryHandle(byte* unsafePointer, int length)
    {
        if (unsafePointer == null)
        {
            throw new ArgumentNullException(nameof(unsafePointer));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _unsafePointer = unsafePointer;
        _length = length;
    }

    /// <summary>
    /// Initializes a new instance of the SafeMemoryHandle class using a managed byte array.
    /// </summary>
    /// <param name="buffer">The managed byte array to wrap. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when buffer is null.</exception>
    public SafeMemoryHandle(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _length = buffer.Length;
    }

    /// <summary>
    /// Releases the unmanaged resources used by the SafeMemoryHandle.
    /// If using unmanaged memory, frees the allocated memory block.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (_unsafePointer != null)
        {
            UtilsMemory.Free(_unsafePointer);
        }
    }
}
