using System;
using System.Collections.Generic;

using Vocore;

namespace Vocore.Test
{
    public class Test_PriorityList
    {
        [Test("Test_PriorityList add")]
        public void Test_Add()
        {
            PriorityList<int> list = new PriorityList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);
            list.Add(4);
            list.Add(7);
            list.Add(6);
            list.Add(5);
            list.Add(10);
            list.Add(3);
            list.Add(-1);

            int[] result = new int[] { -1, 1, 2, 3, 3, 4, 5, 6, 7, 10 };

            if (list.Count != result.Length)
            {
                TestHelper.AddFailed();
                TestHelper.PrintRed("Test_PriorityList add failed");
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != result[i])
                {
                    TestHelper.AddFailed();
                    TestHelper.PrintRed("Test_PriorityList add failed");
                    return;
                }
            }
        }

        [Test("Test_PriorityList remove")]
        public void Test_Remove()
        {
            PriorityList<int> list = new PriorityList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);
            list.Add(4);
            list.Add(7);
            list.Add(6);
            list.Add(5);
            list.Add(10);
            list.Add(3);
            list.Add(-1);

            list.RemoveOnce(3);
            list.RemoveOnce(5);
            list.RemoveOnce(10);
            list.RemoveOnce(-1);

            int[] result = new int[] { 1, 2, 3, 4, 6, 7 };

            if (list.Count != result.Length)
            {
                TestHelper.AddFailed();
                TestHelper.PrintRed("Test_PriorityList add failed");
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != result[i])
                {
                    TestHelper.AddFailed();
                    TestHelper.PrintRed("Test_PriorityList remove failed");
                    return;
                }
            }
        }


    }
}

