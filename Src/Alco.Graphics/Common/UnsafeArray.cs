using System.Runtime.CompilerServices;

namespace Alco.Graphics;

internal unsafe struct UnsafeArray<T> : IDisposable where T : unmanaged
{
    private T* _ptr = null;
    private int _length;

    //indexer
    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (index < 0 || index >= _length)
            {
                throw new IndexOutOfRangeException();
            }
            return ref _ptr[index];
        }
    }

    public ref T this[uint index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (index < 0 || index >= _length)
            {
                throw new IndexOutOfRangeException();
            }
            return ref _ptr[index];
        }
    }

    public readonly T* Ptr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _ptr;
    }

    public readonly int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    public UnsafeArray(int length)
    {
        _length = length;
        _ptr = UtilsInterop.Alloc<T>(length);
    }

    public void EnsureCapacity(int length)
    {
        if (_length < length)
        {
            TryFree();
            _length = length;
            _ptr = UtilsInterop.Alloc<T>(length);
        }
    }

    private void TryFree()
    {
        if (_ptr != null)
        {
            UtilsInterop.Free(_ptr);
            _ptr = null;
            _length = 0;
        }
    }

    public void Dispose()
    {
        TryFree();
    }
}