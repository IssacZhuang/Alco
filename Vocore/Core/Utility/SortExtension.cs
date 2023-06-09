using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using Vocore.Unsafe;

namespace Vocore
{
    public static class SortExtension
    {
        // public static void Sort<T>(this NativeList<T> list) where T : unmanaged, IComparable<T>
        // {
        //     list.Sort(default(DefaultComparer<T>));
        // }

        // public unsafe static void Sort<T, U>(this NativeList<T> list, U comp) where T : unmanaged where U : IComparer<T>
        // {
        //     IntroSort<T, U>((T*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks<T>(list), list.Length, comp);
        // }


        [BurstCompatible(GenericTypeArguments = new[] {
            typeof(int) ,
            typeof(DefaultComparer<int>),
            typeof(float) ,
            typeof(DefaultComparer<float>),
            })]
        public unsafe static JobHandle SortJob<T>(this NativeArrayList<T> list) where T : unmanaged, IComparable<T>
        {
            return list.SortJob(default(DefaultComparer<T>));
        }

        [BurstCompatible(GenericTypeArguments = new[] {
            typeof(int) ,
            typeof(DefaultComparer<int>),
            typeof(float) ,
            typeof(DefaultComparer<float>),
            })]
        public unsafe static JobHandle SortJob<T, U>(this NativeArrayList<T> list, U comp) where T : unmanaged where U : IComparer<T>
        {
            JobSortNativeArrayList<T, U> job = new JobSortNativeArrayList<T, U>
            {
                list = list,
                comp = comp
            };
            return job.Schedule();
        }

        [BurstCompatible(GenericTypeArguments = new[] {
            typeof(int) ,
            typeof(DefaultComparer<int>),
            typeof(float) ,
            typeof(DefaultComparer<float>),
            })]
        public unsafe static void Sort<T , U>(this NativeArrayList<T> list, U comp) where T : unmanaged where U : IComparer<T>
        {

            IntroSort<T, U>(list.Ptr, list.Length, comp);
        }

        [BurstCompatible(GenericTypeArguments = new[] {
            typeof(int) ,
            typeof(DefaultComparer<int>),
            typeof(float) ,
            typeof(DefaultComparer<float>),
            })]
        public unsafe static void Sort<T>(this NativeArrayList<T> list) where T : unmanaged, IComparable<T>
        {

            IntroSort<T, DefaultComparer<T>>(list.Ptr, list.Length, default(DefaultComparer<T>));
        }

        [BurstCompatible(GenericTypeArguments = new[] {
            typeof(int) ,
            typeof(DefaultComparer<int>),
            typeof(float) ,
            typeof(DefaultComparer<float>),
            })]
        public unsafe static void Sort<T>(this T[] array) where T : unmanaged, IComparable<T>
        {
            fixed (T* ptr = array)
            {
                IntroSort<T, DefaultComparer<T>>(ptr, array.Length, default(DefaultComparer<T>));
            }
        }

        [BurstCompatible(GenericTypeArguments = new[] {
            typeof(int) ,
            typeof(DefaultComparer<int>),
            typeof(float) ,
            typeof(DefaultComparer<float>),
            })]
        public unsafe static void Sort<T>(T* array, int length) where T : unmanaged, IComparable<T>
        {
            IntroSort<T, DefaultComparer<T>>(array, length, default(DefaultComparer<T>));
        }

        [BurstCompatible(GenericTypeArguments = new[] {
            typeof(int) ,
            typeof(DefaultComparer<int>),
            typeof(float) ,
            typeof(DefaultComparer<float>),
            })]
        private struct DefaultComparer<T> : IComparer<T> where T : IComparable<T>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare(T x, T y)
            {
                return x.CompareTo(y);
            }
        }

        [BurstCompatible(GenericTypeArguments = new[] {
            typeof(int) ,
            typeof(DefaultComparer<int>),
            typeof(float) ,
            typeof(DefaultComparer<float>),
            })]
        private unsafe static void IntroSort<T, U>(void* array, int length, U comp) where T : unmanaged where U : IComparer<T>
        {
            IntroSort<T, U>(array, 0, length - 1, 2 * Log2Floor(length), comp);
        }

        private unsafe static void IntroSort<T, U>(void* array, int lo, int hi, int depth, U comp) where T : unmanaged where U : IComparer<T>
        {
            while (hi > lo)
            {
                int partitionSize = hi - lo + 1;
                if (partitionSize <= 16)
                {
                    switch (partitionSize)
                    {
                        case 1:
                            break;
                        case 2:
                            SwapIfGreaterWithItems<T, U>(array, lo, hi, comp);
                            break;
                        case 3:
                            SwapIfGreaterWithItems<T, U>(array, lo, hi - 1, comp);
                            SwapIfGreaterWithItems<T, U>(array, lo, hi, comp);
                            SwapIfGreaterWithItems<T, U>(array, hi - 1, hi, comp);
                            break;
                        default:
                            InsertionSort<T, U>(array, lo, hi, comp);
                            break;
                    }
                    break;
                }
                if (depth == 0)
                {
                    HeapSort<T, U>(array, lo, hi, comp);
                    break;
                }
                depth--;
                int p = Partition<T, U>(array, lo, hi, comp);
                IntroSort<T, U>(array, p + 1, hi, depth, comp);
                hi = p - 1;
            }
        }


