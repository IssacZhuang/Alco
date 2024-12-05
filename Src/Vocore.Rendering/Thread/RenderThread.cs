using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class RenderThread : AutoDisposable
{
    private struct CommandBufferJob : IJob
    {
        public string callStack;
        public ObjectPool<GPUCommandBuffer> commandBufferPool;
        public GPUCommandBuffer commandBuffer;
        public Exception? exception;
        public SemaphoreSlim semaphore;
        public IRenderJob job;
        public readonly bool IsFinished
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => commandBuffer.HasBuffer;
        }

        public void Execute()
        {
            try
            {
                commandBuffer.Begin();
                job.Execute(commandBuffer);
                commandBuffer.End();
            }
            catch (Exception e)
            {
                exception = e;
            }
            finally
            {
                semaphore.Release();
            }
        }
    }

    private readonly GPUDevice _device;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0);
    private readonly Thread _submitThread;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ObjectPool<GPUCommandBuffer> _commandBufferPool;
    private readonly ThreadWorkerQueue<CommandBufferJob> _workerThreads;
    private readonly List<CommandBufferJob> _commandBufferJobs = new List<CommandBufferJob>();//just for keeping the order of submitted command buffers
    private readonly AtomicSpinLock _lockPush = new AtomicSpinLock();//optimistic lock
    private int _submittedCommandBufferCount;
    private int _finishedCommandBufferCount;

    public event Action<Exception>? OnException;

    /// <summary>
    /// Whether the command thread has finished processing all submitted command buffers.
    /// </summary>
    public bool IsFinished
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return Volatile.Read(ref _submittedCommandBufferCount) == Volatile.Read(ref _finishedCommandBufferCount);
        }
    }


    public RenderThread(GPUDevice device, int threadCount)
    {
        _device = device;
        _cancellationTokenSource = new CancellationTokenSource();
        _submitThread = new Thread(CreateSubmitThread());
        _submitThread.Name = "command_submit_thread";
        _submitThread.Start();

        _workerThreads = new ThreadWorkerQueue<CommandBufferJob>(threadCount, "command_build_thread");

        _commandBufferPool = new ObjectPool<GPUCommandBuffer>(() => _device.CreateCommandBuffer());
    }

    /// <summary>
    /// Submit a command buffer to the command thread.
    /// The <see cref="GPUCommandBuffer.End()"/> and <see cref="GPUCommandBuffer.Submit()"/> methods are called in the thread. Don't call them in the command buffer yourself.
    /// <br/>This method is thread safe but the command buffer is not. So don't do anything with the command buffer untill it is finished.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to submit.</param>
    /// <exception cref="InvalidOperationException">Thrown when the command buffer is not being recorded.</exception>
    public void ScheduleRenderJob(IRenderJob renderJob, [CallerFilePath] string callStack = "")
    {
        try
        {
            GPUCommandBuffer commandBuffer = _commandBufferPool.Get();

            _lockPush.Lock();
            var job = new CommandBufferJob
            {
                commandBufferPool = _commandBufferPool,
                callStack = callStack,
                job = renderJob,
                semaphore = _semaphore,
                commandBuffer = commandBuffer
            };
            _commandBufferJobs.Add(job);
            _workerThreads.Push(job);
        }
        finally
        {
            _lockPush.Unlock();
        }

        Interlocked.Increment(ref _submittedCommandBufferCount);
    }

    /// <summary>
    /// Reset the render thread.
    /// Make sure all command buffers are finished before resetting the render thread.
    /// <br/>This method is not thread safe.
    /// </summary>
    public void Reset()
    {
        if (Volatile.Read(ref _submittedCommandBufferCount) != Volatile.Read(ref _finishedCommandBufferCount))
        {
            throw new InvalidOperationException("Trying to reset the command thread while there are still unfinished command buffers.");
        }

        _lockPush.Lock();
        _commandBufferJobs.Clear();
        _lockPush.Unlock();

        Interlocked.Exchange(ref _submittedCommandBufferCount, 0);
        Interlocked.Exchange(ref _finishedCommandBufferCount, 0);
    }

    /// <summary>
    /// Wait for the render thread to finish.
    /// <br/>This method is not thread safe.
    /// </summary>
    public void WaitForFinish()
    {
        while (!IsFinished)
        {
            Thread.Yield();
        }

        for (int i = 0; i < _commandBufferJobs.Count; i++)
        {
            Exception? exception = _commandBufferJobs[i].exception;
            GPUCommandBuffer commandBuffer = _commandBufferJobs[i].commandBuffer;
            if (exception != null)
            {
                if (OnException != null)
                {
                    OnException(exception);
                }
                else
                {
                    Log.Error(exception, "Error in command thread.");
                }
                //the command buffer is broken, dispose it
                commandBuffer.Dispose();
            }
            else
            {
                _commandBufferPool.Return(commandBuffer);

            }
        }
    }


    private ThreadStart CreateSubmitThread()
    {
        return () => SubmitThreadLoop(_cancellationTokenSource.Token);
    }

    private void SubmitThreadLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            _semaphore.Wait();
            while (true)
            {
                if (_finishedCommandBufferCount >= _commandBufferJobs.Count)
                {
                    break;
                }

                _lockPush.Lock();
                CommandBufferJob job = _commandBufferJobs[_finishedCommandBufferCount];
                _lockPush.Unlock();

                if (!job.IsFinished)
                {
                    break;
                }

                try
                {
                    _device.Submit(job.commandBuffer!);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error submitting command buffer.");
                    // Consider retry logic or additional error handling here
                }

                Interlocked.Increment(ref _finishedCommandBufferCount);
            }
        }
    }



    protected override void Dispose(bool disposing)
    {
        WaitForFinish();

        _cancellationTokenSource.Cancel();
        _semaphore.Release();
        _submitThread.Join();
        _cancellationTokenSource.Dispose();
        _semaphore.Dispose();
    }
}
