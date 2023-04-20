using System;
using System.Collections.Generic;

namespace Vocore
{
    public class UtilsAlgorithm
    {
        public static Comparison<T> DefaultComparer<T>()
        {
            return (a, b) =>
            {
                return Comparer<T>.Default.Compare(a, b);
            };
        }

        public static Comparison<T> HashComparer<T>()
        {
            return (a, b) =>
            {
                return a.GetHashCode().CompareTo(b.GetHashCode());
            };
        }

        /// <summary>
        /// Binary search.
        /// </summary>
        public static int BinarySearch<T>(IReadOnlyList<T> list, T item, Comparison<T> comparer = null)
        {
            if (comparer == null)
            {
                comparer = DefaultComparer<T>();
            }

            int left = 0;
            int right = list.Count - 1;
            while (left <= right)
            {
                int mid = (left + right) / 2;
                if (comparer(list[mid], item) == 0)
                {
                    return mid;
                }
                else if (comparer(list[mid], item) > 0)
                {
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }
            return -1;
        }

        /// <summary>
        /// Binary search.
        /// </summary>
        public static int BinarySearch<T>(IReadOnlyList<T> list, float t) where T : ISortable
        {
            int left = 0;
            int right = list.Count - 1;
            while (left <= right)
            {
                int mid = (left + right) / 2;
                if (list[mid].SortKey == t)
                {
                    return mid;
                }
                else if (list[mid].SortKey > t)
                {
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }
            return -1;
        }

        /// <summary>
        /// Binary search, return the index of the first element that is lower than or equal to the item.
        /// </summary>
        public static int BinarySearchFloor<T>(IReadOnlyList<T> list, T item, Comparison<T> comparer = null)
        {
            if (comparer == null)
            {
                comparer = DefaultComparer<T>();
            }

            int left = 0;
            int right = list.Count - 1;
            int index = -1;
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (comparer(list[mid], item) == 0)
                {
                    index = mid;
                    break;
                }
                else if (comparer(list[mid], item) > 0)
                {
                    right = mid - 1;
                }
                else
                {
                    index = mid;
                    left = mid + 1;
                }
            }

            return index;
        }

        /// <summary>
        /// Binary search, return the index of the first element that is lower than or equal to the item.
        /// </summary>
        public static int BinarySearchFloor<T>(IReadOnlyList<T> list, float t) where T : ISortable
        {
            int left = 0;
            int right = list.Count - 1;
            int index = -1;
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (list[mid].SortKey == t)
                {
                    index = mid;
                    break;
                }
                else if (list[mid].SortKey > t)
                {
                    right = mid - 1;
                }
                else
                {
                    index = mid;
                    left = mid + 1;
                }
            }

            return index;
        }

        /// <summary>
        /// Binary search, return the index of the first element that is greater than or equal to the item.
        /// </summary>
        public static int BinarySearchCeil<T>(IReadOnlyList<T> list, T item, Comparison<T> comparer = null)
        {
            if (comparer == null)
            {
                comparer = DefaultComparer<T>();
            }

            int left = 0;
            int right = list.Count - 1;
            int index = -1;
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (comparer(list[mid], item) == 0)
                {
                    index = mid;
                    break;
                }
                else if (comparer(list[mid], item) > 0)
                {
                    index = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }
            return index;
        }

        /// <summary>
        /// Binary search, return the index of the first element that is greater than or equal to the item.
        /// </summary>
        public static int BinarySearchCeil<T>(IReadOnlyList<T> list, float t) where T : ISortable
        {
            int left = 0;
            int right = list.Count - 1;
            int index = -1;
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (list[mid].SortKey == t)
                {
                    index = mid;
                    break;
                }
                else if (list[mid].SortKey > t)
                {
                    index = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }
            return index;
        }
    }
}

