using Vortice.Vulkan;

namespace Alco.Graphics.Vulkan;

public abstract class VulkanTextureBase: GPUTexture
{
    public abstract VkImage Native { get; }
}
