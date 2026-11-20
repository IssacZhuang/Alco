// Vulkan buffer, image, sampler, descriptor, and pipeline structs.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vulkan;

internal partial record struct VkExtent2D
{
    public uint width;
    public uint height;

    public VkExtent2D(uint width, uint height) { this.width = width; this.height = height; }
}

internal partial record struct VkExtent3D
{
    public uint width;
    public uint height;
    public uint depth;

    public VkExtent3D(uint width, uint height, uint depth) { this.width = width; this.height = height; this.depth = depth; }
}

internal partial record struct VkOffset2D
{
    public int x;
    public int y;

    public VkOffset2D(int x, int y) { this.x = x; this.y = y; }
}

internal partial record struct VkOffset3D
{
    public int x;
    public int y;
    public int z;

    public VkOffset3D(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
}

internal partial record struct VkRect2D
{
    public VkOffset2D offset;
    public VkExtent2D extent;

    public VkRect2D(int x, int y, uint width, uint height) { offset = new VkOffset2D(x, y); extent = new VkExtent2D(width, height); }
}

internal partial struct VkViewport
{
    public float x;
    public float y;
    public float width;
    public float height;
    public float minDepth;
    public float maxDepth;
}

internal unsafe partial struct VkBufferCreateInfo
{
    public VkStructureType sType = VkStructureType.BufferCreateInfo;
    public void* pNext;
    public VkBufferCreateFlags flags;
    public ulong size;
    public VkBufferUsageFlags usage;
    public VkSharingMode sharingMode;
    public uint queueFamilyIndexCount;
    public uint* pQueueFamilyIndices;
    public VkBufferCreateInfo()
    {
    }
}

internal unsafe partial struct VkBufferDeviceAddressInfo
{
    public VkStructureType sType = VkStructureType.BufferDeviceAddressInfo;
    public void* pNext;
    public VkBuffer buffer;
    public VkBufferDeviceAddressInfo()
    {
    }
}

internal unsafe partial struct VkImageCreateInfo
{
    public VkStructureType sType = VkStructureType.ImageCreateInfo;
    public void* pNext;
    public VkImageCreateFlags flags;
    public VkImageType imageType;
    public VkFormat format;
    public VkExtent3D extent;
    public uint mipLevels;
    public uint arrayLayers;
    public VkSampleCountFlags samples;
    public VkImageTiling tiling;
    public VkImageUsageFlags usage;
    public VkSharingMode sharingMode;
    public uint queueFamilyIndexCount;
    public uint* pQueueFamilyIndices;
    public VkImageLayout initialLayout;
    public VkImageCreateInfo()
    {
    }
}

internal unsafe partial struct VkImageFormatListCreateInfo
{
    public VkStructureType sType = VkStructureType.ImageFormatListCreateInfo;
    public void* pNext;
    public uint viewFormatCount;
    public VkFormat* pViewFormats;
    public VkImageFormatListCreateInfo()
    {
    }
}

internal partial struct VkComponentMapping
{
    public VkComponentSwizzle r;
    public VkComponentSwizzle g;
    public VkComponentSwizzle b;
    public VkComponentSwizzle a;
}

internal partial struct VkImageSubresourceRange
{
    public VkImageAspectFlags aspectMask;
    public uint baseMipLevel;
    public uint levelCount;
    public uint baseArrayLayer;
    public uint layerCount;

