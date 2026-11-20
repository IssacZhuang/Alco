// Startup selection of native core and extension entry points shared by the Vulkan profiles.

using System.Runtime.CompilerServices;
using Vk = Vulkan;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal unsafe struct VulkanNativeDispatch
{
    internal readonly delegate* unmanaged<Vk.VkQueue, uint, Vk.VkSubmitInfo2*, Vk.VkFence, Vk.VkResult> QueueSubmit2;
    internal Vk.PFN_vkVoidFunction CmdPipelineBarrier2Pointer;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkPipelineStageFlags2, Vk.VkQueryPool, uint, void> CmdWriteTimestamp2;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkRenderingInfo*, void> CmdBeginRendering;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, void> CmdEndRendering;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, uint, Vk.VkViewport*, void> CmdSetViewportWithCount;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, uint, Vk.VkRect2D*, void> CmdSetScissorWithCount;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkBool32, void> CmdSetDepthTestEnable;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkBool32, void> CmdSetDepthWriteEnable;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCompareOp, void> CmdSetDepthCompareOp;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkBool32, void> CmdSetStencilTestEnable;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkStencilFaceFlags, Vk.VkStencilOp, Vk.VkStencilOp, Vk.VkStencilOp, Vk.VkCompareOp, void>
        CmdSetStencilOp;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCopyBufferInfo2*, void> CmdCopyBuffer2;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCopyBufferToImageInfo2*, void> CmdCopyBufferToImage2;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCopyImageToBufferInfo2*, void> CmdCopyImageToBuffer2;
    internal readonly delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCopyImageInfo2*, void> CmdCopyImage2;
    internal readonly delegate* unmanaged<Vk.VkDevice, Vk.VkDeviceBufferMemoryRequirements*, Vk.VkMemoryRequirements2*, void>
        GetDeviceBufferMemoryRequirements;
    internal readonly delegate* unmanaged<Vk.VkDevice, Vk.VkDeviceImageMemoryRequirements*, Vk.VkMemoryRequirements2*, void> GetDeviceImageMemoryRequirements;

    internal VulkanNativeDispatch(Vk.VkDeviceApi api, bool compact)
    {
        QueueSubmit2 = (delegate* unmanaged<Vk.VkQueue, uint, Vk.VkSubmitInfo2*, Vk.VkFence, Vk.VkResult>)
            (compact ? api.vkQueueSubmit2KHR_ptr : api.vkQueueSubmit2_ptr).Value;
        CmdPipelineBarrier2Pointer = compact ? api.vkCmdPipelineBarrier2KHR_ptr : api.vkCmdPipelineBarrier2_ptr;
        CmdWriteTimestamp2 = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkPipelineStageFlags2, Vk.VkQueryPool, uint, void>)
            (compact ? api.vkCmdWriteTimestamp2KHR_ptr : api.vkCmdWriteTimestamp2_ptr).Value;
        CmdBeginRendering = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkRenderingInfo*, void>)
            (compact ? api.vkCmdBeginRenderingKHR_ptr : api.vkCmdBeginRendering_ptr).Value;
        CmdEndRendering = (delegate* unmanaged<Vk.VkCommandBuffer, void>)
            (compact ? api.vkCmdEndRenderingKHR_ptr : api.vkCmdEndRendering_ptr).Value;
        CmdSetViewportWithCount = (delegate* unmanaged<Vk.VkCommandBuffer, uint, Vk.VkViewport*, void>)
            (compact ? api.vkCmdSetViewportWithCountEXT_ptr : api.vkCmdSetViewportWithCount_ptr).Value;
        CmdSetScissorWithCount = (delegate* unmanaged<Vk.VkCommandBuffer, uint, Vk.VkRect2D*, void>)
            (compact ? api.vkCmdSetScissorWithCountEXT_ptr : api.vkCmdSetScissorWithCount_ptr).Value;
        CmdSetDepthTestEnable = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkBool32, void>)
            (compact ? api.vkCmdSetDepthTestEnableEXT_ptr : api.vkCmdSetDepthTestEnable_ptr).Value;
        CmdSetDepthWriteEnable = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkBool32, void>)
            (compact ? api.vkCmdSetDepthWriteEnableEXT_ptr : api.vkCmdSetDepthWriteEnable_ptr).Value;
        CmdSetDepthCompareOp = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCompareOp, void>)
            (compact ? api.vkCmdSetDepthCompareOpEXT_ptr : api.vkCmdSetDepthCompareOp_ptr).Value;
        CmdSetStencilTestEnable = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkBool32, void>)
            (compact ? api.vkCmdSetStencilTestEnableEXT_ptr : api.vkCmdSetStencilTestEnable_ptr).Value;
        CmdSetStencilOp =
            (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkStencilFaceFlags, Vk.VkStencilOp, Vk.VkStencilOp, Vk.VkStencilOp, Vk.VkCompareOp, void>)
            (compact ? api.vkCmdSetStencilOpEXT_ptr : api.vkCmdSetStencilOp_ptr).Value;
        CmdCopyBuffer2 = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCopyBufferInfo2*, void>)
            (compact ? api.vkCmdCopyBuffer2KHR_ptr : api.vkCmdCopyBuffer2_ptr).Value;
        CmdCopyBufferToImage2 = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCopyBufferToImageInfo2*, void>)
            (compact ? api.vkCmdCopyBufferToImage2KHR_ptr : api.vkCmdCopyBufferToImage2_ptr).Value;
        CmdCopyImageToBuffer2 = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCopyImageToBufferInfo2*, void>)
            (compact ? api.vkCmdCopyImageToBuffer2KHR_ptr : api.vkCmdCopyImageToBuffer2_ptr).Value;
        CmdCopyImage2 = (delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkCopyImageInfo2*, void>)
            (compact ? api.vkCmdCopyImage2KHR_ptr : api.vkCmdCopyImage2_ptr).Value;
        GetDeviceBufferMemoryRequirements = (delegate* unmanaged<Vk.VkDevice, Vk.VkDeviceBufferMemoryRequirements*, Vk.VkMemoryRequirements2*, void>)
            (compact ? api.vkGetDeviceBufferMemoryRequirementsKHR_ptr : api.vkGetDeviceBufferMemoryRequirements_ptr).Value;
        GetDeviceImageMemoryRequirements = (delegate* unmanaged<Vk.VkDevice, Vk.VkDeviceImageMemoryRequirements*, Vk.VkMemoryRequirements2*, void>)
            (compact ? api.vkGetDeviceImageMemoryRequirementsKHR_ptr : api.vkGetDeviceImageMemoryRequirements_ptr).Value;

        if (QueueSubmit2 == null || CmdPipelineBarrier2Pointer == default || CmdWriteTimestamp2 == null ||
            CmdBeginRendering == null || CmdEndRendering == null || CmdSetViewportWithCount == null || CmdSetScissorWithCount == null ||
            CmdSetDepthTestEnable == null || CmdSetDepthWriteEnable == null || CmdSetDepthCompareOp == null ||
            CmdSetStencilTestEnable == null || CmdSetStencilOp == null || CmdCopyBuffer2 == null || CmdCopyBufferToImage2 == null ||
            CmdCopyImageToBuffer2 == null || CmdCopyImage2 == null || GetDeviceBufferMemoryRequirements == null ||
            GetDeviceImageMemoryRequirements == null)
        {
            throw compact
                ? VulkanBackend.CreationFailure(Error.Unsupported, "The driver did not provide the required Vulkan Compact KHR/EXT command entry points.")
                : VulkanBackend.CreationFailure(Error.DriverError, "The graphics driver did not provide the required core Vulkan command entry points.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly void CmdPipelineBarrier2(Vk.VkCommandBuffer commandBuffer, Vk.VkDependencyInfo* dependencyInfo)
        => ((delegate* unmanaged<Vk.VkCommandBuffer, Vk.VkDependencyInfo*, void>)CmdPipelineBarrier2Pointer.Value)(commandBuffer, dependencyInfo);
}
