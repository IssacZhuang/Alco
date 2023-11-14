using System;
using System.Collections.Generic;

namespace Vocore
{
    public class Pool<T> where T : new()
    {
        private readonly Stack<T> _stack = new Stack<T>();

        private readonly Action<T> _actionOnGet;

        private readonly Action<T> _actionOnRecycle;

        private readonly bool _collectionCheck = true;

        public int CountAll { get; private set; }

        public int CountActive => CountAll - CountInactive;

        public int CountInactive => _stack.Count;

        public Pool(Action<T> actionOnGet, Action<T> actionOnRelease, bool collectionCheck = true)
        {
            _actionOnGet = actionOnGet;
            _actionOnRecycle = actionOnRelease;
            _collectionCheck = collectionCheck;
        }

        public T Get()
        {
            T element;
            if (_stack.Count == 0)
            {
                element = new T();
                CountAll++;
            }
            else
            {
                element = _stack.Pop();
            }
            if (_actionOnGet != null)
            {
                _actionOnGet(element);
            }
            return element;
        }

        public void Recycle(T element)
        {
            if (_collectionCheck && _stack.Count > 0 && _stack.Contains(element))
            {
                Log.Error("Internal error. Trying to destroy object that is already released to pool.");
            }
            if (_actionOnRecycle != null)
            {
                _actionOnRecycle(element);
            }
            _stack.Push(element);
        }
    }

}
