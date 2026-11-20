// Vulkan command, barrier, rendering, query, sync, and swapchain structs.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vulkan;

internal unsafe partial struct VkCommandPoolCreateInfo
{
    public VkStructureType sType = VkStructureType.CommandPoolCreateInfo;
    public void* pNext;
    public VkCommandPoolCreateFlags flags;
    public uint queueFamilyIndex;
    public VkCommandPoolCreateInfo()
    {
    }
}

internal unsafe partial struct VkCommandBufferAllocateInfo
{
    public VkStructureType sType = VkStructureType.CommandBufferAllocateInfo;
    public void* pNext;
    public VkCommandPool commandPool;
    public VkCommandBufferLevel level;
    public uint commandBufferCount;
    public VkCommandBufferAllocateInfo()
    {
    }
}

internal unsafe partial struct VkCommandBufferInheritanceInfo
{
    public VkStructureType sType = VkStructureType.CommandBufferInheritanceInfo;
    public void* pNext;
    public VkRenderPass renderPass;
    public uint subpass;
    public VkFramebuffer framebuffer;
    public VkBool32 occlusionQueryEnable;
    public VkQueryControlFlags queryFlags;
    public VkQueryPipelineStatisticFlags pipelineStatistics;
    public VkCommandBufferInheritanceInfo()
    {
    }
}

internal unsafe partial struct VkCommandBufferBeginInfo
{
    public VkStructureType sType = VkStructureType.CommandBufferBeginInfo;
    public void* pNext;
    public VkCommandBufferUsageFlags flags;
    public VkCommandBufferInheritanceInfo* pInheritanceInfo;
    public VkCommandBufferBeginInfo()
    {
    }
}

internal unsafe partial struct VkCommandBufferSubmitInfo
{
    public VkStructureType sType = VkStructureType.CommandBufferSubmitInfo;
    public void* pNext;
    public VkCommandBuffer commandBuffer;
    public uint deviceMask;
    public VkCommandBufferSubmitInfo()
    {
    }
}

internal unsafe partial struct VkSemaphoreCreateInfo
{
    public VkStructureType sType = VkStructureType.SemaphoreCreateInfo;
    public void* pNext;
    public VkSemaphoreCreateFlags flags;
    public VkSemaphoreCreateInfo()
    {
    }
}

internal unsafe partial struct VkSemaphoreTypeCreateInfo
{
    public VkStructureType sType = VkStructureType.SemaphoreTypeCreateInfo;
    public void* pNext;
    public VkSemaphoreType semaphoreType;
    public ulong initialValue;
    public VkSemaphoreTypeCreateInfo()
    {
    }
}

internal unsafe partial struct VkSemaphoreSubmitInfo
{
    public VkStructureType sType = VkStructureType.SemaphoreSubmitInfo;
    public void* pNext;
    public VkSemaphore semaphore;
    public ulong value;
    public VkPipelineStageFlags2 stageMask;
    public uint deviceIndex;
    public VkSemaphoreSubmitInfo()
    {
    }
}

internal unsafe partial struct VkSemaphoreWaitInfo
{
    public VkStructureType sType = VkStructureType.SemaphoreWaitInfo;
    public void* pNext;
    public VkSemaphoreWaitFlags flags;
    public uint semaphoreCount;
    public VkSemaphore* pSemaphores;
    public ulong* pValues;
    public VkSemaphoreWaitInfo()
    {
    }
}

internal unsafe partial struct VkSubmitInfo2
{
    public VkStructureType sType = VkStructureType.SubmitInfo2;
    public void* pNext;
    public VkSubmitFlags flags;
    public uint waitSemaphoreInfoCount;
    public VkSemaphoreSubmitInfo* pWaitSemaphoreInfos;
    public uint commandBufferInfoCount;
    public VkCommandBufferSubmitInfo* pCommandBufferInfos;
    public uint signalSemaphoreInfoCount;
    public VkSemaphoreSubmitInfo* pSignalSemaphoreInfos;
    public VkSubmitInfo2()
    {
    }
}

internal unsafe partial struct VkFenceCreateInfo
{
    public VkStructureType sType = VkStructureType.FenceCreateInfo;
    public void* pNext;
    public VkFenceCreateFlags flags;
    public VkFenceCreateInfo()
    {
    }
}

