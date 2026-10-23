using System;
using System.Text;

namespace Alco;


public sealed unsafe class NativeUtf8String : AutoDisposable
{
    private readonly byte* _ptr;

    /// <summary>
    /// Gets the pointer to the null-terminated UTF-8 bytes.
    /// </summary>
    public byte* UnsafePointer => _ptr;

    /// <summary>
    /// Creates a null-terminated UTF-8 copy of <paramref name="str"/>.
    /// </summary>
    /// <param name="str">The string to copy.</param>
    public NativeUtf8String(string str) : this(str.AsSpan()) { }

    /// <summary>
    /// Creates a null-terminated UTF-8 copy of <paramref name="str"/>.
    /// </summary>
    /// <param name="str">The characters to copy.</param>
    public NativeUtf8String(ReadOnlySpan<char> str)
    {
        int utf8Bytes = Encoding.UTF8.GetByteCount(str);
        int length = utf8Bytes + 1;
        _ptr = (byte*)MemoryUtility.Alloc(length);
        fixed (char* p = str)
        {
            Encoding.UTF8.GetBytes(p, str.Length, _ptr, utf8Bytes);
        }
        _ptr[utf8Bytes] = 0;
    }

    protected override void Dispose(bool disposing)
    {
        MemoryUtility.Free(_ptr);
    }
}