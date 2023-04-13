using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Vocore
{
    public unsafe struct NativeBuffer<T> : IReadOnlyList<T>, IDisposable where T : unmanaged
    {
        private void* _ptrBuffer;
        private readonly int _size;
        private readonly int _stride;

        public int Length => _size;

        public T* Raw => (T*)_ptrBuffer;
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
                    return ((T*)_ptrBuffer)[index];
                }
            }
            set
            {
                unsafe
                {
                    if (index >= _size) throw ExceptionCollection.OutOfRange;
                    ((T*)_ptrBuffer)[index] = value;
                }
            }
        }

        public NativeBuffer(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;

            _stride = Marshal.SizeOf<T>();
            _ptrBuffer = Marshal.AllocHGlobal(_stride * size).ToPointer();
            this._size = size;
        }

        public void Dispose()
        {
            if (_ptrBuffer != null)
            {
                Marshal.FreeHGlobal((IntPtr)_ptrBuffer);
                _ptrBuffer = null;
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
