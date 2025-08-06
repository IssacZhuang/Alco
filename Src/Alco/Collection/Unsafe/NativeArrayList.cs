using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Alco;

namespace Alco
{
    /// <summary>
    /// A native array list implementation that provides dynamic array functionality with unsafe memory management.
    /// </summary>
    /// <typeparam name="T">The unmanaged type of elements in the array list.</typeparam>
    public unsafe struct NativeArrayList<T> : IDisposable where T : unmanaged
    {
        private const int DefaultSize = 4;
        private void* _ptrBuffer;
        private int _length;
        private int _capacity;
        private bool _isDisposed;

        /// <summary>
        /// Gets the current number of elements in the array list.
        /// </summary>
        public readonly int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length;
        }

        /// <summary>
        /// Gets a pointer to the underlying buffer.
        /// </summary>
        public readonly unsafe T* UnsafePointer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (T*)_ptrBuffer;
        }

        /// <summary>
        /// Returns a span representing the entire array list.
        /// </summary>
        /// <returns>A span that represents the entire array list.</returns>
        public Span<T> AsSpan()
        {
            return new Span<T>((T*)_ptrBuffer, _length);
        }

        /// <summary>
        /// Returns a span representing a portion of the array list starting at the specified index with the specified length.
        /// </summary>
        /// <param name="start">The zero-based starting index of the span.</param>
        /// <param name="length">The number of elements to include in the span.</param>
        /// <returns>A span that represents the specified portion of the array list.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when start or length is negative, or when start + length exceeds the bounds of the array list.</exception>
        public Span<T> AsSpan(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "Start index cannot be negative.");
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
            if (start + length > _length) throw new ArgumentOutOfRangeException(nameof(length), "The start index and length would exceed the bounds of the array list.");

            return new Span<T>((T*)_ptrBuffer + start, length);
        }

        /// <summary>
        /// Gets the size in bytes of each element.
        /// </summary>
        public int Stride => sizeof(T);

        /// <summary>
        /// Gets a value indicating whether this collection is read-only.
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// Gets the current capacity of the array list.
        /// </summary>
        public int Capacity
        {
            get
            {
                return _capacity;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the array list has been initialized.
        /// </summary>
        public bool Initialized
        {
            get
            {
                return _ptrBuffer != null;
            }
        }

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get or set.</param>
        /// <returns>The element at the specified index.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when the index is out of range.</exception>
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

        /// <summary>
        /// Initializes a new instance of the NativeArrayList with the specified initial capacity.
        /// </summary>
        /// <param name="size">The initial capacity of the array list.</param>
        /// <exception cref="EmptySizeException">Thrown when size is less than or equal to zero.</exception>
        public NativeArrayList(int size)
        {
            if (size <= 0) throw new EmptySizeException(nameof(size));
            _ptrBuffer = UtilsMemory.Alloc(sizeof(T) * size);
            _length = 0;
            _capacity = size;
            _isDisposed = false;
        }

        /// <summary>
        /// Adds an element to the end of the array list.
        /// </summary>
        /// <param name="value">The element to add.</param>
        public void Add(T value)
        {
            EnsureSize(_length + 1);
            _length++;
            this[_length - 1] = value;
        }

        /// <summary>
        /// Inserts an element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index at which to insert the element.</param>
        /// <param name="value">The element to insert.</param>
        /// <exception cref="IndexOutOfRangeException">Thrown when the index is out of range.</exception>
        public void Insert(int index, T value)
        {
            if (NotInRange(index)) throw new IndexOutOfRangeException(nameof(index));
            EnsureSize(_length + 1);
            UtilsMemory.MemCopy((T*)_ptrBuffer + index, (T*)_ptrBuffer + index + 1, sizeof(T) * (_length - index));
            _length++;
            this[index] = value;
        }

        /// <summary>
        /// Removes the first occurrence of the specified element from the array list.
        /// </summary>
        /// <param name="value">The element to remove.</param>
        /// <returns>True if the element was successfully removed; otherwise, false.</returns>
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

        /// <summary>
        /// Removes the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to remove.</param>
        /// <exception cref="IndexOutOfRangeException">Thrown when the index is out of range.</exception>
        public void RemoveAt(int index)
        {
            if (NotInRange(index)) throw new IndexOutOfRangeException(nameof(index));
            UtilsMemory.MemCopy((T*)_ptrBuffer + index + 1, (T*)_ptrBuffer + index, sizeof(T) * (_length - index - 1));
            _length--;
        }

        /// <summary>
        /// Returns the zero-based index of the first occurrence of the specified element.
        /// </summary>
        /// <param name="value">The element to locate.</param>
        /// <returns>The zero-based index of the first occurrence of the element, or -1 if not found.</returns>
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

        /// <summary>
        /// Determines whether the array list contains the specified element.
        /// </summary>
        /// <param name="value">The element to locate.</param>
        /// <returns>True if the element is found; otherwise, false.</returns>
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

        /// <summary>
        /// Removes all elements from the array list.
        /// </summary>
        public void Clear()
        {
            _length = 0;
        }

        /// <summary>
        /// Releases all resources used by the NativeArrayList.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            UtilsMemory.Free(_ptrBuffer);
            GC.SuppressFinalize(this);
        }

        private void Resize(int size)
        {
            if (size < DefaultSize) size = DefaultSize;

            void* tmpPtr = UtilsMemory.Alloc(sizeof(T) * size);

            if (_ptrBuffer != null)
            {
                UtilsMemory.MemCopy(_ptrBuffer, tmpPtr, sizeof(T) * _length);
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool NotInRange(int index)
        {
            return index < 0 || index >= _length;
        }
    }
}