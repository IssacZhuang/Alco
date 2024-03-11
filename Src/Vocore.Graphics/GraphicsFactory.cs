using Vocore.Graphics.NoGPU;

#if USE_VULKAN
using Vocore.Graphics.Vulkan;
#endif

#if USE_WEBGPU
using Vocore.Graphics.WebGPU;
#endif

namespace Vocore.Graphics;

public static class GraphicsFactory
{
    /// <summary>
    /// The virtual GPU device that does not support any GPU operations but keep the object not null. Can be used for the development of the game logic without the need for a real GPU.
    /// </summary>
    public static GPUDevice GetNoGPUDevice()
    {
        return new NoDevice();
    }

    public static GPUDevice CreateVulkanDevice(DeviceDescriptor descriptor)
    {
#if USE_VULKAN
        return new VulkanDevice(descriptor);
#else
        throw new PlatformNotSupportedException("Vulkan is not supported");
#endif
    }

    public static GPUDevice CreateWebGPUDevice(DeviceDescriptor descriptor)
    {
#if USE_WEBGPU
        return new WebGPUDevice(descriptor);
#else
        throw new PlatformNotSupportedException("WebGPU is not supported");
#endif
    }
}