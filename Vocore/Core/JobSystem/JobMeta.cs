using System;

namespace Vocore{
    internal struct JobMeta<T> where T : IJob
    {
        public JobHandle jobHandle;
        public T job ;
        public JobMeta(JobHandle handle, T newJob)
        {
            jobHandle = handle;
            job = newJob;
        }
    }
}