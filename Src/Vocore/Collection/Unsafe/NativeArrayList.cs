using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Vocore.Unsafe;

namespace Vocore
{
    public unsafe struct NativeArrayList<T> : IDisposable where T : unmanaged
    {
        private const int DefaultSize = 4;
        private static readonly int _stride = UtilsMemory.SizeOf<T>();
        private void* _ptrBuffer;
        private int _length;
        private int _capacity;
        private int _preAllocSize;
        private bool _isDisposed;
        private bool _autoCompress;
        public bool AutoCompress { get => _autoCompress; set => _autoCompress = value; }
        public int Length => _length;

        public unsafe T* UnsafePointer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (T*)_ptrBuffer;
        }

        public MemoryRef<T> MemoryRef
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new MemoryRef<T>((T*)_ptrBuffer, (uint)_length);
        }

        public int Stride => _stride;
        public int Count => _length;
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

        public NativeArrayList(int size)
        {
            if (size <= 0) throw new EmptySizeException(nameof(size));
            _ptrBuffer = UtilsMemory.Alloc(_stride * size);
            _length = 0;
            _capacity = size;
            _preAllocSize = size;
            _isDisposed = false;
            _autoCompress = true;
        }

        public NativeArrayList(int size, bool autoCompress)
        {
            if (size <= 0) throw new EmptySizeException(nameof(size));
            _ptrBuffer = UtilsMemory.Alloc(_stride * size);
            _length = 0;
            _capacity = size;
            _preAllocSize = size;
            _isDisposed = false;
            _autoCompress = autoCompress;
        }

        public void Add(T value)
        {
            EnsureSize(_length + 1);
            _length++;
            this[_length - 1] = value;
        }

        public void Insert(int index, T value)
        {
            if (NotInRange(index)) throw new IndexOutOfRangeException(nameof(index));
            EnsureSize(_length + 1);
            UtilsMemory.MemCopy((T*)_ptrBuffer + index, (T*)_ptrBuffer + index + 1, _stride * (_length - index));
            _length++;
            this[index] = value;
        }

        public bool Remove(T value)
        {
            for (int i = 0; i < _length; i++)
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
            if (NotInRange(index)) throw new IndexOutOfRangeException(nameof(index));
            UtilsMemory.MemCopy((T*)_ptrBuffer + index + 1, (T*)_ptrBuffer + index, _stride * (_length - index - 1));
            EnsureSize(_length - 1);
            _length--;
        }

        public int IndexOf(T value)
        {
            for (int i = 0; i < _length; i++)
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
            for (int i = 0; i < _length; i++)
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
            _length = 0;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            UtilsMemory.Free(_ptrBuffer);
            GC.SuppressFinalize(this);
        }

        private void Resize(int size)
        {
            if (size < DefaultCapacity) size = DefaultCapacity;

            void* tmpPtr = UtilsMemory.Alloc(_stride * size);

            if (_ptrBuffer != null)
            {
                UtilsMemory.MemCopy(_ptrBuffer, tmpPtr, _stride * _length);
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
            return index < 0 || index >= _length;
        }
    }
}