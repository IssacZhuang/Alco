// Vulkan instance, physical device, memory, queue, and debug structs.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vulkan;

internal unsafe partial struct VkAllocationCallbacks
{
    public void* pUserData;
    public delegate* unmanaged<void*, nuint, nuint, VkSystemAllocationScope, void*> pfnAllocation;
    public delegate* unmanaged<void*, void*, nuint, nuint, VkSystemAllocationScope, void*> pfnReallocation;
    public delegate* unmanaged<void*, void*, void> pfnFree;
    public delegate* unmanaged<void*, nuint, VkInternalAllocationType, VkSystemAllocationScope, void> pfnInternalAllocation;
    public delegate* unmanaged<void*, nuint, VkInternalAllocationType, VkSystemAllocationScope, void> pfnInternalFree;
}

internal unsafe partial struct VkApplicationInfo
{
    public VkStructureType sType = VkStructureType.ApplicationInfo;
    public void* pNext;
    public byte* pApplicationName;
    public VkVersion applicationVersion;
    public byte* pEngineName;
    public VkVersion engineVersion;
    public VkVersion apiVersion;
    public VkApplicationInfo()
    {
    }
}

internal unsafe partial struct VkInstanceCreateInfo
{
    public VkStructureType sType = VkStructureType.InstanceCreateInfo;
    public void* pNext;
    public VkInstanceCreateFlags flags;
    public VkApplicationInfo* pApplicationInfo;
    public uint enabledLayerCount;
    public byte** ppEnabledLayerNames;
    public uint enabledExtensionCount;
    public byte** ppEnabledExtensionNames;
    public VkInstanceCreateInfo()
    {
    }
}

internal unsafe partial struct VkLayerProperties
{
    public fixed byte layerName[256];
    public VkVersion specVersion;
    public uint implementationVersion;
    public fixed byte description[256];
}

internal unsafe partial struct VkExtensionProperties
{
    public fixed byte extensionName[256];
    public VkVersion specVersion;
}

internal partial struct VkConformanceVersion
{
    public byte major;
    public byte minor;
    public byte subminor;
    public byte patch;
}