    public VkImageSubresourceRange(VkImageAspectFlags aspectMask, uint baseMipLevel = 0, uint levelCount = Vulkan.VK_REMAINING_MIP_LEVELS, uint baseArrayLayer = 0, uint layerCount = Vulkan.VK_REMAINING_ARRAY_LAYERS)
    {
        this.aspectMask = aspectMask;
        this.baseMipLevel = baseMipLevel;
        this.levelCount = levelCount;
        this.baseArrayLayer = baseArrayLayer;
        this.layerCount = layerCount;
    }
}

internal unsafe partial struct VkImageViewCreateInfo
{
    public VkStructureType sType = VkStructureType.ImageViewCreateInfo;
    public void* pNext;
    public VkImageViewCreateFlags flags;
    public VkImage image;
    public VkImageViewType viewType;
    public VkFormat format;
    public VkComponentMapping components;
    public VkImageSubresourceRange subresourceRange;
    public VkImageViewCreateInfo()
    {
    }
}

internal unsafe partial struct VkImageViewUsageCreateInfo
{
    public VkStructureType sType = VkStructureType.ImageViewUsageCreateInfo;
    public void* pNext;
    public VkImageUsageFlags usage;
    public VkImageViewUsageCreateInfo()
    {
    }
}

internal unsafe partial struct VkSamplerCreateInfo
{
    public VkStructureType sType = VkStructureType.SamplerCreateInfo;
    public void* pNext;
    public VkSamplerCreateFlags flags;
    public VkFilter magFilter;
    public VkFilter minFilter;
    public VkSamplerMipmapMode mipmapMode;
    public VkSamplerAddressMode addressModeU;
    public VkSamplerAddressMode addressModeV;
    public VkSamplerAddressMode addressModeW;
    public float mipLodBias;
    public VkBool32 anisotropyEnable;
    public float maxAnisotropy;
    public VkBool32 compareEnable;
    public VkCompareOp compareOp;
    public float minLod;
    public float maxLod;
    public VkBorderColor borderColor;
    public VkBool32 unnormalizedCoordinates;
    public VkSamplerCreateInfo()
    {
    }
}

internal unsafe partial struct VkShaderModuleCreateInfo
{
    public VkStructureType sType = VkStructureType.ShaderModuleCreateInfo;
    public void* pNext;
    public VkShaderModuleCreateFlags flags;
    public nuint codeSize;
    public uint* pCode;
    public VkShaderModuleCreateInfo()
    {
    }
}

internal partial struct VkSpecializationMapEntry
{
    public uint constantID;
    public uint offset;
    public nuint size;
}

internal unsafe partial struct VkSpecializationInfo
{
    public uint mapEntryCount;
    public VkSpecializationMapEntry* pMapEntries;
    public nuint dataSize;
    public void* pData;
}

internal unsafe partial struct VkPipelineCacheCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineCacheCreateInfo;
    public void* pNext;
    public VkPipelineCacheCreateFlags flags;
    public nuint initialDataSize;
    public void* pInitialData;
    public VkPipelineCacheCreateInfo()
    {
    }
}

internal unsafe partial struct VkPipelineShaderStageCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineShaderStageCreateInfo;
    public void* pNext;
    public VkPipelineShaderStageCreateFlags flags;
    public VkShaderStageFlags stage;
    public VkShaderModule module;
    public byte* pName;
    public VkSpecializationInfo* pSpecializationInfo;
    public VkPipelineShaderStageCreateInfo()
    {
    }
}

internal partial struct VkVertexInputBindingDescription
{
    public uint binding;
    public uint stride;
    public VkVertexInputRate inputRate;
}

internal partial struct VkVertexInputAttributeDescription
{
    public uint location;
    public uint binding;
    public VkFormat format;
    public uint offset;
}

internal unsafe partial struct VkPipelineVertexInputStateCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineVertexInputStateCreateInfo;
    public void* pNext;
    public VkPipelineVertexInputStateCreateFlags flags;
    public uint vertexBindingDescriptionCount;
    public VkVertexInputBindingDescription* pVertexBindingDescriptions;
    public uint vertexAttributeDescriptionCount;
    public VkVertexInputAttributeDescription* pVertexAttributeDescriptions;
    public VkPipelineVertexInputStateCreateInfo()
    {
    }
}

internal unsafe partial struct VkPipelineInputAssemblyStateCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineInputAssemblyStateCreateInfo;
    public void* pNext;
    public VkPipelineInputAssemblyStateCreateFlags flags;
    public VkPrimitiveTopology topology;
    public VkBool32 primitiveRestartEnable;
    public VkPipelineInputAssemblyStateCreateInfo()
    {
    }
}

internal unsafe partial struct VkPipelineTessellationStateCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineTessellationStateCreateInfo;
    public void* pNext;
    public VkPipelineTessellationStateCreateFlags flags;
    public uint patchControlPoints;
    public VkPipelineTessellationStateCreateInfo()
    {
    }
}

internal unsafe partial struct VkPipelineViewportStateCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineViewportStateCreateInfo;
    public void* pNext;
    public VkPipelineViewportStateCreateFlags flags;
    public uint viewportCount;
    public VkViewport* pViewports;
    public uint scissorCount;
    public VkRect2D* pScissors;
    public VkPipelineViewportStateCreateInfo()
    {
    }
}

internal unsafe partial struct VkPipelineRasterizationStateCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineRasterizationStateCreateInfo;
    public void* pNext;
    public VkPipelineRasterizationStateCreateFlags flags;
    public VkBool32 depthClampEnable;
    public VkBool32 rasterizerDiscardEnable;
    public VkPolygonMode polygonMode;
    public VkCullModeFlags cullMode;
    public VkFrontFace frontFace;
    public VkBool32 depthBiasEnable;
    public float depthBiasConstantFactor;
    public float depthBiasClamp;
    public float depthBiasSlopeFactor;
    public float lineWidth;
    public VkPipelineRasterizationStateCreateInfo()
    {
    }
}

