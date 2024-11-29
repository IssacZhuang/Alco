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
    private uint _submittedCommandBufferCount;
    private uint _finishedCommandBufferCount;

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

    public void SubmitCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        if (!commandBuffer.IsRecording)
        {
            throw new InvalidOperationException("Trying to submit a command buffer that is not being recorded.");
        }
        commandBuffer.End();
        _commandBuffers.Push(new CommandBufferContext { commandBuffer = commandBuffer });
        Interlocked.Increment(ref _submittedCommandBufferCount);
        _semaphore.Release();
    }

    public void Reset()
    {
        _commandBuffers.Clear();
        Interlocked.Exchange(ref _submittedCommandBufferCount, 0);
        Interlocked.Exchange(ref _finishedCommandBufferCount, 0);
    }

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