internal unsafe partial struct VkPhysicalDeviceLimits
{
    public uint maxImageDimension1D;
    public uint maxImageDimension2D;
    public uint maxImageDimension3D;
    public uint maxImageDimensionCube;
    public uint maxImageArrayLayers;
    public uint maxTexelBufferElements;
    public uint maxUniformBufferRange;
    public uint maxStorageBufferRange;
    public uint maxPushConstantsSize;
    public uint maxMemoryAllocationCount;
    public uint maxSamplerAllocationCount;
    public ulong bufferImageGranularity;
    public ulong sparseAddressSpaceSize;
    public uint maxBoundDescriptorSets;
    public uint maxPerStageDescriptorSamplers;
    public uint maxPerStageDescriptorUniformBuffers;
    public uint maxPerStageDescriptorStorageBuffers;
    public uint maxPerStageDescriptorSampledImages;
    public uint maxPerStageDescriptorStorageImages;
    public uint maxPerStageDescriptorInputAttachments;
    public uint maxPerStageResources;
    public uint maxDescriptorSetSamplers;
    public uint maxDescriptorSetUniformBuffers;
    public uint maxDescriptorSetUniformBuffersDynamic;
    public uint maxDescriptorSetStorageBuffers;
    public uint maxDescriptorSetStorageBuffersDynamic;
    public uint maxDescriptorSetSampledImages;
    public uint maxDescriptorSetStorageImages;
    public uint maxDescriptorSetInputAttachments;
    public uint maxVertexInputAttributes;
    public uint maxVertexInputBindings;
    public uint maxVertexInputAttributeOffset;
    public uint maxVertexInputBindingStride;
    public uint maxVertexOutputComponents;
    public uint maxTessellationGenerationLevel;
    public uint maxTessellationPatchSize;
    public uint maxTessellationControlPerVertexInputComponents;
    public uint maxTessellationControlPerVertexOutputComponents;
    public uint maxTessellationControlPerPatchOutputComponents;
    public uint maxTessellationControlTotalOutputComponents;
    public uint maxTessellationEvaluationInputComponents;
    public uint maxTessellationEvaluationOutputComponents;
    public uint maxGeometryShaderInvocations;
    public uint maxGeometryInputComponents;
    public uint maxGeometryOutputComponents;
    public uint maxGeometryOutputVertices;
    public uint maxGeometryTotalOutputComponents;
    public uint maxFragmentInputComponents;
    public uint maxFragmentOutputAttachments;
    public uint maxFragmentDualSrcAttachments;
    public uint maxFragmentCombinedOutputResources;
    public uint maxComputeSharedMemorySize;
    public fixed uint maxComputeWorkGroupCount[3];
    public uint maxComputeWorkGroupInvocations;
    public fixed uint maxComputeWorkGroupSize[3];
    public uint subPixelPrecisionBits;
    public uint subTexelPrecisionBits;
    public uint mipmapPrecisionBits;
    public uint maxDrawIndexedIndexValue;
    public uint maxDrawIndirectCount;
    public float maxSamplerLodBias;
    public float maxSamplerAnisotropy;
    public uint maxViewports;
    public fixed uint maxViewportDimensions[2];
    public fixed float viewportBoundsRange[2];
    public uint viewportSubPixelBits;
    public nuint minMemoryMapAlignment;
    public ulong minTexelBufferOffsetAlignment;
    public ulong minUniformBufferOffsetAlignment;
    public ulong minStorageBufferOffsetAlignment;
    public int minTexelOffset;
    public uint maxTexelOffset;
    public int minTexelGatherOffset;
    public uint maxTexelGatherOffset;
    public float minInterpolationOffset;
    public float maxInterpolationOffset;
    public uint subPixelInterpolationOffsetBits;
    public uint maxFramebufferWidth;
    public uint maxFramebufferHeight;
    public uint maxFramebufferLayers;
    public VkSampleCountFlags framebufferColorSampleCounts;
    public VkSampleCountFlags framebufferDepthSampleCounts;
    public VkSampleCountFlags framebufferStencilSampleCounts;
    public VkSampleCountFlags framebufferNoAttachmentsSampleCounts;
    public uint maxColorAttachments;
    public VkSampleCountFlags sampledImageColorSampleCounts;
    public VkSampleCountFlags sampledImageIntegerSampleCounts;
    public VkSampleCountFlags sampledImageDepthSampleCounts;
    public VkSampleCountFlags sampledImageStencilSampleCounts;
    public VkSampleCountFlags storageImageSampleCounts;
    public uint maxSampleMaskWords;
    public VkBool32 timestampComputeAndGraphics;
    public float timestampPeriod;
    public uint maxClipDistances;
    public uint maxCullDistances;
    public uint maxCombinedClipAndCullDistances;
    public uint discreteQueuePriorities;
    public fixed float pointSizeRange[2];
    public fixed float lineWidthRange[2];
    public float pointSizeGranularity;
    public float lineWidthGranularity;
    public VkBool32 strictLines;
    public VkBool32 standardSampleLocations;
    public ulong optimalBufferCopyOffsetAlignment;
    public ulong optimalBufferCopyRowPitchAlignment;
    public ulong nonCoherentAtomSize;
}

internal partial struct VkPhysicalDeviceSparseProperties
{
    public VkBool32 residencyStandard2DBlockShape;
    public VkBool32 residencyStandard2DMultisampleBlockShape;
    public VkBool32 residencyStandard3DBlockShape;
    public VkBool32 residencyAlignedMipSize;
    public VkBool32 residencyNonResidentStrict;
}

internal unsafe partial struct VkPhysicalDeviceProperties
{
    public VkVersion apiVersion;
    public uint driverVersion;
    public uint vendorID;
    public uint deviceID;
    public VkPhysicalDeviceType deviceType;
    public fixed byte deviceName[256];
    public fixed byte pipelineCacheUUID[16];
    public VkPhysicalDeviceLimits limits;
    public VkPhysicalDeviceSparseProperties sparseProperties;
}

