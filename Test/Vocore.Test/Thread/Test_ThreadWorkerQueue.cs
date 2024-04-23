using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Vocore.Test
{
    public class ThreadWorkerQueueTest
    {
        [Test(Description = "ThreadWorkerQueue Add")]
        public void TestPush()
        {
            ThreadWorkerQueue<QuickJob> queue = null;
            try
            {
                queue = new ThreadWorkerQueue<QuickJob>(2);
            }
            catch (Exception e)
            {
                TestContext.WriteLine(e);
            }
            var job = new QuickJob()
            {
                value = 2
            };

            queue.Push(job);


            QuickJob result = null;
            // Assert
            // Assert.IsTrue(queue.TryGetFinishedTask(out var finishedJob));
            // Assert.AreEqual(job, finishedJob);
            while (!(queue.TryGetFinishedTask(out result, out Exception __) == StealingResult.Success))
            {
                // Do nothing
            }

            Assert.That(result, Is.EqualTo(job));
            Assert.That(job.value, Is.EqualTo(4));
        }


        [Test]
        [Timeout(10000)]
        public void TestMultiQuickJob()
        {
            int count = 10000;
            QuickJob[] jobs = new QuickJob[count];
            for (int i = 0; i < count; i++)
            {
                jobs[i] = new QuickJob()
                {
                    value = i
                };
            }
            ThreadWorkerQueue<QuickJob> queue = new ThreadWorkerQueue<QuickJob>(8);
            int finishedCount = 0;
            foreach (var job in jobs)
            {
                queue.Push(job);
            }
            while (finishedCount < count)
            {
                if (queue.TryGetFinishedTask(out _, out Exception __) == StealingResult.Success)
                {
                    finishedCount++;
                }
            }

            for (int i = 0; i < count; i++)
            {
                Assert.That(jobs[i].value, Is.EqualTo(i * 2));
            }

        }

        [Test]
        [Timeout(10000)]
        public void TestMultiSlowJob()
        {
            int count = 20;
            SlowJob[] jobs = new SlowJob[count];
            for (int i = 0; i < count; i++)
            {
                jobs[i] = new SlowJob()
                {
                    value = i
                };
            }
            ThreadWorkerQueue<SlowJob> queue = new ThreadWorkerQueue<SlowJob>(4);
            int finishedCount = 0;
            foreach (var job in jobs)
            {
                queue.Push(job);
            }
            while (finishedCount < count)
            {
                if (queue.TryGetFinishedTask(out _, out Exception __) == StealingResult.Success)
                {
                    finishedCount++;
                }
            }

            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(jobs[i].value == i * 2);
            }
        }

        [Test]
        [Timeout(10000)]
        public void TestMultiSlowJobWaitAll()
        {
            int count = 20;
            SlowJob[] jobs = new SlowJob[count];
            for (int i = 0; i < count; i++)
            {
                jobs[i] = new SlowJob()
                {
                    value = i
                };
            }
            ThreadWorkerQueue<SlowJob> queue = new ThreadWorkerQueue<SlowJob>(4);

            foreach (var job in jobs)
            {
                queue.Push(job);
            }
            
            int finishedCount = queue.WaitForAllCompleted().Count();
    
            Assert.That(finishedCount, Is.EqualTo(count));
            for (int i = 0; i < count; i++)
            {
                Assert.That(jobs[i].value, Is.EqualTo(i * 2));
            }
        }

        [Test]
        public void TestErrorJob()
        {
            ErrorJob job = new ErrorJob();
            ThreadWorkerQueue<ErrorJob> queue = new ThreadWorkerQueue<ErrorJob>(2);
            queue.Push(job);

            Exception exception = null;

            while (queue.TryGetFinishedTask(out _, out exception) != StealingResult.Success)
            {
                // just wait
            }

            Assert.IsNotNull(exception);
        }

        [Test]
        public void TestNoErrorJob()
        {
            QuickJob job = new QuickJob();
            ThreadWorkerQueue<QuickJob> queue = new ThreadWorkerQueue<QuickJob>(2);
            queue.Push(job);

            Exception exception = null;

            while (queue.TryGetFinishedTask(out _, out exception) != StealingResult.Success)
            {
                // just wait
            }

            Assert.IsNull(exception);
        }



        private class QuickJob : IJob
        {
            public int value;

            public void Execute()
            {
                value *= 2;
            }
        }

        private class SlowJob : IJob
        {
            public int value;
            public void Execute()
            {
                Thread.Sleep(50);
                value *= 2;
            }
        }

        private class ErrorJob : IJob
        {
            public void Execute()
            {
                throw new Exception("Error");
            }
        }
    }
}