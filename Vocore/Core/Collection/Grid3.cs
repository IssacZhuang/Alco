using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Vocore
{
    public class Grid3<T> where T : unmanaged
    {
        public const int MaxBufferSize = 256 * 256;
        private int _sizeX;
        private int _sizeY;
        private int _sizeZ;

        private T[] _buffer;
        private T _defaultValue;

        public int SizeX => _sizeX;
        public int SizeY => _sizeY;
        public int SizeZ => _sizeZ;

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

        public Grid3(int sizeX, int sizeY, int sizeZ, T defaultValue = default(T))
        {
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0) throw ExceptionCollection.SizeIsEmpty;
            if (sizeX * sizeY * sizeZ > MaxBufferSize) throw ExceptionCollection.GridSizeTooLarge(sizeX, sizeY, sizeZ);

            _sizeX = sizeX;
            _sizeY = sizeY;
            _sizeZ = sizeZ;
            _buffer = new T[sizeX * sizeY * sizeZ];
        }

        public T this[int x, int y, int z]
        {
            get
            {
                if (NotInRange(x, y, z)) return _defaultValue;
                return _buffer[x + y * _sizeX + z * _sizeX * _sizeY];
            }
            set
            {
                if (NotInRange(x, y, z)) return;
                _buffer[x + y * _sizeX + z * _sizeX * _sizeY] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool NotInRange(int x, int y, int z)
        {
            return x < 0 || x >= _sizeX || y < 0 || y >= _sizeY || z < 0 || z >= _sizeZ;
        }

        public void Resize(int sizeX, int sizeY, int sizeZ)
        {
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0) throw ExceptionCollection.SizeIsEmpty;
            if (sizeX * sizeY * sizeZ > MaxBufferSize) throw ExceptionCollection.GridSizeTooLarge(sizeX, sizeY, sizeZ);

            T[] newBuffer = new T[sizeX * sizeY * sizeZ];
            int minX = Math.Min(sizeX, _sizeX);
            int minY = Math.Min(sizeY, _sizeY);
            int minZ = Math.Min(sizeZ, _sizeZ);

            for (int z = 0; z < minZ; z++)
            {
                for (int y = 0; y < minY; y++)
                {
                    for (int x = 0; x < minX; x++)
                    {
                        newBuffer[x + y * sizeX + z * sizeX * sizeY] = _buffer[x + y * _sizeX + z * _sizeX * _sizeY];
                    }
                }
            }

            _buffer = newBuffer;
            _sizeX = sizeX;
            _sizeY = sizeY;
            _sizeZ = sizeZ;
        }
    }
}