internal unsafe partial struct VkPhysicalDeviceProperties2
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceProperties2;
    public void* pNext;
    public VkPhysicalDeviceProperties properties;
    public VkPhysicalDeviceProperties2()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceVulkan12Properties
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceVulkan12Properties;
    public void* pNext;
    public VkDriverId driverID;
    public fixed byte driverName[256];
    public fixed byte driverInfo[256];
    public VkConformanceVersion conformanceVersion;
    public VkShaderFloatControlsIndependence denormBehaviorIndependence;
    public VkShaderFloatControlsIndependence roundingModeIndependence;
    public VkBool32 shaderSignedZeroInfNanPreserveFloat16;
    public VkBool32 shaderSignedZeroInfNanPreserveFloat32;
    public VkBool32 shaderSignedZeroInfNanPreserveFloat64;
    public VkBool32 shaderDenormPreserveFloat16;
    public VkBool32 shaderDenormPreserveFloat32;
    public VkBool32 shaderDenormPreserveFloat64;
    public VkBool32 shaderDenormFlushToZeroFloat16;
    public VkBool32 shaderDenormFlushToZeroFloat32;
    public VkBool32 shaderDenormFlushToZeroFloat64;
    public VkBool32 shaderRoundingModeRTEFloat16;
    public VkBool32 shaderRoundingModeRTEFloat32;
    public VkBool32 shaderRoundingModeRTEFloat64;
    public VkBool32 shaderRoundingModeRTZFloat16;
    public VkBool32 shaderRoundingModeRTZFloat32;
    public VkBool32 shaderRoundingModeRTZFloat64;
    public uint maxUpdateAfterBindDescriptorsInAllPools;
    public VkBool32 shaderUniformBufferArrayNonUniformIndexingNative;
    public VkBool32 shaderSampledImageArrayNonUniformIndexingNative;
    public VkBool32 shaderStorageBufferArrayNonUniformIndexingNative;
    public VkBool32 shaderStorageImageArrayNonUniformIndexingNative;
    public VkBool32 shaderInputAttachmentArrayNonUniformIndexingNative;
    public VkBool32 robustBufferAccessUpdateAfterBind;
    public VkBool32 quadDivergentImplicitLod;
    public uint maxPerStageDescriptorUpdateAfterBindSamplers;
    public uint maxPerStageDescriptorUpdateAfterBindUniformBuffers;
    public uint maxPerStageDescriptorUpdateAfterBindStorageBuffers;
    public uint maxPerStageDescriptorUpdateAfterBindSampledImages;
    public uint maxPerStageDescriptorUpdateAfterBindStorageImages;
    public uint maxPerStageDescriptorUpdateAfterBindInputAttachments;
    public uint maxPerStageUpdateAfterBindResources;
    public uint maxDescriptorSetUpdateAfterBindSamplers;
    public uint maxDescriptorSetUpdateAfterBindUniformBuffers;
    public uint maxDescriptorSetUpdateAfterBindUniformBuffersDynamic;
    public uint maxDescriptorSetUpdateAfterBindStorageBuffers;
    public uint maxDescriptorSetUpdateAfterBindStorageBuffersDynamic;
    public uint maxDescriptorSetUpdateAfterBindSampledImages;
    public uint maxDescriptorSetUpdateAfterBindStorageImages;
    public uint maxDescriptorSetUpdateAfterBindInputAttachments;
    public VkResolveModeFlags supportedDepthResolveModes;
    public VkResolveModeFlags supportedStencilResolveModes;
    public VkBool32 independentResolveNone;
    public VkBool32 independentResolve;
    public VkBool32 filterMinmaxSingleComponentFormats;
    public VkBool32 filterMinmaxImageComponentMapping;
    public ulong maxTimelineSemaphoreValueDifference;
    public VkSampleCountFlags framebufferIntegerColorSampleCounts;
    public VkPhysicalDeviceVulkan12Properties()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceDescriptorHeapPropertiesEXT
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceDescriptorHeapPropertiesEXT;
    public void* pNext;
    public ulong samplerHeapAlignment;
    public ulong resourceHeapAlignment;
    public ulong maxSamplerHeapSize;
    public ulong maxResourceHeapSize;
    public ulong minSamplerHeapReservedRange;
    public ulong minSamplerHeapReservedRangeWithEmbedded;
    public ulong minResourceHeapReservedRange;
    public ulong samplerDescriptorSize;
    public ulong imageDescriptorSize;
    public ulong bufferDescriptorSize;
    public ulong samplerDescriptorAlignment;
    public ulong imageDescriptorAlignment;
    public ulong bufferDescriptorAlignment;
    public ulong maxPushDataSize;
    public nuint imageCaptureReplayOpaqueDataSize;
    public uint maxDescriptorHeapEmbeddedSamplers;
    public uint samplerYcbcrConversionCount;
    public VkBool32 sparseDescriptorHeaps;
    public VkBool32 protectedDescriptorHeaps;
    public VkPhysicalDeviceDescriptorHeapPropertiesEXT()
    {
    }
}

