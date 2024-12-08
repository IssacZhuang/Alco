using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class RenderThread : AutoDisposable
{
    private struct CommandBufferJob : IJob
    {
        public Lock @lock;
        public List<CommandBufferJob> parent;
        public int index;

        public GPUCommandBuffer commandBuffer;
        public Exception? exception;
        public SemaphoreSlim semaphore;

        public IRenderJob job;
        public bool isFinished;

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
                //update the value because it is a struct
                isFinished = true;
                using (@lock.EnterScope())
                {
                    parent[index] = this;
                }
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
    private readonly Lock _lockPush = new Lock();
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
    /// Schedule a render job to the render thread.
    /// </summary>
    /// <param name="renderJob">The render job to schedule.</param>
    public void ScheduleRenderJob(IRenderJob renderJob)
    {

        GPUCommandBuffer commandBuffer = _commandBufferPool.Get();
        var job = new CommandBufferJob
        {
            job = renderJob,
            semaphore = _semaphore,
            commandBuffer = commandBuffer,
            parent = _commandBufferJobs,
            isFinished = false,
            @lock = _lockPush,
        };

        using (_lockPush.EnterScope())
        {
            job.index = _commandBufferJobs.Count;
            _commandBufferJobs.Add(job);
            Interlocked.Increment(ref _submittedCommandBufferCount);
            _workerThreads.Push(job);
        }
    }

    /// <summary>
    /// Schedule a command buffer already ended.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to submit.</param>
    public void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        //no need to use thread worker because the command buffer is already ended
        using (_lockPush.EnterScope())
        {
            _commandBufferJobs.Add(new CommandBufferJob
            {
                commandBuffer = commandBuffer,
                isFinished = true,
            });
        }

        Interlocked.Increment(ref _submittedCommandBufferCount);
        //Interlocked.Increment(ref _finishedCommandBufferCount);
        _semaphore.Release();
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

        using (_lockPush.EnterScope())
        {
            _commandBufferJobs.Clear();
        }

        Interlocked.Exchange(ref _submittedCommandBufferCount, 0);
        Interlocked.Exchange(ref _finishedCommandBufferCount, 0);
    }

    /// <summary>
    /// Wait for the render thread to finish.
    /// <br/>This method is not thread safe.
    /// </summary>
    public void WaitForFinish()
    {
        int handleIndex = 0;
        while (handleIndex < Volatile.Read(ref _submittedCommandBufferCount))
        {
            if (handleIndex >= Volatile.Read(ref _finishedCommandBufferCount))
            {
                Thread.Yield();
            }
            else
            {
                Exception? exception = _commandBufferJobs[handleIndex].exception;
                GPUCommandBuffer commandBuffer = _commandBufferJobs[handleIndex].commandBuffer;
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
                    // Dispose broken command buffer
                    commandBuffer.Dispose();
                }
                else
                {
                    _commandBufferPool.Return(commandBuffer);
                }

                handleIndex++;
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
                if (_finishedCommandBufferCount >= _submittedCommandBufferCount)
                {
                    break;
                }

                CommandBufferJob job;
                int index;
                using (_lockPush.EnterScope())
                {
                    index = _finishedCommandBufferCount;
                    job = _commandBufferJobs[index];
                }

                if (!job.isFinished)
                {
                    break;
                }

                try
                {
                    _device.Submit(job.commandBuffer!);
                }
                catch (Exception e)
                {
                    job.exception = e;
                    _commandBufferJobs[index] = job;
                }

                Interlocked.Increment(ref _finishedCommandBufferCount);
            }
        }
    }



    protected override void Dispose(bool disposing)
    {
        WaitForFinish();

        _workerThreads.Dispose();

        _cancellationTokenSource.Cancel();
        _semaphore.Release();
        _submitThread.Join();
        _cancellationTokenSource.Dispose();
        _semaphore.Dispose();
    }
}
