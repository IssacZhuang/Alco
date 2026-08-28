using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

internal sealed unsafe class VulkanTimestampQuerySet : GPUTimestampQuerySet
{
    public VkQueryPool Native;

    protected override VulkanDevice Device { get; }

    public VulkanTimestampQuerySet(VulkanDevice device, uint count, string name) : base(count, name)
    {
        Device = device;

        VkQueryPoolCreateInfo createInfo = new()
        {
            queryType = VkQueryType.Timestamp,
            queryCount = count,
        };

        VkQueryPool nativePool = default;
        VkResult result = vkCreateQueryPool(device.NativeDevice, &createInfo, null, &nativePool);
        Native = nativePool;
        VulkanException.ThrowIfFailed(result, $"Failed to create timestamp query set '{name}'");

        device.SetDebugName(VkObjectType.QueryPool, Native.Handle, name);
        device.ResetQueryPoolInitial(Native, count);
    }

    protected override void Dispose(bool disposing)
    {
        if (Native.Handle == 0)
        {
            return;
        }

        Device.QueueNativeDestroy(Native);
        Native = default;
    }
}
