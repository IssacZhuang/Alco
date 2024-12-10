using System;
using System.Collections.Generic;
using TestFramework;
using Vocore;

namespace Vocore.Test
{
    public class TestPriorityList
    {
        [Test(Description = "TestPriorityList add")]
        public void TestAdd()
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
                Assert.Fail("PriorityList add failed");
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != result[i])
                {
                    Assert.Fail("PriorityList add failed");
                    return;
                }
            }
        }

        [Test(Description = "TestPriorityList remove")]
        public void TestRemove()
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

            list.Remove(3);
            list.Remove(5);
            list.Remove(10);
            list.Remove(-1);

            int[] result = new int[] { 1, 2, 3, 4, 6, 7 };

            if (list.Count != result.Length)
            {
                Assert.Fail("PriorityList remove failed");
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != result[i])
                {
                    Assert.Fail("PriorityList remove failed");
                    return;
                }
            }
        }
    }
}

