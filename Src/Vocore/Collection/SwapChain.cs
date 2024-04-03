using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Vocore;

public class SwapChain<T>
{
    private readonly List<T> _items = new List<T>();
    private int _index;

    public SwapChain()
    {

    }

    public int Count => _items.Count;

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _items[index];
    }

    public T Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_items.Count == 0)
            {
                throw new System.InvalidOperationException("No items in the swap chain");
            }

            return _items[_index];
        }
    }

    public T Previous
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_items.Count == 0)
            {
                throw new System.InvalidOperationException("No items in the swap chain");
            }

            return _items[(_index - 1 + _items.Count) % _items.Count];
        }
    }

    public T Next
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_items.Count == 0)
            {
                throw new System.InvalidOperationException("No items in the swap chain");
            }

            return _items[(_index + 1) % _items.Count];
        }
    }

    public void Add(T item)
    {
        _items.Add(item);
    }

    public void Clear()
    {
        _items.Clear();
        _index = 0;
    }

    public void Swap()
    {
        _index = (_index + 1) % _items.Count;
    }
}