using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

using Vocore.Unsafe;

namespace Vocore
{
    public static class SortExtension
    {
        private struct SegmentSort<T> : IJobParallelFor where T : unmanaged, IComparable<T>
        {
            [NativeDisableUnsafePtrRestriction]
            public unsafe T* Data;

            public int Length;

            public int SegmentWidth;

            public unsafe void Execute(int index)
            {
                int startIndex = index * SegmentWidth;
                int segmentLength = ((Length - startIndex < SegmentWidth) ? (Length - startIndex) : SegmentWidth);
                Sort(Data + startIndex, segmentLength);
            }
        }

        private struct SegmentSortMerge<T> : IJob where T : unmanaged, IComparable<T>
        {
            [NativeDisableUnsafePtrRestriction]
            public unsafe T* Data;

            public int Length;

            public int SegmentWidth;

            public unsafe void Execute()
            {
                int segmentCount = (Length + (SegmentWidth - 1)) / SegmentWidth;
                int* segmentIndex = stackalloc int[segmentCount];
                T* resultCopy = (T*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>() * Length, 16, Unity.Collections.Allocator.Temp);
                for (int sortIndex = 0; sortIndex < Length; sortIndex++)
                {
                    int bestSegmentIndex = -1;
                    T bestValue = default(T);
                    for (int i = 0; i < segmentCount; i++)
                    {
                        int startIndex = i * SegmentWidth;
                        int offset = segmentIndex[i];
                        int segmentLength = ((Length - startIndex < SegmentWidth) ? (Length - startIndex) : SegmentWidth);
                        if (offset != segmentLength)
                        {
                            T nextValue = Data[startIndex + offset];
                            if (bestSegmentIndex == -1 || nextValue.CompareTo(bestValue) <= 0)
                            {
                                bestValue = nextValue;
                                bestSegmentIndex = i;
                            }
                        }
                    }
                    segmentIndex[bestSegmentIndex]++;
                    resultCopy[sortIndex] = bestValue;
                }
                UnsafeUtility.MemCpy((void*)Data, (void*)resultCopy, (long)(UtilsUnsafe.SizeOf<T>() * Length));
            }
        }

        public unsafe static JobHandle SortJob<T>(this NativeList<T> array, JobHandle inputDeps = default(JobHandle)) where T : unmanaged, IComparable<T>
        {
            return SortJob(array.Raw, array.Length, inputDeps);
        }

        public unsafe static JobHandle SortJob<T>(T* array, int length, JobHandle inputDeps = default(JobHandle)) where T : unmanaged, IComparable<T>
        {
            if (length == 0)
            {
                return inputDeps;
            }
            int segmentCount = (length + 1023) / 1024;
            int workerCount = math.max(1, 128);
            int workerSegmentCount = segmentCount / workerCount;
            SegmentSort<T> segmentSort = default(SegmentSort<T>);
            segmentSort.Data = array;
            segmentSort.Length = length;
            segmentSort.SegmentWidth = 1024;
            SegmentSort<T> segmentSortJob = segmentSort;
            JobHandle segmentSortJobHandle = segmentSortJob.Schedule(segmentCount, workerSegmentCount, inputDeps);
            SegmentSortMerge<T> segmentSortMerge = default(SegmentSortMerge<T>);
            segmentSortMerge.Data = array;
            segmentSortMerge.Length = length;
            segmentSortMerge.SegmentWidth = 1024;
            SegmentSortMerge<T> segmentSortMergeJob = segmentSortMerge;
            return segmentSortMergeJob.Schedule(segmentSortJobHandle);
        }

        public static void Sort<T>(this NativeList<T> list) where T : unmanaged, IComparable<T>
        {
            list.Sort(default(DefaultComparer<T>));
        }

        public unsafe static void Sort<T, U>(this NativeList<T> list, U comp) where T : unmanaged where U : IComparer<T>
        {
            IntroSort<T, U>(list.Raw, list.Length, comp);
        }

        private struct DefaultComparer<T> : IComparer<T> where T : IComparable<T>
        {
            public int Compare(T x, T y)
            {
                return x.CompareTo(y);
            }
        }

        public unsafe static void Sort<T>(T* array, int length) where T : unmanaged, IComparable<T>
        {
            IntroSort<T, DefaultComparer<T>>(array, length, default(DefaultComparer<T>));
        }

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
            T pivot = UtilsUnsafe.ReadArrayElement<T>(array, mid);
            Swap<T>(array, mid, hi - 1);
            int left = lo;
            int right = hi - 1;
            while (left < right)
            {
                while (comp.Compare(pivot, UtilsUnsafe.ReadArrayElement<T>(array, ++left)) > 0)
                {
                }
                while (comp.Compare(pivot, UtilsUnsafe.ReadArrayElement<T>(array, --right)) < 0)
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
                T t = UtilsUnsafe.ReadArrayElement<T>(array, i + 1);
                while (j >= lo && comp.Compare(t, UtilsUnsafe.ReadArrayElement<T>(array, j)) < 0)
                {
                    UtilsUnsafe.WriteArrayElement<T>(array, j + 1, UtilsUnsafe.ReadArrayElement<T>(array, j));
                    j--;
                }
                UtilsUnsafe.WriteArrayElement<T>(array, j + 1, t);
            }
        }

        private unsafe static void SwapIfGreaterWithItems<T, U>(void* array, int lhs, int rhs, U comp) where T : unmanaged where U : IComparer<T>
        {
            if (lhs != rhs && comp.Compare(UtilsUnsafe.ReadArrayElement<T>(array, lhs), UtilsUnsafe.ReadArrayElement<T>(array, rhs)) > 0)
            {
                Swap<T>(array, lhs, rhs);
            }
        }

        private unsafe static void Heapify<T, U>(void* array, int i, int n, int lo, U comp) where T : unmanaged where U : IComparer<T>
        {
            T val = UtilsUnsafe.ReadArrayElement<T>(array, lo + i - 1);
            while (i <= n / 2)
            {
                int child = 2 * i;
                if (child < n && comp.Compare(UtilsUnsafe.ReadArrayElement<T>(array, lo + child - 1), UtilsUnsafe.ReadArrayElement<T>(array, lo + child)) < 0)
                {
                    child++;
                }
                if (comp.Compare(UtilsUnsafe.ReadArrayElement<T>(array, lo + child - 1), val) < 0)
                {
                    break;
                }
                UtilsUnsafe.WriteArrayElement<T>(array, lo + i - 1, UtilsUnsafe.ReadArrayElement<T>(array, lo + child - 1));
                i = child;
            }
            UtilsUnsafe.WriteArrayElement<T>(array, lo + i - 1, val);
        }

        private unsafe static void Swap<T>(void* array, int lhs, int rhs) where T : unmanaged
        {
            T val = UtilsUnsafe.ReadArrayElement<T>(array, lhs);
            UtilsUnsafe.WriteArrayElement<T>(array, lhs, UtilsUnsafe.ReadArrayElement<T>(array, rhs));
            UtilsUnsafe.WriteArrayElement<T>(array, rhs, val);
        }

        private static int Log2Floor(int value)
        {
            return 31 - math.lzcnt((uint)value);
        }
    }
}

