using System;
using System.Collections;
using System.Collections.Generic;

namespace Vocore
{
    public class PriorityList<T> : IReadOnlyList<T>
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

        public T this[int index]
        {
            get
            {
                return _innerList[index];
            }
        }

        public PriorityList()
        {
            _comparer = Comparer<T>.Default;
        }

        public PriorityList(IComparer<T> comparer)
        {
            _comparer = comparer;
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

		public void RemoveOnce(T item){
			//binary search and remove
			int index = UtilsAlgorithm.BinarySearch(_innerList, item, _comparer);
			if (index == -1)
			{
				return;
			}

			_innerList.RemoveAt(index);
		}

        public void Remove(T item){
            _innerList.Remove(item);
        }

        public bool Contains(T item)
        {
            return _innerList.Contains(item);
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
    }
}


