using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Vocore
{

    public static class JobSystem
    {
        internal static class JobCache<T> where T : unmanaged, IJobBatch
        {
            public static T Job;
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
            //use static cache to avoid boxing
            JobCache<T>.Job = job;
            ParallelScheduler.Instance.For(count, (i) =>
            {
                JobCache<T>.Job.Execute(i);
            });
        }
    }
}