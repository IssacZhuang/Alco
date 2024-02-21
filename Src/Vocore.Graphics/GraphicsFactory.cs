using Vocore.Graphics.Vulkan;
using Vocore.Graphics.WebGPU;

namespace Vocore.Graphics;

public static class GraphicsFactory
{
    public static GPUDevice CreateVulkanDevice(DeviceDescriptor descriptor)
    {
        return new VulkanDevice(descriptor);
    }

    public static GPUDevice CreateWebGPUDevice(DeviceDescriptor descriptor)
    {
        return new WebGPUDevice(descriptor);
    }
}