internal partial struct VkMemoryType
{
    public VkMemoryPropertyFlags propertyFlags;
    public uint heapIndex;
}

internal partial struct VkMemoryHeap
{
    public ulong size;
    public VkMemoryHeapFlags flags;
}

internal partial struct VkPhysicalDeviceMemoryProperties
{
    public uint memoryTypeCount;
    public memoryTypes__FixedBuffer memoryTypes;
    [InlineArray(32)]
    public partial struct memoryTypes__FixedBuffer
    {
        public VkMemoryType e0;
    }
    public uint memoryHeapCount;
    public memoryHeaps__FixedBuffer memoryHeaps;
    [InlineArray(16)]
    public partial struct memoryHeaps__FixedBuffer
    {
        public VkMemoryHeap e0;
    }
}

internal partial struct VkQueueFamilyProperties
{
    public VkQueueFlags queueFlags;
    public uint queueCount;
    public uint timestampValidBits;
    public VkExtent3D minImageTransferGranularity;
}

internal partial struct VkPhysicalDeviceFeatures
{
    public VkBool32 robustBufferAccess;
    public VkBool32 fullDrawIndexUint32;
    public VkBool32 imageCubeArray;
    public VkBool32 independentBlend;
    public VkBool32 geometryShader;
    public VkBool32 tessellationShader;
    public VkBool32 sampleRateShading;
    public VkBool32 dualSrcBlend;
    public VkBool32 logicOp;
    public VkBool32 multiDrawIndirect;
    public VkBool32 drawIndirectFirstInstance;
    public VkBool32 depthClamp;
    public VkBool32 depthBiasClamp;
    public VkBool32 fillModeNonSolid;
    public VkBool32 depthBounds;
    public VkBool32 wideLines;
    public VkBool32 largePoints;
    public VkBool32 alphaToOne;
    public VkBool32 multiViewport;
    public VkBool32 samplerAnisotropy;
    public VkBool32 textureCompressionETC2;
    public VkBool32 textureCompressionASTC_LDR;
    public VkBool32 textureCompressionBC;
    public VkBool32 occlusionQueryPrecise;
    public VkBool32 pipelineStatisticsQuery;
    public VkBool32 vertexPipelineStoresAndAtomics;
    public VkBool32 fragmentStoresAndAtomics;
    public VkBool32 shaderTessellationAndGeometryPointSize;
    public VkBool32 shaderImageGatherExtended;
    public VkBool32 shaderStorageImageExtendedFormats;
    public VkBool32 shaderStorageImageMultisample;
    public VkBool32 shaderStorageImageReadWithoutFormat;
    public VkBool32 shaderStorageImageWriteWithoutFormat;
    public VkBool32 shaderUniformBufferArrayDynamicIndexing;
    public VkBool32 shaderSampledImageArrayDynamicIndexing;
    public VkBool32 shaderStorageBufferArrayDynamicIndexing;
    public VkBool32 shaderStorageImageArrayDynamicIndexing;
    public VkBool32 shaderClipDistance;
    public VkBool32 shaderCullDistance;
    public VkBool32 shaderFloat64;
    public VkBool32 shaderInt64;
    public VkBool32 shaderInt16;
    public VkBool32 shaderResourceResidency;
    public VkBool32 shaderResourceMinLod;
    public VkBool32 sparseBinding;
    public VkBool32 sparseResidencyBuffer;
    public VkBool32 sparseResidencyImage2D;
    public VkBool32 sparseResidencyImage3D;
    public VkBool32 sparseResidency2Samples;
    public VkBool32 sparseResidency4Samples;
    public VkBool32 sparseResidency8Samples;
    public VkBool32 sparseResidency16Samples;
    public VkBool32 sparseResidencyAliased;
    public VkBool32 variableMultisampleRate;
    public VkBool32 inheritedQueries;
}

