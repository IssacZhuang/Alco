using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static class JobSystem
    {
        private struct JobBatchElement<T>:IJob where T:unmanaged,IJobBatch{
            public int index;
            public T jobBatch;
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
            // JobHandle[] jobHandles = new JobHandle[count];
            // for (int i = 0; i < count; i++)
            // {
            //     JobBatchElement<T> jobElement = new JobBatchElement<T>() { index = i, jobBatch = job };
            //     jobHandles[i] = JobScheduler<JobBatchElement<T>>.Instance.Schedule(jobElement);
            // }
            // JobScheduler<JobBatchElement<T>>.Instance.Flush();
            // JobHandle.Complete(jobHandles);
            ParallelScheduler.Instance.For(count, (i) =>
            {
                job.Execute(i);
            });
        }
    }
}