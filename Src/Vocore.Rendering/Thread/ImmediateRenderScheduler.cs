using Vocore.Graphics;

namespace Vocore.Rendering;

public class ImmediateRenderScheduler : IRenderScheduler
{
    private readonly GPUDevice _device;
    private readonly ConcurrentPool<GPUCommandBuffer> _commandBufferPool;

    public ImmediateRenderScheduler(GPUDevice device)
    {
        _device = device;
        _commandBufferPool = new ConcurrentPool<GPUCommandBuffer>(() => device.CreateCommandBuffer());
    }

    public void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        _device.Submit(commandBuffer);
    }

    public void ScheduleRenderJob(IRenderJob job)
    {
        GPUCommandBuffer commandBuffer = _commandBufferPool.Get();
        commandBuffer.Begin();
        job.Execute(commandBuffer);
        commandBuffer.End();
        _device.Submit(commandBuffer);
        _commandBufferPool.Return(commandBuffer);
    }
}