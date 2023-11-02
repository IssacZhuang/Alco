using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Vocore
{
    internal class IndexWorkStealingDeque
    {
        // inclusive
        private int _top;
        // exclusive
        private int _bottom;

        private int _start;

        public IndexWorkStealingDeque()
        {
            _top = 0;
            _bottom = 0;
        }

        public void Set(int start, int count)
        {
            VolatileTop = 0;
            VolatileBottom = count;
            Volatile.Write(ref _start, start);
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
        /// Pop an item from the bottom of the deque
        /// not concurrent with itself or Push, only concurrent with TrySteal
        /// </summary>
        public StealingResult TryPop(out int item)
        {
            int b = VolatileBottom;
            b--;
            VolatileBottom = b;
            int t = VolatileTop;
            int size = b - t;
            if (size < 0)
            {
                VolatileBottom = t;
                item = -1;
                return StealingResult.Empty;
            }
            item = b + _start;
            if (size > 0)
            {
                return StealingResult.Success;
            }
            if (!CASTop(t, t + 1))
            {
                item = -1;
                VolatileBottom = t + 1;
                // ???? the origin code in the paper is return empty 
                return StealingResult.Abort;
            }
            VolatileBottom = t + 1;
            return StealingResult.Success;
        }

        /// <summary>
        /// Pop an item from the top of the deque
        /// Can be concurrent with itself, push, and TryPop
        /// </summary>
        public StealingResult TrySteal(out int item)
        {
            int t = VolatileTop;
            int b = VolatileBottom;
            int size = b - t;
            if (size <= 0)
            {
                item = -1;
                return StealingResult.Empty;
            }
            item = t + _start;
            if (!CASTop(t, t + 1))
            {
                item = -1;
                return StealingResult.Abort;
            }
            return StealingResult.Success;
        }
    }
}