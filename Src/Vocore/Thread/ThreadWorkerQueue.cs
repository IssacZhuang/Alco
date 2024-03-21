using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Vocore;


public class ThreadWorkerQueue<TJob> : IDisposable where TJob : IJob
{
    private struct Task
    {
        public TJob job;
        public Exception? exception;
    }

    private struct WorkerData
    {
        public int index;
        public bool isRunning;
        public CircularWorkStealingDeque<Task> outputs;
    }

    private readonly WorkerData[] _threadData;
    private readonly Thread[] _threads;
    private readonly ManualResetEvent _event;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CircularWorkStealingDeque<Task> _inputs;
    private bool _isDisposed;
    private int _ownerThreadId;
    private int _count;


    public ThreadWorkerQueue(int threadCount, string threadPrefix = "JobThread")
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _event = new ManualResetEvent(false);
        _inputs = new CircularWorkStealingDeque<Task>(512);
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _count = 0;

        _threadData = new WorkerData[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            _threadData[i] = new WorkerData()
            {
                index = i,
                isRunning = false,
                outputs = new CircularWorkStealingDeque<Task>(256)
            };
        }

        _threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            Thread thread = new Thread(ThreadWorker(i));
            thread.Name = $"{threadPrefix} {i}";
            _threads[i] = thread;
            thread.Start();
        }
    }

    /// <summary>
    /// Set the owner thread id.
    /// </summary>
    /// <param name="id">The owner thread id.</param>
    public void SetOnwerThreadId(int id)
    {
        _ownerThreadId = id;
    }

    /// <summary>
    /// Push a job to the queue.
    /// <br/> This method can only be called by the owner thread.
    /// </summary>
    /// <param name="job">The job to do.</param>
    /// <param name="startImmediately">Start the worker immediately.</param>
    public void Push(TJob job)
    {
        CheckThread();
        if (job == null)
        {
            return;
        }
        _inputs.Push(new Task() { job = job });
        _event.Set();
        _count++;
    }


    /// <summary>
    /// Try to get a finished task.
    /// <br/> This method can only be called by the owner thread.
    /// </summary>
    /// <param name="job">The finished job.</param>
    /// <param name="exception">The exception if the job failed.</param>
    /// <returns><see cref="StealingResult.Success"/> if a finished task is found, <see cref="StealingResult.Empty"/> if no finished task is found, <see cref="StealingResult.CASFailed"/> if the queue is interrupted.</returns>
    public StealingResult TryGetFinishedTask([NotNullWhen(true)] out TJob? job, out Exception? exception)
    {
        CheckThread();
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
    /// <br/> This method can only be called by the owner thread.
    /// </summary>
    /// <returns>An enumerable of the finished tasks.</returns>
    public IEnumerable<JobExcuteResult<TJob>> WaitForAllCompleted()
    {
        CheckThread();
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("This method can only be called by the owner thread.");
        }
    }

    private ThreadStart ThreadWorker(int index){
        return () => ThreadLoop(_cancellationTokenSource.Token, index);
    }

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
                StealingResult status = _inputs.TrySteal(out var task);
                if (status == StealingResult.Success)
                {
                    try
                    {
                        task.job.Execute();
                    }
                    catch (Exception e)
                    {
                        //Log.Error(e);
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
            _event.Reset();
            Volatile.Write(ref selfData.isRunning, false);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        _event.Set();
        foreach (var thread in _threads)
        {
            thread.Join();
        }
        _cancellationTokenSource.Dispose();
        _event.Dispose();

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}