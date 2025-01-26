using System;
using System.Runtime.CompilerServices;

namespace Alco
{
    internal class CircularArray<T>
    {
        private readonly int _logSize;
        private readonly T[] _segment;

        public CircularArray(int logSize)
        {
            _logSize = logSize;
            _segment = new T[Capacity];
        }

        public long Capacity
        {
            get => 1 << _logSize;
        }


        public T this[long i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _segment[i % Capacity];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _segment[i % Capacity] = value;
            }
        }

        public CircularArray<T> EnsureCapacity(long b, long t)
        {
            CircularArray<T> newArray = new CircularArray<T>(_logSize + 1);
            for (var i = t; i < b; i++)
            {
                newArray[i] = this[i];
            }

            return newArray;
        }
    }
}