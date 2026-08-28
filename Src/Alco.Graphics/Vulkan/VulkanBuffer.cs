using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Vortice.Vulkan.Vma;

namespace Alco.Graphics.Vulkan;

internal sealed unsafe class VulkanBuffer : GPUBuffer
{
    public VkBuffer Native;
    public VmaAllocation Allocation;

    /// <summary>Persistent mapping, only valid when <see cref="IsHostVisible"/>.</summary>
    public void* MappedPointer;

    public bool IsHostVisible { get; }

    protected override VulkanDevice Device { get; }

    public VulkanBuffer(VulkanDevice device, in BufferDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        VkBufferUsageFlags usage = VulkanUtility.ConvertBufferUsage(descriptor.Usage);
        if (!descriptor.Usage.HasFlag(BufferUsage.MapWrite) && !descriptor.Usage.HasFlag(BufferUsage.MapRead))
        {
            // Device-side buffers are uploaded through staging copies even when the
            // engine did not declare CopyDst, so the transfer usage is always added.
            usage |= VkBufferUsageFlags.TransferDst | VkBufferUsageFlags.TransferSrc;
        }

        bool wantsHostVisible = descriptor.Usage.HasFlag(BufferUsage.MapRead) || descriptor.Usage.HasFlag(BufferUsage.MapWrite);

        VkBufferCreateInfo bufferInfo = new()
        {
            size = Size,
            usage = usage,
            sharingMode = VkSharingMode.Exclusive,
        };

        VmaAllocationCreateInfo allocInfo = new()
        {
            usage = wantsHostVisible ? VmaMemoryUsage.AutoPreferHost : VmaMemoryUsage.AutoPreferDevice,
            requiredFlags = wantsHostVisible
                ? VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent
                : VkMemoryPropertyFlags.None,
        };

        VmaAllocationInfo outInfo = default;
        VkBuffer nativeBuffer = default;
        VmaAllocation allocation = default;
        VkResult result = vmaCreateBuffer(device.Allocator, &bufferInfo, &allocInfo, &nativeBuffer, &allocation, &outInfo);
        VulkanException.ThrowIfFailed(result, $"Failed to create buffer '{descriptor.Name}'");
        Native = nativeBuffer;
        Allocation = allocation;

        IsHostVisible = wantsHostVisible;
        if (IsHostVisible)
        {
            void* mapped = null;
            result = vmaMapMemory(device.Allocator, Allocation, &mapped);
            VulkanException.ThrowIfFailed(result, $"Failed to map buffer '{descriptor.Name}'");
            MappedPointer = mapped;
        }

        device.SetDebugName(VkObjectType.Buffer, Native.Handle, descriptor.Name);
    }

    protected override void Dispose(bool disposing)
    {
        if (Native.Handle == 0)
        {
            return;
        }

        Device.QueueNativeDestroy(Native, Allocation, IsHostVisible ? MappedPointer : null);
        // drop the tracked state so the device tracker does not keep growing
        Device.Tracker.Remove(this);
        Native = default;
        Allocation = default;
    }
}

/// <summary>Extension checking VkResult at the call site.</summary>
internal static class VulkanResultExtensions
{
    public static void ThrowOnFailure(this VkResult result, string message = "Vulkan call failed")
    {
        if (result != VkResult.Success)
        {
            throw new GraphicsException($"{message} (VkResult: {result})");
        }
    }
}

/// <summary>Thrown when a Vulkan call returns an error result.</summary>
internal static class VulkanException
{
    public static void ThrowIfFailed(VkResult result, string message)
    {
        if (result != VkResult.Success)
        {
            throw new GraphicsException($"{message} (VkResult: {result})");
        }
    }
}
