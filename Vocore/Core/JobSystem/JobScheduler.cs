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
        private readonly CircularWorkStealingDeque<JobMeta<TJob>> _jobs = new CircularWorkStealingDeque<JobMeta<TJob>>(64);

        private bool _isDisposed = false;
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

        ~JobScheduler()
        {
            Dispose();
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

                while (true)
                {
                    StealingResult status = _jobs.TrySteal(out var meta);
                    if (status == StealingResult.Success)
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
                        }
                        continue;
                    }
                    if (status == StealingResult.Empty)
                    {
                        break;
                    }
                }
                _event.Reset();
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle Schedule(TJob job)
        {
            var handle = new JobHandle(false);
            var jobMeta = new JobMeta<TJob>(handle, job);
            _queuedJobs.Enqueue(jobMeta);
            return jobMeta.jobHandle;
        }

        /// <summary>
        /// Flushes all queued <see cref="IJob"/>'s to the worker threads. 
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flush()
        {
            while (_queuedJobs.TryDequeue(out var jobMeta))
            {
                _jobs.Push(jobMeta);
            }
            _event.Set();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _cancellationTokenSource.Cancel(false);
            _event.Set();
            _isDisposed = true;
        }
    }
}