        private unsafe static int Partition<T, U>(void* array, int lo, int hi, U comp) where T : unmanaged where U : IComparer<T>
        {
            int mid = lo + (hi - lo) / 2;
            SwapIfGreaterWithItems<T, U>(array, lo, mid, comp);
            SwapIfGreaterWithItems<T, U>(array, lo, hi, comp);
            SwapIfGreaterWithItems<T, U>(array, mid, hi, comp);
            T pivot = UtilsMemory.ReadArrayElement<T>(array, mid);
            Swap<T>(array, mid, hi - 1);
            int left = lo;
            int right = hi - 1;
            while (left < right)
            {
                while (comp.Compare(pivot, UtilsMemory.ReadArrayElement<T>(array, ++left)) > 0)
                {
                }
                while (comp.Compare(pivot, UtilsMemory.ReadArrayElement<T>(array, --right)) < 0)
                {
                }
                if (left >= right)
                {
                    break;
                }
                Swap<T>(array, left, right);
            }
            Swap<T>(array, left, hi - 1);
            return left;
        }


        private unsafe static void HeapSort<T, U>(void* array, int lo, int hi, U comp) where T : unmanaged where U : IComparer<T>
        {
            int k = hi - lo + 1;
            for (int j = k / 2; j >= 1; j--)
            {
                Heapify<T, U>(array, j, k, lo, comp);
            }
            for (int i = k; i > 1; i--)
            {
                Swap<T>(array, lo, lo + i - 1);
                Heapify<T, U>(array, 1, i - 1, lo, comp);
            }
        }

        private unsafe static void InsertionSort<T, U>(void* array, int lo, int hi, U comp) where T : unmanaged where U : IComparer<T>
        {
            for (int i = lo; i < hi; i++)
            {
                int j = i;
                T t = UtilsMemory.ReadArrayElement<T>(array, i + 1);
                while (j >= lo && comp.Compare(t, UtilsMemory.ReadArrayElement<T>(array, j)) < 0)
                {
                    UtilsMemory.WriteArrayElement<T>(array, j + 1, UtilsMemory.ReadArrayElement<T>(array, j));
                    j--;
                }
                UtilsMemory.WriteArrayElement<T>(array, j + 1, t);
            }
        }

        private unsafe static void SwapIfGreaterWithItems<T, U>(void* array, int lhs, int rhs, U comp) where T : unmanaged where U : IComparer<T>
        {
            if (lhs != rhs && comp.Compare(UtilsMemory.ReadArrayElement<T>(array, lhs), UtilsMemory.ReadArrayElement<T>(array, rhs)) > 0)
            {
                Swap<T>(array, lhs, rhs);
            }
        }

        private unsafe static void Heapify<T, U>(void* array, int i, int n, int lo, U comp) where T : unmanaged where U : IComparer<T>
        {
            T val = UtilsMemory.ReadArrayElement<T>(array, lo + i - 1);
            while (i <= n / 2)
            {
                int child = 2 * i;
                if (child < n && comp.Compare(UtilsMemory.ReadArrayElement<T>(array, lo + child - 1), UtilsMemory.ReadArrayElement<T>(array, lo + child)) < 0)
                {
                    child++;
                }
                if (comp.Compare(UtilsMemory.ReadArrayElement<T>(array, lo + child - 1), val) < 0)
                {
                    break;
                }
                UtilsMemory.WriteArrayElement<T>(array, lo + i - 1, UtilsMemory.ReadArrayElement<T>(array, lo + child - 1));
                i = child;
            }
            UtilsMemory.WriteArrayElement<T>(array, lo + i - 1, val);
        }

        private unsafe static void Swap<T>(void* array, int lhs, int rhs) where T : unmanaged
        {
            T val = UtilsMemory.ReadArrayElement<T>(array, lhs);
            UtilsMemory.WriteArrayElement<T>(array, lhs, UtilsMemory.ReadArrayElement<T>(array, rhs));
            UtilsMemory.WriteArrayElement<T>(array, rhs, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Log2Floor(int value)
        {
            return 31 - math.lzcnt((uint)value);
        }

        [BurstCompile]
        internal struct JobSortNativeArrayList<T, U> : IJob where T : unmanaged where U : IComparer<T>
        {
            public NativeArrayList<T> list;
            public U comp;
            public void Execute()
            {
                list.Sort(comp);
            }
        }
    }
}