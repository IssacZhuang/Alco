using System;
using System.Runtime.CompilerServices;

using Alco;

namespace Alco
{
    public unsafe struct NativeBuffer<T> : IDisposable where T : unmanaged
    {
        private void* _ptrBuffer;
        private int _length;
        private bool _isDisposed;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length;
        }

        public unsafe T* UnsafePointer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (T*)_ptrBuffer;
        }

        public int Stride
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => sizeof(T);
        }
        public bool IsDisposed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isDisposed;
        }

        public T this[int index]
        {
            get
            {
                unsafe
                {
                    if (NotInRange(index)) throw new IndexOutOfRangeException(nameof(index));
                    return ((T*)_ptrBuffer)[index];
                }
            }
            set
            {
                unsafe
                {
                    if (NotInRange(index)) throw new IndexOutOfRangeException(nameof(index));
                    ((T*)_ptrBuffer)[index] = value;
                }
            }
        }

        public MemoryRef<T> MemoryRef
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new MemoryRef<T>((T*)_ptrBuffer, _length);
        }

        public ReadOnlySpan<T> ReadOnlySpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new ReadOnlySpan<T>((T*)_ptrBuffer, _length);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get(int index)
        {
            if (NotInRange(index)) throw new IndexOutOfRangeException(nameof(index));
            return UnsafePointer[index];
        }

        public NativeBuffer(int size)
        {
            if (size <= 0) throw new EmptySizeException(nameof(size));
            _ptrBuffer = UtilsMemory.Alloc(size * sizeof(T));
            _length = size;
            _isDisposed = false;
        }

        public NativeBuffer(uint size) : this((int)size)
        {
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            FreeMemory();
            GC.SuppressFinalize(this);
        }

        public void EnsureSizeWithoutCopy(int size)
        {
            if (size <= 0) throw new EmptySizeException(nameof(size));
            if (size <= _length) return;
            FreeMemory();
            _ptrBuffer = UtilsMemory.Alloc(size * sizeof(T));
            _length = size;
        }

        public void EnsureSize(int size)
        {
            if (size <= 0) return;
            if (size <= _length) return;
            Resize(size);
        }

        public void Resize(int size)
        {
            if (size <= 0) throw new EmptySizeException(nameof(size));
            if (size == _length) return;
            void* ptr = UtilsMemory.Alloc(size * sizeof(T));
            int min = Math.Min(size, _length);
            UtilsMemory.MemCopy(_ptrBuffer, ptr, min * sizeof(T));
            FreeMemory();
            _ptrBuffer = ptr;
            _length = size;
        }

        private void FreeMemory()
        {
            if (_ptrBuffer != null)
            {
                UtilsMemory.Free(_ptrBuffer);
                _ptrBuffer = null;
                _length = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool NotInRange(int index)
        {
            return index < 0 || index >= _length;
        }
    }
}
