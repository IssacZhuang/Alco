using System;
using System.Collections.Generic;

namespace Vocore.Test
{
    //[DisabledTestTemporarily]
    public class Test_NativeList
    {
        [Test("NativeList add")]
        public void TestAdd()
        {
            int[] result = new int[100];
            for (int i = 0; i < 100; i++)
            {
                result[i] = i * 2;
            }

            NativeList<int> list = new NativeList<int>();
            for (int i = 0; i < 100; i++)
            {
                list.Add(i * 2);
            }

            bool success = true;

            for (int i = 0; i < 100; i++)
            {
                if (list[i] != result[i])
                {
                    success = false;
                    break;
                }
            }

            TestHelper.Assert(!success);
            list.Dispose();
        }

        [Test("NativeList insert")]
        public void TestInsert()
        {
            int[] orgin = new int[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            int[] result = new int[11] { 0, 1, 2, 3, 4, 10, 5, 6, 7, 8, 9 };

            NativeList<int> list = new NativeList<int>();
            for (int i = 0; i < 10; i++)
            {
                list.Add(i);
            }

            list.Insert(5, 10);

            bool success = true;

            for (int i = 0; i < result.Length; i++)
            {
                if (list[i] != result[i])
                {
                    success = false;
                    break;
                }
            }

            TestHelper.Assert(!success);
            list.Dispose();
        }

        [Test("NativeList remove")]
        public void TestRemove()
        {
            int[] orgin = new int[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            int[] result = new int[9] { 0, 1, 2, 3, 4, 6, 7, 8, 9 };

            NativeList<int> list = new NativeList<int>();
            for (int i = 0; i < 10; i++)
            {
                list.Add(i);
            }

            list.Remove(5);

            bool success = true;

            for (int i = 0; i < result.Length; i++)
            {
                if (list[i] != result[i])
                {
                    success = false;
                    break;
                }
            }

            TestHelper.Assert(!success);
            list.Dispose();
        }

        [Test("NativeList remove at")]
        public void TestRemoveAt()
        {
            int[] orgin = new int[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            int[] result = new int[9] { 0, 1, 2, 3, 4, 6, 7, 8, 9 };

            NativeList<int> list = new NativeList<int>();
            for (int i = 0; i < 10; i++)
            {
                list.Add(i);
            }

            list.RemoveAt(5);

            bool success = true;

            for (int i = 0; i < result.Length; i++)
            {
                if (list[i] != result[i])
                {
                    success = false;
                    break;
                }
            }

            TestHelper.Assert(!success);
            list.Dispose();
        }

        [Test("NativeList index of")]
        public void TestIndexOf()
        {
            int[] orgin = new int[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            int indexOfSeven = 7;

            NativeList<int> list = new NativeList<int>();
            for (int i = 0; i < 10; i++)
            {
                list.Add(i);
            }

            int index = list.IndexOf(7);


            TestHelper.Assert(index != indexOfSeven);
            list.Dispose();
        }

        [Test("NativeList contains")]
        public void TestContains()
        {
            int[] orgin = new int[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            int contained = 7;
            int notContained = 11;

            NativeList<int> list = new NativeList<int>();
            for (int i = 0; i < 10; i++)
            {
                list.Add(i);
            }

            bool success = list.Contains(contained) && !list.Contains(notContained);

            TestHelper.Assert(!success);
            list.Dispose();
        }

        [Test("NativeList resize")]
        public void TestResize()
        {
            int count = 10000;
            NativeList<int> list = new NativeList<int>(100);
            for (int i = 0; i < count; i++)
            {
                list.Add(i);
            }

            for (int i = 0; i < count; i++)
            {
                list.RemoveAt(0);
            }

            TestHelper.Assert(list.Count != 0);
            TestHelper.Assert(list.DefaultCapacity != 100);
            list.Dispose();
        }

    }
}

