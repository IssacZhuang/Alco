using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Vocore;

/// <summary>
/// A swap chain is a collection of items that can be swapped between.
/// </summary>
/// <typeparam name="T">The type of items in the swap chain.</typeparam>
public class SwapChain<T>
{
    private readonly List<T> _items = new List<T>();
    private int _index;

    public SwapChain()
    {

    }

    public int Count => _items.Count;

    /// <summary>
    /// Gets the item at the specified index.
    /// </summary>
    /// <param name="index">The index of the item to get.</param>
    /// <returns>The item at the specified index.</returns>
    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _items[index];
    }

    /// <summary>
    /// Gets the current item in the swap chain.
    /// </summary>
    /// <returns>The current item in the swap chain.</returns>
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

    /// <summary>
    /// Adds an item to the swap chain.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        _items.Add(item);
    }

    /// <summary>
    /// Clears the swap chain.
    /// </summary>
    public void Clear()
    {
        _items.Clear();
        _index = 0;
    }

    /// <summary>
    /// Swaps the current item in the swap chain.
    /// </summary>
    /// <returns>The next item in the swap chain.</returns>
    public T Swap()
    {
        _index = (_index + 1) % _items.Count;
        return _items[_index];
    }
}