internal unsafe partial struct VkPipelineMultisampleStateCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineMultisampleStateCreateInfo;
    public void* pNext;
    public VkPipelineMultisampleStateCreateFlags flags;
    public VkSampleCountFlags rasterizationSamples;
    public VkBool32 sampleShadingEnable;
    public float minSampleShading;
    public uint* pSampleMask;
    public VkBool32 alphaToCoverageEnable;
    public VkBool32 alphaToOneEnable;
    public VkPipelineMultisampleStateCreateInfo()
    {
    }
}

internal partial struct VkStencilOpState
{
    public VkStencilOp failOp;
    public VkStencilOp passOp;
    public VkStencilOp depthFailOp;
    public VkCompareOp compareOp;
    public uint compareMask;
    public uint writeMask;
    public uint reference;
}

internal unsafe partial struct VkPipelineDepthStencilStateCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineDepthStencilStateCreateInfo;
    public void* pNext;
    public VkPipelineDepthStencilStateCreateFlags flags;
    public VkBool32 depthTestEnable;
    public VkBool32 depthWriteEnable;
    public VkCompareOp depthCompareOp;
    public VkBool32 depthBoundsTestEnable;
    public VkBool32 stencilTestEnable;
    public VkStencilOpState front;
    public VkStencilOpState back;
    public float minDepthBounds;
    public float maxDepthBounds;
    public VkPipelineDepthStencilStateCreateInfo()
    {
    }
}

internal partial struct VkPipelineColorBlendAttachmentState
{
    public VkBool32 blendEnable;
    public VkBlendFactor srcColorBlendFactor;
    public VkBlendFactor dstColorBlendFactor;
    public VkBlendOp colorBlendOp;
    public VkBlendFactor srcAlphaBlendFactor;
    public VkBlendFactor dstAlphaBlendFactor;
    public VkBlendOp alphaBlendOp;
    public VkColorComponentFlags colorWriteMask;
}

internal unsafe partial struct VkPipelineColorBlendStateCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineColorBlendStateCreateInfo;
    public void* pNext;
    public VkPipelineColorBlendStateCreateFlags flags;
    public VkBool32 logicOpEnable;
    public VkLogicOp logicOp;
    public uint attachmentCount;
    public VkPipelineColorBlendAttachmentState* pAttachments;
    public fixed float blendConstants[4];
    public VkPipelineColorBlendStateCreateInfo()
    {
    }
}

internal unsafe partial struct VkPipelineDynamicStateCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineDynamicStateCreateInfo;
    public void* pNext;
    public VkPipelineDynamicStateCreateFlags flags;
    public uint dynamicStateCount;
    public VkDynamicState* pDynamicStates;
    public VkPipelineDynamicStateCreateInfo()
    {
    }
}

internal unsafe partial struct VkPipelineRenderingCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineRenderingCreateInfo;
    public void* pNext;
    public uint viewMask;
    public uint colorAttachmentCount;
    public VkFormat* pColorAttachmentFormats;
    public VkFormat depthAttachmentFormat;
    public VkFormat stencilAttachmentFormat;
    public VkPipelineRenderingCreateInfo()
    {
    }
}

internal unsafe partial struct VkPipelineCreateFlags2CreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineCreateFlags2CreateInfo;
    public void* pNext;
    public VkPipelineCreateFlags2 flags;
    public VkPipelineCreateFlags2CreateInfo()
    {
    }
}

internal unsafe partial struct VkGraphicsPipelineCreateInfo
{
    public VkStructureType sType = VkStructureType.GraphicsPipelineCreateInfo;
    public void* pNext;
    public VkPipelineCreateFlags flags;
    public uint stageCount;
    public VkPipelineShaderStageCreateInfo* pStages;
    public VkPipelineVertexInputStateCreateInfo* pVertexInputState;
    public VkPipelineInputAssemblyStateCreateInfo* pInputAssemblyState;
    public VkPipelineTessellationStateCreateInfo* pTessellationState;
    public VkPipelineViewportStateCreateInfo* pViewportState;
    public VkPipelineRasterizationStateCreateInfo* pRasterizationState;
    public VkPipelineMultisampleStateCreateInfo* pMultisampleState;
    public VkPipelineDepthStencilStateCreateInfo* pDepthStencilState;
    public VkPipelineColorBlendStateCreateInfo* pColorBlendState;
    public VkPipelineDynamicStateCreateInfo* pDynamicState;
    public VkPipelineLayout layout;
    public VkRenderPass renderPass;
    public uint subpass;
    public VkPipeline basePipelineHandle;
    public int basePipelineIndex;
    public VkGraphicsPipelineCreateInfo()
    {
    }
}

