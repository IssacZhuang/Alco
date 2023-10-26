using System;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Collections.Generic;


namespace Vocore
{
    public class JobScheduler
    {
        private struct Task
        {
            public IJob job;
        }

        private struct WorkerData
        {
            public int index;
            public bool isRunning;
            public CircularWorkStealingDeque<Task> tasks;
        }
        public static JobScheduler Instance = new JobScheduler(Environment.ProcessorCount * 2, "JobThread");
        private readonly Thread[] _threads;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ManualResetEvent _event = new ManualResetEvent(false);
        private readonly WorkerData[] _threadData;
        private readonly int _threadCount;
        private readonly int _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        private int _selectedThread = 0;

        private bool _isDisposed = false;
        public JobScheduler(int threadCount, string threadPrefix = "JobThread")
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _threadData = new WorkerData[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                _threadData[i] = new WorkerData()
                {
                    index = i,
                    isRunning = false,
                    tasks = new CircularWorkStealingDeque<Task>(1024)
                };
            }
            _threadCount = threadCount;
            _threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                _threads[i] = new Thread(ThreadWorker(i));
                _threads[i].Name = $"{threadPrefix} {i}";
                _threads[i].Start();
            }
        }

        ~JobScheduler()
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
                _event.WaitOne();
                Volatile.Write(ref selfData.isRunning, true);
                //exploit local queue
                while (true)
                {
                    StealingResult status = selfData.tasks.TryPop(out var meta);
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

                        }
                        continue;
                    }
                    if (status == StealingResult.Empty)
                    {
                        break;
                    }
                }
                //steal from other queues
                for (int i = index + 1; i < index + _threadCount; i++)
                {
                    int stealIndex = i % _threadCount;
                    WorkerData stealData = _threadData[stealIndex];
                    while (true)
                    {
                        StealingResult status = stealData.tasks.TrySteal(out var meta);
                        if (status == StealingResult.Empty)
                        {
                            break;
                        }
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

                            }
                            continue;
                        }
                    }

                }
                _event.Reset();
                Volatile.Write(ref selfData.isRunning, false);
            }

        }

        //using generic to avoid boxing
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Push<T>(T job) where T : class, IJob
        {
            WorkerData selectedData = _threadData[_selectedThread];
            selectedData.tasks.Push(new Task() { job = job });
            _selectedThread = (_selectedThread + 1) % _threadCount;
        }

        public void ExecuteAndWaitForComplete()
        {
            WaitForComplete();
        }

        public void Execute()
        {
            _event.Set();
        }

        public void WaitForComplete()
        {
            //wait for all threads to finish
            for (int i = 0; i < _threadCount; i++)
            {
                while (Volatile.Read(ref _threadData[i].isRunning)) ;
            }
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
            _event.Set();
            // for (int i = 0; i < _threadCount; i++)
            // {
            //     _threads[i].Join();
            // }
            _isDisposed = true;
        }
    }
}