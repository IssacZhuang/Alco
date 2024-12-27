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
        try
        {
            _device.Submit(commandBuffer);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void Dispose()
    {

    }

    public void OnBeginFrame()
    {
       
    }

    public void OnEndFrame()
    {
        
    }
}