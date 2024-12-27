#define TRACR_CALLSTACK

using Vocore.Graphics;

namespace Vocore.Rendering;



public class DefferedRenderScheduler : IRenderScheduler
{
    private readonly GPUDevice _device;
    private readonly List<GPUCommandBuffer> _commandBuffers = new();
#if TRACR_CALLSTACK
    private readonly List<string> _callStacks = new();
#endif

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
            try
            {
                _device.Submit(_commandBuffers[i]);
            }
            catch (Exception e)
            {
                Log.Error($"Error in command buffer: {e.Message}, callstakc: {_callStacks[i]}");
            }
        }
        _commandBuffers.Clear();
    }

    public void OnBeginFrame()
    {
        
    }

    public void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        _commandBuffers.Add(commandBuffer);
#if TRACR_CALLSTACK
        _callStacks.Add(Environment.StackTrace);
#endif
    }
}