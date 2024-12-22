using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class RenderTask : ReusableTask
{
    private readonly SwapChain<GPUCommandBuffer> _commandBuffers;
    private GPUCommandBuffer _currentCommandBuffer;

    public RenderTask(GPUDevice device, int pooledCommandBuffers = 1)
    {
        if (pooledCommandBuffers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pooledCommandBuffers), "Must be greater than 0");
        }

        _commandBuffers = new SwapChain<GPUCommandBuffer>();
        for (int i = 0; i < pooledCommandBuffers; i++)
        {
            _commandBuffers.Add(device.CreateCommandBuffer());
        }
        _currentCommandBuffer = _commandBuffers.Current;
    }

    protected override void ExecuteCore()
    {
        GPUCommandBuffer commandBuffer = _currentCommandBuffer;
        ExecuteCore(commandBuffer);
        _currentCommandBuffer = _commandBuffers.Swap();
    }

    protected abstract void ExecuteCore(GPUCommandBuffer commandBuffer);
}
