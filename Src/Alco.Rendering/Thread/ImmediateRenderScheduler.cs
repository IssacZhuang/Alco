using Alco.Graphics;

namespace Alco.Rendering;

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
            Log.Error($"Error in command buffer: {e.Message}, callstakc: {Environment.StackTrace}");
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