internal unsafe partial struct VkPhysicalDeviceFeatures2
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceFeatures2;
    public void* pNext;
    public VkPhysicalDeviceFeatures features;
    public VkPhysicalDeviceFeatures2()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceVulkan11Features
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceVulkan11Features;
    public void* pNext;
    public VkBool32 storageBuffer16BitAccess;
    public VkBool32 uniformAndStorageBuffer16BitAccess;
    public VkBool32 storagePushConstant16;
    public VkBool32 storageInputOutput16;
    public VkBool32 multiview;
    public VkBool32 multiviewGeometryShader;
    public VkBool32 multiviewTessellationShader;
    public VkBool32 variablePointersStorageBuffer;
    public VkBool32 variablePointers;
    public VkBool32 protectedMemory;
    public VkBool32 samplerYcbcrConversion;
    public VkBool32 shaderDrawParameters;
    public VkPhysicalDeviceVulkan11Features()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceVulkan12Features
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceVulkan12Features;
    public void* pNext;
    public VkBool32 samplerMirrorClampToEdge;
    public VkBool32 drawIndirectCount;
    public VkBool32 storageBuffer8BitAccess;
    public VkBool32 uniformAndStorageBuffer8BitAccess;
    public VkBool32 storagePushConstant8;
    public VkBool32 shaderBufferInt64Atomics;
    public VkBool32 shaderSharedInt64Atomics;
    public VkBool32 shaderFloat16;
    public VkBool32 shaderInt8;
    public VkBool32 descriptorIndexing;
    public VkBool32 shaderInputAttachmentArrayDynamicIndexing;
    public VkBool32 shaderUniformTexelBufferArrayDynamicIndexing;
    public VkBool32 shaderStorageTexelBufferArrayDynamicIndexing;
    public VkBool32 shaderUniformBufferArrayNonUniformIndexing;
    public VkBool32 shaderSampledImageArrayNonUniformIndexing;
    public VkBool32 shaderStorageBufferArrayNonUniformIndexing;
    public VkBool32 shaderStorageImageArrayNonUniformIndexing;
    public VkBool32 shaderInputAttachmentArrayNonUniformIndexing;
    public VkBool32 shaderUniformTexelBufferArrayNonUniformIndexing;
    public VkBool32 shaderStorageTexelBufferArrayNonUniformIndexing;
    public VkBool32 descriptorBindingUniformBufferUpdateAfterBind;
    public VkBool32 descriptorBindingSampledImageUpdateAfterBind;
    public VkBool32 descriptorBindingStorageImageUpdateAfterBind;
    public VkBool32 descriptorBindingStorageBufferUpdateAfterBind;
    public VkBool32 descriptorBindingUniformTexelBufferUpdateAfterBind;
    public VkBool32 descriptorBindingStorageTexelBufferUpdateAfterBind;
    public VkBool32 descriptorBindingUpdateUnusedWhilePending;
    public VkBool32 descriptorBindingPartiallyBound;
    public VkBool32 descriptorBindingVariableDescriptorCount;
    public VkBool32 runtimeDescriptorArray;
    public VkBool32 samplerFilterMinmax;
    public VkBool32 scalarBlockLayout;
    public VkBool32 imagelessFramebuffer;
    public VkBool32 uniformBufferStandardLayout;
    public VkBool32 shaderSubgroupExtendedTypes;
    public VkBool32 separateDepthStencilLayouts;
    public VkBool32 hostQueryReset;
    public VkBool32 timelineSemaphore;
    public VkBool32 bufferDeviceAddress;
    public VkBool32 bufferDeviceAddressCaptureReplay;
    public VkBool32 bufferDeviceAddressMultiDevice;
    public VkBool32 vulkanMemoryModel;
    public VkBool32 vulkanMemoryModelDeviceScope;
    public VkBool32 vulkanMemoryModelAvailabilityVisibilityChains;
    public VkBool32 shaderOutputViewportIndex;
    public VkBool32 shaderOutputLayer;
    public VkBool32 subgroupBroadcastDynamicId;
    public VkPhysicalDeviceVulkan12Features()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceVulkan13Features
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceVulkan13Features;
    public void* pNext;
    public VkBool32 robustImageAccess;
    public VkBool32 inlineUniformBlock;
    public VkBool32 descriptorBindingInlineUniformBlockUpdateAfterBind;
    public VkBool32 pipelineCreationCacheControl;
    public VkBool32 privateData;
    public VkBool32 shaderDemoteToHelperInvocation;
    public VkBool32 shaderTerminateInvocation;
    public VkBool32 subgroupSizeControl;
    public VkBool32 computeFullSubgroups;
    public VkBool32 synchronization2;
    public VkBool32 textureCompressionASTC_HDR;
    public VkBool32 shaderZeroInitializeWorkgroupMemory;
    public VkBool32 dynamicRendering;
    public VkBool32 shaderIntegerDotProduct;
    public VkBool32 maintenance4;
    public VkPhysicalDeviceVulkan13Features()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceVulkan14Features
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceVulkan14Features;
    public void* pNext;
    public VkBool32 globalPriorityQuery;
    public VkBool32 shaderSubgroupRotate;
    public VkBool32 shaderSubgroupRotateClustered;
    public VkBool32 shaderFloatControls2;
    public VkBool32 shaderExpectAssume;
    public VkBool32 rectangularLines;
    public VkBool32 bresenhamLines;
    public VkBool32 smoothLines;
    public VkBool32 stippledRectangularLines;
    public VkBool32 stippledBresenhamLines;
    public VkBool32 stippledSmoothLines;
    public VkBool32 vertexAttributeInstanceRateDivisor;
    public VkBool32 vertexAttributeInstanceRateZeroDivisor;
    public VkBool32 indexTypeUint8;
    public VkBool32 dynamicRenderingLocalRead;
    public VkBool32 maintenance5;
    public VkBool32 maintenance6;
    public VkBool32 pipelineProtectedAccess;
    public VkBool32 pipelineRobustness;
    public VkBool32 hostImageCopy;
    public VkBool32 pushDescriptor;
    public VkPhysicalDeviceVulkan14Features()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceSynchronization2Features
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceSynchronization2Features;
    public void* pNext;
    public VkBool32 synchronization2;
    public VkPhysicalDeviceSynchronization2Features()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceDynamicRenderingFeatures
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceDynamicRenderingFeatures;
    public void* pNext;
    public VkBool32 dynamicRendering;
    public VkPhysicalDeviceDynamicRenderingFeatures()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceMaintenance4Features
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceMaintenance4Features;
    public void* pNext;
    public VkBool32 maintenance4;
    public VkPhysicalDeviceMaintenance4Features()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceExtendedDynamicStateFeaturesEXT
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceExtendedDynamicStateFeaturesEXT;
    public void* pNext;
    public VkBool32 extendedDynamicState;
    public VkPhysicalDeviceExtendedDynamicStateFeaturesEXT()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceDescriptorHeapFeaturesEXT
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceDescriptorHeapFeaturesEXT;
    public void* pNext;
    public VkBool32 descriptorHeap;
    public VkBool32 descriptorHeapCaptureReplay;
    public VkPhysicalDeviceDescriptorHeapFeaturesEXT()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceDeviceAddressCommandsFeaturesKHR
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceDeviceAddressCommandsFeaturesKHR;
    public void* pNext;
    public VkBool32 deviceAddressCommands;
    public VkPhysicalDeviceDeviceAddressCommandsFeaturesKHR()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceShaderUntypedPointersFeaturesKHR
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceShaderUntypedPointersFeaturesKHR;
    public void* pNext;
    public VkBool32 shaderUntypedPointers;
    public VkPhysicalDeviceShaderUntypedPointersFeaturesKHR()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceUnifiedImageLayoutsFeaturesKHR
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceUnifiedImageLayoutsFeaturesKHR;
    public void* pNext;
    public VkBool32 unifiedImageLayouts;
    public VkBool32 unifiedImageLayoutsVideo;
    public VkPhysicalDeviceUnifiedImageLayoutsFeaturesKHR()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceMeshShaderFeaturesEXT
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceMeshShaderFeaturesEXT;
    public void* pNext;
    public VkBool32 taskShader;
    public VkBool32 meshShader;
    public VkBool32 multiviewMeshShader;
    public VkBool32 primitiveFragmentShadingRateMeshShader;
    public VkBool32 meshShaderQueries;
    public VkPhysicalDeviceMeshShaderFeaturesEXT()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceSwapchainMaintenance1FeaturesKHR;
    public void* pNext;
    public VkBool32 swapchainMaintenance1;
    public VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR()
    {
    }
}

