using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Vocore.Unsafe;

namespace Vocore
{
    public unsafe struct NativeArrayList<T> : IList<T>, IDisposable where T : unmanaged
    {
        private const int DefaultSize = 4;
        private static readonly int _stride = UtilsMemory.SizeOf<T>();
        private void* _ptrBuffer;
        private int _size;
        private int _capacity;
        private int _preAllocSize;
        private bool _isDisposed;
        private bool _autoCompress;
        public bool AutoCompress { get => _autoCompress; set => _autoCompress = value; }
        public int Length => _size;

        public T* Ptr => (T*)_ptrBuffer;
        public int Size => _size;
        public int Stride => _stride;
        public int Count => Size;
        public bool IsReadOnly => false;
        public bool IsDisposed => _isDisposed;
        public int DefaultCapacity
        {
            get
            {
                return _preAllocSize > 0 ? _preAllocSize : DefaultSize;
            }
        }

        public int Capacity
        {
            get
            {
                return _capacity;
            }
        }


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

        public NativeArrayList(int size)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            _ptrBuffer = UtilsMemory.Alloc(_stride * size);
            _size = 0;
            _capacity = size;
            _preAllocSize = size;
            _isDisposed = false;
            _autoCompress = true;
        }

        public NativeArrayList(int size, bool autoCompress)
        {
            if (size <= 0) throw ExceptionCollection.SizeIsEmpty;
            _ptrBuffer = UtilsMemory.Alloc(_stride * size);
            _size = 0;
            _capacity = size;
            _preAllocSize = size;
            _isDisposed = false;
            _autoCompress = autoCompress;
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
            UtilsMemory.MemCopy((T*)_ptrBuffer + index, (T*)_ptrBuffer + index + 1, _stride * (_size - index));
            _size++;
            this[index] = value;
        }

        public unsafe void UnsafeAdd(T value)
        {
            EnsureSize(_size + 1);
            _size++;
            Ptr[_size - 1] = value;
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
            UtilsMemory.MemCopy((T*)_ptrBuffer + index + 1, (T*)_ptrBuffer + index, _stride * (_size - index - 1));
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
            if (AutoCompress) Resize(DefaultCapacity);
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
                    UtilsMemory.MemCopy(ptr + arrayIndex, _ptrBuffer, _stride * _size);
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
            UtilsMemory.Free(_ptrBuffer);
            GC.SuppressFinalize(this);
        }

        public void EnsureCapacityAndDiableAutoCompress(int size)
        {
            Resize(size);
            _autoCompress = false;
        }

        private void Resize(int size)
        {
            if (size < DefaultCapacity) size = DefaultCapacity;

            void* tmpPtr = UtilsMemory.Alloc(_stride * size);

            if (_ptrBuffer != null)
            {
                UtilsMemory.MemCopy(_ptrBuffer, tmpPtr, _stride * _size);
                UtilsMemory.Free(_ptrBuffer);
            }

            _ptrBuffer = tmpPtr;
            _capacity = size;
        }

        private void EnsureSize(int size)
        {
            if (size > _capacity)
            {
                Resize(_capacity * 2);
            }

            if (AutoCompress && size > DefaultCapacity && size < _capacity / 2)
            {
                Resize(_capacity / 2);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool NotInRange(int index)
        {
            return index < 0 || index >= _size;
        }
    }
}