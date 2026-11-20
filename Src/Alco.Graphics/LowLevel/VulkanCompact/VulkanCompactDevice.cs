// Vulkan Compact device construction for the fixed Vulkan 1.2 profile.

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class VulkanCompactDevice : VulkanDeviceBase
{
    private VulkanCompactDevice() : base(GraphicsBackend.VulkanCompact) { }

    internal static VulkanCompactDevice Create(DeviceDesc desc)
    {
        VulkanCompactDevice device = new();
        device.Initialize(desc);
        return device;
    }
}
