using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Vocore
{
    public unsafe struct NativeBuffer<T> : IReadOnlyList<T>, IDisposable where T : unmanaged
    {
        private void* _ptrArray;
        private readonly int _size;
        private readonly int _stride;

        public int Length => _size;

        public T* Raw => (T*)_ptrArray;
        public int Size => _size;
        public int Stride => _stride;

        public int Count => Size;

        public T this[int index]
        {
            get
            {
                unsafe
                {
                    if (index >= _size) throw ExceptionCollection.OutOfRange;
                    return ((T*)_ptrArray)[index];
                }
            }
            set
            {
                unsafe
                {
                    if (index >= _size) throw ExceptionCollection.OutOfRange;
                    ((T*)_ptrArray)[index] = value;
                }
            }
        }


        public NativeBuffer(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;

            _stride = Marshal.SizeOf<T>();
            _ptrArray = Marshal.AllocHGlobal(_stride * size).ToPointer();
            this._size = size;
        }

        public void Dispose()
        {
            if (_ptrArray != null)
            {
                Marshal.FreeHGlobal((IntPtr)_ptrArray);
                _ptrArray = null;
            }
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
    }
}