internal unsafe partial struct VkQueryPoolCreateInfo
{
    public VkStructureType sType = VkStructureType.QueryPoolCreateInfo;
    public void* pNext;
    public VkQueryPoolCreateFlags flags;
    public VkQueryType queryType;
    public uint queryCount;
    public VkQueryPipelineStatisticFlags pipelineStatistics;
    public VkQueryPoolCreateInfo()
    {
    }
}

[StructLayout(LayoutKind.Explicit)]
internal unsafe partial struct VkClearColorValue
{
    [FieldOffset(0)] public fixed float float32[4];
    [FieldOffset(0)] public fixed int int32[4];
    [FieldOffset(0)] public fixed uint uint32[4];
}

internal readonly partial struct VkClearDepthStencilValue
{
    public readonly float depth;
    public readonly uint stencil;

    public VkClearDepthStencilValue(float depth, uint stencil) { this.depth = depth; this.stencil = stencil; }
}

[StructLayout(LayoutKind.Explicit)]
internal partial struct VkClearValue
{
    [FieldOffset(0)]
    public VkClearColorValue color;
    [FieldOffset(0)]
    public VkClearDepthStencilValue depthStencil;
}

internal unsafe partial struct VkRenderingAttachmentInfo
{
    public VkStructureType sType = VkStructureType.RenderingAttachmentInfo;
    public void* pNext;
    public VkImageView imageView;
    public VkImageLayout imageLayout;
    public VkResolveModeFlags resolveMode;
    public VkImageView resolveImageView;
    public VkImageLayout resolveImageLayout;
    public VkAttachmentLoadOp loadOp;
    public VkAttachmentStoreOp storeOp;
    public VkClearValue clearValue;
    public VkRenderingAttachmentInfo()
    {
    }
}

internal unsafe partial struct VkRenderingInfo
{
    public VkStructureType sType = VkStructureType.RenderingInfo;
    public void* pNext;
    public VkRenderingFlags flags;
    public VkRect2D renderArea;
    public uint layerCount;
    public uint viewMask;
    public uint colorAttachmentCount;
    public VkRenderingAttachmentInfo* pColorAttachments;
    public VkRenderingAttachmentInfo* pDepthAttachment;
    public VkRenderingAttachmentInfo* pStencilAttachment;
    public VkRenderingInfo()
    {
    }
}

internal unsafe partial struct VkMemoryBarrier2
{
    public VkStructureType sType = VkStructureType.MemoryBarrier2;
    public void* pNext;
    public VkPipelineStageFlags2 srcStageMask;
    public VkAccessFlags2 srcAccessMask;
    public VkPipelineStageFlags2 dstStageMask;
    public VkAccessFlags2 dstAccessMask;
    public VkMemoryBarrier2()
    {
    }
}

internal unsafe partial struct VkBufferMemoryBarrier2
{
    public VkStructureType sType = VkStructureType.BufferMemoryBarrier2;
    public void* pNext;
    public VkPipelineStageFlags2 srcStageMask;
    public VkAccessFlags2 srcAccessMask;
    public VkPipelineStageFlags2 dstStageMask;
    public VkAccessFlags2 dstAccessMask;
    public uint srcQueueFamilyIndex;
    public uint dstQueueFamilyIndex;
    public VkBuffer buffer;
    public ulong offset;
    public ulong size;
    public VkBufferMemoryBarrier2()
    {
    }
}

internal unsafe partial struct VkImageMemoryBarrier2
{
    public VkStructureType sType = VkStructureType.ImageMemoryBarrier2;
    public void* pNext;
    public VkPipelineStageFlags2 srcStageMask;
    public VkAccessFlags2 srcAccessMask;
    public VkPipelineStageFlags2 dstStageMask;
    public VkAccessFlags2 dstAccessMask;
    public VkImageLayout oldLayout;
    public VkImageLayout newLayout;
    public uint srcQueueFamilyIndex;
    public uint dstQueueFamilyIndex;
    public VkImage image;
    public VkImageSubresourceRange subresourceRange;
    public VkImageMemoryBarrier2()
    {
    }
}

