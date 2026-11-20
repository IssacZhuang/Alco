// Minimal Vulkan device-level dispatch table.

using System.Runtime.CompilerServices;

namespace Vulkan;

internal sealed unsafe partial class VkDeviceApi
{
    public VkDevice Device { get; }

    public readonly PFN_vkVoidFunction vkDestroyDevice_ptr;
    public readonly PFN_vkVoidFunction vkGetDeviceQueue_ptr;
    public readonly PFN_vkVoidFunction vkDeviceWaitIdle_ptr;
    public readonly PFN_vkVoidFunction vkAllocateMemory_ptr;
    public readonly PFN_vkVoidFunction vkFreeMemory_ptr;
    public readonly PFN_vkVoidFunction vkMapMemory_ptr;
    public readonly PFN_vkVoidFunction vkUnmapMemory_ptr;
    public readonly PFN_vkVoidFunction vkBindBufferMemory_ptr;
    public readonly PFN_vkVoidFunction vkBindImageMemory_ptr;
    public readonly PFN_vkVoidFunction vkGetBufferMemoryRequirements_ptr;
    public readonly PFN_vkVoidFunction vkCreateFence_ptr;
    public readonly PFN_vkVoidFunction vkDestroyFence_ptr;
    public readonly PFN_vkVoidFunction vkResetFences_ptr;
    public readonly PFN_vkVoidFunction vkGetFenceStatus_ptr;
    public readonly PFN_vkVoidFunction vkWaitForFences_ptr;
    public readonly PFN_vkVoidFunction vkCreateSemaphore_ptr;
    public readonly PFN_vkVoidFunction vkDestroySemaphore_ptr;
    public readonly PFN_vkVoidFunction vkCreateQueryPool_ptr;
    public readonly PFN_vkVoidFunction vkDestroyQueryPool_ptr;
    public readonly PFN_vkVoidFunction vkGetQueryPoolResults_ptr;
    public readonly PFN_vkVoidFunction vkCreateBuffer_ptr;
    public readonly PFN_vkVoidFunction vkDestroyBuffer_ptr;
    public readonly PFN_vkVoidFunction vkCreateImage_ptr;
    public readonly PFN_vkVoidFunction vkDestroyImage_ptr;
    public readonly PFN_vkVoidFunction vkCreateImageView_ptr;
    public readonly PFN_vkVoidFunction vkDestroyImageView_ptr;
    public readonly PFN_vkVoidFunction vkCreateCommandPool_ptr;
    public readonly PFN_vkVoidFunction vkDestroyCommandPool_ptr;
    public readonly PFN_vkVoidFunction vkResetCommandPool_ptr;
    public readonly PFN_vkVoidFunction vkAllocateCommandBuffers_ptr;
    public readonly PFN_vkVoidFunction vkFreeCommandBuffers_ptr;
    public readonly PFN_vkVoidFunction vkBeginCommandBuffer_ptr;
    public readonly PFN_vkVoidFunction vkEndCommandBuffer_ptr;
    public readonly PFN_vkVoidFunction vkResetCommandBuffer_ptr;
    public readonly PFN_vkVoidFunction vkCreateShaderModule_ptr;
    public readonly PFN_vkVoidFunction vkDestroyShaderModule_ptr;
    public readonly PFN_vkVoidFunction vkCreatePipelineCache_ptr;
    public readonly PFN_vkVoidFunction vkDestroyPipelineCache_ptr;
    public readonly PFN_vkVoidFunction vkGetPipelineCacheData_ptr;
    public readonly PFN_vkVoidFunction vkCreateComputePipelines_ptr;
    public readonly PFN_vkVoidFunction vkDestroyPipeline_ptr;
    public readonly PFN_vkVoidFunction vkCreatePipelineLayout_ptr;
    public readonly PFN_vkVoidFunction vkDestroyPipelineLayout_ptr;
    public readonly PFN_vkVoidFunction vkCreateSampler_ptr;
    public readonly PFN_vkVoidFunction vkDestroySampler_ptr;
    public readonly PFN_vkVoidFunction vkCreateDescriptorSetLayout_ptr;
    public readonly PFN_vkVoidFunction vkDestroyDescriptorSetLayout_ptr;
    public readonly PFN_vkVoidFunction vkCreateDescriptorPool_ptr;
    public readonly PFN_vkVoidFunction vkDestroyDescriptorPool_ptr;
    public readonly PFN_vkVoidFunction vkAllocateDescriptorSets_ptr;
    public readonly PFN_vkVoidFunction vkUpdateDescriptorSets_ptr;
    public readonly PFN_vkVoidFunction vkCmdBindPipeline_ptr;
    public readonly PFN_vkVoidFunction vkCmdBindDescriptorSets_ptr;
    public readonly PFN_vkVoidFunction vkCmdDispatch_ptr;
    public readonly PFN_vkVoidFunction vkCmdDispatchIndirect_ptr;
    public readonly PFN_vkVoidFunction vkCmdPushConstants_ptr;
    public readonly PFN_vkVoidFunction vkCreateGraphicsPipelines_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetStencilCompareMask_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetStencilWriteMask_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetStencilReference_ptr;
    public readonly PFN_vkVoidFunction vkCmdBindIndexBuffer_ptr;
    public readonly PFN_vkVoidFunction vkCmdDraw_ptr;
    public readonly PFN_vkVoidFunction vkCmdDrawIndexed_ptr;
    public readonly PFN_vkVoidFunction vkCmdDrawIndirect_ptr;
    public readonly PFN_vkVoidFunction vkCmdDrawIndexedIndirect_ptr;
    public readonly PFN_vkVoidFunction vkGetDescriptorSetLayoutSupport_ptr;
    public readonly PFN_vkVoidFunction vkResetQueryPool_ptr;
    public readonly PFN_vkVoidFunction vkGetSemaphoreCounterValue_ptr;
    public readonly PFN_vkVoidFunction vkWaitSemaphores_ptr;
    public readonly PFN_vkVoidFunction vkGetBufferDeviceAddress_ptr;
    public readonly PFN_vkVoidFunction vkCmdPipelineBarrier2_ptr;
    public readonly PFN_vkVoidFunction vkCmdWriteTimestamp2_ptr;
    public readonly PFN_vkVoidFunction vkQueueSubmit2_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyBuffer2_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyImage2_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyBufferToImage2_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyImageToBuffer2_ptr;
    public readonly PFN_vkVoidFunction vkGetDeviceBufferMemoryRequirements_ptr;
    public readonly PFN_vkVoidFunction vkGetDeviceImageMemoryRequirements_ptr;
    public readonly PFN_vkVoidFunction vkCmdBeginRendering_ptr;
    public readonly PFN_vkVoidFunction vkCmdEndRendering_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetViewportWithCount_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetScissorWithCount_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetDepthTestEnable_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetDepthWriteEnable_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetDepthCompareOp_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetStencilTestEnable_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetStencilOp_ptr;
    public readonly PFN_vkVoidFunction vkCmdBindIndexBuffer2_ptr;
    public readonly PFN_vkVoidFunction vkCreateSwapchainKHR_ptr;
    public readonly PFN_vkVoidFunction vkDestroySwapchainKHR_ptr;
    public readonly PFN_vkVoidFunction vkGetSwapchainImagesKHR_ptr;
    public readonly PFN_vkVoidFunction vkAcquireNextImageKHR_ptr;
    public readonly PFN_vkVoidFunction vkQueuePresentKHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdBeginRenderingKHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdEndRenderingKHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdPipelineBarrier2KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdWriteTimestamp2KHR_ptr;
    public readonly PFN_vkVoidFunction vkQueueSubmit2KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdBindIndexBuffer3KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdDrawIndirect2KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdDrawIndexedIndirect2KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdDispatchIndirect2KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyMemoryKHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyMemoryToImageKHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyImageToMemoryKHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdDrawMeshTasksIndirect2EXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyBuffer2KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyImage2KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyBufferToImage2KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdCopyImageToBuffer2KHR_ptr;
    public readonly PFN_vkVoidFunction vkGetDeviceBufferMemoryRequirementsKHR_ptr;
    public readonly PFN_vkVoidFunction vkGetDeviceImageMemoryRequirementsKHR_ptr;
    public readonly PFN_vkVoidFunction vkReleaseSwapchainImagesKHR_ptr;
    public readonly PFN_vkVoidFunction vkWriteSamplerDescriptorsEXT_ptr;
    public readonly PFN_vkVoidFunction vkWriteResourceDescriptorsEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdBindSamplerHeapEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdBindResourceHeapEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdPushDataEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetViewportWithCountEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetScissorWithCountEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetDepthTestEnableEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetDepthWriteEnableEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetDepthCompareOpEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetStencilTestEnableEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdSetStencilOpEXT_ptr;
    public readonly PFN_vkVoidFunction vkReleaseSwapchainImagesEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdDrawMeshTasksEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdDrawMeshTasksIndirectEXT_ptr;

