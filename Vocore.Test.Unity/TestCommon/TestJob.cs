using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.Concurrent;

using UnityToolBox.UnitTest;
using UnityToolBox;
using UnityEngine;

namespace Vocore.Test.Unity
{
    public class TestJob
    {
        [UnitTest("TestJob")]
        public void Test()
        {
            Queue<string> queue = new Queue<string>();
            List<string> list = new List<string>();
            ConcurrentQueue<string> cQueue = new ConcurrentQueue<string>();
            int count = 1000000;
            TestHelper.Benchmark("list", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    list.Add("test");
                }
            });

            TestHelper.Benchmark("queue", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    queue.Enqueue("test");
                }
            });

            TestHelper.Benchmark("cQueue", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    cQueue.Enqueue("test");
                }
            });
        }
    }
}

