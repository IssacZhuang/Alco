using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.Concurrent;

using UnityToolBox.UnitTest;
using UnityToolBox;
using Vocore;
using Vocore.Unsafe;
using Unity.Collections;

namespace Vocore.Test.Unity
{
    public class TestJob
    {
        [UnitTest("TestJob")]
        public void Test()
        {
            int count = 100;
            int[] array = new int[count];

            for (int i = 0; i < count; i++)
            {
                array[i] = count - i;
            }

            TestHelper.Benchmark(".net sort", () =>
            {
                Array.Sort(array);
            });

            for (int i = 0; i < count; i++)
            {
                array[i] = count - i;
            }

            TestHelper.Benchmark("unsafe sort", () =>
            {
                array.UnsafeSort();
            });


            // TestHelper.Benchmark("multi thread", () =>
            // {
            //     list2.SortJob().Schedule().Complete();
            // });

            // for (int i = 0; i < count; i++)
            // {
            //     TestHelper.Print(list[i] + " " + list2[i]);
            // }
        }
    }
}

