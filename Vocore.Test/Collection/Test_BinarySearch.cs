using System;
using System.Collections.Generic;

using Vocore;

namespace Vocore.Test
{
    public class Test_BinarySearch
    {
        [Test("binary search")]
        public void Test_Search()
        {
            int[] array = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            int index = UtilsAlgorithm.BinarySearch<int>(array, 5);
            TestHelper.AssertFalse(index != 4, "binary search failed: expect 4, got " + index);
        }

        [Test("binary search floor")]
        public void Test_SearchFloor()
        {
            int[] array = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            int index = UtilsAlgorithm.BinarySearchFloor<int>(array, 5);
            TestHelper.AssertFalse(index != 4, "binary search failed: expect 4, got " + index);

            array = new int[] { 1, 1, 3, 5, 6, 7, 8, 9, 10 };
            index = UtilsAlgorithm.BinarySearchFloor<int>(array, 4);
            TestHelper.AssertFalse(index != 2, "binary search failed: expect 2, got " + index);

            index = UtilsAlgorithm.BinarySearchFloor<int>(array, -1);
            TestHelper.AssertFalse(index != -1, "binary search failed: expect -1, got " + index);

            index = UtilsAlgorithm.BinarySearchFloor<int>(array, 13);
            TestHelper.AssertFalse(index != 8, "binary search failed: expect 8, got " + index);
        }

        [Test("binary search ceil")]
        public void Test_SearchCeil()
        {
            int[] array = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            int index = UtilsAlgorithm.BinarySearchCeil<int>(array, 5);
            TestHelper.AssertFalse(index != 4, "binary search failed: expect 4, got " + index);

            array = new int[] { 1, 1, 3, 5, 6, 7, 8, 9, 10 };
            index = UtilsAlgorithm.BinarySearchCeil<int>(array, 4);
            TestHelper.AssertFalse(index != 3, "binary search failed: expect 3, got " + index);

            index = UtilsAlgorithm.BinarySearchFloor<int>(array, -1);
            TestHelper.AssertFalse(index != -1, "binary search failed: expect -1, got " + index);

            index = UtilsAlgorithm.BinarySearchFloor<int>(array, 13);
            TestHelper.AssertFalse(index != 8, "binary search failed: expect 8, got " + index);
        }
    }
}

