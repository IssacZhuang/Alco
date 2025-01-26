using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Alco;


public class ThreadWorkerQueue<TJob> : AutoDisposable where TJob : IJob
{
    private struct Task
    {
        public TJob job;
        public Exception? exception;
    }

    private struct WorkerData
    {
        public int index;
        public CircularWorkStealingDeque<Task> outputs;
    }

    private readonly WorkerData[] _threadData;
    private readonly Thread[] _threads;
    private readonly SemaphoreSlim _semaphore;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CircularWorkStealingDeque<Task> _inputs;
    private int _count;


    public ThreadWorkerQueue(int threadCount, string threadPrefix = "JobThread")
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _semaphore = new SemaphoreSlim(0);
        _inputs = new CircularWorkStealingDeque<Task>(512);
        _count = 0;

        _threadData = new WorkerData[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            _threadData[i] = new WorkerData()
            {
                index = i,
                outputs = new CircularWorkStealingDeque<Task>(256)
            };
        }

        _threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            Thread thread = new Thread(CreateThreadWorker(i));
            thread.Name = $"{threadPrefix} {i}";
            _threads[i] = thread;
            thread.Start();
        }
    }

    /// <summary>
    /// Push a job to the queue.
    /// <br/> This method is not thread-safe.
    /// </summary>
    /// <param name="job">The job to do.</param>
    public void Push(TJob job)
    {
        if (job == null)
        {
            return;
        }
        _inputs.Push(new Task() { job = job });
        _semaphore.Release();
        _count++;
    }


    /// <summary>
    /// Try to get a finished task.
    /// <br/> This method is not thread-safe.
    /// </summary>
    /// <param name="job">The finished job.</param>
    /// <param name="exception">The exception if the job failed.</param>
    /// <returns><see cref="StealingResult.Success"/> if a finished task is found, <see cref="StealingResult.Empty"/> if no finished task is found, <see cref="StealingResult.CASFailed"/> if the queue is interrupted.</returns>
    public StealingResult TryGetFinishedTask([NotNullWhen(true)] out TJob? job, out Exception? exception)
    {
        job = default;
        bool hasAbort = false;
        exception = null;
        for (int i = 0; i < _threadData.Length; i++)
        {
            ref WorkerData selfData = ref _threadData[i];
            StealingResult result = selfData.outputs.TrySteal(out Task task);

            if (result == StealingResult.Success)
            {
                job = task.job;
                exception = task.exception;
                _count--;
                return StealingResult.Success;
            }
            else if (result == StealingResult.CASFailed)
            {
                hasAbort = true;
            }

        }

        if (hasAbort)
        {
            return StealingResult.CASFailed;
        }

        return StealingResult.Empty;
    }

    /// <summary>
    /// Wait for all tasks to be completed.
    /// <br/> This method is not thread-safe.
    /// </summary>
    /// <returns>An enumerable of the finished tasks.</returns>
    public IEnumerable<JobExcuteResult<TJob>> WaitForAllCompleted()
    {
        StealingResult result;
        while (true)
        {
            result = TryGetFinishedTask(out var job, out var exception);

            if (result == StealingResult.Success)
            {
                yield return new JobExcuteResult<TJob>(job!, exception);
            }

            if (_count == 0)
            {
                break;
            }
        }

        yield break;
    }


    private ThreadStart CreateThreadWorker(int index){
        return () => ThreadLoop(_cancellationTokenSource.Token, index);
    }

    private void ThreadLoop(CancellationToken token, int index)
    {
        ref WorkerData selfData = ref _threadData[index];
        while (!token.IsCancellationRequested)
        {
            _semaphore.Wait();
            //exploit local queue
            while (true)
            {
                StealingResult status = _inputs.TrySteal(out var task);
                if (status == StealingResult.Success)
                {
                    try
                    {
                        task.job.Execute();
                    }
                    catch (Exception e)
                    {
                        task.exception = e;
                    }
                    finally
                    {
                        selfData.outputs.Push(task);
                    }
    
                    continue;
                }
                if (status == StealingResult.Empty)
                {
                    break;
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        _cancellationTokenSource.Cancel();
        _semaphore.Release(_threads.Length);
        foreach (var thread in _threads)
        {
            thread.Join();
        }
        _cancellationTokenSource.Dispose();
        _semaphore.Dispose();
    }
}