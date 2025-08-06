using System;
using System.Runtime.CompilerServices;

using Alco;

namespace Alco
{
    /// <summary>
    /// A high-performance native buffer that manages unmanaged memory allocation and provides efficient access to elements.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the buffer, must be unmanaged.</typeparam>
    public unsafe struct NativeBuffer<T> : IDisposable where T : unmanaged
    {
        public const int DefaultCapacity = 16;

        private void* _ptrBuffer;
        private int _length;
        private int _capacity;
        private bool _isDisposed;

        /// <summary>
        /// Gets the logical size of the buffer.
        /// </summary>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length;
        }

        /// <summary>
        /// Gets the actual capacity of the underlying buffer.
        /// </summary>
        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _capacity;
        }

        /// <summary>
        /// Gets the unsafe pointer to the buffer data.
        /// </summary>
        public unsafe T* UnsafePointer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (T*)_ptrBuffer;
        }

        /// <summary>
        /// Gets a value indicating whether the buffer has been disposed.
        /// </summary>
        public bool IsDisposed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isDisposed;
        }

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get or set.</param>
        /// <returns>The element at the specified index.</returns>
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
        /// Gets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get.</param>
        /// <returns>The element at the specified index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get(int index)
        {
            if (NotInRange(index)) throw new IndexOutOfRangeException(nameof(index));
            return UnsafePointer[index];
        }

        /// <summary>
        /// Initializes a new instance of the NativeBuffer struct with the specified size.
        /// </summary>
        /// <param name="size">The initial size of the buffer.</param>
        public NativeBuffer(int size)
        {
            if (size <= 0) throw new EmptySizeException(nameof(size));
            _ptrBuffer = UtilsMemory.Alloc(size * sizeof(T));
            _length = size;
            _capacity = size;
            _isDisposed = false;
        }

        /// <summary>
        /// Initializes a new instance of the NativeBuffer struct with the specified size.
        /// </summary>
        /// <param name="size">The initial size of the buffer.</param>
        public NativeBuffer(uint size) : this((int)size)
        {
        }

        /// <summary>
        /// Releases all resources used by the NativeBuffer.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            FreeMemory();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Ensures the buffer has at least the specified size without copying existing data.
        /// Uses intelligent expansion strategy similar to ArrayBuffer.
        /// </summary>
        /// <param name="size">The minimum size required.</param>
        public void SetSizeWithoutCopy(int size)
        {
            if (size <= 0) throw new EmptySizeException(nameof(size));
            if (size <= _capacity)
            {
                _length = size;
                return;
            }

            int newCapacity = CalculateNewCapacity(size);
            FreeMemory();
            _ptrBuffer = UtilsMemory.Alloc(newCapacity * sizeof(T));
            _capacity = newCapacity;
            _length = size;
        }

        /// <summary>
        /// Ensures the buffer has at least the specified size. Uses intelligent expansion strategy:
        /// - If buffer is empty, expands to DefaultCapacity first
        /// - If still not enough, doubles capacity until requirement is met
        /// </summary>
        /// <param name="size">The minimum size required.</param>
        public void SetSize(int size)
        {
            if (size <= 0) return;
            if (size <= _capacity)
            {
                _length = size;
                return;
            }

            int newCapacity = CalculateNewCapacity(size);
            Resize(newCapacity);
            _length = size;
        }

        private int CalculateNewCapacity(int size)
        {
            int newCapacity;
            if (_capacity <= 0)
            {
                // Start with DefaultCapacity if buffer is empty
                newCapacity = DefaultCapacity;
            }
            else
            {
                // Double capacity until we meet the requirement
                newCapacity = _capacity;
            }

            while (newCapacity < size)
            {
                newCapacity *= 2;
            }

            return newCapacity;
        }

        private void Resize(int newCapacity)
        {
            if (newCapacity <= 0) throw new EmptySizeException(nameof(newCapacity));
            if (newCapacity == _capacity) return;

            void* ptr = UtilsMemory.Alloc(newCapacity * sizeof(T));
            int copySize = Math.Min(newCapacity, _length);
            if (_ptrBuffer != null && copySize > 0)
            {
                UtilsMemory.MemCopy(_ptrBuffer, ptr, copySize * sizeof(T));
            }
            FreeMemory();
            _ptrBuffer = ptr;
            _capacity = newCapacity;
        }

        /// <summary>
        /// Returns a span that represents the buffer data.
        /// </summary>
        /// <returns>A span over the buffer elements.</returns>
        public Span<T> AsSpan()
        {
            return new Span<T>((T*)_ptrBuffer, _length);
        }

        /// <summary>
        /// Returns a span that represents a portion of the buffer data.
        /// </summary>
        /// <param name="start">The zero-based index at which to begin the span.</param>
        /// <param name="length">The number of elements to include in the span.</param>
        /// <returns>A span over the specified portion of buffer elements.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when start or length is negative, or when start + length exceeds the buffer length.</exception>
        public Span<T> AsSpan(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "Start index cannot be negative.");
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
            if (start + length > _length) throw new ArgumentOutOfRangeException(nameof(length), "Start index and length exceed the buffer length.");

            return new Span<T>((T*)_ptrBuffer + start, length);
        }

        private void FreeMemory()
        {
            if (_ptrBuffer != null)
            {
                UtilsMemory.Free(_ptrBuffer);
                _ptrBuffer = null;
                _capacity = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool NotInRange(int index)
        {
            return index < 0 || index >= _length;
        }
    }
}
