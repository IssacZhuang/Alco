using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public class StructuredBuffer<T> where T : unmanaged
    {
        private readonly T[] _innerArray;
        private readonly int _size;

        public int Length => _size;

        public T this[int index] {
            get
            {
                return _innerArray[index];
            }
            set
            {
                _innerArray[index] = value;
            }
        }

        public T[] Raw => _innerArray;
        public unsafe T* UnsafePtr
        {
            get
            {
                fixed(T* result = &_innerArray[0])
                {
                    return result;
                }
            }
        }

        public StructuredBuffer(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            _innerArray = new T[size];
            this._size = size;
        }
    }
}