internal unsafe partial struct VkDependencyInfo
{
    public VkStructureType sType = VkStructureType.DependencyInfo;
    public void* pNext;
    public VkDependencyFlags dependencyFlags;
    public uint memoryBarrierCount;
    public VkMemoryBarrier2* pMemoryBarriers;
    public uint bufferMemoryBarrierCount;
    public VkBufferMemoryBarrier2* pBufferMemoryBarriers;
    public uint imageMemoryBarrierCount;
    public VkImageMemoryBarrier2* pImageMemoryBarriers;
    public VkDependencyInfo()
    {
    }
}

internal partial struct VkImageSubresourceLayers
{
    public VkImageAspectFlags aspectMask;
    public uint mipLevel;
    public uint baseArrayLayer;
    public uint layerCount;
}

internal unsafe partial struct VkBufferCopy2
{
    public VkStructureType sType = VkStructureType.BufferCopy2;
    public void* pNext;
    public ulong srcOffset;
    public ulong dstOffset;
    public ulong size;
    public VkBufferCopy2()
    {
    }
}

internal unsafe partial struct VkCopyBufferInfo2
{
    public VkStructureType sType = VkStructureType.CopyBufferInfo2;
    public void* pNext;
    public VkBuffer srcBuffer;
    public VkBuffer dstBuffer;
    public uint regionCount;
    public VkBufferCopy2* pRegions;
    public VkCopyBufferInfo2()
    {
    }
}

internal unsafe partial struct VkBufferImageCopy2
{
    public VkStructureType sType = VkStructureType.BufferImageCopy2;
    public void* pNext;
    public ulong bufferOffset;
    public uint bufferRowLength;
    public uint bufferImageHeight;
    public VkImageSubresourceLayers imageSubresource;
    public VkOffset3D imageOffset;
    public VkExtent3D imageExtent;
    public VkBufferImageCopy2()
    {
    }
}

internal unsafe partial struct VkCopyBufferToImageInfo2
{
    public VkStructureType sType = VkStructureType.CopyBufferToImageInfo2;
    public void* pNext;
    public VkBuffer srcBuffer;
    public VkImage dstImage;
    public VkImageLayout dstImageLayout;
    public uint regionCount;
    public VkBufferImageCopy2* pRegions;
    public VkCopyBufferToImageInfo2()
    {
    }
}

internal unsafe partial struct VkCopyImageToBufferInfo2
{
    public VkStructureType sType = VkStructureType.CopyImageToBufferInfo2;
    public void* pNext;
    public VkImage srcImage;
    public VkImageLayout srcImageLayout;
    public VkBuffer dstBuffer;
    public uint regionCount;
    public VkBufferImageCopy2* pRegions;
    public VkCopyImageToBufferInfo2()
    {
    }
}

internal unsafe partial struct VkImageCopy2
{
    public VkStructureType sType = VkStructureType.ImageCopy2;
    public void* pNext;
    public VkImageSubresourceLayers srcSubresource;
    public VkOffset3D srcOffset;
    public VkImageSubresourceLayers dstSubresource;
    public VkOffset3D dstOffset;
    public VkExtent3D extent;
    public VkImageCopy2()
    {
    }
}

internal unsafe partial struct VkCopyImageInfo2
{
    public VkStructureType sType = VkStructureType.CopyImageInfo2;
    public void* pNext;
    public VkImage srcImage;
    public VkImageLayout srcImageLayout;
    public VkImage dstImage;
    public VkImageLayout dstImageLayout;
    public uint regionCount;
    public VkImageCopy2* pRegions;
    public VkCopyImageInfo2()
    {
    }
}

internal unsafe partial struct VkDeviceMemoryCopyKHR
{
    public VkStructureType sType = VkStructureType.DeviceMemoryCopyKHR;
    public void* pNext;
    public VkDeviceAddressRangeKHR srcRange;
    public VkAddressCommandFlagsKHR srcFlags;
    public VkDeviceAddressRangeKHR dstRange;
    public VkAddressCommandFlagsKHR dstFlags;
    public VkDeviceMemoryCopyKHR()
    {
    }
}

internal unsafe partial struct VkCopyDeviceMemoryInfoKHR
{
    public VkStructureType sType = VkStructureType.CopyDeviceMemoryInfoKHR;
    public void* pNext;
    public uint regionCount;
    public VkDeviceMemoryCopyKHR* pRegions;
    public VkCopyDeviceMemoryInfoKHR()
    {
    }
}