internal unsafe partial struct VkDeviceQueueCreateInfo
{
    public VkStructureType sType = VkStructureType.DeviceQueueCreateInfo;
    public void* pNext;
    public VkDeviceQueueCreateFlags flags;
    public uint queueFamilyIndex;
    public uint queueCount;
    public float* pQueuePriorities;
    public VkDeviceQueueCreateInfo()
    {
    }
}

internal unsafe partial struct VkDeviceCreateInfo
{
    public VkStructureType sType = VkStructureType.DeviceCreateInfo;
    public void* pNext;
    public VkDeviceCreateFlags flags;
    public uint queueCreateInfoCount;
    public VkDeviceQueueCreateInfo* pQueueCreateInfos;
    public uint enabledLayerCount;
    public byte** ppEnabledLayerNames;
    public uint enabledExtensionCount;
    public byte** ppEnabledExtensionNames;
    public VkPhysicalDeviceFeatures* pEnabledFeatures;
    public VkDeviceCreateInfo()
    {
    }
}

internal unsafe partial struct VkDebugUtilsLabelEXT
{
    public VkStructureType sType = VkStructureType.DebugUtilsLabelEXT;
    public void* pNext;
    public byte* pLabelName;
    public fixed float color[4];
    public VkDebugUtilsLabelEXT()
    {
    }
}

