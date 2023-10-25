using System;
using System.Collections.Generic;
using System.Threading;

namespace Vocore.Test
{
    public class Test_Scheduler
    {
        [Test("Test Job Scheduler")]
        public void TestJobScheduler()
        {
            int count = 100000;
            int[] array = new int[count];
            JobScheduler.Instance.ScheduleParallel(count, (i) =>
            {

                array[i] = 2 * i / 3 + 100;
            });
            int successCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (array[i] == (2 * i / 3 + 100))
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
            JobScheduler testScheduler = new JobScheduler(10000, "TestScheduler");
            testScheduler.ScheduleParallel(count, (i) =>
            {
                array[i] = 2 * i / 3 + 100;
            });
            int successCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (array[i] == (2 * i / 3 + 100))
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