    public VkDeviceApi(VkInstanceApi api, in VkDevice device)
    {
        Device = device;
        vkDestroyDevice_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyDevice"u8);
        vkGetDeviceQueue_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetDeviceQueue"u8);
        vkDeviceWaitIdle_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDeviceWaitIdle"u8);
        vkAllocateMemory_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkAllocateMemory"u8);
        vkFreeMemory_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkFreeMemory"u8);
        vkMapMemory_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkMapMemory"u8);
        vkUnmapMemory_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkUnmapMemory"u8);
        vkBindBufferMemory_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkBindBufferMemory"u8);
        vkBindImageMemory_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkBindImageMemory"u8);
        vkGetBufferMemoryRequirements_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetBufferMemoryRequirements"u8);
        vkCreateFence_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateFence"u8);
        vkDestroyFence_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyFence"u8);
        vkResetFences_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkResetFences"u8);
        vkGetFenceStatus_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetFenceStatus"u8);
        vkWaitForFences_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkWaitForFences"u8);
        vkCreateSemaphore_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateSemaphore"u8);
        vkDestroySemaphore_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroySemaphore"u8);
        vkCreateQueryPool_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateQueryPool"u8);
        vkDestroyQueryPool_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyQueryPool"u8);
        vkGetQueryPoolResults_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetQueryPoolResults"u8);
        vkCreateBuffer_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateBuffer"u8);
        vkDestroyBuffer_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyBuffer"u8);
        vkCreateImage_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateImage"u8);
        vkDestroyImage_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyImage"u8);
        vkCreateImageView_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateImageView"u8);
        vkDestroyImageView_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyImageView"u8);
        vkCreateCommandPool_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateCommandPool"u8);
        vkDestroyCommandPool_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyCommandPool"u8);
        vkResetCommandPool_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkResetCommandPool"u8);
        vkAllocateCommandBuffers_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkAllocateCommandBuffers"u8);
        vkFreeCommandBuffers_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkFreeCommandBuffers"u8);
        vkBeginCommandBuffer_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkBeginCommandBuffer"u8);
        vkEndCommandBuffer_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkEndCommandBuffer"u8);
        vkResetCommandBuffer_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkResetCommandBuffer"u8);
        vkCreateShaderModule_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateShaderModule"u8);
        vkDestroyShaderModule_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyShaderModule"u8);
        vkCreatePipelineCache_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreatePipelineCache"u8);
        vkDestroyPipelineCache_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyPipelineCache"u8);
        vkGetPipelineCacheData_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetPipelineCacheData"u8);
        vkCreateComputePipelines_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateComputePipelines"u8);
        vkDestroyPipeline_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyPipeline"u8);
        vkCreatePipelineLayout_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreatePipelineLayout"u8);
        vkDestroyPipelineLayout_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyPipelineLayout"u8);
        vkCreateSampler_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateSampler"u8);
        vkDestroySampler_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroySampler"u8);
        vkCreateDescriptorSetLayout_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateDescriptorSetLayout"u8);
        vkDestroyDescriptorSetLayout_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyDescriptorSetLayout"u8);
        vkCreateDescriptorPool_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateDescriptorPool"u8);
        vkDestroyDescriptorPool_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroyDescriptorPool"u8);
        vkAllocateDescriptorSets_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkAllocateDescriptorSets"u8);
        vkUpdateDescriptorSets_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkUpdateDescriptorSets"u8);
        vkCmdBindPipeline_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdBindPipeline"u8);
        vkCmdBindDescriptorSets_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdBindDescriptorSets"u8);
        vkCmdDispatch_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDispatch"u8);
        vkCmdDispatchIndirect_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDispatchIndirect"u8);
        vkCmdPushConstants_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdPushConstants"u8);
        vkCreateGraphicsPipelines_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateGraphicsPipelines"u8);
        vkCmdSetStencilCompareMask_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetStencilCompareMask"u8);
        vkCmdSetStencilWriteMask_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetStencilWriteMask"u8);
        vkCmdSetStencilReference_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetStencilReference"u8);
        vkCmdBindIndexBuffer_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdBindIndexBuffer"u8);
        vkCmdDraw_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDraw"u8);
        vkCmdDrawIndexed_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDrawIndexed"u8);
        vkCmdDrawIndirect_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDrawIndirect"u8);
        vkCmdDrawIndexedIndirect_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDrawIndexedIndirect"u8);
        vkGetDescriptorSetLayoutSupport_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetDescriptorSetLayoutSupport"u8);
        vkResetQueryPool_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkResetQueryPool"u8);
        vkGetSemaphoreCounterValue_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetSemaphoreCounterValue"u8);
        vkWaitSemaphores_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkWaitSemaphores"u8);
        vkGetBufferDeviceAddress_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetBufferDeviceAddress"u8);
        vkCmdPipelineBarrier2_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdPipelineBarrier2"u8);
        vkCmdWriteTimestamp2_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdWriteTimestamp2"u8);
        vkQueueSubmit2_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkQueueSubmit2"u8);
        vkCmdCopyBuffer2_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyBuffer2"u8);
        vkCmdCopyImage2_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyImage2"u8);
        vkCmdCopyBufferToImage2_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyBufferToImage2"u8);
        vkCmdCopyImageToBuffer2_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyImageToBuffer2"u8);
        vkGetDeviceBufferMemoryRequirements_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetDeviceBufferMemoryRequirements"u8);
        vkGetDeviceImageMemoryRequirements_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetDeviceImageMemoryRequirements"u8);
        vkCmdBeginRendering_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdBeginRendering"u8);
        vkCmdEndRendering_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdEndRendering"u8);
        vkCmdSetViewportWithCount_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetViewportWithCount"u8);
        vkCmdSetScissorWithCount_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetScissorWithCount"u8);
        vkCmdSetDepthTestEnable_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetDepthTestEnable"u8);
        vkCmdSetDepthWriteEnable_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetDepthWriteEnable"u8);
        vkCmdSetDepthCompareOp_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetDepthCompareOp"u8);
        vkCmdSetStencilTestEnable_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetStencilTestEnable"u8);
        vkCmdSetStencilOp_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetStencilOp"u8);
        vkCmdBindIndexBuffer2_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdBindIndexBuffer2"u8);
        vkCreateSwapchainKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCreateSwapchainKHR"u8);
        vkDestroySwapchainKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkDestroySwapchainKHR"u8);
        vkGetSwapchainImagesKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetSwapchainImagesKHR"u8);
        vkAcquireNextImageKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkAcquireNextImageKHR"u8);
        vkQueuePresentKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkQueuePresentKHR"u8);
        vkCmdBeginRenderingKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdBeginRenderingKHR"u8);
        vkCmdEndRenderingKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdEndRenderingKHR"u8);
        vkCmdPipelineBarrier2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdPipelineBarrier2KHR"u8);
        vkCmdWriteTimestamp2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdWriteTimestamp2KHR"u8);
        vkQueueSubmit2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkQueueSubmit2KHR"u8);
        vkCmdBindIndexBuffer3KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdBindIndexBuffer3KHR"u8);
        vkCmdDrawIndirect2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDrawIndirect2KHR"u8);
        vkCmdDrawIndexedIndirect2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDrawIndexedIndirect2KHR"u8);
        vkCmdDispatchIndirect2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDispatchIndirect2KHR"u8);
        vkCmdCopyMemoryKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyMemoryKHR"u8);
        vkCmdCopyMemoryToImageKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyMemoryToImageKHR"u8);
        vkCmdCopyImageToMemoryKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyImageToMemoryKHR"u8);
        vkCmdDrawMeshTasksIndirect2EXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDrawMeshTasksIndirect2EXT"u8);
        vkCmdCopyBuffer2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyBuffer2KHR"u8);
        vkCmdCopyImage2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyImage2KHR"u8);
        vkCmdCopyBufferToImage2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyBufferToImage2KHR"u8);
        vkCmdCopyImageToBuffer2KHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdCopyImageToBuffer2KHR"u8);
        vkGetDeviceBufferMemoryRequirementsKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetDeviceBufferMemoryRequirementsKHR"u8);
        vkGetDeviceImageMemoryRequirementsKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkGetDeviceImageMemoryRequirementsKHR"u8);
        vkReleaseSwapchainImagesKHR_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkReleaseSwapchainImagesKHR"u8);
        vkWriteSamplerDescriptorsEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkWriteSamplerDescriptorsEXT"u8);
        vkWriteResourceDescriptorsEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkWriteResourceDescriptorsEXT"u8);
        vkCmdBindSamplerHeapEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdBindSamplerHeapEXT"u8);
        vkCmdBindResourceHeapEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdBindResourceHeapEXT"u8);
        vkCmdPushDataEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdPushDataEXT"u8);
        vkCmdSetViewportWithCountEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetViewportWithCountEXT"u8);
        vkCmdSetScissorWithCountEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetScissorWithCountEXT"u8);
        vkCmdSetDepthTestEnableEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetDepthTestEnableEXT"u8);
        vkCmdSetDepthWriteEnableEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetDepthWriteEnableEXT"u8);
        vkCmdSetDepthCompareOpEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetDepthCompareOpEXT"u8);
        vkCmdSetStencilTestEnableEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetStencilTestEnableEXT"u8);
        vkCmdSetStencilOpEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdSetStencilOpEXT"u8);
        vkReleaseSwapchainImagesEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkReleaseSwapchainImagesEXT"u8);
        vkCmdDrawMeshTasksEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDrawMeshTasksEXT"u8);
        vkCmdDrawMeshTasksIndirectEXT_ptr = api.vkGetDeviceProcAddr(device.Handle, "vkCmdDrawMeshTasksIndirectEXT"u8);
    }

