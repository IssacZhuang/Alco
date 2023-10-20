using System;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static class JobSystem
    {
        private struct JobBatchElement:IJob{
            public int index;
            public IJobBatch jobBatch;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Execute()
            {
                jobBatch.Execute(index);
            }
        }
        public static void Run<T>(this T job) where T : unmanaged, IJob
        {
            job.Execute();
        }

        public static void Run<T>(this T job, int count) where T : unmanaged, IJobBatch
        {
            for (int i = 0; i < count; i++)
            {
                job.Execute(i);
            }

        }

        public static void RunParallel<T>(this T job, int count) where T : unmanaged, IJobBatch
        {
            // for(int i=0;i<count;i++)
            // {
            //     job.Execute(i);
            // }
            //FastParallel.For(0, count, job.Execute);
            //Parallel.For(0, count, job.Execute);
            JobHandle[] jobHandles = new JobHandle[count];
            for (int i = 0; i < count; i++)
            {
                JobBatchElement jobElement = new JobBatchElement() { index = i, jobBatch = job };
                jobHandles[i] = JobScheduler<JobBatchElement>.Instance.Schedule(jobElement);
            }
            JobHandle.Complete(jobHandles);
        }
    }
}