internal unsafe partial struct VkDebugUtilsObjectNameInfoEXT
{
    public VkStructureType sType = VkStructureType.DebugUtilsObjectNameInfoEXT;
    public void* pNext;
    public VkObjectType objectType;
    public ulong objectHandle;
    public byte* pObjectName;
    public VkDebugUtilsObjectNameInfoEXT()
    {
    }
}

internal unsafe partial struct VkDebugUtilsMessengerCallbackDataEXT
{
    public VkStructureType sType = VkStructureType.DebugUtilsMessengerCallbackDataEXT;
    public void* pNext;
    public VkDebugUtilsMessengerCallbackDataFlagsEXT flags;
    public byte* pMessageIdName;
    public int messageIdNumber;
    public byte* pMessage;
    public uint queueLabelCount;
    public VkDebugUtilsLabelEXT* pQueueLabels;
    public uint cmdBufLabelCount;
    public VkDebugUtilsLabelEXT* pCmdBufLabels;
    public uint objectCount;
    public VkDebugUtilsObjectNameInfoEXT* pObjects;
    public VkDebugUtilsMessengerCallbackDataEXT()
    {
    }
}

internal unsafe partial struct VkDebugUtilsMessengerCreateInfoEXT
{
    public VkStructureType sType = VkStructureType.DebugUtilsMessengerCreateInfoEXT;
    public void* pNext;
    public VkDebugUtilsMessengerCreateFlagsEXT flags;
    public VkDebugUtilsMessageSeverityFlagsEXT messageSeverity;
    public VkDebugUtilsMessageTypeFlagsEXT messageType;
    public delegate* unmanaged<VkDebugUtilsMessageSeverityFlagsEXT, VkDebugUtilsMessageTypeFlagsEXT, VkDebugUtilsMessengerCallbackDataEXT*, void*, uint> pfnUserCallback;
    public void* pUserData;
    public VkDebugUtilsMessengerCreateInfoEXT()
    {
    }
}

