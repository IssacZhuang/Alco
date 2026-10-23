using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Alco
{
    /// <summary>
    /// An object pool implmented by array. Not thread safe. Use <see cref="ConcurrentPool{T}"/> if you need thread safe.
    /// </summary>
    public class Pool<T> where T : class
    {
        private readonly T?[] _stack;
        private int _index = -1;
        private readonly Func<T>? _create;
        /// <summary>
        /// Gets the number of items currently available in the pool.
        /// </summary>
        public int Count => _index + 1;

        /// <summary>
        /// Initializes an empty pool with the given capacity and no item factory.
        /// </summary>
        /// <param name="size">The maximum number of items the pool can hold.</param>
        public Pool(int size)
        {
            _stack = new T[size];
        }

        /// <summary>
        /// Initializes an empty pool with the given capacity and item factory.
        /// </summary>
        /// <param name="size">The maximum number of items the pool can hold.</param>
        /// <param name="create">The factory used to create items when the pool is empty.</param>
        public Pool(int size, Func<T> create)
        {
            _stack = new T[size];
            _create = create;
        }

        /// <summary>
        /// Tries to take an item from the pool, creating one when the pool is empty and a factory is available.
        /// </summary>
        /// <param name="result">The taken item, or null when none could be obtained.</param>
        /// <returns>True when an item was obtained.</returns>
        public bool TryGet([NotNullWhen(true)] out T? result)
        {
            if (_index < 0)
            {
                return TryCreate(out result);
            }
            result = _stack[_index];

            if (result == null)
            {
                return TryCreate(out result);
            }

            _stack[_index] = null;
            _index--;
            return true;
        }

        /// <summary>
        /// Tries to return an item to the pool.
        /// </summary>
        /// <param name="item">The item to return.</param>
        /// <returns>True when the item was stored; false when the pool is full.</returns>
        public bool TryReturn(T item)
        {
            if (_index >= _stack.Length - 1)
            {
                return false;
            }
            _index++;
            _stack[_index] = item;
            return true;
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

        /// <summary>
        /// Pre-populates the pool with the specified number of items.
        /// </summary>
        /// <param name="count">The number of items to create and add to the pool.</param>
        public void Warmup(int count)
        {
            if (_create == null)
            {
                throw new InvalidOperationException("Cannot warmup pool without a create function.");
            }

            int itemsToCreate = Math.Min(count, _stack.Length - Count);
            for (int i = 0; i < itemsToCreate; i++)
            {
                T item = _create();
                TryReturn(item);
            }
        }

        /// <summary>
        /// Removes all items from the pool.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _stack.Length; i++)
            {
                _stack[i] = null;
            }
            _index = -1;
        }
    }
}


