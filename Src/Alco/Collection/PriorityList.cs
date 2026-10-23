using System;
using System.Collections;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// A list that keeps its elements sorted by a comparison delegate.
    /// </summary>
    public class PriorityList<T> : IReadOnlyList<T>, ICollection<T>
    {
        private readonly List<T> _innerList = new List<T>();

        private readonly Comparison<T> _comparer;

        public int Count
        {
            get
            {
                return _innerList.Count;
            }
        }

        /// <summary>
        /// Gets whether the list is read-only; always false.
        /// </summary>
        public bool IsReadOnly => false;

        public T this[int index]
        {
            get
            {
                return _innerList[index];
            }
        }

        /// <summary>
        /// Initializes an empty list that sorts with the specified comparison.
        /// </summary>
        /// <param name="comparer">The comparison used for ordering, or null to use the default comparer.</param>
        public PriorityList(Comparison<T>? comparer = null)
        {
            if (comparer == null)
            {
                _comparer = AlgoBinarySearch.DefaultComparer<T>();
            }
            else
            {
                _comparer = comparer;
            }
        }

        /// <summary>
        /// Initializes a list containing the sorted elements of the source sequence.
        /// </summary>
        /// <param name="source">The elements to add.</param>
        /// <param name="comparer">The comparison used for ordering, or null to use the default comparer.</param>
        public PriorityList(IReadOnlyList<T> source, Comparison<T>? comparer = null)
        {
            _innerList = new List<T>(source);

            if (comparer == null)
            {
                _comparer = AlgoBinarySearch.DefaultComparer<T>();
            }
            else
            {
                _comparer = comparer;
            }

            _innerList.Sort(_comparer);
        }

        /// <summary>
        /// Adds an item while preserving sort order.
        /// </summary>
        /// <param name="item">The item to add.</param>
        public void Add(T item)
        {
            // Binary search, then insert behind all existing items with the same priority.
            int index = AlgoBinarySearch.BinarySearchCeil(_innerList, item, _comparer);
            if (index == -1)
            {
                _innerList.Add(item);
            }
            else
            {
                while (index < _innerList.Count && _comparer(_innerList[index], item) == 0)
                {
                    index++;
                }
                _innerList.Insert(index, item);
            }
        }

        /// <summary>
        /// Removes the first occurrence of the specified item.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if an item was removed; otherwise, false.</returns>
        public bool Remove(T item)
        {
            return _innerList.Remove(item);
        }

        /// <summary>
        /// Determines whether the list contains the specified item.
        /// </summary>
        /// <param name="item">The item to search for.</param>
        /// <returns>True if the item is present.</returns>
        public bool Contains(T item)
        {
            int index = AlgoBinarySearch.BinarySearch(_innerList, item, _comparer);
            return index != -1;
        }

        /// <summary>
        /// Returns an enumerator over the sorted elements.
        /// </summary>
        /// <returns>An enumerator over the list.</returns>
        public List<T>.Enumerator GetEnumerator()
        {
            return _innerList.GetEnumerator();
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return _innerList.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _innerList.GetEnumerator();
        }

        /// <summary>
        /// Removes all items from the list.
        /// </summary>
        public void Clear()
        {
            _innerList.Clear();
        }

        protected void SwapElements(int i, int j)
        {
            T value = _innerList[i];
            _innerList[i] = _innerList[j];
            _innerList[j] = value;
        }

        protected int CompareElements(int i, int j)
        {
            return _comparer(_innerList[i], _innerList[j]);
        }

        /// <summary>
        /// Copies the elements to an array starting at the specified index.
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The index in the destination array at which copying starts.</param>
        public void CopyTo(T[] array, int arrayIndex)
        {
            _innerList.CopyTo(array, arrayIndex);
        }
    }
}


