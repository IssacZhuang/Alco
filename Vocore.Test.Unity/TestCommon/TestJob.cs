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
            int count = 1000000;
            NativeArrayList<float> list = new NativeArrayList<float>();
            NativeList<float> list2 = new NativeList<float>(Allocator.Temp);

            for (int i = 0; i < count; i++)
            {
                list.Add(count - i);
                list2.Add(count - i);
            }

            TestHelper.Benchmark("vocore native list", () =>
            {
                list.Sort();
            });

            TestHelper.Benchmark("unity native list", () =>
            {
                list2.Sort();
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

