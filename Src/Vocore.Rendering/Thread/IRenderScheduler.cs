using Vocore.Graphics;

namespace Vocore.Rendering;

public interface IRenderScheduler
{
    /// <summary>
    /// Schedule a command buffer to be executed.
    /// <br/> Thread safe.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to be executed.</param>
    void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer);

    /// <summary>
    /// Schedule a render job to be executed.
    /// <br/> Thread safe.
    /// </summary>
    /// <param name="job">The render job to be executed.</param>
    void ScheduleRenderJob(IRenderJob job);
}