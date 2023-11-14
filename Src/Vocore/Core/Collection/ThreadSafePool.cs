using System;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Vocore
{
    public class ThreadSafePool<T> where T : class
    {
        private readonly Func<T> _createFunc;
        private readonly int _maxCapacity;
        private int _numItems;

        private protected readonly ConcurrentQueue<T> _items = new ConcurrentQueue<T>();
        private protected T _fastItem;

        public ThreadSafePool()
            : this(Environment.ProcessorCount * 2)
        {
        }

        public ThreadSafePool(int maximumRetained, Func<T> createFunc = null)
        {
            _createFunc = createFunc;
            _maxCapacity = maximumRetained - 1;  // -1 to account for _fastItem
        }
        public T Get()
        {
            var item = _fastItem;
            if (item == null || Interlocked.CompareExchange(ref _fastItem, null, item) != item)
            {
                if (_items.TryDequeue(out item))
                {
                    Interlocked.Decrement(ref _numItems);
                    return item;
                }

                // no object available, so go get a brand new one
                return _createFunc();
            }

            return item;
        }

        public void Return(T obj)
        {
            ReturnCore(obj);
        }

        private protected bool ReturnCore(T obj)
        {
            if (_fastItem != null || Interlocked.CompareExchange(ref _fastItem, obj, null) != null)
            {
                if (Interlocked.Increment(ref _numItems) <= _maxCapacity)
                {
                    _items.Enqueue(obj);
                    return true;
                }

                // no room, clean up the count and drop the object on the floor
                Interlocked.Decrement(ref _numItems);
                return false;
            }

            return true;
        }
    }
}

