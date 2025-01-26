using System;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

#pragma warning disable CS8618

namespace Alco
{
    public class ParallelScheduler : AutoDisposable
    {
        private struct WorkerData
        {
            public int index;
            public bool isRunning;
            public IndexWorkStealingDeque tasks;
            public ManualResetEvent signal;
        }

        private class DelegateJob : IJobBatch
        {
            public ParallelForDelegate action;
            public void Execute(int i)
            {
                action(i);
            }
        }

        private readonly Thread[] _threads;
        private readonly CancellationTokenSource _cancellationTokenSource;
        //private readonly ManualResetEvent _event = new ManualResetEvent(false);
        private readonly WorkerData[] _threadData;
        private readonly int _threadCount;
        private readonly int _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        private readonly int _maxStealFailCount;
        private readonly DelegateJob _delegateJob = new DelegateJob();
        private IJobBatch _currentJob;

        public ParallelScheduler(string threadPrefix = "JobThread") : this(Environment.ProcessorCount * 2, threadPrefix)
        {
        }

        public ParallelScheduler(int threadCount, string threadPrefix = "JobThread")
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _threadData = new WorkerData[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                _threadData[i] = new WorkerData()
                {
                    index = i,
                    isRunning = false,
                    tasks = new IndexWorkStealingDeque(),
                    signal = new ManualResetEvent(false)
                };
            }
            _threadCount = threadCount;
            _maxStealFailCount = (threadCount - 1);
            _threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                _threads[i] = new Thread(CreateThreadWorker(i));
                _threads[i].Name = $"{threadPrefix} {i}";
                _threads[i].Start();
            }
        }

        ~ParallelScheduler()
        {
            Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ThreadStart CreateThreadWorker(int index)
        {
            return () => ThreadLoop(_cancellationTokenSource.Token, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThreadLoop(CancellationToken token, int index)
        {
            ref WorkerData selfData = ref _threadData[index];
            while (!token.IsCancellationRequested)
            {
                selfData.signal.WaitOne();
                //Volatile.Write(ref selfData.isRunning, true);
                //exploit local queue
                while (true)
                {
                    StealingResult status = selfData.tasks.TryPop(out var jobIndex);
                    if (status == StealingResult.Success)
                    {
                        try
                        {
                            _currentJob.Execute(jobIndex);
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                        finally
                        {

                        }
                        continue;
                    }
                    if (status == StealingResult.Empty)
                    {
                        break;
                    }
                }
                int failedCount = 0;
                //steal from other queues
                for (int i = index + 1; i < index + _threadCount; i++)
                {
                    int stealIndex = i % _threadCount;
                    ref WorkerData stealData = ref _threadData[stealIndex];
                    while (true)
                    {
                        StealingResult status = stealData.tasks.TrySteal(out var jobIndex);
                        if (status == StealingResult.Empty)
                        {
                            failedCount++;
                            break;
                        }
                        if (status == StealingResult.Success)
                        {
                            try
                            {
                                _currentJob.Execute(jobIndex);
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                            finally
                            {

                            }
                            continue;
                        }
                        failedCount++;
                    }
                    if (failedCount > _maxStealFailCount)
                    {
                        break;
                    }
                }
                selfData.signal.Reset();
                Volatile.Write(ref selfData.isRunning, false);
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void For(int count, ParallelForDelegate action)
        {
            _delegateJob.action = action;
            Run(_delegateJob, count);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run(IJobBatch job, int count)
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new Exception("ScheduleParallel can only be called by the thread constructed this scheduler");
            }
            _currentJob = job;
            int chunkSize = count / _threadCount;
            int remainder = count % _threadCount;
            int start = 0;
            int end = 0;
            for (int i = 0; i < _threadCount; i++)
            {
                start = end;
                end = start + chunkSize;
                if (remainder > 0)
                {
                    end++;
                    remainder--;
                }
                ref WorkerData workerData = ref _threadData[i];
                workerData.tasks.Clear();

                Volatile.Write(ref workerData.isRunning, true);
                workerData.tasks.Set(start, end - start); 
            }

            for (int i = 0; i < _threadCount; i++)
            {
                _threadData[i].signal.Set();
            }

            //wait for all threads to finish
            for (int i = 0; i < _threadCount; i++)
            {
                while (Volatile.Read(ref _threadData[i].isRunning)) ;
            }
        }

        protected override void Dispose(bool disposing)
        {
            for (int i = 0; i < _threadCount; i++)
            {
                _threadData[i].tasks.Clear();
            }
            _cancellationTokenSource.Cancel(false);
            for (int i = 0; i < _threadCount; i++)
            {
                _threadData[i].signal.Set();
            }

            foreach (var thread in _threads)
            {
                thread.Join();
            }
        }
    }
}