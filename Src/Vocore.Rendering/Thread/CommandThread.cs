using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class CommandThread : AutoDisposable
{
    //the circular work stealing deque is struct only, so we need to wrap the command buffer in a struct to avoid boxing    
    private struct CommandBufferContext
    {
        public GPUCommandBuffer commandBuffer;
    }

    private readonly GPUDevice _device;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0);
    private readonly Thread _submitThread;
    private readonly CancellationTokenSource _cancellationTokenSource;
    //the lock free deque
    private readonly CircularWorkStealingDeque<CommandBufferContext> _commandBuffers = new CircularWorkStealingDeque<CommandBufferContext>(64);
    //the push operation of circular work stealing deque is not thread safe, so we need a lock to protect it
    private AtomicSpinLock _lockPush = new AtomicSpinLock();
    private uint _submittedCommandBufferCount;
    private uint _finishedCommandBufferCount;

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


    public CommandThread(GPUDevice device)
    {
        _device = device;
        _cancellationTokenSource = new CancellationTokenSource();
        _submitThread = new Thread(CreateSubmitThread());
        _submitThread.Name = "command_submit_thread";
        _submitThread.Start();
    }

    /// <summary>
    /// Submit a command buffer to the command thread.
    /// <br/>This method is thread safe.
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
        _commandBuffers.Push(new CommandBufferContext { commandBuffer = commandBuffer });
        _lockPush.Unlock();
    
        Interlocked.Increment(ref _submittedCommandBufferCount);
        _semaphore.Release();
    }

    /// <summary>
    /// Reset the command thread.
    /// <br/>This method is not thread safe.
    /// </summary>
    public void Reset()
    {
        _commandBuffers.Clear();
        Interlocked.Exchange(ref _submittedCommandBufferCount, 0);
        Interlocked.Exchange(ref _finishedCommandBufferCount, 0);
    }

    /// <summary>
    /// Wait for the command thread to finish.
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
            ProcessCommandBuffers();
        }
    }

    private void ProcessCommandBuffers()
    {
        StealingResult result;
        // Process all available command buffers
        while ((result = _commandBuffers.TrySteal(out var context)) != StealingResult.Empty)
        {
            if (result == StealingResult.Success)
            {
                try
                {
                    GPUCommandBuffer commandBuffer = context.commandBuffer;
                    commandBuffer.End();
                    _device.Submit(commandBuffer);
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
        _cancellationTokenSource.Cancel();
        _semaphore.Release();
        _submitThread.Join();
        _cancellationTokenSource.Dispose();
        _semaphore.Dispose();
    }
}
