using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

using Vocore;

namespace Vocore.Test
{
    public class Test_AsyncOperation
    {
        [Test("AsyncOperationBatch vs Task+Lock")]
        public void TestThread()
        {
            List<string> targetList = new List<string>();
            int count = 1000000;

            Task[] tasks = new Task[count];

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            for (int i = 0; i < count; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    lock (targetList)
                    {
                        targetList.Add("test");
                    }
                });
            }
            Task.WaitAll(tasks);
            stopwatch.Stop();
            TestHelper.PrintBlue("Task+Lock: " + stopwatch.ElapsedMilliseconds);
            AsyncOperationBatch<string> batch = new AsyncOperationBatch<string>();
            stopwatch.Restart();
            for (int i = 0; i < count; i++)
            {
                batch.Enqueue(() =>
                {
                    return "test";
                }, (str) =>
                {
                    targetList.Add(str);
                });
            }
            batch.Run();
            stopwatch.Stop();
            TestHelper.PrintBlue("AsyncOperationBatch: " + stopwatch.ElapsedMilliseconds);

        }
    }
}

