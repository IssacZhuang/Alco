
using System;
using System.Collections;
using System.Collections.Generic;

namespace Alco;

public sealed class Deque<T> : ICollection<T>
{
    private T[] _array = Array.Empty<T>();
    private int _head; // First valid element in the queue
    private int _tail; // First open slot in the dequeue, unless the dequeue is full
    private int _size; // Number of elements.
    private int _version; // Mutation counter for enumerator versioning

    public int Count => _size;

    public bool IsEmpty => _size == 0;

    public void EnqueueTail(T item)
    {
        if (_size == _array.Length)
        {
            Grow();
        }

        _array[_tail] = item;
        if (++_tail == _array.Length)
        {
            _tail = 0;
        }
        _size++;
        _version++;
    }

    public void EnqueueHead(T item)
    {
       if (_size == _array.Length)
       {
           Grow();
       }
    
       _head = (_head == 0 ? _array.Length : _head) - 1;
       _array[_head] = item;
       _size++;
        _version++;
    }

    public bool TryDequeueHead(out T item)
    {
        if (_size == 0)
        {
            item = default!;
            return false;
        }
        item = _array[_head];
        _array[_head] = default!;

        if (++_head == _array.Length)
        {
            _head = 0;
        }
        _size--;
        _version++;

        return true;
    }

    public bool TryPeekHead(out T item)
    {
        if (_size == 0)
        {
            item = default!;
            return false;
        }
        item = _array[_head];
        return true;
    }

    public bool TryPeekTail(out T item)
    {
        if (_size == 0)
        {
            item = default!;
            return false;
        }
        var index = _tail - 1;
        if (index == -1)
        {
            index = _array.Length - 1;
        }
        item = _array[index];
        return true;
    }

    public bool TryDequeueTail(out T item)
    {
        if (_size == 0)
        {
            item = default!;
            return false;
        }
        if (--_tail == -1)
        {
            _tail = _array.Length - 1;
        }

        item = _array[_tail];
        _array[_tail] = default!;

        _size--;
        _version++;
        return true;
    }

    public Enumerator GetEnumerator() => new Enumerator(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    void ICollection<T>.Add(T item)
    {
        EnqueueTail(item);
    }

    bool ICollection<T>.Remove(T item)
    {
        if (_size == 0)
        {
            return false;
        }

        int length = _array.Length;
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < _size; i++)
        {
            int idx = _head + i;
            if (idx >= length)
            {
                idx -= length;
            }

            if (comparer.Equals(_array[idx], item))
            {
                // Removing at head
                if (i == 0)
                {
                    TryDequeueHead(out _);
                    return true;
                }
                // Removing at tail
                if (i == _size - 1)
                {
                    TryDequeueTail(out _);
                    return true;
                }

                // Choose the cheaper side to shift
                if (i < _size / 2)
                {
                    // Shift left towards head: move elements [head..idx-1] one step right
                    for (int j = i; j > 0; j--)
                    {
                        int dst = _head + j;
                        if (dst >= length)
                        {
                            dst -= length;
                        }
                        int src = dst - 1;
                        if (src < 0)
                        {
                            src += length;
                        }
                        _array[dst] = _array[src];
                    }
                    _array[_head] = default!;
                    if (++_head == length)
                    {
                        _head = 0;
                    }
                    _size--;
                    _version++;
                }
                else
                {
                    // Shift right towards tail: move elements [idx+1..tail-1] one step left
                    for (int j = i; j < _size - 1; j++)
                    {
                        int dst = _head + j;
                        if (dst >= length)
                        {
                            dst -= length;
                        }
                        int src = dst + 1;
                        if (src >= length)
                        {
                            src -= length;
                        }
                        _array[dst] = _array[src];
                    }
                    // Move tail back one position
                    if (--_tail < 0)
                    {
                        _tail = length - 1;
                    }
                    _array[_tail] = default!;
                    _size--;
                    _version++;
                }
                return true;
            }
        }

        return false;
    }