    public void vkDestroyDevice(VkAllocationCallbacks* allocator)
    {
        ((delegate* unmanaged<VkDevice, VkAllocationCallbacks*, void>)vkDestroyDevice_ptr.Value)(Device, allocator);
        _ = Vulkan.RemoveDeviceTable(Device);
    }

    public void vkGetDeviceQueue(uint queueFamilyIndex, uint queueIndex, out VkQueue queue)
    {
        Unsafe.SkipInit(out queue);

        fixed (VkQueue* queuePtr = &queue)
        {
            ((delegate* unmanaged<VkDevice, uint, uint, VkQueue*, void>)vkGetDeviceQueue_ptr.Value)(Device, queueFamilyIndex, queueIndex, queuePtr);
        }
    }

    public VkResult vkDeviceWaitIdle() =>
        ((delegate* unmanaged<VkDevice, VkResult>)vkDeviceWaitIdle_ptr.Value)(Device);

    public VkResult vkAllocateMemory(VkMemoryAllocateInfo* allocateInfo, VkAllocationCallbacks* allocator, out VkDeviceMemory memory)
    {
        Unsafe.SkipInit(out memory);

        fixed (VkDeviceMemory* memoryPtr = &memory)
        {
            return ((delegate* unmanaged<VkDevice, VkMemoryAllocateInfo*, VkAllocationCallbacks*, VkDeviceMemory*, VkResult>)vkAllocateMemory_ptr.Value)(Device, allocateInfo, allocator, memoryPtr);
        }
    }

