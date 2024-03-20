using System;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

#pragma warning disable CS8618

namespace Vocore
{
    public class ParallelScheduler : IDisposable
    {
        private struct WorkerData
        {
            public int index;
            public bool isRunning;
            public IndexWorkStealingDeque tasks;
            public ManualResetEvent signal;
        }
        public static ParallelScheduler Instance = new ParallelScheduler(Environment.ProcessorCount * 2, "JobThread");
        private readonly Thread[] _threads;
        private readonly CancellationTokenSource _cancellationTokenSource;
        //private readonly ManualResetEvent _event = new ManualResetEvent(false);
        private readonly CountdownEvent _countdownEvent;
        private readonly WorkerData[] _threadData;
        private readonly int _threadCount;
        private readonly int _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        private readonly int _maxStealFailCount;
        private ParallelForDelegate _currentJob;


        private bool _isDisposed = false;
        public ParallelScheduler(int threadCount, string threadPrefix = "JobThread")
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _countdownEvent = new CountdownEvent(threadCount);
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
                _threads[i] = new Thread(ThreadWorker(i));
                _threads[i].Name = $"{threadPrefix} {i}";
                _threads[i].Start();
            }
        }

        ~ParallelScheduler()
        {
            Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ThreadStart ThreadWorker(int index)
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
                            _currentJob(jobIndex);
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
                                _currentJob(jobIndex);
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
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new Exception("ScheduleParallel can only be called by the thread constructed this scheduler");
            }
            _currentJob = action;
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
            //Log.Info("Parallel for finished");
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
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
            _isDisposed = true;
        }
    }
}