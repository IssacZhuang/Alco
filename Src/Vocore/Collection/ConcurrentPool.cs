using System;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace Vocore
{
    public class ConcurrentPool<T> where T : class
    {
        private readonly T?[] _stack;
        private int _index = -1;
        private readonly Func<T>? _create;

        public int Count
        {
            get { return Interlocked.CompareExchange(ref _index, 0, 0) + 1; }
        }

        public ConcurrentPool(int size)
        {
            _stack = new T[size];
        }

        public ConcurrentPool(int size, Func<T> create)
        {
            _stack = new T[size];
            _create = create;
        }

        public bool TryGet([NotNullWhen(true)] out T? result)
        {
            int localIndex = Interlocked.Decrement(ref _index) + 1;

            if (localIndex < 0)
            {
                Interlocked.Increment(ref _index);
                return TryCreate(out result);
            }

            result = _stack[localIndex];
            _stack[localIndex] = null;

            if (result == null)
            {
                Interlocked.Increment(ref _index);
                return TryCreate(out result);
            }

            return true;
        }

        public bool TryReturn(T item)
        {
            int localIndex = Interlocked.Increment(ref _index);

            if (localIndex >= _stack.Length)
            {
                Interlocked.Decrement(ref _index);
                return false;
            }

            _stack[localIndex] = item;
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
            Interlocked.Exchange(ref _index, -1);
        }
    }
}