internal unsafe partial struct VkComputePipelineCreateInfo
{
    public VkStructureType sType = VkStructureType.ComputePipelineCreateInfo;
    public void* pNext;
    public VkPipelineCreateFlags flags;
    public VkPipelineShaderStageCreateInfo stage;
    public VkPipelineLayout layout;
    public VkPipeline basePipelineHandle;
    public int basePipelineIndex;
    public VkComputePipelineCreateInfo()
    {
    }
}

internal partial struct VkPushConstantRange
{
    public VkShaderStageFlags stageFlags;
    public uint offset;
    public uint size;
}

internal unsafe partial struct VkPipelineLayoutCreateInfo
{
    public VkStructureType sType = VkStructureType.PipelineLayoutCreateInfo;
    public void* pNext;
    public VkPipelineLayoutCreateFlags flags;
    public uint setLayoutCount;
    public VkDescriptorSetLayout* pSetLayouts;
    public uint pushConstantRangeCount;
    public VkPushConstantRange* pPushConstantRanges;
    public VkPipelineLayoutCreateInfo()
    {
    }
}

internal partial struct VkDescriptorPoolSize
{
    public VkDescriptorType type;
    public uint descriptorCount;
}

internal unsafe partial struct VkDescriptorPoolCreateInfo
{
    public VkStructureType sType = VkStructureType.DescriptorPoolCreateInfo;
    public void* pNext;
    public VkDescriptorPoolCreateFlags flags;
    public uint maxSets;
    public uint poolSizeCount;
    public VkDescriptorPoolSize* pPoolSizes;
    public VkDescriptorPoolCreateInfo()
    {
    }
}

internal unsafe partial struct VkDescriptorSetLayoutBinding
{
    public uint binding;
    public VkDescriptorType descriptorType;
    public uint descriptorCount;
    public VkShaderStageFlags stageFlags;
    public VkSampler* pImmutableSamplers;
}

internal unsafe partial struct VkDescriptorSetLayoutBindingFlagsCreateInfo
{
    public VkStructureType sType = VkStructureType.DescriptorSetLayoutBindingFlagsCreateInfo;
    public void* pNext;
    public uint bindingCount;
    public VkDescriptorBindingFlags* pBindingFlags;
    public VkDescriptorSetLayoutBindingFlagsCreateInfo()
    {
    }
}

internal unsafe partial struct VkDescriptorSetLayoutCreateInfo
{
    public VkStructureType sType = VkStructureType.DescriptorSetLayoutCreateInfo;
    public void* pNext;
    public VkDescriptorSetLayoutCreateFlags flags;
    public uint bindingCount;
    public VkDescriptorSetLayoutBinding* pBindings;
    public VkDescriptorSetLayoutCreateInfo()
    {
    }
}

internal unsafe partial struct VkDescriptorSetLayoutSupport
{
    public VkStructureType sType = VkStructureType.DescriptorSetLayoutSupport;
    public void* pNext;
    public VkBool32 supported;
    public VkDescriptorSetLayoutSupport()
    {
    }
}

internal unsafe partial struct VkDescriptorSetVariableDescriptorCountLayoutSupport
{
    public VkStructureType sType = VkStructureType.DescriptorSetVariableDescriptorCountLayoutSupport;
    public void* pNext;
    public uint maxVariableDescriptorCount;
    public VkDescriptorSetVariableDescriptorCountLayoutSupport()
    {
    }
}

internal unsafe partial struct VkDescriptorSetAllocateInfo
{
    public VkStructureType sType = VkStructureType.DescriptorSetAllocateInfo;
    public void* pNext;
    public VkDescriptorPool descriptorPool;
    public uint descriptorSetCount;
    public VkDescriptorSetLayout* pSetLayouts;
    public VkDescriptorSetAllocateInfo()
    {
    }
}

internal unsafe partial struct VkDescriptorSetVariableDescriptorCountAllocateInfo
{
    public VkStructureType sType = VkStructureType.DescriptorSetVariableDescriptorCountAllocateInfo;
    public void* pNext;
    public uint descriptorSetCount;
    public uint* pDescriptorCounts;
    public VkDescriptorSetVariableDescriptorCountAllocateInfo()
    {
    }
}

internal partial struct VkDescriptorImageInfo
{
    public VkSampler sampler;
    public VkImageView imageView;
    public VkImageLayout imageLayout;
}

