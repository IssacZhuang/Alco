using System;
using System.Collections.Generic;
using System.Threading;

namespace Vocore.Test
{
    public class Test_Scheduler
    {
        private class JobAction : IJob
        {
            public Action action;
            public void Execute()
            {
                action();
            }
        }
        [Test("Test Job Scheduler")]
        public void TestJobScheduler()
        {
            int count = 100000;
            int[] array = new int[count];
            for (int i = 0; i < count; i++)
            {
                JobScheduler.Instance.Push(new JobAction() { action = () => { array[i] = i; } });
            }
            JobScheduler.Instance.ExecuteAndWaitForComplete();
            int successCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (array[i] == i)
                {
                    successCount++;
                }
            }
            UnitTest.PrintBlue("success: " + successCount);
            UnitTest.AssertTrue(successCount == count);

        }

        [Test("Test Job Scheduler High Concurrent")]
        public void TestJobSchedulerHighConcurrent()
        {
            int count = 100000;
            int[] array = new int[count];
            JobScheduler testScheduler = new JobScheduler(8889, "TestScheduler");
            for (int i = 0; i < count; i++)
            {
                testScheduler.Push(new JobAction() { action = () => { array[i] = i; } });
            }
            testScheduler.ExecuteAndWaitForComplete();
            int successCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (array[i] == i)
                {
                    successCount++;
                }
            }
            UnitTest.PrintBlue("success: " + successCount);
            UnitTest.AssertTrue(successCount == count);
            testScheduler.Dispose();

        }

        [Test("Test Parallel Scheduler")]
        public void TestParallelScheduler()
        {
            int count = 100000;
            int[] array = new int[count];
            ParallelScheduler.Instance.For(count, (i) =>
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
            UnitTest.PrintBlue("success: " + successCount);
            UnitTest.AssertTrue(successCount == count);

        }

        [Test("Test Parallel Scheduler High Concurrent")]
        public void TestParallelSchedulerHighConcurrent()
        {
            int count = 100000;
            int[] array = new int[count];
            ParallelScheduler testScheduler = new ParallelScheduler(8889, "TestScheduler");
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
            }
            UnitTest.PrintBlue("success: " + successCount);
            UnitTest.AssertTrue(successCount == count);
            testScheduler.Dispose();

        }
    }
}

