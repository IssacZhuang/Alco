using System;
using System.Collections.Generic;

namespace Alco
{
    public static class CollectionUtility
    {
        /// <summary>
        /// Generates all permutations of the given array.
        /// </summary>
        /// <typeparam name="T">The type of the elements in the array.</typeparam>
        /// <param name="array">The array to permute.</param>
        /// <returns>An array of arrays, each containing a permutation of the input array.</returns>
        public static T[][] GetPermutations<T>(Span<T> array)
        {
            var result = new List<T[]>();
            T[] arrayCopy = array.ToArray();
            Permute(arrayCopy, 0, array.Length - 1, result);
            return result.ToArray();
        }

        /// <summary>
        /// Generates all permutations of the given array.
        /// </summary>
        /// <typeparam name="T">The type of the elements in the array.</typeparam>
        /// <param name="array">The array to permute.</param>
        /// <returns>An array of arrays, each containing a permutation of the input array.</returns>
        public static T[][] GetPermutations<T>(T[] array)
        {
            var result = new List<T[]>();
            Permute(array, 0, array.Length - 1, result);
            return result.ToArray();
        }


        /// <summary>
        /// Recursively generates permutations of the array.
        /// </summary>
        /// <typeparam name="T">The type of the elements in the array.</typeparam>
        /// <param name="array">The array to permute.</param>
        /// <param name="start">The starting index for the permutation.</param>
        /// <param name="end">The ending index for the permutation.</param>
        /// <param name="result">The list to store the permutations.</param>
        private static void Permute<T>(T[] array, int start, int end, List<T[]> result)
        {
            if (start == end)
            {
                result.Add((T[])array.Clone());
            }
            else
            {
                for (int i = start; i <= end; i++)
                {
                    Swap(ref array[start], ref array[i]);
                    Permute(array, start + 1, end, result);
                    Swap(ref array[start], ref array[i]);
                }
            }
        }

        /// <summary>
        /// Swaps two elements in the array.
        /// </summary>
        /// <typeparam name="T">The type of the elements to swap.</typeparam>
        /// <param name="a">The first element to swap.</param>
        /// <param name="b">The second element to swap.</param>
        private static void Swap<T>(ref T a, ref T b)
        {
            T temp = a;
            a = b;
            b = temp;
        }

        /// <summary>
        /// Generates all possible combinations of the given array.
        /// Unlike permutations, combinations don't care about the order of elements.
        /// </summary>
        /// <typeparam name="T">The type of the elements in the array.</typeparam>
        /// <param name="array">The array to generate combinations from.</param>
        /// <returns>An array of arrays, each containing a combination of the input array.</returns>
        public static T[][] GetCombinations<T>(T[] array)
        {
            var result = new List<T[]>();
            // Add empty combination
            result.Add(Array.Empty<T>());

            // Generate combinations of all possible lengths (1 to array.Length)
            for (int length = 1; length <= array.Length; length++)
            {
                GenerateCombinations(array, length, 0, new T[length], 0, result);
            }

            return result.ToArray();
        }

        private static void GenerateCombinations<T>(T[] array, int length, int startPosition, T[] current, int currentPosition, List<T[]> result)
        {
            if (currentPosition == length)
            {
                result.Add((T[])current.Clone());
                return;
            }

            for (int i = startPosition; i <= array.Length - (length - currentPosition); i++)
            {
                current[currentPosition] = array[i];
                GenerateCombinations(array, length, i + 1, current, currentPosition + 1, result);
            }
        }
    }
}