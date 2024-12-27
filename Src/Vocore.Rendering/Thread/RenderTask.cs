using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class RenderTask : ReusableTask
{
    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly CircularBuffer<GPUCommandBuffer> _commandBuffers;
    private GPUCommandBuffer _currentCommandBuffer;
    private GPUFrameBuffer? _renderTarget;

    public RenderTask(RenderingSystem renderingSystem, int pooledCommandBuffers = 1)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        if (pooledCommandBuffers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pooledCommandBuffers), "Must be greater than 0");
        }

        _commandBuffers = new CircularBuffer<GPUCommandBuffer>();
        for (int i = 0; i < pooledCommandBuffers; i++)
        {
            _commandBuffers.Add(_device.CreateCommandBuffer());
        }
        _currentCommandBuffer = _commandBuffers.Current;
    }

    protected override void ExecuteCore()
    {
        GPUCommandBuffer commandBuffer = _currentCommandBuffer;
        commandBuffer.Begin();
        commandBuffer.SetFrameBuffer(_renderTarget!);
        ExecuteCore(commandBuffer, _renderTarget!);
        commandBuffer.End();
        _currentCommandBuffer = _commandBuffers.Swap();
    }

    /// <summary>
    /// Executes the task on a thread pool.
    /// <br/>Note: The render target is already set in the command buffer, there is no need to set it again.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to use for the task.</param>
    /// <param name="renderTarget">The render target to use for the task.</param>
    protected abstract void ExecuteCore(GPUCommandBuffer commandBuffer, GPUFrameBuffer renderTarget);

    public void Run(GPUFrameBuffer renderTarget)
    {
        _renderTarget = renderTarget ?? throw new ArgumentNullException(nameof(renderTarget));
        RunCore();
    }

    public void Submit()
    {
        Wait();
        _renderingSystem.ScheduleCommandBuffer(_currentCommandBuffer);
    }
}
