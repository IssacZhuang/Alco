using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class RenderThread : AutoDisposable
{
    private struct CommandBufferJob : IJob
    {
        public int index;
        public GPUCommandBuffer commandBuffer;
        public SemaphoreSlim semaphore;


        public void Execute()
        {
            try
            {
                commandBuffer.End();
            }
            catch (Exception e)
            {
                Log.Error(e, "Error ending command buffer.");
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
    private readonly ThreadWorkerQueue<CommandBufferJob> _workerThreads;
    private readonly List<CommandBufferJob> _commandBuffers = new List<CommandBufferJob>();//just for keeping the order of submitted command buffers
    private AtomicSpinLock _lockPush = new AtomicSpinLock();
    private int _submittedCommandBufferCount;
    private int _finishedCommandBufferCount;

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

        _workerThreads = new ThreadWorkerQueue<CommandBufferJob>(threadCount);
    }

    /// <summary>
    /// Submit a command buffer to the command thread.
    /// The <see cref="GPUCommandBuffer.End()"/> and <see cref="GPUCommandBuffer.Submit()"/> methods are called in the thread. Don't call them in the command buffer yourself.
    /// <br/>This method is thread safe but the command buffer is not. So don't do anything with the command buffer untill it is finished.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to submit.</param>
    /// <exception cref="InvalidOperationException">Thrown when the command buffer is not being recorded.</exception>
    public void SubmitCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        if (!commandBuffer.IsRecording)
        {
            throw new InvalidOperationException("Trying to submit a command buffer that is not being recorded.");
        }

        _lockPush.Lock();
        var job = new CommandBufferJob
        {
            commandBuffer = commandBuffer,
            semaphore = _semaphore
        };
        _commandBuffers.Add(job);
        _workerThreads.Push(job);
        _lockPush.Unlock();

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
        _commandBuffers.Clear();
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
                _lockPush.Lock();
                CommandBufferJob currentJob = _commandBuffers[_finishedCommandBufferCount];
                _lockPush.Unlock();
                if (currentJob.commandBuffer.IsRecording)
                {
                    break;
                }

                try
                {
                    _device.Submit(currentJob.commandBuffer);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error submitting command buffer.");
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