    bool ICollection<T>.Contains(T item)
    {
        var comparer = EqualityComparer<T>.Default;
        int pos = _head;
        int count = _size;
        int length = _array.Length;
        while (count-- > 0)
        {
            if (comparer.Equals(_array[pos], item))
            {
                return true;
            }
            if (++pos == length)
            {
                pos = 0;
            }
        }
        return false;
    }

    void ICollection<T>.CopyTo(T[] array, int arrayIndex)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }
        if (arrayIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }
        if (array.Length - arrayIndex < _size)
        {
            throw new ArgumentException("Destination array is not long enough.");
        }

        int pos = _head;
        int count = _size;
        int length = _array.Length;
        while (count-- > 0)
        {
            array[arrayIndex++] = _array[pos];
            if (++pos == length)
            {
                pos = 0;
            }
        }
    }

    public void Clear()
    {
        int pos = _head;
        int count = _size;
        int length = _array.Length;
        while (count-- > 0)
        {
            _array[pos] = default!;
            if (++pos == length)
            {
                pos = 0;
            }
        }
        _head = 0;
        _tail = 0;
        if (_size != 0)
        {
            _size = 0;
            _version++;
        }
    }

    bool ICollection<T>.IsReadOnly => false;

    int ICollection<T>.Count => _size;

    void ICollection<T>.Clear()
    {
        Clear();
    }

    private void Grow()
    {
        const int MinimumGrow = 4;

        int capacity = (int)(_array.Length * 2L);
        if (capacity < _array.Length + MinimumGrow)
        {
            capacity = _array.Length + MinimumGrow;
        }

        T[] newArray = new T[capacity];

        if (_head == 0)
        {
            Array.Copy(_array, newArray, _size);
        }
        else
        {
            Array.Copy(_array, _head, newArray, 0, _array.Length - _head);
            Array.Copy(_array, 0, newArray, _array.Length - _head, _tail);
        }

        _array = newArray;
        _head = 0;
        _tail = _size;
    }


    public struct Enumerator : IEnumerator<T>,
            IEnumerator
    {
        private readonly Deque<T> _q;
        private readonly int _version;
        private int _index;   // -1 = not started, -2 = ended/disposed
        private T? _currentElement;

        internal Enumerator(Deque<T> q)
        {
            _q = q;
            _version = q._version;
            _index = -1;
            _currentElement = default;
        }

        public void Dispose()
        {
            _index = -2;
            _currentElement = default;
        }

        public bool MoveNext()
        {
            if (_version != _q._version)
            {
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            }

            if (_index == -2)
                return false;

            _index++;

            if (_index == _q._size)
            {
                // We've run past the last element
                _index = -2;
                _currentElement = default;
                return false;
            }

            // Cache some fields in locals to decrease code size
            T[] array = _q._array;
            uint capacity = (uint)array.Length;

            // _index represents the 0-based index into the queue, however the queue
            // doesn't have to start from 0 and it may not even be stored contiguously in memory.

            uint arrayIndex = (uint)(_q._head + _index); // this is the actual index into the queue's backing array
            if (arrayIndex >= capacity)
            {
                // NOTE: Originally we were using the modulo operator here, however
                // on Intel processors it has a very high instruction latency which
                // was slowing down the loop quite a bit.
                // Replacing it with simple comparison/subtraction operations sped up
                // the average foreach loop by 2x.

                arrayIndex -= capacity; // wrap around if needed
            }

            _currentElement = array[arrayIndex];
            return true;
        }

        public T Current
        {
            get
            {
                if (_index < 0)
                    ThrowEnumerationNotStartedOrEnded();
                return _currentElement!;
            }
        }

        private void ThrowEnumerationNotStartedOrEnded()
        {
            throw new InvalidOperationException(_index == -1 ? "Enumeration has not started." : "Enumeration already finished.");
        }

        object? IEnumerator.Current
        {
            get { return Current; }
        }

        void IEnumerator.Reset()
        {
            if (_version != _q._version)
            {
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            }
            _index = -1;
            _currentElement = default;
        }
    }
}