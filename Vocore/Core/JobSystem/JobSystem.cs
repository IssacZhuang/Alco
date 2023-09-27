using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Vocore
{
    public static class JobSystem
    {
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
            Parallel.For(0, count, job.Execute);
        }
    }
}