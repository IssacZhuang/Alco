using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Vocore.Unsafe;

namespace Vocore
{
    public unsafe struct NativeBuffer<T> : IReadOnlyList<T>, IDisposable where T : unmanaged
    {
        private void* _ptrBuffer;
        private int _size;
        private bool _isDisposed;
        private static readonly int _stride = UtilsMemory.SizeOf<T>();

        public int Length => _size;
        public T* Ptr => (T*)_ptrBuffer;
        public int Size => _size;
        public int Stride => _stride;
        public int Count => Size;


        public T this[int index]
        {
            get
            {
                unsafe
                {
                    if (NotInRange(index)) throw ExceptionCollection.OutOfRange;
                    return ((T*)_ptrBuffer)[index];
                }
            }
            set
            {
                unsafe
                {
                    if (NotInRange(index)) throw ExceptionCollection.OutOfRange;
                    ((T*)_ptrBuffer)[index] = value;
                }
            }
        }

        public NativeBuffer(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            _ptrBuffer = UtilsMemory.Alloc(size * _stride);
            _size = size;
            _isDisposed = false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            FreeMemory();
            GC.SuppressFinalize(this);
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _size; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void FastEnsureSize(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            if (size <= _size) return;
            FreeMemory();
            _ptrBuffer = UtilsMemory.Alloc(size * _stride);
            _size = size;
        }

        public void Resize(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            if (size == _size) return;
            void* ptr = UtilsMemory.Alloc(size * _stride);
            int min = Math.Min(size, _size);
            UtilsMemory.MemCopy(_ptrBuffer, ptr, min * _stride);
            FreeMemory();
            _ptrBuffer = ptr;
            _size = size;
        }

        private void FreeMemory()
        {
            if (_ptrBuffer != null)
            {
                UtilsMemory.Free(_ptrBuffer);
                _ptrBuffer = null;
                _size = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool NotInRange(int index)
        {
            return index < 0 || index >= _size;
        }
    }
}
