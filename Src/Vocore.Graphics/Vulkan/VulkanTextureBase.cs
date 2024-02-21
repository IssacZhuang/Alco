using Vortice.Vulkan;

namespace Vocore.Graphics.Vulkan;

public abstract class VulkanTextureBase: GPUTexture
{
    public abstract VkImage Native { get; }
}
