using Alco.Graphics.NoGPU;

#if USE_WEBGPU
using Alco.Graphics.WebGPU;
#endif

using Alco.Graphics.Vulkan;

namespace Alco.Graphics;

public static class GraphicsDeviceFactory
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
        return new VulkanDevice(descriptor);
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
