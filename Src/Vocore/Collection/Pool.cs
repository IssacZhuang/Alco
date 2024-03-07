using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Vocore
{
    /// <summary>
    /// An object pool implmented by array. Not thread safe. Use <see cref="ConcurrentPool{T}"/> if you need thread safe.
    /// </summary>
    public class Pool<T> where T : class
    {
        private readonly T?[] _stack;
        private int _index = -1;
        private readonly Func<T>? _create;
        public int Count => _index + 1;

        public Pool(int size)
        {
            _stack = new T[size];
        }

        public Pool(int size, Func<T> create)
        {
            _stack = new T[size];
            _create = create;
        }

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


