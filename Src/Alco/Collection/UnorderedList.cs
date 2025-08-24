using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// The UnorderedList, same usage as List<T> but it might be unordered when removing elements.
/// <br/> It is faster than List<T> when removing elements.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public class UnorderedList<T> : IList<T>, IReadOnlyList<T>
{
    private static readonly bool IsReferenceType = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
    private T[] _items;
    private int _size;
    private int _version;

    public UnorderedList()
    {
        _items = Array.Empty<T>();
        _size = 0;
    }

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_size)
            {
                throw new IndexOutOfRangeException(nameof(index));
            }
            return _items[index];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set { _items[index] = value; _version++; }
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _size;
    }

    public int Capacity
    {
        get
        {
            return _items.Length;
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, _size);
            if (value == _items.Length)
            {
                return;
            }
            if (value > 0)
            {
                T[] array = new T[value];
                if (_size > 0)
                {
                    Array.Copy(_items, array, _size);
                }
                _items = array;
            }
            else
            {
                _items = Array.Empty<T>();
            }
            _version++;
        }
    }

    public bool IsReadOnly => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        T[] items = _items;
        int size = _size;
        if ((uint)size < (uint)items.Length)
        {
            _size = size + 1;
            items[size] = item;
        }
        else
        {
            AddWithResize(item);
            return;
        }
        _version++;
    }

    public void Clear()
    {
        if (IsReferenceType)
        {
            int size = _size;
            _size = 0;
            if (size > 0)
            {
                Array.Clear(_items, 0, size);
            }
        }
        else
        {
            _size = 0;
        }
        _version++;
    }

    public bool Contains(T item)
    {
        if (_size != 0)
        {
            return IndexOf(item) != -1;
        }
        return false;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        Array.Copy(_items, 0, array, arrayIndex, _size);
    }

    public Enumerator GetEnumerator() => new Enumerator(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    public int IndexOf(T item)
    {
        return Array.IndexOf(_items, item, 0, _size);
    }

    public void Insert(int index, T item)
    {
        if (index < 0 || index > _size)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (_size == _items.Length)
        {
            EnsureCapacity(_size + 1);
        }
        if (index < _size)
        {
            Array.Copy(_items, index, _items, index + 1, _size - index);
        }
        _items[index] = item;
        _size++;
        _version++;
    }

    public bool Remove(T item)
    {
        int num = IndexOf(item);
        if (num >= 0)
        {
            RemoveAt(num);
            return true;
        }
        return false;
    }

    public void RemoveAt(int index)
    {
        if (index >= _size)
        {
            throw new IndexOutOfRangeException(nameof(index));
        }
        //move the last element to the index and decrease the size
        T last = _items[_size - 1];
        _items[index] = last;
        _size--;
        _items[_size] = default!;
        _version++;
    }

    public T RemoveLast()
    {
        if (_size <= 0)
        {
            throw new InvalidOperationException("The list is empty");
        }
        _size--;
        T item = _items[_size];
        if (IsReferenceType)
        {
            _items[_size] = default!;
        }
        _version++;
        return item;
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    public Span<T> AsSpan()
    {
        return _items.AsSpan(0, _size);
    }

    public Span<T> AsSpan(int start, int count)
    {
        return _items.AsSpan(start, count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddWithResize(T item)
    {
        int size = _size;
        EnsureCapacity(size + 1);
        _size = size + 1;
        _items[size] = item;
        _version++;
    }

    private void EnsureCapacity(int capacity)
    {
        int num = (_items.Length == 0) ? 4 : (2 * _items.Length);
        if ((uint)num > Array.MaxLength)
        {
            num = Array.MaxLength;
        }
        if (num < capacity)
        {
            num = capacity;
        }
        Capacity = num;
    }

    public struct Enumerator : IEnumerator<T>, IEnumerator
    {
        private readonly UnorderedList<T> _list;
        private int _index;
        private readonly int _version;
        private T? _current;

        internal Enumerator(UnorderedList<T> list)
        {
            _list = list;
            _index = 0;
            _version = list._version;
            _current = default;
        }

        public T Current => _current!;

        object? IEnumerator.Current
        {
            get
            {
                if (_index == 0 || _index == _list._size + 1)
                {
                    throw new InvalidOperationException("Enumeration has not started or has already finished.");
                }
                return Current;
            }
        }

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            UnorderedList<T> localList = _list;
            if (_version == localList._version && ((uint)_index < (uint)localList._size))
            {
                _current = localList._items[_index];
                _index++;
                return true;
            }
            return MoveNextRare();
        }

        private bool MoveNextRare()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            }
            _index = _list._size + 1;
            _current = default;
            return false;
        }

        void IEnumerator.Reset()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            }
            _index = 0;
            _current = default;
        }
    }
}