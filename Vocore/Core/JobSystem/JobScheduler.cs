using System;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Vocore
{
    public class JobScheduler<TJob> : IDisposable where TJob : IJob
    {
        public static JobScheduler<TJob> Instance = new JobScheduler<TJob>(Environment.ProcessorCount * 2, "JobThread");
        private readonly Thread[] _threads;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ManualResetEvent _event = new ManualResetEvent(false);
        private readonly Queue<JobMeta<TJob>> _queuedJobs = new Queue<JobMeta<TJob>>();
        private readonly ConcurrentQueue<JobMeta<TJob>> _jobs = new ConcurrentQueue<JobMeta<TJob>>();

        public JobScheduler(int threadCount, string threadPrefix = "JobThread")
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                _threads[i] = new Thread(ThreadWorker);
                _threads[i].Name = $"{threadPrefix} {i}";
                _threads[i].Start();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThreadWorker()
        {
            ThreadLoop(_cancellationTokenSource.Token);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThreadLoop(CancellationToken token)
        {

            while (!token.IsCancellationRequested)
            {
                _event.WaitOne();
                _event.Reset();

                while (_jobs.TryDequeue(out var meta))
                {

                    try
                    {
                        meta.job.Execute();
                    }
                    catch (Exception e)
                    {
                        Log.Error(e);
                    }
                    finally
                    {
                        meta.jobHandle.Notify();

                        if (meta.jobHandle._poolOnComplete)
                        {
                            JobHandle.ManualResetEventPool.Return(meta.jobHandle._event);
                        }
                    }
                }
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle Schedule(TJob job, bool poolOnComplete = false)
        {

            var handle = new JobHandle(JobHandle.ManualResetEventPool.Get(), poolOnComplete);
            handle._event.Reset();

            var jobMeta = new JobMeta<TJob>(handle, job);
            _queuedJobs.Enqueue(jobMeta);
            return handle;
        }

        /// <summary>
        /// Flushes all queued <see cref="IJob"/>'s to the worker threads. 
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flush()
        {

            while (_queuedJobs.TryDequeue(out var jobMeta))
            {
                _jobs.Enqueue(jobMeta);
            }
            _event.Set();
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel(false);

        }
    }
}