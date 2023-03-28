namespace Vocore
{
    public class ArrayPool<T> where T : class
    {
        private readonly T[] _stack;
        private int _index = -1;
        public int Count => _index + 1;
        public ArrayPool(int size)
        {
            _stack = new T[size];
        }

        public T Get()
        {
            if (_index < 0)
            {
                return null;
            }
            T result = _stack[_index];
            _stack[_index] = null;
            _index--;
            return result;
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


