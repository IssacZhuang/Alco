using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class RenderTask : ReusableTask
{
    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly CircularBuffer<GPUCommandBuffer> _commandBuffers;
    private GPUCommandBuffer _currentCommandBuffer;

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
        ExecuteCore(commandBuffer);
        commandBuffer.End();
        _currentCommandBuffer = _commandBuffers.Swap();
    }

    protected abstract void ExecuteCore(GPUCommandBuffer commandBuffer);

    public void Submit()
    {
        Wait();
        _renderingSystem.ScheduleCommandBuffer(_currentCommandBuffer);
    }
}
