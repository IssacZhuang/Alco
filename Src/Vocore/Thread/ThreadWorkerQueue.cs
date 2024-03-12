using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Vocore;

public class ThreadWorkerQueue<TJob> : IDisposable where TJob : IJob
{
    private struct Task
    {
        public TJob job;
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


    public ThreadWorkerQueue(int threadCount, string threadPrefix = "JobThread")
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _event = new ManualResetEvent(false);
        _inputs = new CircularWorkStealingDeque<Task>(512);

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
    

    public void Push(TJob job, bool startImmediately = true)
    {
        if (job == null)
        {
            return;
        }
        _inputs.Push(new Task() { job = job });
        if (startImmediately)
        {
            _event.Set();
        }
    }

    public void Start()
    {
        _event.Set();
    }

    public StealingResult TryGetFinishedTask([NotNullWhen(true)] out TJob? job)
    {
        job = default;
        bool hasAbort = false;
        for (int i = 0; i < _threadData.Length; i++)
        {
            ref WorkerData selfData = ref _threadData[i];
            StealingResult result = selfData.outputs.TrySteal(out Task task);

            if (result == StealingResult.Success)
            {
                job = task.job;
                return StealingResult.Success;
            }
            else if (result == StealingResult.Interrupted)
            {
                hasAbort = true;
            }

        }

        if (hasAbort)
        {
            return StealingResult.Interrupted;
        }

        return StealingResult.Empty;
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
                        selfData.outputs.Push(task);
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
    }
}