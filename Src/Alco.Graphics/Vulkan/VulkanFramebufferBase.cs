using Vortice.Vulkan;

namespace Alco.Graphics.Vulkan;


internal abstract class VulkanFramebufferBase : GPUFrameBuffer
{
    public abstract VkRenderPassBeginInfo PassBegineInfo { get; }
}