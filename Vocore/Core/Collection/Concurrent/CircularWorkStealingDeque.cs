using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Vocore
{
    internal class CircularWorkStealingDeque<T>
    {
        private long _bottom = 0;
        private long _top = 0;
        private volatile CircularArray<T> _array;
        private static readonly T DefaultValue = default(T);

        public CircularWorkStealingDeque(int capacity)
        {
            _array = new CircularArray<T>((int)Math.Ceiling(Math.Log(capacity, 2)));
        }

        private long VolatileBottom
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Volatile.Read(ref _bottom);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Volatile.Write(ref _bottom, value);
            }
        }

        private long InterlockTop
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Interlocked.Read(ref _top);
            }
        }

        public long Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return VolatileBottom - InterlockTop;
            }
        }

        public bool HasContent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return VolatileBottom > InterlockTop;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CASTop(long oldTop, long newTop)
        {
            return Interlocked.CompareExchange(ref _top, newTop, oldTop) == oldTop;
        }

        /// <summary>
        /// Push an item to the bottom of the deque
        /// not concurrent with itself or TryPop, only concurrent with TrySteal
        /// </summary>
        public void Push(T item)
        {
            long b = VolatileBottom;
            long t = InterlockTop;
            CircularArray<T> a = _array;
            long size = b - t;
            if (size >= a.Capacity - 1)
            {
                a = a.EnsureCapacity(b, t);
                _array = a;
            }
            a[b] = item;
            VolatileBottom = b + 1;
        }

        /// <summary>
        /// Pop an item from the bottom of the deque
        /// not concurrent with itself or Push, only concurrent with TrySteal
        /// </summary>
        public bool TryPop(out T item)
        {
            long b = VolatileBottom;
            CircularArray<T> a = _array;
            b--;
            VolatileBottom = b;
            long t = InterlockTop;
            long size = b - t;
            if (size < 0)
            {
                VolatileBottom = t;
                item = DefaultValue;
                return false;
            }
            item = a[b];
            if (size > 0)
            {
                return true;
            }
            if (!CASTop(t, t + 1))
            {
                item = DefaultValue;
                return false;
            }
            VolatileBottom = t + 1;
            return true;
        }

        /// <summary>
        /// Pop an item from the top of the deque
        /// Can be concurrent with itself, push, and TryPop
        /// </summary>
        public bool TrySteal(out T item)
        {
            long t = InterlockTop;
            long b = VolatileBottom;
            CircularArray<T> a = _array;
            long size = b - t;
            if (size <= 0)
            {
                item = DefaultValue;
                return false;
            }
            item = a[t];
            if (!CASTop(t, t + 1))
            {
                item = DefaultValue;
                return false;
            }
            return true;
        }
    }
}