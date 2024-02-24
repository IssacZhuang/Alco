using Vortice.Vulkan;

namespace Vocore.Graphics.Vulkan;


internal abstract class VulkanFramebufferBase : GPUFrameBuffer
{
    public abstract VkRenderPassBeginInfo PassBegineInfo { get; }
}