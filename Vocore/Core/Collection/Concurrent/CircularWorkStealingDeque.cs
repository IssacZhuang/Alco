using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Vocore
{
    public enum StealingResult
    {
        Success,
        Empty,
        Abort
    }
    internal class CircularWorkStealingDeque<T>
    {
        private int _bottom = 0;
        private int _top = 0;
        private volatile CircularArray<T> _array;
        private static readonly T DefaultValue = default(T);

        public CircularWorkStealingDeque(int capacity)
        {
            _array = new CircularArray<T>((int)Math.Ceiling(Math.Log(capacity, 2)));
        }

        private int VolatileBottom
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

        private int VolatileTop
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Volatile.Read(ref _top);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Volatile.Write(ref _top, value);
            }
        }

        public long Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return VolatileBottom - VolatileTop;
            }
        }

        public bool HasContent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return VolatileBottom > VolatileTop;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CASTop(int oldTop, int newTop)
        {
            return Interlocked.CompareExchange(ref _top, newTop, oldTop) == oldTop;
        }


        /// <summary>
        /// Clear the deque, also reset the index in circular array
        /// not concurrent with any other operations
        /// </summary>
        public void Clear()
        {
            VolatileBottom = 0;
            VolatileTop = 0;
        }

        /// <summary>
        /// Push an item to the bottom of the deque
        /// not concurrent with itself or TryPop, only concurrent with TrySteal
        /// </summary>
        public void Push(T item)
        {
            int b = VolatileBottom;
            int t = VolatileTop;
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
        public StealingResult TryPop(out T item)
        {
            int b = VolatileBottom;
            CircularArray<T> a = _array;
            b--;
            VolatileBottom = b;
            int t = VolatileTop;
            int size = b - t;
            if (size < 0)
            {
                VolatileBottom = t;
                item = DefaultValue;
                return StealingResult.Empty;
            }
            item = a[b];
            if (size > 0)
            {
                return StealingResult.Success;
            }
            if (!CASTop(t, t + 1))
            {
                item = DefaultValue;
                return StealingResult.Abort;
            }
            VolatileBottom = t + 1;
            return StealingResult.Success;
        }

        /// <summary>
        /// Pop an item from the top of the deque
        /// Can be concurrent with itself, push, and TryPop
        /// </summary>
        public StealingResult TrySteal(out T item)
        {
            int t = VolatileTop;
            int b = VolatileBottom;
            CircularArray<T> a = _array;
            int size = b - t;
            if (size <= 0)
            {
                item = DefaultValue;
                return StealingResult.Empty;
            }
            item = a[t];
            if (!CASTop(t, t + 1))
            {
                item = DefaultValue;
                return StealingResult.Abort;
            }
            return StealingResult.Success;
        }
    }
}