using NUnit.Framework;
using System;

using Alco;

namespace Alco.Test
{
    public class TestBinarySearch
    {
        [Test(Description = "binary search")]
        public void TestSearch()
        {
            int[] array = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            int index = AlgoBinarySearch.BinarySearch<int>(array, 5);
            Assert.IsFalse(index != 4, "binary search failed: expect 4, got " + index);
        }

        [Test(Description = "binary search floor")]
        public void TestSearchFloor()
        {
            int[] array = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            int index = AlgoBinarySearch.BinarySearchFloor<int>(array, 5);
            Assert.IsFalse(index != 4, "binary search failed: expect 4, got " + index);

            array = new int[] { 1, 1, 3, 5, 6, 7, 8, 9, 10 };
            index = AlgoBinarySearch.BinarySearchFloor<int>(array, 4);
            Assert.IsFalse(index != 2, "binary search failed: expect 2, got " + index);

            index = AlgoBinarySearch.BinarySearchFloor<int>(array, -1);
            Assert.IsFalse(index != -1, "binary search failed: expect -1, got " + index);

            index = AlgoBinarySearch.BinarySearchFloor<int>(array, 13);
            Assert.IsFalse(index != 8, "binary search failed: expect 8, got " + index);
        }

        [Test(Description = "binary search ceil")]
        public void TestSearchCeil()
        {
            int[] array = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            int index = AlgoBinarySearch.BinarySearchCeil<int>(array, 5);
            Assert.IsFalse(index != 4, "binary search failed: expect 4, got " + index);

            array = new int[] { 1, 1, 3, 5, 6, 7, 8, 9, 10 };
            index = AlgoBinarySearch.BinarySearchCeil<int>(array, 4);
            Assert.IsFalse(index != 3, "binary search failed: expect 3, got " + index);

            index = AlgoBinarySearch.BinarySearchFloor<int>(array, -1);
            Assert.IsFalse(index != -1, "binary search failed: expect -1, got " + index);

            index = AlgoBinarySearch.BinarySearchFloor<int>(array, 13);
            Assert.IsFalse(index != 8, "binary search failed: expect 8, got " + index);
        }
    }
}
