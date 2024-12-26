using Vocore.Graphics;

namespace Vocore.Rendering;

public class ImmediateRenderScheduler : IRenderScheduler
{
    private readonly GPUDevice _device;

    public ImmediateRenderScheduler(GPUDevice device)
    {
        _device = device;
    }

    public void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        _device.Submit(commandBuffer);
    }

    public void Dispose()
    {

    }

    public void OnPreSwapBuffers()
    {
       
    }

    public void OnPostSwapBuffers()
    {
        
    }
}