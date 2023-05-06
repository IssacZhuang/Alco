using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public unsafe struct NativeList<T> : IList<T>, IDisposable where T : unmanaged
    {
        private const int DefaultSize = 4;
        private static readonly int _stride = UtilsUnsafe.SizeOf<T>();
        private void* _ptrBuffer;
        private int _size;
        private int _listSize;
        private bool _isDisposed;
        public int Length => _size;

        public T* Raw => (T*)_ptrBuffer;
        public int Size => _size;
        public int Stride => _stride;
        public int Count => Size;
        public bool IsReadOnly => false;
        public bool IsDisposed => _isDisposed;

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

        public NativeList(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            _ptrBuffer = UtilsUnsafe.Alloc(_stride * size);
            _size = 0;
            _listSize = size;
            _isDisposed = false;
        }

        public void Add(T value)
        {
            EnsureSize(_size + 1);
            _size++;
            this[_size - 1] = value;
        }

        public void Insert(int index, T value)
        {
            if (NotInRange(index)) throw ExceptionCollection.OutOfRange;
            EnsureSize(_size + 1);
            UtilsUnsafe.MemCopy((T*)_ptrBuffer + index, (T*)_ptrBuffer + index + 1, _stride * (_size - index));
            _size++;
            this[index] = value;
        }

        public bool Remove(T value)
        {
            for (int i = 0; i < _size; i++)
            {
                if (this[i].Equals(value))
                {
                    RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            if (NotInRange(index)) throw ExceptionCollection.OutOfRange;
            UtilsUnsafe.MemCopy((T*)_ptrBuffer + index + 1, (T*)_ptrBuffer + index, _stride * (_size - index - 1));
            EnsureSize(_size - 1);
            _size--;
        }

        public int IndexOf(T value)
        {
            for (int i = 0; i < _size; i++)
            {
                if (this[i].Equals(value))
                {
                    return i;
                }
            }
            return -1;
        }


        public bool Contains(T value)
        {
            for (int i = 0; i < _size; i++)
            {
                if (this[i].Equals(value))
                {
                    return true;
                }
            }
            return false;
        }

        public void Clear()
        {
            Resize(DefaultSize);
            _size = 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (arrayIndex < 0) throw ExceptionCollection.OutOfRange;
            if (array.Length - arrayIndex < _size) throw ExceptionCollection.OutOfRange;
            unsafe
            {
                fixed (T* ptr = array)
                {
                    UtilsUnsafe.MemCopy(ptr + arrayIndex, _ptrBuffer, _stride * _size);
                }
            }
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

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            UtilsUnsafe.Free(_ptrBuffer);
            GC.SuppressFinalize(this);
        }

        private void Resize(int size)
        {
            if (size < DefaultSize) size = DefaultSize;

            void* tmpPtr = UtilsUnsafe.Alloc(_stride * size);

            if (_ptrBuffer != null)
            {
                UtilsUnsafe.MemCopy(_ptrBuffer, tmpPtr, _stride * _size);
                UtilsUnsafe.Free(_ptrBuffer);
            }

            _ptrBuffer = tmpPtr;
            _listSize = size;
        }

        private void EnsureSize(int size)
        {
            if (size > _listSize)
            {
                Resize(_listSize * 2);
            }
            else if (size >= DefaultSize && size < _listSize / 2)
            {
                Resize(_listSize / 2);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool NotInRange(int index)
        {
            return index < 0 || index >= _size;
        }
    }
}

