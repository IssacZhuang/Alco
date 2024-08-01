using System;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;

namespace Vocore
{
    public class ConcurrentPool<T> where T : class
    {
        private readonly int _maxCount;
        private int _count;
        private readonly ConcurrentBag<T> _bag = new();
        private readonly Func<T>? _create;

        public int Count
        {
            get { return Volatile.Read(ref _count); }
        }

        public ConcurrentPool(int size)
        {
            _maxCount = size;
        }

        public ConcurrentPool(int size, Func<T> create)
        {
            _maxCount = size;
            _create = create;
        }

        public bool TryGet([NotNullWhen(true)] out T? result)
        {
            if (_bag.TryTake(out result))
            {
                Interlocked.Decrement(ref _count);
                return true;
            }


            if (TryCreate(out result))
            {
                return true;
            }
            

            return false;
        }

        public bool TryReturn(T item)
        {
            if (Interlocked.Increment(ref _count) <= _maxCount)
            {
                _bag.Add(item);
                return true;
            }

            Interlocked.Decrement(ref _count);
            return false;
        }

        private bool TryCreate([NotNullWhen(true)] out T? result)
        {
            if (_create != null)
            {
                result = _create();
                return true;
            }
            result = null;
            return false;
        }

        public void Clear()
        {
            _bag.Clear();
        }
    }
}

