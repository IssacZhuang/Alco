using System;
using System.Collections;
using System.Collections.Generic;

namespace Vocore
{
    public class PriorityList<T> : IReadOnlyList<T>, ICollection<T>
    {
        private List<T> _innerList = new List<T>();

        private IComparer<T> _comparer;

        public int Count
        {
            get
            {
                return _innerList.Count;
            }
        }

        public bool IsReadOnly => false;

        public T this[int index]
        {
            get
            {
                return _innerList[index];
            }
        }

        public PriorityList(IComparer<T> comparer = null)
        {
            if (comparer == null)
            {
                _comparer = Comparer<T>.Default;
            }
            else
            {
                _comparer = comparer;
            }
        }

        public PriorityList(IList<T> source, IComparer<T> comparer = null)
        {
            _innerList = new List<T>(source);

            if (comparer == null)
            {
                _comparer = Comparer<T>.Default;
            }
            else
            {
                _comparer = comparer;
            }

            _innerList.Sort(_comparer);
        }

        public void Add(T item)
        {
            //binary search and insert behind
            int index = UtilsAlgorithm.BinarySearchCeil(_innerList, item, _comparer);
            if (index == -1)
            {
                _innerList.Add(item);
            }
            else
            {
                _innerList.Insert(index, item);
            }

        }

        public bool RemoveOnce(T item)
        {
            //binary search and remove
            int index = UtilsAlgorithm.BinarySearch(_innerList, item, _comparer);
            if (index == -1)
            {
                return false;
            }

            _innerList.RemoveAt(index);
            return true;
        }

        public bool Remove(T item)
        {
            return _innerList.Remove(item);
        }

        public bool Contains(T item)
        {
            int index = UtilsAlgorithm.BinarySearch(_innerList, item, _comparer);
            return index != -1;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _innerList.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _innerList.GetEnumerator();
        }

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
            return _comparer.Compare(_innerList[i], _innerList[j]);
        }

        public static int DefaultComparer(T x, T y)
        {
            int hashX = x.GetHashCode();
            int hashY = y.GetHashCode();

            if (hashX > hashY)
            {
                return 1;
            }
            else if (hashX < hashY)
            {
                return -1;
            }
            
            return 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _innerList.CopyTo(array, arrayIndex);
        }
    }
}


