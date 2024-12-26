using Vocore.Graphics;

namespace Vocore.Rendering;


public class DefferedRenderScheduler : IRenderScheduler
{
    private readonly GPUDevice _device;
    private readonly List<GPUCommandBuffer> _commandBuffers = new();

    public DefferedRenderScheduler(GPUDevice device)
    {
        _device = device;
    }

    public void Dispose()
    {
        _commandBuffers.Clear();
    }

    public void OnEndFrame()
    {
        for (int i = 0; i < _commandBuffers.Count; i++)
        {
            _device.Submit(_commandBuffers[i]);
        }
        _commandBuffers.Clear();
    }

    public void OnBeginFrame()
    {
        
    }

    public void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        _commandBuffers.Add(commandBuffer);
    }
}