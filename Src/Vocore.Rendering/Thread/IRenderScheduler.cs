using Vocore.Graphics;

namespace Vocore.Rendering;

public interface IRenderScheduler : IDisposable
{
    /// <summary>
    /// Schedule a command buffer to be executed.
    /// <br/> Thread safe.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to be executed.</param>
    void ScheduleCommandBuffer(GPUCommandBuffer commandBuffer);

    void OnPreSwapBuffers();

    void OnPostSwapBuffers();
}