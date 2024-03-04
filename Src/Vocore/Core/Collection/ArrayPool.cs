using System;
using System.Threading.Tasks;

namespace Vocore
{
    public class ArrayPool<T> where T : class
    {
        private readonly T[] _stack;
        private int _index = -1;
        private readonly Func<T>? _create;
        public int Count => _index + 1;

        public ArrayPool(int size)
        {
            _stack = new T[size];
        }

        public ArrayPool(int size, Func<T> create)
        {
            _stack = new T[size];
            _create = create;
        }

        public bool TryGet(out T result)
        {
            if (_index < 0)
            {
                if (_create != null)
                {
                    result = _create();
                    return true;
                }
                result = null;
                return false;
            }
            result = _stack[_index];
            _stack[_index] = null;
            _index--;
            return true;
        }

        public void Return(T item)
        {
            if (_index >= _stack.Length - 1)
            {
                return;
            }
            _index++;
            _stack[_index] = item;
        }

        private void Clear()
        {
            for (int i = 0; i < _stack.Length; i++)
            {
                _stack[i] = null;
            }
            _index = -1;
        }
    }
}