internal partial struct VkDescriptorBufferInfo
{
    public VkBuffer buffer;
    public ulong offset;
    public ulong range;
}

internal unsafe partial struct VkWriteDescriptorSet
{
    public VkStructureType sType = VkStructureType.WriteDescriptorSet;
    public void* pNext;
    public VkDescriptorSet dstSet;
    public uint dstBinding;
    public uint dstArrayElement;
    public uint descriptorCount;
    public VkDescriptorType descriptorType;
    public VkDescriptorImageInfo* pImageInfo;
    public VkDescriptorBufferInfo* pBufferInfo;
    public VkBufferView* pTexelBufferView;
    public VkWriteDescriptorSet()
    {
    }
}

internal unsafe partial struct VkCopyDescriptorSet
{
    public VkStructureType sType = VkStructureType.CopyDescriptorSet;
    public void* pNext;
    public VkDescriptorSet srcSet;
    public uint srcBinding;
    public uint srcArrayElement;
    public VkDescriptorSet dstSet;
    public uint dstBinding;
    public uint dstArrayElement;
    public uint descriptorCount;
    public VkCopyDescriptorSet()
    {
    }
}

internal unsafe partial struct VkDescriptorSetAndBindingMappingEXT
{
    public VkStructureType sType = VkStructureType.DescriptorSetAndBindingMappingEXT;
    public void* pNext;
    public uint descriptorSet;
    public uint firstBinding;
    public uint bindingCount;
    public VkSpirvResourceTypeFlagsEXT resourceMask;
    public VkDescriptorMappingSourceEXT source;
    public VkDescriptorMappingSourceDataEXT sourceData;
    public VkDescriptorSetAndBindingMappingEXT()
    {
    }
}

[StructLayout(LayoutKind.Explicit)]
internal unsafe partial struct VkDescriptorMappingSourceDataEXT
{
    // VkDescriptorMappingSourceIndirectIndexEXT determines the union's size and alignment.
    [FieldOffset(0)] private readonly IndirectIndexLayout _indirectIndex;
    [FieldOffset(0)] public uint pushAddressOffset;

    [StructLayout(LayoutKind.Sequential)]
    private struct IndirectIndexLayout
    {
        private fixed uint _heapFields[5];
        private readonly VkSamplerCreateInfo* _embeddedSampler;
        private fixed uint _samplerFields[6];
    }
}

internal unsafe partial struct VkShaderDescriptorSetAndBindingMappingInfoEXT
{
    public VkStructureType sType = VkStructureType.ShaderDescriptorSetAndBindingMappingInfoEXT;
    public void* pNext;
    public uint mappingCount;
    public VkDescriptorSetAndBindingMappingEXT* pMappings;
    public VkShaderDescriptorSetAndBindingMappingInfoEXT()
    {
    }
}

internal unsafe partial struct VkHostAddressRangeEXT
{
    public void* address;
    public nuint size;
}

internal unsafe partial struct VkHostAddressRangeConstEXT
{
    public void* address;
    public nuint size;
}

internal partial struct VkDeviceAddressRangeKHR
{
    public ulong address;
    public ulong size;
}

internal partial struct VkStridedDeviceAddressRangeKHR
{
    public ulong address;
    public ulong size;
    public ulong stride;
}

internal unsafe partial struct VkImageDescriptorInfoEXT
{
    public VkStructureType sType = VkStructureType.ImageDescriptorInfoEXT;
    public void* pNext;
    public VkImageViewCreateInfo* pView;
    public VkImageLayout layout;
    public VkImageDescriptorInfoEXT()
    {
    }
}

[StructLayout(LayoutKind.Explicit)]
internal unsafe partial struct VkResourceDescriptorDataEXT
{
    [FieldOffset(0)] public VkImageDescriptorInfoEXT* pImage;
    [FieldOffset(0)] public VkDeviceAddressRangeKHR* pAddressRange;
}

internal unsafe partial struct VkResourceDescriptorInfoEXT
{
    public VkStructureType sType = VkStructureType.ResourceDescriptorInfoEXT;
    public void* pNext;
    public VkDescriptorType type;
    public VkResourceDescriptorDataEXT data;
    public VkResourceDescriptorInfoEXT()
    {
    }
}

internal unsafe partial struct VkBindHeapInfoEXT
{
    public VkStructureType sType = VkStructureType.BindHeapInfoEXT;
    public void* pNext;
    public VkDeviceAddressRangeKHR heapRange;
    public ulong reservedRangeOffset;
    public ulong reservedRangeSize;
    public VkBindHeapInfoEXT()
    {
    }
}
