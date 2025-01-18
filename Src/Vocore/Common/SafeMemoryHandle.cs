using System;
using Vocore.Unsafe;

namespace Vocore;

public unsafe class SafeMemoryHandle : AutoDisposable
{
    //both unmanaged or managed are supported

    private readonly byte* _unsafePointer;
    private readonly int _length;

    private readonly byte[]? _buffer;

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

    public SafeMemoryHandle(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        _unsafePointer = (byte*)UtilsMemory.Alloc(byteCount);
        _length = byteCount;
    }

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

    public SafeMemoryHandle(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _length = buffer.Length;
    }

    protected override void Dispose(bool disposing)
    {
        if (_unsafePointer != null)
        {
            UtilsMemory.Free(_unsafePointer);
        }
    }
}
