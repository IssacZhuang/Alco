using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Vocore;

public class ThreadWorkerQueue<TJob> where TJob : IJob
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

    public bool TryGetFinishedTask([NotNullWhen(true)] out TJob? job)
    {
        job = default;
        for (int i = 0; i < _threadData.Length; i++)
        {
            ref WorkerData selfData = ref _threadData[i];

            if (selfData.outputs.TrySteal(out Task task) == StealingResult.Success)
            {
                job = task.job;
                return true;
            }

        }
        return false;
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
}