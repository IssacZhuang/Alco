using System;
using System.Collections.Generic;
using System.Threading;

namespace Vocore.Test
{
    public class TestScheduler
    {

        [Test(Description = "Parallel Scheduler")]
        public void TestParallelScheduler()
        {
            int count = 100000;
            int[] array = new int[count];
            using ParallelScheduler scheduler = new ParallelScheduler();
            scheduler.For(count, (i) =>
            {

                array[i] = i;
            });
            int successCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (array[i] == i)
                {
                    successCount++;
                }
            }
            TestContext.WriteLine("success: " + successCount);
            Assert.IsTrue(successCount == count);

        }

        //[Test(Description = "Parallel Scheduler High Concurrent")]
        public void TestParallelSchedulerHighConcurrent()
        {
            int count = 5000;
            int[] array = new int[count];
            ParallelScheduler testScheduler = new ParallelScheduler(8999, "TestScheduler");
            int testCount = 1;

            for (int j = 0; j < testCount; j++)
            {
                for (int i = 0; i < count; i++)
                {
                    array[i] = 0;
                }

                testScheduler.For(count, (i) =>
                {
                    array[i] = i;
                });
                int successCount = 0;
                for (int i = 0; i < count; i++)
                {
                    if (array[i] == i)
                    {
                        successCount++;
                    }
                    else
                    {
                        break;
                    }
                }

                TestContext.WriteLine("success: " + successCount);
                Assert.IsTrue(successCount == count);
            }

            testScheduler.Dispose();

        }
    }
}