internal unsafe partial struct VkDeviceMemoryImageCopyKHR
{
    public VkStructureType sType = VkStructureType.DeviceMemoryImageCopyKHR;
    public void* pNext;
    public VkDeviceAddressRangeKHR addressRange;
    public VkAddressCommandFlagsKHR addressFlags;
    public uint addressRowLength;
    public uint addressImageHeight;
    public VkImageSubresourceLayers imageSubresource;
    public VkImageLayout imageLayout;
    public VkOffset3D imageOffset;
    public VkExtent3D imageExtent;
    public VkDeviceMemoryImageCopyKHR()
    {
    }
}

internal unsafe partial struct VkCopyDeviceMemoryImageInfoKHR
{
    public VkStructureType sType = VkStructureType.CopyDeviceMemoryImageInfoKHR;
    public void* pNext;
    public VkImage image;
    public uint regionCount;
    public VkDeviceMemoryImageCopyKHR* pRegions;
    public VkCopyDeviceMemoryImageInfoKHR()
    {
    }
}

internal unsafe partial struct VkPushDataInfoEXT
{
    public VkStructureType sType = VkStructureType.PushDataInfoEXT;
    public void* pNext;
    public uint offset;
    public VkHostAddressRangeConstEXT data;
    public VkPushDataInfoEXT()
    {
    }
}

internal unsafe partial struct VkBindIndexBuffer3InfoKHR
{
    public VkStructureType sType = VkStructureType.BindIndexBuffer3InfoKHR;
    public void* pNext;
    public VkDeviceAddressRangeKHR addressRange;
    public VkAddressCommandFlagsKHR addressFlags;
    public VkIndexType indexType;
    public VkBindIndexBuffer3InfoKHR()
    {
    }
}

internal partial struct VkDrawIndirectCommand
{
    public uint vertexCount;
    public uint instanceCount;
    public uint firstVertex;
    public uint firstInstance;
}

internal partial struct VkDrawIndexedIndirectCommand
{
    public uint indexCount;
    public uint instanceCount;
    public uint firstIndex;
    public int vertexOffset;
    public uint firstInstance;
}

internal partial struct VkDrawMeshTasksIndirectCommandEXT
{
    public uint groupCountX;
    public uint groupCountY;
    public uint groupCountZ;
}

internal unsafe partial struct VkDrawIndirect2InfoKHR
{
    public VkStructureType sType = VkStructureType.DrawIndirect2InfoKHR;
    public void* pNext;
    public VkStridedDeviceAddressRangeKHR addressRange;
    public VkAddressCommandFlagsKHR addressFlags;
    public uint drawCount;
    public VkDrawIndirect2InfoKHR()
    {
    }
}

internal unsafe partial struct VkDispatchIndirect2InfoKHR
{
    public VkStructureType sType = VkStructureType.DispatchIndirect2InfoKHR;
    public void* pNext;
    public VkDeviceAddressRangeKHR addressRange;
    public VkAddressCommandFlagsKHR addressFlags;
    public VkDispatchIndirect2InfoKHR()
    {
    }
}

internal unsafe partial struct VkWin32SurfaceCreateInfoKHR
{
    public VkStructureType sType = VkStructureType.Win32SurfaceCreateInfoKHR;
    public void* pNext;
    public VkWin32SurfaceCreateFlagsKHR flags;
    public nint hinstance;
    public nint hwnd;
    public VkWin32SurfaceCreateInfoKHR()
    {
    }
}

internal unsafe partial struct VkXlibSurfaceCreateInfoKHR
{
    public VkStructureType sType = VkStructureType.XlibSurfaceCreateInfoKHR;
    public void* pNext;
    public VkXlibSurfaceCreateFlagsKHR flags;
    public nint dpy;
    public ulong window;
    public VkXlibSurfaceCreateInfoKHR()
    {
    }
}

internal unsafe partial struct VkXcbSurfaceCreateInfoKHR
{
    public VkStructureType sType = VkStructureType.XcbSurfaceCreateInfoKHR;
    public void* pNext;
    public VkXcbSurfaceCreateFlagsKHR flags;
    public nint connection;
    public uint window;
    public VkXcbSurfaceCreateInfoKHR()
    {
    }
}

