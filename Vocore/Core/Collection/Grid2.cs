using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Vocore
{
    public class Grid2<T> where T : unmanaged
    {
        public const int MaxBufferSize = 256 * 256;
        private int _width;
        private int _height;
        private T[] _buffer;
        private T _defaultValue;
        public int Width => _width;
        public int Height => _height;
        public T[] Raw => _buffer;
        public unsafe T* UnsafePtr
        {
            get
            {
                fixed (T* result = &_buffer[0])
                {
                    return result;
                }
            }
        }

        public Grid2(int width, int height, T defaultValue = default(T))
        {
            if (width <= 0 || height <= 0) throw ExceptionCollection.SizeIsEmpty;
            if (width * height > MaxBufferSize) throw ExceptionCollection.GridSizeTooLarge(width, height);

            _width = width;
            _height = height;
            _buffer = new T[width * height];
        }

        public T this[int x, int y]
        {
            get
            {
                if (NotInRange(x, y)) return _defaultValue;
                return _buffer[x + y * _width];
            }
            set
            {
                if (NotInRange(x, y)) return;
                _buffer[x + y * _width] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool NotInRange(int x, int y)
        {
            return x < 0 || x >= _width || y < 0 || y >= _height;
        }

        public void Resize(int width, int height)
        {
            if (width <= 0 || height <= 0) throw ExceptionCollection.SizeIsEmpty;
            if (width * height > MaxBufferSize) throw ExceptionCollection.GridSizeTooLarge(width, height);

            T[] newBuffer = new T[width * height];
            int copyWidth = Math.Min(width, _width);
            int copyHeight = Math.Min(height, _height);
            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                {
                    newBuffer[x + y * width] = _buffer[x + y * _width];
                }
            }

            _width = width;
            _height = height;
            _buffer = newBuffer;
        }

    }
}

