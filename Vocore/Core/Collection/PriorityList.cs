using System;
using System.Collections;
using System.Collections.Generic;

namespace Vocore
{
    public class PriorityList<T> : IReadOnlyList<T>
    {
        protected List<T> innerList = new List<T>();

        protected IComparer<T> comparer;

        public int Count
        {
            get
            {
                return this.innerList.Count;
            }
        }

        public T this[int index]
        {
            get
            {
                return this.innerList[index];
            }
        }

        public PriorityList()
        {
            this.comparer = Comparer<T>.Default;
        }

        public PriorityList(IComparer<T> comparer)
        {
            this.comparer = comparer;
        }

        public void Add(T item)
        {
            int count = this.innerList.Count;
            this.innerList.Add(item);
            while (count != 0)
            {
                int mid = (count - 1) / 2;
                if (this.CompareElements(count, mid) >= 0)
                {
                    break;
                }
                this.SwapElements(count, mid);
                count = mid;
            }
        }

		public void Remove(T item){
			//binary search and remove
			int index = -1;
			int left = 0;
			int right = this.innerList.Count - 1;
			while (left <= right)
			{
				int mid = (left + right) / 2;
				if (this.comparer.Compare(this.innerList[mid], item) == 0)
				{
					index = mid;
					break;
				}
				else if (this.comparer.Compare(this.innerList[mid], item) > 0)
				{
					right = mid - 1;
				}
				else
				{
					left = mid + 1;
				}
			}
			
			if (index == -1)
			{
				return;
			}

			this.innerList.RemoveAt(index);
		}

        public bool Contains(T item)
        {
            return this.innerList.Contains(item);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return this.innerList.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.innerList.GetEnumerator();
        }

        public void Clear()
        {
            this.innerList.Clear();
        }

        protected void SwapElements(int i, int j)
        {
            T value = this.innerList[i];
            this.innerList[i] = this.innerList[j];
            this.innerList[j] = value;
        }

        protected int CompareElements(int i, int j)
        {
            return this.comparer.Compare(this.innerList[i], this.innerList[j]);
        }
    }
}