internal unsafe partial struct VkMemoryAllocateFlagsInfo
{
    public VkStructureType sType = VkStructureType.MemoryAllocateFlagsInfo;
    public void* pNext;
    public VkMemoryAllocateFlags flags;
    public uint deviceMask;
    public VkMemoryAllocateFlagsInfo()
    {
    }
}

internal unsafe partial struct VkMemoryAllocateInfo
{
    public VkStructureType sType = VkStructureType.MemoryAllocateInfo;
    public void* pNext;
    public ulong allocationSize;
    public uint memoryTypeIndex;
    public VkMemoryAllocateInfo()
    {
    }
}

internal partial struct VkMemoryRequirements
{
    public ulong size;
    public ulong alignment;
    public uint memoryTypeBits;
}

internal unsafe partial struct VkMemoryRequirements2
{
    public VkStructureType sType = VkStructureType.MemoryRequirements2;
    public void* pNext;
    public VkMemoryRequirements memoryRequirements;
    public VkMemoryRequirements2()
    {
    }
}

internal unsafe partial struct VkDeviceBufferMemoryRequirements
{
    public VkStructureType sType = VkStructureType.DeviceBufferMemoryRequirements;
    public void* pNext;
    public VkBufferCreateInfo* pCreateInfo;
    public VkDeviceBufferMemoryRequirements()
    {
    }
}

internal unsafe partial struct VkDeviceImageMemoryRequirements
{
    public VkStructureType sType = VkStructureType.DeviceImageMemoryRequirements;
    public void* pNext;
    public VkImageCreateInfo* pCreateInfo;
    public VkImageAspectFlags planeAspect;
    public VkDeviceImageMemoryRequirements()
    {
    }
}

internal partial struct VkFormatProperties
{
    public VkFormatFeatureFlags linearTilingFeatures;
    public VkFormatFeatureFlags optimalTilingFeatures;
    public VkFormatFeatureFlags bufferFeatures;
}

internal unsafe partial struct VkFormatProperties2
{
    public VkStructureType sType = VkStructureType.FormatProperties2;
    public void* pNext;
    public VkFormatProperties formatProperties;
    public VkFormatProperties2()
    {
    }
}

internal unsafe partial struct VkFormatProperties3
{
    public VkStructureType sType = VkStructureType.FormatProperties3;
    public void* pNext;
    public VkFormatFeatureFlags2 linearTilingFeatures;
    public VkFormatFeatureFlags2 optimalTilingFeatures;
    public VkFormatFeatureFlags2 bufferFeatures;
    public VkFormatProperties3()
    {
    }
}

internal unsafe partial struct VkPhysicalDeviceImageFormatInfo2
{
    public VkStructureType sType = VkStructureType.PhysicalDeviceImageFormatInfo2;
    public void* pNext;
    public VkFormat format;
    public VkImageType type;
    public VkImageTiling tiling;
    public VkImageUsageFlags usage;
    public VkImageCreateFlags flags;
    public VkPhysicalDeviceImageFormatInfo2()
    {
    }
}

internal partial struct VkImageFormatProperties
{
    public VkExtent3D maxExtent;
    public uint maxMipLevels;
    public uint maxArrayLayers;
    public VkSampleCountFlags sampleCounts;
    public ulong maxResourceSize;
}

internal unsafe partial struct VkImageFormatProperties2
{
    public VkStructureType sType = VkStructureType.ImageFormatProperties2;
    public void* pNext;
    public VkImageFormatProperties imageFormatProperties;
    public VkImageFormatProperties2()
    {
    }
}