    public void vkFreeMemory(VkDeviceMemory memory, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkDeviceMemory, VkAllocationCallbacks*, void>)vkFreeMemory_ptr.Value)(Device, memory, allocator);

    public VkResult vkMapMemory(VkDeviceMemory memory, ulong offset, ulong size, VkMemoryMapFlags flags, void** data) =>
        ((delegate* unmanaged<VkDevice, VkDeviceMemory, ulong, ulong, VkMemoryMapFlags, void**, VkResult>)vkMapMemory_ptr.Value)(Device, memory, offset, size, flags, data);

    public void vkUnmapMemory(VkDeviceMemory memory) =>
        ((delegate* unmanaged<VkDevice, VkDeviceMemory, void>)vkUnmapMemory_ptr.Value)(Device, memory);

    public VkResult vkBindBufferMemory(VkBuffer buffer, VkDeviceMemory memory, ulong memoryOffset) =>
        ((delegate* unmanaged<VkDevice, VkBuffer, VkDeviceMemory, ulong, VkResult>)vkBindBufferMemory_ptr.Value)(Device, buffer, memory, memoryOffset);

    public VkResult vkBindImageMemory(VkImage image, VkDeviceMemory memory, ulong memoryOffset) =>
        ((delegate* unmanaged<VkDevice, VkImage, VkDeviceMemory, ulong, VkResult>)vkBindImageMemory_ptr.Value)(Device, image, memory, memoryOffset);

    public void vkGetBufferMemoryRequirements(VkBuffer buffer, out VkMemoryRequirements memoryRequirements)
    {
        Unsafe.SkipInit(out memoryRequirements);

        fixed (VkMemoryRequirements* memoryRequirementsPtr = &memoryRequirements)
        {
            ((delegate* unmanaged<VkDevice, VkBuffer, VkMemoryRequirements*, void>)vkGetBufferMemoryRequirements_ptr.Value)(Device, buffer, memoryRequirementsPtr);
        }
    }

    public VkResult vkCreateFence(VkFenceCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkFence fence)
    {
        Unsafe.SkipInit(out fence);

        fixed (VkFence* fencePtr = &fence)
        {
            return ((delegate* unmanaged<VkDevice, VkFenceCreateInfo*, VkAllocationCallbacks*, VkFence*, VkResult>)vkCreateFence_ptr.Value)(Device, createInfo, allocator, fencePtr);
        }
    }

    public void vkDestroyFence(VkFence fence, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkFence, VkAllocationCallbacks*, void>)vkDestroyFence_ptr.Value)(Device, fence, allocator);

    public VkResult vkResetFences(uint fenceCount, VkFence* fences) =>
        ((delegate* unmanaged<VkDevice, uint, VkFence*, VkResult>)vkResetFences_ptr.Value)(Device, fenceCount, fences);

    public VkResult vkGetFenceStatus(VkFence fence) =>
        ((delegate* unmanaged<VkDevice, VkFence, VkResult>)vkGetFenceStatus_ptr.Value)(Device, fence);

    public VkResult vkWaitForFences(uint fenceCount, VkFence* fences, VkBool32 waitAll, ulong timeout) =>
        ((delegate* unmanaged<VkDevice, uint, VkFence*, VkBool32, ulong, VkResult>)vkWaitForFences_ptr.Value)(Device, fenceCount, fences, waitAll, timeout);

    public VkResult vkCreateSemaphore(VkSemaphoreCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkSemaphore semaphore)
    {
        Unsafe.SkipInit(out semaphore);

        fixed (VkSemaphore* semaphorePtr = &semaphore)
        {
            return ((delegate* unmanaged<VkDevice, VkSemaphoreCreateInfo*, VkAllocationCallbacks*, VkSemaphore*, VkResult>)vkCreateSemaphore_ptr.Value)(Device, createInfo, allocator, semaphorePtr);
        }
    }

    public void vkDestroySemaphore(VkSemaphore semaphore, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkSemaphore, VkAllocationCallbacks*, void>)vkDestroySemaphore_ptr.Value)(Device, semaphore, allocator);

    public VkResult vkCreateQueryPool(VkQueryPoolCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkQueryPool queryPool)
    {
        Unsafe.SkipInit(out queryPool);

        fixed (VkQueryPool* queryPoolPtr = &queryPool)
        {
            return ((delegate* unmanaged<VkDevice, VkQueryPoolCreateInfo*, VkAllocationCallbacks*, VkQueryPool*, VkResult>)vkCreateQueryPool_ptr.Value)(Device, createInfo, allocator, queryPoolPtr);
        }
    }

    public void vkDestroyQueryPool(VkQueryPool queryPool, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkQueryPool, VkAllocationCallbacks*, void>)vkDestroyQueryPool_ptr.Value)(Device, queryPool, allocator);

    public VkResult vkGetQueryPoolResults(VkQueryPool queryPool, uint firstQuery, uint queryCount, nuint dataSize, void* data, ulong stride, VkQueryResultFlags flags) =>
        ((delegate* unmanaged<VkDevice, VkQueryPool, uint, uint, nuint, void*, ulong, VkQueryResultFlags, VkResult>)vkGetQueryPoolResults_ptr.Value)(Device, queryPool, firstQuery, queryCount, dataSize, data, stride, flags);

    public VkResult vkCreateBuffer(VkBufferCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkBuffer buffer)
    {
        Unsafe.SkipInit(out buffer);

        fixed (VkBuffer* bufferPtr = &buffer)
        {
            return ((delegate* unmanaged<VkDevice, VkBufferCreateInfo*, VkAllocationCallbacks*, VkBuffer*, VkResult>)vkCreateBuffer_ptr.Value)(Device, createInfo, allocator, bufferPtr);
        }
    }

    public void vkDestroyBuffer(VkBuffer buffer, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkBuffer, VkAllocationCallbacks*, void>)vkDestroyBuffer_ptr.Value)(Device, buffer, allocator);

    public VkResult vkCreateImage(VkImageCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkImage image)
    {
        Unsafe.SkipInit(out image);

        fixed (VkImage* imagePtr = &image)
        {
            return ((delegate* unmanaged<VkDevice, VkImageCreateInfo*, VkAllocationCallbacks*, VkImage*, VkResult>)vkCreateImage_ptr.Value)(Device, createInfo, allocator, imagePtr);
        }
    }

    public void vkDestroyImage(VkImage image, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkImage, VkAllocationCallbacks*, void>)vkDestroyImage_ptr.Value)(Device, image, allocator);

    public VkResult vkCreateImageView(VkImageViewCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkImageView view)
    {
        Unsafe.SkipInit(out view);

        fixed (VkImageView* viewPtr = &view)
        {
            return ((delegate* unmanaged<VkDevice, VkImageViewCreateInfo*, VkAllocationCallbacks*, VkImageView*, VkResult>)vkCreateImageView_ptr.Value)(Device, createInfo, allocator, viewPtr);
        }
    }

    public void vkDestroyImageView(VkImageView imageView, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkImageView, VkAllocationCallbacks*, void>)vkDestroyImageView_ptr.Value)(Device, imageView, allocator);

    public VkResult vkCreateCommandPool(VkCommandPoolCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkCommandPool commandPool)
    {
        Unsafe.SkipInit(out commandPool);

        fixed (VkCommandPool* commandPoolPtr = &commandPool)
        {
            return ((delegate* unmanaged<VkDevice, VkCommandPoolCreateInfo*, VkAllocationCallbacks*, VkCommandPool*, VkResult>)vkCreateCommandPool_ptr.Value)(Device, createInfo, allocator, commandPoolPtr);
        }
    }

    public void vkDestroyCommandPool(VkCommandPool commandPool, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkCommandPool, VkAllocationCallbacks*, void>)vkDestroyCommandPool_ptr.Value)(Device, commandPool, allocator);

    public VkResult vkResetCommandPool(VkCommandPool commandPool, VkCommandPoolResetFlags flags) =>
        ((delegate* unmanaged<VkDevice, VkCommandPool, VkCommandPoolResetFlags, VkResult>)vkResetCommandPool_ptr.Value)(Device, commandPool, flags);

    public VkResult vkAllocateCommandBuffers(VkCommandBufferAllocateInfo* allocateInfo, VkCommandBuffer* commandBuffers) =>
        ((delegate* unmanaged<VkDevice, VkCommandBufferAllocateInfo*, VkCommandBuffer*, VkResult>)vkAllocateCommandBuffers_ptr.Value)(Device, allocateInfo, commandBuffers);

    public void vkFreeCommandBuffers(VkCommandPool commandPool, VkCommandBuffer commandBuffer)
    {
        ((delegate* unmanaged<VkDevice, VkCommandPool, uint, VkCommandBuffer*, void>)vkFreeCommandBuffers_ptr.Value)(Device, commandPool, 1, &commandBuffer);
    }

    public VkResult vkBeginCommandBuffer(VkCommandBuffer commandBuffer, VkCommandBufferBeginInfo* beginInfo) =>
        ((delegate* unmanaged<VkCommandBuffer, VkCommandBufferBeginInfo*, VkResult>)vkBeginCommandBuffer_ptr.Value)(commandBuffer, beginInfo);

    public VkResult vkEndCommandBuffer(VkCommandBuffer commandBuffer) =>
        ((delegate* unmanaged<VkCommandBuffer, VkResult>)vkEndCommandBuffer_ptr.Value)(commandBuffer);

    public VkResult vkResetCommandBuffer(VkCommandBuffer commandBuffer, VkCommandBufferResetFlags flags) =>
        ((delegate* unmanaged<VkCommandBuffer, VkCommandBufferResetFlags, VkResult>)vkResetCommandBuffer_ptr.Value)(commandBuffer, flags);

    public VkResult vkCreateShaderModule(VkShaderModuleCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkShaderModule shaderModule)
    {
        Unsafe.SkipInit(out shaderModule);

        fixed (VkShaderModule* shaderModulePtr = &shaderModule)
        {
            return ((delegate* unmanaged<VkDevice, VkShaderModuleCreateInfo*, VkAllocationCallbacks*, VkShaderModule*, VkResult>)vkCreateShaderModule_ptr.Value)(Device, createInfo, allocator, shaderModulePtr);
        }
    }

    public void vkDestroyShaderModule(VkShaderModule shaderModule, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkShaderModule, VkAllocationCallbacks*, void>)vkDestroyShaderModule_ptr.Value)(Device, shaderModule, allocator);

    public VkResult vkCreatePipelineCache(VkPipelineCacheCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkPipelineCache pipelineCache)
    {
        Unsafe.SkipInit(out pipelineCache);

        fixed (VkPipelineCache* pipelineCachePtr = &pipelineCache)
        {
            return ((delegate* unmanaged<VkDevice, VkPipelineCacheCreateInfo*, VkAllocationCallbacks*, VkPipelineCache*, VkResult>)vkCreatePipelineCache_ptr.Value)(Device, createInfo, allocator, pipelineCachePtr);
        }
    }

    public void vkDestroyPipelineCache(VkPipelineCache pipelineCache, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkPipelineCache, VkAllocationCallbacks*, void>)vkDestroyPipelineCache_ptr.Value)(Device, pipelineCache, allocator);

    public VkResult vkGetPipelineCacheData(VkPipelineCache pipelineCache, nuint* dataSize, void* data) =>
        ((delegate* unmanaged<VkDevice, VkPipelineCache, nuint*, void*, VkResult>)vkGetPipelineCacheData_ptr.Value)(Device, pipelineCache, dataSize, data);

    public VkResult vkCreateComputePipelines(VkPipelineCache pipelineCache, uint createInfoCount, VkComputePipelineCreateInfo* createInfos, VkAllocationCallbacks* allocator, VkPipeline* pipelines) =>
        ((delegate* unmanaged<VkDevice, VkPipelineCache, uint, VkComputePipelineCreateInfo*, VkAllocationCallbacks*, VkPipeline*, VkResult>)vkCreateComputePipelines_ptr.Value)(Device, pipelineCache, createInfoCount, createInfos, allocator, pipelines);

    public void vkDestroyPipeline(VkPipeline pipeline, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkPipeline, VkAllocationCallbacks*, void>)vkDestroyPipeline_ptr.Value)(Device, pipeline, allocator);

    public VkResult vkCreatePipelineLayout(VkPipelineLayoutCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkPipelineLayout pipelineLayout)
    {
        Unsafe.SkipInit(out pipelineLayout);

        fixed (VkPipelineLayout* pipelineLayoutPtr = &pipelineLayout)
        {
            return ((delegate* unmanaged<VkDevice, VkPipelineLayoutCreateInfo*, VkAllocationCallbacks*, VkPipelineLayout*, VkResult>)vkCreatePipelineLayout_ptr.Value)(Device, createInfo, allocator, pipelineLayoutPtr);
        }
    }

    public void vkDestroyPipelineLayout(VkPipelineLayout pipelineLayout, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkPipelineLayout, VkAllocationCallbacks*, void>)vkDestroyPipelineLayout_ptr.Value)(Device, pipelineLayout, allocator);

    public VkResult vkCreateSampler(VkSamplerCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkSampler sampler)
    {
        Unsafe.SkipInit(out sampler);

        fixed (VkSampler* samplerPtr = &sampler)
        {
            return ((delegate* unmanaged<VkDevice, VkSamplerCreateInfo*, VkAllocationCallbacks*, VkSampler*, VkResult>)vkCreateSampler_ptr.Value)(Device, createInfo, allocator, samplerPtr);
        }
    }

    public void vkDestroySampler(VkSampler sampler, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkSampler, VkAllocationCallbacks*, void>)vkDestroySampler_ptr.Value)(Device, sampler, allocator);

    public VkResult vkCreateDescriptorSetLayout(VkDescriptorSetLayoutCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkDescriptorSetLayout setLayout)
    {
        Unsafe.SkipInit(out setLayout);

        fixed (VkDescriptorSetLayout* setLayoutPtr = &setLayout)
        {
            return ((delegate* unmanaged<VkDevice, VkDescriptorSetLayoutCreateInfo*, VkAllocationCallbacks*, VkDescriptorSetLayout*, VkResult>)vkCreateDescriptorSetLayout_ptr.Value)(Device, createInfo, allocator, setLayoutPtr);
        }
    }

    public void vkDestroyDescriptorSetLayout(VkDescriptorSetLayout descriptorSetLayout, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkDescriptorSetLayout, VkAllocationCallbacks*, void>)vkDestroyDescriptorSetLayout_ptr.Value)(Device, descriptorSetLayout, allocator);

    public VkResult vkCreateDescriptorPool(VkDescriptorPoolCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkDescriptorPool descriptorPool)
    {
        Unsafe.SkipInit(out descriptorPool);

        fixed (VkDescriptorPool* descriptorPoolPtr = &descriptorPool)
        {
            return ((delegate* unmanaged<VkDevice, VkDescriptorPoolCreateInfo*, VkAllocationCallbacks*, VkDescriptorPool*, VkResult>)vkCreateDescriptorPool_ptr.Value)(Device, createInfo, allocator, descriptorPoolPtr);
        }
    }

    public void vkDestroyDescriptorPool(VkDescriptorPool descriptorPool, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkDescriptorPool, VkAllocationCallbacks*, void>)vkDestroyDescriptorPool_ptr.Value)(Device, descriptorPool, allocator);

    public VkResult vkAllocateDescriptorSets(VkDescriptorSetAllocateInfo* allocateInfo, VkDescriptorSet* descriptorSets) =>
        ((delegate* unmanaged<VkDevice, VkDescriptorSetAllocateInfo*, VkDescriptorSet*, VkResult>)vkAllocateDescriptorSets_ptr.Value)(Device, allocateInfo, descriptorSets);

    public void vkUpdateDescriptorSets(uint descriptorWriteCount, VkWriteDescriptorSet* descriptorWrites, uint descriptorCopyCount, VkCopyDescriptorSet* descriptorCopies) =>
        ((delegate* unmanaged<VkDevice, uint, VkWriteDescriptorSet*, uint, VkCopyDescriptorSet*, void>)vkUpdateDescriptorSets_ptr.Value)(Device, descriptorWriteCount, descriptorWrites, descriptorCopyCount, descriptorCopies);

    public void vkCmdBindPipeline(VkCommandBuffer commandBuffer, VkPipelineBindPoint pipelineBindPoint, VkPipeline pipeline) =>
        ((delegate* unmanaged<VkCommandBuffer, VkPipelineBindPoint, VkPipeline, void>)vkCmdBindPipeline_ptr.Value)(commandBuffer, pipelineBindPoint, pipeline);

    public void vkCmdBindDescriptorSets(VkCommandBuffer commandBuffer, VkPipelineBindPoint pipelineBindPoint, VkPipelineLayout layout, uint firstSet, uint descriptorSetCount, VkDescriptorSet* descriptorSets, uint dynamicOffsetCount, uint* dynamicOffsets) =>
        ((delegate* unmanaged<VkCommandBuffer, VkPipelineBindPoint, VkPipelineLayout, uint, uint, VkDescriptorSet*, uint, uint*, void>)vkCmdBindDescriptorSets_ptr.Value)(commandBuffer, pipelineBindPoint, layout, firstSet, descriptorSetCount, descriptorSets, dynamicOffsetCount, dynamicOffsets);

    public void vkCmdDispatch(VkCommandBuffer commandBuffer, uint groupCountX, uint groupCountY, uint groupCountZ) =>
        ((delegate* unmanaged<VkCommandBuffer, uint, uint, uint, void>)vkCmdDispatch_ptr.Value)(commandBuffer, groupCountX, groupCountY, groupCountZ);

    public void vkCmdDispatchIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset) =>
        ((delegate* unmanaged<VkCommandBuffer, VkBuffer, ulong, void>)vkCmdDispatchIndirect_ptr.Value)(commandBuffer, buffer, offset);

    public void vkCmdPushConstants(VkCommandBuffer commandBuffer, VkPipelineLayout layout, VkShaderStageFlags stageFlags, uint offset, uint size, void* values) =>
        ((delegate* unmanaged<VkCommandBuffer, VkPipelineLayout, VkShaderStageFlags, uint, uint, void*, void>)vkCmdPushConstants_ptr.Value)(commandBuffer, layout, stageFlags, offset, size, values);

    public VkResult vkCreateGraphicsPipelines(VkPipelineCache pipelineCache, uint createInfoCount, VkGraphicsPipelineCreateInfo* createInfos, VkAllocationCallbacks* allocator, VkPipeline* pipelines) =>
        ((delegate* unmanaged<VkDevice, VkPipelineCache, uint, VkGraphicsPipelineCreateInfo*, VkAllocationCallbacks*, VkPipeline*, VkResult>)vkCreateGraphicsPipelines_ptr.Value)(Device, pipelineCache, createInfoCount, createInfos, allocator, pipelines);

    public void vkCmdSetStencilCompareMask(VkCommandBuffer commandBuffer, VkStencilFaceFlags faceMask, uint compareMask) =>
        ((delegate* unmanaged<VkCommandBuffer, VkStencilFaceFlags, uint, void>)vkCmdSetStencilCompareMask_ptr.Value)(commandBuffer, faceMask, compareMask);

    public void vkCmdSetStencilWriteMask(VkCommandBuffer commandBuffer, VkStencilFaceFlags faceMask, uint writeMask) =>
        ((delegate* unmanaged<VkCommandBuffer, VkStencilFaceFlags, uint, void>)vkCmdSetStencilWriteMask_ptr.Value)(commandBuffer, faceMask, writeMask);

    public void vkCmdSetStencilReference(VkCommandBuffer commandBuffer, VkStencilFaceFlags faceMask, uint reference) =>
        ((delegate* unmanaged<VkCommandBuffer, VkStencilFaceFlags, uint, void>)vkCmdSetStencilReference_ptr.Value)(commandBuffer, faceMask, reference);

    public void vkCmdBindIndexBuffer(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, VkIndexType indexType) =>
        ((delegate* unmanaged<VkCommandBuffer, VkBuffer, ulong, VkIndexType, void>)vkCmdBindIndexBuffer_ptr.Value)(commandBuffer, buffer, offset, indexType);

    public void vkCmdDraw(VkCommandBuffer commandBuffer, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) =>
        ((delegate* unmanaged<VkCommandBuffer, uint, uint, uint, uint, void>)vkCmdDraw_ptr.Value)(commandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);

    public void vkCmdDrawIndexed(VkCommandBuffer commandBuffer, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance) =>
        ((delegate* unmanaged<VkCommandBuffer, uint, uint, uint, int, uint, void>)vkCmdDrawIndexed_ptr.Value)(commandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);

    public void vkCmdDrawIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, uint drawCount, uint stride) =>
        ((delegate* unmanaged<VkCommandBuffer, VkBuffer, ulong, uint, uint, void>)vkCmdDrawIndirect_ptr.Value)(commandBuffer, buffer, offset, drawCount, stride);

    public void vkCmdDrawIndexedIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, uint drawCount, uint stride) =>
        ((delegate* unmanaged<VkCommandBuffer, VkBuffer, ulong, uint, uint, void>)vkCmdDrawIndexedIndirect_ptr.Value)(commandBuffer, buffer, offset, drawCount, stride);

    public void vkGetDescriptorSetLayoutSupport(VkDescriptorSetLayoutCreateInfo* createInfo, VkDescriptorSetLayoutSupport* support) =>
        ((delegate* unmanaged<VkDevice, VkDescriptorSetLayoutCreateInfo*, VkDescriptorSetLayoutSupport*, void>)vkGetDescriptorSetLayoutSupport_ptr.Value)(Device, createInfo, support);

    public void vkResetQueryPool(VkQueryPool queryPool, uint firstQuery, uint queryCount) =>
        ((delegate* unmanaged<VkDevice, VkQueryPool, uint, uint, void>)vkResetQueryPool_ptr.Value)(Device, queryPool, firstQuery, queryCount);

    public VkResult vkGetSemaphoreCounterValue(VkSemaphore semaphore, ulong* value) =>
        ((delegate* unmanaged<VkDevice, VkSemaphore, ulong*, VkResult>)vkGetSemaphoreCounterValue_ptr.Value)(Device, semaphore, value);

    public VkResult vkWaitSemaphores(VkSemaphoreWaitInfo* waitInfo, ulong timeout) =>
        ((delegate* unmanaged<VkDevice, VkSemaphoreWaitInfo*, ulong, VkResult>)vkWaitSemaphores_ptr.Value)(Device, waitInfo, timeout);

    public ulong vkGetBufferDeviceAddress(VkBufferDeviceAddressInfo* info) =>
        ((delegate* unmanaged<VkDevice, VkBufferDeviceAddressInfo*, ulong>)vkGetBufferDeviceAddress_ptr.Value)(Device, info);

    public void vkCmdBindIndexBuffer2(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, ulong size, VkIndexType indexType) =>
        ((delegate* unmanaged<VkCommandBuffer, VkBuffer, ulong, ulong, VkIndexType, void>)vkCmdBindIndexBuffer2_ptr.Value)(commandBuffer, buffer, offset, size, indexType);

    public VkResult vkCreateSwapchainKHR(VkSwapchainCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, out VkSwapchainKHR swapchain)
    {
        Unsafe.SkipInit(out swapchain);

        fixed (VkSwapchainKHR* swapchainPtr = &swapchain)
        {
            return ((delegate* unmanaged<VkDevice, VkSwapchainCreateInfoKHR*, VkAllocationCallbacks*, VkSwapchainKHR*, VkResult>)vkCreateSwapchainKHR_ptr.Value)(Device, createInfo, allocator, swapchainPtr);
        }
    }

    public void vkDestroySwapchainKHR(VkSwapchainKHR swapchain, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkDevice, VkSwapchainKHR, VkAllocationCallbacks*, void>)vkDestroySwapchainKHR_ptr.Value)(Device, swapchain, allocator);

    public VkResult vkGetSwapchainImagesKHR(VkSwapchainKHR swapchain, uint* swapchainImageCount, VkImage* swapchainImages) =>
        ((delegate* unmanaged<VkDevice, VkSwapchainKHR, uint*, VkImage*, VkResult>)vkGetSwapchainImagesKHR_ptr.Value)(Device, swapchain, swapchainImageCount, swapchainImages);

    public VkResult vkAcquireNextImageKHR(VkSwapchainKHR swapchain, ulong timeout, VkSemaphore semaphore, VkFence fence, out uint imageIndex)
    {
        Unsafe.SkipInit(out imageIndex);

        fixed (uint* imageIndexPtr = &imageIndex)
        {
            return ((delegate* unmanaged<VkDevice, VkSwapchainKHR, ulong, VkSemaphore, VkFence, uint*, VkResult>)vkAcquireNextImageKHR_ptr.Value)(Device, swapchain, timeout, semaphore, fence, imageIndexPtr);
        }
    }

    public VkResult vkQueuePresentKHR(VkQueue queue, VkPresentInfoKHR* presentInfo) =>
        ((delegate* unmanaged<VkQueue, VkPresentInfoKHR*, VkResult>)vkQueuePresentKHR_ptr.Value)(queue, presentInfo);

    public void vkCmdBindIndexBuffer3KHR(VkCommandBuffer commandBuffer, VkBindIndexBuffer3InfoKHR* info) =>
        ((delegate* unmanaged<VkCommandBuffer, VkBindIndexBuffer3InfoKHR*, void>)vkCmdBindIndexBuffer3KHR_ptr.Value)(commandBuffer, info);

    public void vkCmdDrawIndirect2KHR(VkCommandBuffer commandBuffer, VkDrawIndirect2InfoKHR* info) =>
        ((delegate* unmanaged<VkCommandBuffer, VkDrawIndirect2InfoKHR*, void>)vkCmdDrawIndirect2KHR_ptr.Value)(commandBuffer, info);

    public void vkCmdDrawIndexedIndirect2KHR(VkCommandBuffer commandBuffer, VkDrawIndirect2InfoKHR* info) =>
        ((delegate* unmanaged<VkCommandBuffer, VkDrawIndirect2InfoKHR*, void>)vkCmdDrawIndexedIndirect2KHR_ptr.Value)(commandBuffer, info);

    public void vkCmdDispatchIndirect2KHR(VkCommandBuffer commandBuffer, VkDispatchIndirect2InfoKHR* info) =>
        ((delegate* unmanaged<VkCommandBuffer, VkDispatchIndirect2InfoKHR*, void>)vkCmdDispatchIndirect2KHR_ptr.Value)(commandBuffer, info);

    public void vkCmdCopyMemoryKHR(VkCommandBuffer commandBuffer, VkCopyDeviceMemoryInfoKHR* copyMemoryInfo) =>
        ((delegate* unmanaged<VkCommandBuffer, VkCopyDeviceMemoryInfoKHR*, void>)vkCmdCopyMemoryKHR_ptr.Value)(commandBuffer, copyMemoryInfo);

    public void vkCmdCopyMemoryToImageKHR(VkCommandBuffer commandBuffer, VkCopyDeviceMemoryImageInfoKHR* copyMemoryInfo) =>
        ((delegate* unmanaged<VkCommandBuffer, VkCopyDeviceMemoryImageInfoKHR*, void>)vkCmdCopyMemoryToImageKHR_ptr.Value)(commandBuffer, copyMemoryInfo);

    public void vkCmdCopyImageToMemoryKHR(VkCommandBuffer commandBuffer, VkCopyDeviceMemoryImageInfoKHR* copyMemoryInfo) =>
        ((delegate* unmanaged<VkCommandBuffer, VkCopyDeviceMemoryImageInfoKHR*, void>)vkCmdCopyImageToMemoryKHR_ptr.Value)(commandBuffer, copyMemoryInfo);

    public void vkCmdDrawMeshTasksIndirect2EXT(VkCommandBuffer commandBuffer, VkDrawIndirect2InfoKHR* info) =>
        ((delegate* unmanaged<VkCommandBuffer, VkDrawIndirect2InfoKHR*, void>)vkCmdDrawMeshTasksIndirect2EXT_ptr.Value)(commandBuffer, info);

    public VkResult vkReleaseSwapchainImagesKHR(VkReleaseSwapchainImagesInfoKHR* releaseInfo) =>
        ((delegate* unmanaged<VkDevice, VkReleaseSwapchainImagesInfoKHR*, VkResult>)vkReleaseSwapchainImagesKHR_ptr.Value)(Device, releaseInfo);

    public VkResult vkWriteSamplerDescriptorsEXT(uint samplerCount, VkSamplerCreateInfo* samplers, VkHostAddressRangeEXT* descriptors) =>
        ((delegate* unmanaged<VkDevice, uint, VkSamplerCreateInfo*, VkHostAddressRangeEXT*, VkResult>)vkWriteSamplerDescriptorsEXT_ptr.Value)(Device, samplerCount, samplers, descriptors);

    public VkResult vkWriteResourceDescriptorsEXT(uint resourceCount, VkResourceDescriptorInfoEXT* resources, VkHostAddressRangeEXT* descriptors) =>
        ((delegate* unmanaged<VkDevice, uint, VkResourceDescriptorInfoEXT*, VkHostAddressRangeEXT*, VkResult>)vkWriteResourceDescriptorsEXT_ptr.Value)(Device, resourceCount, resources, descriptors);

    public void vkCmdBindSamplerHeapEXT(VkCommandBuffer commandBuffer, VkBindHeapInfoEXT* bindInfo) =>
        ((delegate* unmanaged<VkCommandBuffer, VkBindHeapInfoEXT*, void>)vkCmdBindSamplerHeapEXT_ptr.Value)(commandBuffer, bindInfo);

    public void vkCmdBindResourceHeapEXT(VkCommandBuffer commandBuffer, VkBindHeapInfoEXT* bindInfo) =>
        ((delegate* unmanaged<VkCommandBuffer, VkBindHeapInfoEXT*, void>)vkCmdBindResourceHeapEXT_ptr.Value)(commandBuffer, bindInfo);

    public void vkCmdPushDataEXT(VkCommandBuffer commandBuffer, VkPushDataInfoEXT* pushDataInfo) =>
        ((delegate* unmanaged<VkCommandBuffer, VkPushDataInfoEXT*, void>)vkCmdPushDataEXT_ptr.Value)(commandBuffer, pushDataInfo);

    public VkResult vkReleaseSwapchainImagesEXT(VkReleaseSwapchainImagesInfoKHR* releaseInfo) =>
        ((delegate* unmanaged<VkDevice, VkReleaseSwapchainImagesInfoKHR*, VkResult>)vkReleaseSwapchainImagesEXT_ptr.Value)(Device, releaseInfo);

    public void vkCmdDrawMeshTasksEXT(VkCommandBuffer commandBuffer, uint groupCountX, uint groupCountY, uint groupCountZ) =>
        ((delegate* unmanaged<VkCommandBuffer, uint, uint, uint, void>)vkCmdDrawMeshTasksEXT_ptr.Value)(commandBuffer, groupCountX, groupCountY, groupCountZ);

    public void vkCmdDrawMeshTasksIndirectEXT(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, uint drawCount, uint stride) =>
        ((delegate* unmanaged<VkCommandBuffer, VkBuffer, ulong, uint, uint, void>)vkCmdDrawMeshTasksIndirectEXT_ptr.Value)(commandBuffer, buffer, offset, drawCount, stride);
}
