using System;

namespace Vocore;

public struct JobExcuteResult<TJob> where TJob:IJob
{
    public JobExcuteResult(TJob job, Exception? e = null)
    {
        this.job = job;
        exception = e;
    }
    public TJob job;
    public Exception? exception;
}