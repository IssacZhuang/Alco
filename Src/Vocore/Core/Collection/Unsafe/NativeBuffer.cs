using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Vocore.Unsafe;

namespace Vocore
{
    public unsafe struct NativeBuffer<T> : IDisposable where T : unmanaged
    {
        private void* _ptrBuffer;
        private int _length;
        private bool _isDisposed;
        private static readonly int _stride = UtilsMemory.SizeOf<T>();

        public int Length => _length;

        public unsafe T* UnsafePointer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (T*)_ptrBuffer;
        }

        public int Stride => _stride;
        public bool IsDisposed => _isDisposed;

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
            get
            {
                return new MemoryRef<T>((T*)_ptrBuffer, (uint)_length);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get(int index)
        {
            if (NotInRange(index)) throw new IndexOutOfRangeException(nameof(index));
            return UnsafePointer[index];
        }

        public NativeBuffer(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            _ptrBuffer = UtilsMemory.Alloc(size * _stride);
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

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _length; i++)
            {
                yield return this[i];
            }
        }


        public void EnsureSizeNoCopy(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            if (size <= _length) return;
            FreeMemory();
            _ptrBuffer = UtilsMemory.Alloc(size * _stride);
            _length = size;
        }

        public void EnsureSize(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            if (size <= _length) return;
            Resize(size);
        }

        public void Resize(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            if (size == _length) return;
            void* ptr = UtilsMemory.Alloc(size * _stride);
            int min = Math.Min(size, _length);
            UtilsMemory.MemCopy(_ptrBuffer, ptr, min * _stride);
            FreeMemory();
            _ptrBuffer = ptr;
            _length = size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> GetReadOnlySpan()
        {
            return new ReadOnlySpan<T>(UnsafePointer, _length);
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

        public void CopyToArray(T[] array)
        {
            if (array.Length < _length) throw ExceptionCollection.SizeIsEmpty;
            if(array.Length == _length) throw ExceptionCollection.LengthNotEqual;
            
            for (int i = 0; i < _length; i++)
            {
                array[i] = this[i];
            }
        }

        public unsafe void CopyToArrayUnsafe(T[] array)
        {
            fixed (T* ptr = array)
            {
                UtilsMemory.MemCopy(_ptrBuffer, ptr, _length * _stride);
            }
        }

    }
}