internal unsafe partial struct VkWaylandSurfaceCreateInfoKHR
{
    public VkStructureType sType = VkStructureType.WaylandSurfaceCreateInfoKHR;
    public void* pNext;
    public VkWaylandSurfaceCreateFlagsKHR flags;
    public nint display;
    public nint surface;
    public VkWaylandSurfaceCreateInfoKHR()
    {
    }
}

internal partial struct VkSurfaceFormatKHR
{
    public VkFormat format;
    public VkColorSpaceKHR colorSpace;
}

internal partial struct VkSurfaceCapabilitiesKHR
{
    public uint minImageCount;
    public uint maxImageCount;
    public VkExtent2D currentExtent;
    public VkExtent2D minImageExtent;
    public VkExtent2D maxImageExtent;
    public uint maxImageArrayLayers;
    public VkSurfaceTransformFlagsKHR supportedTransforms;
    public VkSurfaceTransformFlagsKHR currentTransform;
    public VkCompositeAlphaFlagsKHR supportedCompositeAlpha;
    public VkImageUsageFlags supportedUsageFlags;
}

internal unsafe partial struct VkSurfacePresentModeKHR
{
    public VkStructureType sType = VkStructureType.SurfacePresentModeKHR;
    public void* pNext;
    public VkPresentModeKHR presentMode;
    public VkSurfacePresentModeKHR()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceSurfaceInfo2KHR
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceSurfaceInfo2KHR;
    public void* pNext;
    public VkSurfaceKHR surface;
    public VkPhysicalDeviceSurfaceInfo2KHR()
    {
    }
}

internal unsafe partial struct VkSurfaceCapabilities2KHR
{
    public VkStructureType sType = VkStructureType.SurfaceCapabilities2KHR;
    public void* pNext;
    public VkSurfaceCapabilitiesKHR surfaceCapabilities;
    public VkSurfaceCapabilities2KHR()
    {
    }
}

internal unsafe partial struct VkSwapchainCreateInfoKHR
{
    public VkStructureType sType = VkStructureType.SwapchainCreateInfoKHR;
    public void* pNext;
    public VkSwapchainCreateFlagsKHR flags;
    public VkSurfaceKHR surface;
    public uint minImageCount;
    public VkFormat imageFormat;
    public VkColorSpaceKHR imageColorSpace;
    public VkExtent2D imageExtent;
    public uint imageArrayLayers;
    public VkImageUsageFlags imageUsage;
    public VkSharingMode imageSharingMode;
    public uint queueFamilyIndexCount;
    public uint* pQueueFamilyIndices;
    public VkSurfaceTransformFlagsKHR preTransform;
    public VkCompositeAlphaFlagsKHR compositeAlpha;
    public VkPresentModeKHR presentMode;
    public VkBool32 clipped;
    public VkSwapchainKHR oldSwapchain;
    public VkSwapchainCreateInfoKHR()
    {
    }
}

internal unsafe partial struct VkSwapchainPresentModesCreateInfoKHR
{
    public VkStructureType sType = VkStructureType.SwapchainPresentModesCreateInfoKHR;
    public void* pNext;
    public uint presentModeCount;
    public VkPresentModeKHR* pPresentModes;
    public VkSwapchainPresentModesCreateInfoKHR()
    {
    }
}

internal unsafe partial struct VkSwapchainPresentFenceInfoKHR
{
    public VkStructureType sType = VkStructureType.SwapchainPresentFenceInfoKHR;
    public void* pNext;
    public uint swapchainCount;
    public VkFence* pFences;
    public VkSwapchainPresentFenceInfoKHR()
    {
    }
}

internal unsafe partial struct VkPresentInfoKHR
{
    public VkStructureType sType = VkStructureType.PresentInfoKHR;
    public void* pNext;
    public uint waitSemaphoreCount;
    public VkSemaphore* pWaitSemaphores;
    public uint swapchainCount;
    public VkSwapchainKHR* pSwapchains;
    public uint* pImageIndices;
    public VkResult* pResults;
    public VkPresentInfoKHR()
    {
    }
}

internal unsafe partial struct VkReleaseSwapchainImagesInfoKHR
{
    public VkStructureType sType = VkStructureType.ReleaseSwapchainImagesInfoKHR;
    public void* pNext;
    public VkSwapchainKHR swapchain;
    public uint imageIndexCount;
    public uint* pImageIndices;
    public VkReleaseSwapchainImagesInfoKHR()
    {
    }
}
