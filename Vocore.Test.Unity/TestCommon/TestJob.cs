using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.Concurrent;

using UnityToolBox.UnitTest;
using UnityToolBox;
using Vocore;
using Vocore.Unsafe;

namespace Vocore.Test.Unity
{
    public class TestJob
    {
        [UnitTest("TestJob")]
        public void Test()
        {
            NativeList<int> list = new NativeList<int>(10);
            NativeList<int> list2 = new NativeList<int>(10);

            int count = 100000;
            for (int i = 0; i < count; i++)
            {
                list.Add(count - i);
            }

            for (int i = 0; i < count; i++)
            {
                list2.Add(count - i);
            }

            TestHelper.Benchmark("single thread", () =>
            {
                list.Sort();
            });

            TestHelper.Benchmark("multi thread", () =>
            {
                list2.SortJob().Complete();
            });

            // for (int i = 0; i < count; i++)
            // {
            //     TestHelper.Print(list[i] + " " + list2[i]);
            // }
        }
    }
}

