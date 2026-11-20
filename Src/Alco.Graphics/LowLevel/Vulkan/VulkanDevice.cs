// Vulkan backend for Alco.Graphics.
// External synchronization everywhere; immediate destruction; asserts for programmer errors.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Numerics;
using Vk = Vulkan;
using static global::Alco.Graphics.LowLevel.VulkanBackend;
using static Vulkan.Vulkan;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal unsafe struct BackingBuffer
{
    public Vk.VkBuffer Buffer;
    public Vk.VkDeviceMemory Memory;
    public void* mapped;
    public ulong Address;
}

internal sealed unsafe partial class VulkanGpuHeapOwner : GPUHeapOwner
{
    internal VulkanDeviceBase Device = null!;
    internal BackingBuffer Backing;
}

internal sealed unsafe partial class VulkanTextureHeapOwner : GPUTextureHeapOwner
{
    internal VulkanDeviceBase Device = null!;
    internal Vk.VkDeviceMemory Memory;
}

internal sealed unsafe partial class VulkanTextureDescriptorHeap : GPUTextureDescriptorHeap
{
    internal VulkanDeviceBase Owner = null!;
    internal GPUHeap Storage;
    internal byte[]? Descriptors;
}

internal sealed unsafe partial class VulkanSamplerDescriptorHeap : GPUSamplerDescriptorHeap
{
    internal VulkanDeviceBase Owner = null!;
    internal GPUHeap Storage;
    internal byte[]? Descriptors;
}

internal sealed unsafe partial class VulkanTimelineSemaphore : GPUTimelineSemaphore
{
    internal VulkanDeviceBase Owner = null!;
    internal Vk.VkSemaphore Semaphore;
}

internal sealed unsafe partial class VulkanTexture : GPUTexture
{
    internal VulkanDeviceBase Owner = null!;
    internal VulkanSwapchain? Swapchain;
    internal Vk.VkImage Image;
    internal uint Width;
    internal uint Height;
    internal uint Depth;
    internal uint LayerCount;
    internal uint SampleCount;
    internal TextureType Type;
    internal Format Format;
    internal TextureUsage Usage;
}

internal sealed unsafe partial class VulkanRenderView : GPURenderView
{
    internal VulkanDeviceBase Owner = null!;
    internal required VulkanTexture Source;
    internal Vk.VkImageView View;
    internal uint Width;
    internal uint Height;
    internal bool SwapchainView;
}

internal sealed unsafe partial class VulkanPso : GPUPipeline
{
    internal VulkanDeviceBase Owner = null!;
    internal Vk.VkPipeline Pipeline;
    internal Vk.VkPipelineBindPoint BindPoint;
}

internal abstract unsafe partial class VulkanCommandBuffer : GPUCommandBuffer
{
    internal VulkanDeviceBase Owner = null!;
    internal VulkanCommandBuffer? Next;
    internal Vk.VkCommandPool CommandPool;
    internal Vk.VkCommandBuffer Handle;
    internal Vk.VkQueryPool TimestampPool;
    internal ulong*[]? TimestampDestinations;
    internal ulong[]? TimestampResults;
    internal VulkanSwapchain? Swapchain;
    internal uint QueueIndex;
    internal bool Submitted;
    internal GPURange BoundIndices;
    internal IndexType BoundIndexType;
    internal bool HasIndexBinding;
}

internal sealed unsafe partial class VulkanCommandPool : GPUCommandPool
{
    internal VulkanDeviceBase Owner = null!;
    internal Vk.VkCommandPool Handle;
    internal VulkanCommandBuffer? First;
    internal VulkanCommandBuffer? Last;
    internal VulkanCommandBuffer? NextBuffer;
    internal uint QueueIndex;
    internal bool Timestamps;
}

internal sealed unsafe class Queue
{
    internal Vk.VkQueue Handle;
    internal uint FamilyIndex;
    internal bool Timestamps;
    internal Vk.VkCommandBufferSubmitInfo* CommandSubmitInfos;
    internal Vk.VkSemaphoreSubmitInfo* WaitSubmitInfos;
    internal uint CommandSubmitCapacity;
    internal uint WaitSubmitCapacity;
}

// Feature support snapshot extracted from a chained pNext query; see queryFeatureSupport.
internal sealed class FeatureSupport
{
    public bool ShaderInt16;
    public bool ShaderInt64;
    public bool SamplerAnisotropy;
    public bool DepthBiasClamp;
    public bool FillModeNonSolid;
    public bool IndependentBlend;
    public bool FragmentStoresAndAtomics;
    public bool VertexPipelineStoresAndAtomics;
    public bool ShaderStorageImageReadWithoutFormat;
    public bool ShaderStorageImageWriteWithoutFormat;
    public bool MultiDrawIndirect;
    public bool DrawIndirectFirstInstance;
    public bool ImageCubeArray;
    public bool TextureCompressionBc;
    public bool TextureCompressionAstc;
    public bool TextureCompressionEtc2;
    public bool StorageBuffer16;
    public bool StoragePushConstant16;
    public bool StorageInputOutput16;
    public bool ShaderDrawParameters;
    public bool ShaderFloat16;
    public bool ScalarBlockLayout;
    public bool BufferDeviceAddress;
    public bool TimelineSemaphore;
    public bool HostQueryReset;
    public bool Synchronization2;
    public bool DynamicRendering;
    public bool Maintenance4;
    public bool ExtendedDynamicState;
    public bool BindlessDescriptors;
    public bool Maintenance5;
    public bool DescriptorHeap;
    public bool DeviceAddressCommands;
    public bool ShaderUntypedPointers;
    public bool UnifiedImageLayouts;
    public bool MeshShader;
    public bool SwapchainMaintenance1;
}

internal sealed class Candidate
{
    internal Vk.VkPhysicalDevice PhysicalDevice;
    internal uint[] QueueFamilies = new uint[Backend.QueueTypeCount];
    internal uint[] QueueCounts = new uint[Backend.QueueTypeCount];
    internal bool[] Timestamps = new bool[Backend.QueueTypeCount];
    internal Extent3D CopyTextureGranularity = new(1, 1, 1);
    internal Vk.VkPhysicalDeviceProperties Properties;
    internal Vk.VkPhysicalDeviceMemoryProperties MemoryProperties;
    internal bool UnifiedImageLayouts;
    internal bool AddressCommands;
    internal bool ImageCubeArray;
    internal bool TextureCompressionBc;
    internal bool TextureCompressionAstc;
    internal bool TextureCompressionEtc2;
    internal bool StorageInputOutput16;
    internal bool KhrSwapchainMaintenance1;
    internal Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT HeapProperties;
    internal Vk.VkPhysicalDeviceVulkan12Properties Vulkan12Properties;
    internal string DeviceName = "";
    internal string[] MissingExtensions = Array.Empty<string>();
}

/// <summary>Shared Vulkan device state and resource operations.</summary>
internal abstract unsafe partial class VulkanDeviceBase : GPUDevice
{
    protected VulkanDeviceBase(GraphicsBackend backend) : base(backend) { }

    internal Vk.VkInstance Instance;
    internal bool InstanceCreated;
    internal Vk.VkInstanceApi InstanceApi = null!;
    internal Vk.VkDebugUtilsMessengerEXT DebugMessenger;
    internal bool DebugMessengerCreated;
    internal bool DebugMarkers;
    internal Vk.VkPhysicalDevice PhysicalDevice;
    internal Vk.VkDevice VkDevice;
    internal bool DeviceCreated;
    internal Vk.VkDeviceApi Api = null!;
    internal VulkanNativeDispatch Dispatch;
    internal Vk.VkAccessFlags2 DescriptorReadAccess;
    internal Queue[]? Queues;
    internal uint[] QueueFamilies = new uint[Backend.QueueTypeCount];
    internal uint QueueFamilyCount;
    internal Vk.VkPhysicalDeviceMemoryProperties MemoryProperties;
    internal Vk.VkPhysicalDeviceProperties PhysicalProperties;
    internal Vk.VkPhysicalDeviceVulkan12Properties Vulkan12Properties;
    internal Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT HeapProperties;
    internal ulong MaxTimelineValueDifference;
    internal ulong TextureHeapAlignment = 16;
    internal uint TextureMemoryType;
    internal bool TextureMemoryTypeValid;
    internal Vk.VkFormatFeatureFlags2[] FormatFeatures = new Vk.VkFormatFeatureFlags2[Backend.FormatCount];
    internal bool TextureCompressionEtc2;
    internal bool PresentationEnabled;
    internal bool SwapchainColorSpaceEnabled;
    internal bool Win32SurfaceEnabled;
    internal bool XlibSurfaceEnabled;
    internal bool XcbSurfaceEnabled;
    internal bool WaylandSurfaceEnabled;
    internal bool KhrSwapchainMaintenance1;
    internal int SwapchainCount;
    internal bool AddressCommands;
    internal BackingBuffer BufferViewTable;
    internal BufferViewSlots? BufferViews;
    private bool ShaderBindingStarted;

    internal virtual void InitializeShaderBinding() { }
    internal virtual void DestroyShaderBinding() { }

    internal bool FindMemoryType(uint bits, Vk.VkMemoryPropertyFlags required, Vk.VkMemoryPropertyFlags preferred, ulong minimumHeapSize,
        out uint output, Vk.VkMemoryPropertyFlags avoided = 0)
    {
        bool hasBest = false;
        bool bestIsAvoided = false;
        uint best = 0;
        uint bestScore = 0;
        ulong bestHeapSize = 0;
        for (uint i = 0; i < MemoryProperties.memoryTypeCount; ++i)
        {
            if ((bits & (1u << (int)i)) == 0)
                continue;
            Vk.VkMemoryPropertyFlags flags = MemoryProperties.memoryTypes[(int)i].propertyFlags;
            if ((flags & required) != required)
                continue;
            if (!Backend.IsUsableMemoryType(in MemoryProperties, i))
                continue;
            ref readonly Vk.VkMemoryHeap heap = ref MemoryProperties.memoryHeaps[(int)MemoryProperties.memoryTypes[(int)i].heapIndex];
            if (heap.size < minimumHeapSize)
                continue;
            bool isAvoided = (flags & avoided) != 0;
            uint score = (uint)BitOperations.PopCount((ulong)(flags & preferred));
            if (!hasBest || (bestIsAvoided && !isAvoided) ||
                (bestIsAvoided == isAvoided &&
                 (score > bestScore || (score == bestScore && heap.size > bestHeapSize))))
            {
                best = i;
                hasBest = true;
                bestIsAvoided = isAvoided;
                bestScore = score;
                bestHeapSize = heap.size;
            }
        }
        output = best;
        return hasBest;
    }

    internal BackingBuffer CreateBackingBuffer(ulong size, Vk.VkBufferUsageFlags usage, Vk.VkMemoryPropertyFlags required,
                                               Vk.VkMemoryPropertyFlags preferred, Vk.VkMemoryPropertyFlags avoided = 0)
    {
        BackingBuffer result = default;
        bool created = false;
        try
        {
            fixed (uint* queueFamilies = this.QueueFamilies)
            {
                Vk.VkBufferCreateInfo bufferInfo = new()
                {
                    size = size,
                    usage = usage,
                    sharingMode = QueueFamilyCount > 1 ? Vk.VkSharingMode.Concurrent : Vk.VkSharingMode.Exclusive,
                    queueFamilyIndexCount = QueueFamilyCount,
                    pQueueFamilyIndices = queueFamilies,
                };
                Backend.CheckVk(Api.vkCreateBuffer(&bufferInfo, null, out result.Buffer), "vkCreateBuffer", size);

                Api.vkGetBufferMemoryRequirements(result.Buffer, out Vk.VkMemoryRequirements requirements);
                if (!FindMemoryType(requirements.memoryTypeBits, required, preferred, requirements.size, out uint memoryType, avoided))
                {
                    throw CreationFailure(Error.Unsupported,
                        $"No compatible {((required & Vk.VkMemoryPropertyFlags.HostVisible) != 0
                            ? "CPU-visible" : "device-local")} GPU memory heap can hold {requirements.size / (1024.0 * 1024.0):F2} MiB.");
                }

                Vk.VkMemoryAllocateFlagsInfo flagsInfo = new() { flags = Vk.VkMemoryAllocateFlags.DeviceAddress };
                Vk.VkMemoryAllocateInfo allocateInfo = new()
                {
                    pNext = &flagsInfo,
                    allocationSize = requirements.size,
                    memoryTypeIndex = memoryType,
                };
                string operation = (required & Vk.VkMemoryPropertyFlags.HostVisible) != 0
                    ? "vkAllocateMemory (CPU-visible GPU heap)" : "vkAllocateMemory (GPU-only heap)";
                Backend.CheckVk(Api.vkAllocateMemory(&allocateInfo, null, out result.Memory), operation, requirements.size);
                Backend.CheckVk(Api.vkBindBufferMemory(result.Buffer, result.Memory, 0), "vkBindBufferMemory", requirements.size);
            }

            if ((required & Vk.VkMemoryPropertyFlags.HostVisible) != 0)
            {
                void* mapped = null;
                Backend.CheckVk(Api.vkMapMemory(result.Memory, 0, ulong.MaxValue, 0, &mapped), "vkMapMemory", size);
                result.mapped = mapped;
            }

            Vk.VkBufferDeviceAddressInfo addressInfo = new() { buffer = result.Buffer };
            result.Address = Api.vkGetBufferDeviceAddress(&addressInfo);
            created = true;
            return result;
        }
        finally
        {
            if (!created)
            {
                if (result.mapped != null) Api.vkUnmapMemory(result.Memory);
                if (!result.Buffer.IsNull) Api.vkDestroyBuffer(result.Buffer, null);
                if (!result.Memory.IsNull) Api.vkFreeMemory(result.Memory, null);
            }
        }
    }

    /// <summary>Allocates a linear GPU heap on this device.</summary>
    /// <param name="size">The heap size in bytes.</param>
    /// <param name="memory">The heap's memory type.</param>
    /// <returns>The allocated heap.</returns>
    public override GPUHeap AllocateGPUHeap(ulong size, MemoryType memory)
    {
        Vk.VkMemoryPropertyFlags required = 0;
        Vk.VkMemoryPropertyFlags preferred = 0;
        Vk.VkMemoryPropertyFlags avoided = 0;
        switch (memory)
        {
            case MemoryType.CpuVisible:
                required = Backend.CpuVisibleMemoryProperties;
                break;
            case MemoryType.GpuOnly:
                required = Vk.VkMemoryPropertyFlags.DeviceLocal;
                avoided = Vk.VkMemoryPropertyFlags.HostVisible;
                break;
            case MemoryType.Readback:
                required = Backend.CpuVisibleMemoryProperties;
                preferred = Vk.VkMemoryPropertyFlags.HostCached;
                break;
            default:
                Debug.Assert(false, "createGpuHeap received an invalid memory type");
                throw CreationFailure(Error.Unsupported, "The requested GPU heap memory type is unsupported.");
        }

        VulkanGpuHeapOwner heap = new()
        {
            Device = this,
            Backing = CreateBackingBuffer(size, Backend.UniversalBufferUsage, required, preferred, avoided),
        };
        heap.ViewIndex = BufferViews!.Allocate(heap.Backing.Address);
        if (heap.ViewIndex == 0)
        {
            if (heap.Backing.mapped != null) Api.vkUnmapMemory(heap.Backing.Memory);
            Api.vkDestroyBuffer(heap.Backing.Buffer, null);
            Api.vkFreeMemory(heap.Backing.Memory, null);
            throw CreationFailure(Error.Unsupported, "The device's 65,535 backing-buffer view slots are exhausted.");
        }
        return new GPUHeap
        {
            Range = new GPUAllocation<byte>
            {
                Cpu = (byte*)heap.Backing.mapped,
                Buffer = heap,
                SizeBytes = size,
            },
            Owner = heap,
        };
    }

    internal GPUHeap AllocateDescriptorHeap(ulong size, DescriptorHeapType type)
    {
        ulong resourceAlignment = HeapProperties.imageDescriptorAlignment > HeapProperties.bufferDescriptorAlignment
                                       ? HeapProperties.imageDescriptorAlignment
                                       : HeapProperties.bufferDescriptorAlignment;
        ulong reservedAlignment = type == DescriptorHeapType.Texture ? resourceAlignment : HeapProperties.samplerDescriptorAlignment;
        ulong heapAlignment = type == DescriptorHeapType.Texture ? HeapProperties.resourceHeapAlignment : HeapProperties.samplerHeapAlignment;
        ulong reservedSize = type == DescriptorHeapType.Texture
            ? HeapProperties.minResourceHeapReservedRange : HeapProperties.minSamplerHeapReservedRange;

        ulong reservedOffset = Backend.AlignUp(size, reservedAlignment);
        ulong bindSize = reservedOffset + reservedSize;
        ulong allocationAlignment = heapAlignment > Backend.GpuAllocationAlignment ? heapAlignment : Backend.GpuAllocationAlignment;
        ulong alignmentPadding = allocationAlignment - 1;
        ulong backingSize = bindSize + alignmentPadding;

        VulkanGpuHeapOwner heap = new()
        {
            Device = this,
            Backing = CreateBackingBuffer(backingSize, Backend.UniversalBufferUsage | Vk.VkBufferUsageFlags.DescriptorHeapEXT,
                                          Backend.CpuVisibleMemoryProperties, 0),
        };

        ulong gpuAddress = Backend.AlignUp(heap.Backing.Address, allocationAlignment);
        ulong allocationOffset = gpuAddress - heap.Backing.Address;
        return new GPUHeap
        {
            Range = new GPUAllocation<byte>
            {
                Cpu = (byte*)heap.Backing.mapped + allocationOffset,
                Buffer = heap,
                Offset = allocationOffset,
                SizeBytes = size,
            },
            Owner = heap,
        };
    }

    /// <summary>Releases the device after its resources have been destroyed and all submitted work has completed.</summary>
    public override void Destroy()
    {
        Debug.Assert(Volatile.Read(ref SwapchainCount) == 0, "destroy every swapchain before its device");
        if (ShaderBindingStarted)
        {
            DestroyShaderBinding();
            ShaderBindingStarted = false;
        }
        if (Queues != null)
        {
            foreach (Queue queue in Queues)
            {
                NativeMemory.Free(queue.CommandSubmitInfos);
                NativeMemory.Free(queue.WaitSubmitInfos);
            }
        }
        Queues = null;
        BufferViews = null;
        if (!BufferViewTable.Memory.IsNull)
        {
            Api.vkUnmapMemory(BufferViewTable.Memory);
            Api.vkDestroyBuffer(BufferViewTable.Buffer, null);
            Api.vkFreeMemory(BufferViewTable.Memory, null);
            BufferViewTable = default;
        }
        DestroyPipelineCache();
        if (DeviceCreated) Api.vkDestroyDevice(null);
#if DEBUG
        if (InstanceCreated && DebugMessengerCreated) InstanceApi.vkDestroyDebugUtilsMessengerEXT(DebugMessenger, null);
#endif
        if (InstanceCreated) InstanceApi.vkDestroyInstance(null);
        InstanceCreated = false;
        DeviceCreated = false;
    }
}

internal static unsafe partial class VulkanBackend
{
    internal static readonly Lock VulkanInitializationLock = new();
    internal static bool VulkanInitialized;

    [DllImport("kernel32")]
    internal static extern nint GetModuleHandleW(nint fileName);

#if DEBUG
    [UnmanagedCallersOnly]
    internal static uint DebugCallback(Vk.VkDebugUtilsMessageSeverityFlagsEXT severity, Vk.VkDebugUtilsMessageTypeFlagsEXT types,
                                       Vk.VkDebugUtilsMessengerCallbackDataEXT* callbackData, void* userData)
    {
        if ((severity & (Vk.VkDebugUtilsMessageSeverityFlagsEXT.Warning | Vk.VkDebugUtilsMessageSeverityFlagsEXT.Error)) != 0 &&
            callbackData != null && callbackData->pMessage != null)
        {
            Console.Error.WriteLine($"Alco.Graphics validation: {Backend.ReadVkString(callbackData->pMessage)}");
        }
        return 0;
    }
#endif

    private static Error EnumerateDeviceExtensions(Vk.VkInstanceApi api, Vk.VkPhysicalDevice PhysicalDevice, Vk.VkExtensionProperties[] values, out uint count)
    {
        fixed (Vk.VkExtensionProperties* pointer = values)
        {
            uint available = (uint)values.Length;
            Vk.VkResult result = api.vkEnumerateDeviceExtensionProperties(PhysicalDevice, null, &available, pointer);
            count = available;
            if (result == Vk.VkResult.Incomplete)
                return Error.Unsupported;
            Backend.CheckVk(result, "vkEnumerateDeviceExtensionProperties");
            return Error.None;
        }
    }

    internal static uint EnumerateInstanceExtensions(Vk.VkExtensionProperties[] values)
    {
        fixed (Vk.VkExtensionProperties* pointer = values)
        {
            uint available = (uint)values.Length;
            Vk.VkResult result = Vk.Vulkan.vkEnumerateInstanceExtensionProperties(&available, pointer);
            if (result == Vk.VkResult.Incomplete)
                throw CreationFailure(Error.Unsupported, "The Vulkan instance extension list exceeds the supported capacity.");
            Backend.CheckVk(result, "vkEnumerateInstanceExtensionProperties");
            return available;
        }
    }

#if DEBUG
    internal static uint EnumerateInstanceLayers(Vk.VkLayerProperties[] values)
    {
        fixed (Vk.VkLayerProperties* pointer = values)
        {
            uint available = (uint)values.Length;
            Vk.VkResult result = Vk.Vulkan.vkEnumerateInstanceLayerProperties(&available, pointer);
            if (result == Vk.VkResult.Incomplete)
                throw CreationFailure(Error.Unsupported, "The Vulkan instance layer list exceeds the supported capacity.");
            Backend.CheckVk(result, "vkEnumerateInstanceLayerProperties");
            return available;
        }
    }
#endif

    private static FeatureSupport QueryFeatureSupport(Vk.VkInstanceApi api, Vk.VkPhysicalDevice physicalDevice,
        bool compact, bool presentation, bool includeUnifiedImageLayouts, bool includeAddressCommands)
    {
        Vk.VkPhysicalDeviceFeatures2 core = new();
        Vk.VkPhysicalDeviceVulkan11Features vulkan11 = new();
        Vk.VkPhysicalDeviceVulkan12Features vulkan12 = new();
        Vk.VkPhysicalDeviceVulkan13Features vulkan13 = new();
        Vk.VkPhysicalDeviceVulkan14Features vulkan14 = new();
        Vk.VkPhysicalDeviceSynchronization2Features synchronization2 = new();
        Vk.VkPhysicalDeviceDynamicRenderingFeatures dynamicRendering = new();
        Vk.VkPhysicalDeviceMaintenance4Features maintenance4 = new();
        Vk.VkPhysicalDeviceExtendedDynamicStateFeaturesEXT extendedDynamicState = new();
        Vk.VkPhysicalDeviceDescriptorHeapFeaturesEXT descriptorHeap = new();
        Vk.VkPhysicalDeviceDeviceAddressCommandsFeaturesKHR addressCommands = new();
        Vk.VkPhysicalDeviceShaderUntypedPointersFeaturesKHR untypedPointers = new();
        Vk.VkPhysicalDeviceUnifiedImageLayoutsFeaturesKHR unifiedImageLayouts = new();
        Vk.VkPhysicalDeviceMeshShaderFeaturesEXT meshShader = new();
        Vk.VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR swapchainMaintenance1 = new();
        void* presentationFeatures = presentation ? &swapchainMaintenance1 : null;

        core.pNext = &vulkan11;
        vulkan11.pNext = &vulkan12;
        if (compact)
        {
            vulkan12.pNext = &synchronization2;
            synchronization2.pNext = &dynamicRendering;
            dynamicRendering.pNext = &maintenance4;
            maintenance4.pNext = &extendedDynamicState;
            extendedDynamicState.pNext = presentationFeatures;
        }
        else
        {
            vulkan12.pNext = &vulkan13;
            vulkan13.pNext = &vulkan14;
            vulkan14.pNext = &descriptorHeap;
            descriptorHeap.pNext = includeAddressCommands ? &addressCommands : (void*)&untypedPointers;
            addressCommands.pNext = &untypedPointers;
            untypedPointers.pNext = includeUnifiedImageLayouts ? &unifiedImageLayouts : (void*)&meshShader;
            unifiedImageLayouts.pNext = &meshShader;
            meshShader.pNext = presentationFeatures;
        }
        api.vkGetPhysicalDeviceFeatures2(physicalDevice, &core);

        return new FeatureSupport
        {
            ShaderInt16 = core.features.shaderInt16,
            ShaderInt64 = core.features.shaderInt64,
            SamplerAnisotropy = core.features.samplerAnisotropy,
            DepthBiasClamp = core.features.depthBiasClamp,
            FillModeNonSolid = core.features.fillModeNonSolid,
            IndependentBlend = core.features.independentBlend,
            FragmentStoresAndAtomics = core.features.fragmentStoresAndAtomics,
            VertexPipelineStoresAndAtomics = core.features.vertexPipelineStoresAndAtomics,
            ShaderStorageImageReadWithoutFormat = core.features.shaderStorageImageReadWithoutFormat,
            ShaderStorageImageWriteWithoutFormat = core.features.shaderStorageImageWriteWithoutFormat,
            MultiDrawIndirect = core.features.multiDrawIndirect,
            DrawIndirectFirstInstance = core.features.drawIndirectFirstInstance,
            ImageCubeArray = core.features.imageCubeArray,
            TextureCompressionBc = core.features.textureCompressionBC,
            TextureCompressionAstc = core.features.textureCompressionASTC_LDR,
            TextureCompressionEtc2 = core.features.textureCompressionETC2,
            StorageBuffer16 = vulkan11.storageBuffer16BitAccess,
            StoragePushConstant16 = vulkan11.storagePushConstant16,
            StorageInputOutput16 = vulkan11.storageInputOutput16,
            ShaderDrawParameters = vulkan11.shaderDrawParameters,
            ShaderFloat16 = vulkan12.shaderFloat16,
            ScalarBlockLayout = vulkan12.scalarBlockLayout,
            BufferDeviceAddress = vulkan12.bufferDeviceAddress,
            TimelineSemaphore = vulkan12.timelineSemaphore,
            HostQueryReset = vulkan12.hostQueryReset,
            Synchronization2 = compact ? synchronization2.synchronization2 : vulkan13.synchronization2,
            DynamicRendering = compact ? dynamicRendering.dynamicRendering : vulkan13.dynamicRendering,
            Maintenance4 = compact ? maintenance4.maintenance4 : vulkan13.maintenance4,
            ExtendedDynamicState = extendedDynamicState.extendedDynamicState,
            BindlessDescriptors = core.features.shaderSampledImageArrayDynamicIndexing && core.features.shaderStorageImageArrayDynamicIndexing &&
                vulkan12.descriptorIndexing && vulkan12.runtimeDescriptorArray &&
                vulkan12.shaderSampledImageArrayNonUniformIndexing && vulkan12.shaderStorageImageArrayNonUniformIndexing &&
                vulkan12.descriptorBindingSampledImageUpdateAfterBind && vulkan12.descriptorBindingStorageImageUpdateAfterBind &&
                vulkan12.descriptorBindingPartiallyBound && vulkan12.descriptorBindingUpdateUnusedWhilePending &&
                vulkan12.descriptorBindingVariableDescriptorCount,
            Maintenance5 = vulkan14.maintenance5,
            DescriptorHeap = descriptorHeap.descriptorHeap,
            DeviceAddressCommands = addressCommands.deviceAddressCommands,
            ShaderUntypedPointers = untypedPointers.shaderUntypedPointers,
            UnifiedImageLayouts = unifiedImageLayouts.unifiedImageLayouts,
            MeshShader = meshShader.meshShader,
            SwapchainMaintenance1 = swapchainMaintenance1.swapchainMaintenance1,
        };
    }

    internal static void AppendCapabilityDiagnostic(ref StringBuilder message, string subject, string[] missingExtensions, bool compact)
    {
        message.Append('\n').Append(subject).Append(": ");
        if (missingExtensions.Length == 0)
        {
            message.Append(compact
                ? "required Vulkan 1.2, memory, shader, bindless descriptor, or presentation capabilities are unavailable."
                : "required Vulkan 1.4, memory, shader, or presentation capabilities are unavailable.");
            return;
        }
        message.Append("missing required Vulkan extensions:");
        foreach (string name in missingExtensions)
            message.Append("\n  ").Append(name);
    }

    internal static Error InspectCandidate(Vk.VkInstanceApi api, Vk.VkPhysicalDevice PhysicalDevice, bool compact,
        bool khrSurfaceMaintenance1, bool extSurfaceMaintenance1, DeviceDesc desc, Candidate output)
    {
        bool presentation = !desc.Headless;
        Vk.VkExtensionProperties[] extensions = new Vk.VkExtensionProperties[Backend.MaxDeviceExtensions];
        Error extensionError = EnumerateDeviceExtensions(api, PhysicalDevice, extensions, out uint extensionCount);
        if (extensionError != Error.None)
            return extensionError;
        ReadOnlySpan<Vk.VkExtensionProperties> available = extensions.AsSpan(0, (int)extensionCount);

        System.Collections.Generic.List<string> missing = new();
        if (compact)
        {
            Backend.NoteMissingExtension(available, VK_KHR_SYNCHRONIZATION_2_EXTENSION_NAME, missing);
            Backend.NoteMissingExtension(available, VK_KHR_DYNAMIC_RENDERING_EXTENSION_NAME, missing);
            Backend.NoteMissingExtension(available, VK_KHR_COPY_COMMANDS_2_EXTENSION_NAME, missing);
            Backend.NoteMissingExtension(available, VK_KHR_MAINTENANCE_4_EXTENSION_NAME, missing);
            Backend.NoteMissingExtension(available, VK_KHR_FORMAT_FEATURE_FLAGS_2_EXTENSION_NAME, missing);
            Backend.NoteMissingExtension(available, VK_EXT_EXTENDED_DYNAMIC_STATE_EXTENSION_NAME, missing);
        }
        else
        {
            Backend.NoteMissingExtension(available, VK_EXT_DESCRIPTOR_HEAP_EXTENSION_NAME, missing);
            Backend.NoteMissingExtension(available, VK_KHR_SHADER_UNTYPED_POINTERS_EXTENSION_NAME, missing);
            Backend.NoteMissingExtension(available, VK_EXT_MESH_SHADER_EXTENSION_NAME, missing);
        }
        if (presentation)
            Backend.NoteMissingExtension(available, VK_KHR_SWAPCHAIN_EXTENSION_NAME, missing);
        bool KhrSwapchainMaintenance1 = khrSurfaceMaintenance1 && Backend.HasName(available, VK_KHR_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME);
        bool extSwapchainMaintenance1 = extSurfaceMaintenance1 && Backend.HasName(available, VK_EXT_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME);
        if (presentation && !KhrSwapchainMaintenance1 && !extSwapchainMaintenance1)
        {
            missing.Add(khrSurfaceMaintenance1 && extSurfaceMaintenance1
                ? "VK_KHR_swapchain_maintenance1 or VK_EXT_swapchain_maintenance1"
                : khrSurfaceMaintenance1 ? "VK_KHR_swapchain_maintenance1" : "VK_EXT_swapchain_maintenance1");
        }
        if (missing.Count != 0)
        {
            output.MissingExtensions = missing.ToArray();
            return Error.Unsupported;
        }

        Candidate result = new() { PhysicalDevice = PhysicalDevice };
        Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT heapProperties = new();
        Vk.VkPhysicalDeviceVulkan12Properties vulkan12Properties = new();
        Vk.VkPhysicalDeviceProperties2 properties2 = new() { pNext = &vulkan12Properties };
        if (!compact)
        {
            heapProperties.pNext = &vulkan12Properties;
            properties2.pNext = &heapProperties;
        }
        api.vkGetPhysicalDeviceProperties2(PhysicalDevice, &properties2);
        result.Properties = properties2.properties;
        if (result.Properties.apiVersion < (compact ? Vk.VkVersion.Version_1_2 : Vk.VkVersion.Version_1_4))
            return Error.Unsupported;
        heapProperties.pNext = null;
        result.HeapProperties = heapProperties;
        result.Vulkan12Properties = vulkan12Properties;
        fixed (byte* namePointer = result.Properties.deviceName)
            result.DeviceName = Backend.ReadVkString(namePointer);

        api.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, out result.MemoryProperties);
        bool cpuVisibleMemory = false;
        for (uint index = 0; index < result.MemoryProperties.memoryTypeCount; ++index)
        {
            if (Backend.IsUsableMemoryType(in result.MemoryProperties, index) &&
                (result.MemoryProperties.memoryTypes[(int)index].propertyFlags & Backend.CpuVisibleMemoryProperties) == Backend.CpuVisibleMemoryProperties)
            {
                cpuVisibleMemory = true;
                break;
            }
        }
        if (!cpuVisibleMemory) return Error.Unsupported;

        bool addressCommandsExtension = !compact && Backend.HasName(available, VK_KHR_DEVICE_ADDRESS_COMMANDS_EXTENSION_NAME);
        bool unifiedImageLayoutsExtension = !compact && Backend.HasName(available, VK_KHR_UNIFIED_IMAGE_LAYOUTS_EXTENSION_NAME);
        FeatureSupport features = QueryFeatureSupport(api, PhysicalDevice, compact, presentation, unifiedImageLayoutsExtension, addressCommandsExtension);
        bool requiredFeatures =
            features.ShaderInt64 &&
            features.SamplerAnisotropy &&
            features.DepthBiasClamp &&
            features.FillModeNonSolid &&
            features.IndependentBlend &&
            features.FragmentStoresAndAtomics &&
            features.VertexPipelineStoresAndAtomics &&
            features.ShaderStorageImageReadWithoutFormat &&
            features.ShaderStorageImageWriteWithoutFormat &&
            features.MultiDrawIndirect &&
            features.DrawIndirectFirstInstance &&
            features.ShaderDrawParameters &&
            features.ScalarBlockLayout &&
            features.BufferDeviceAddress &&
            features.TimelineSemaphore &&
            features.Synchronization2 &&
            features.DynamicRendering &&
            features.Maintenance4 &&
            (features.TextureCompressionBc || features.TextureCompressionAstc) &&
            (!presentation || features.SwapchainMaintenance1);
        bool profileFeatures = compact ? features.ExtendedDynamicState && features.BindlessDescriptors
            : features.ShaderInt16 && features.StorageBuffer16 && features.StoragePushConstant16 && features.ShaderFloat16 &&
              features.Maintenance5 && features.DescriptorHeap && features.ShaderUntypedPointers && features.MeshShader;
        if (!requiredFeatures || !profileFeatures)
            return Error.Unsupported;
        result.UnifiedImageLayouts = unifiedImageLayoutsExtension && features.UnifiedImageLayouts;
        result.AddressCommands = addressCommandsExtension && features.DeviceAddressCommands;
        result.ImageCubeArray = features.ImageCubeArray;
        result.TextureCompressionBc = features.TextureCompressionBc;
        result.TextureCompressionAstc = features.TextureCompressionAstc;
        result.TextureCompressionEtc2 = features.TextureCompressionEtc2;
        result.StorageInputOutput16 = !compact && features.StorageInputOutput16;
        result.KhrSwapchainMaintenance1 = KhrSwapchainMaintenance1;

        uint availableQueueCount;
        api.vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &availableQueueCount, null);
        if (availableQueueCount > Backend.MaxQueueFamilies)
            return Error.Unsupported;
        Vk.VkQueueFamilyProperties* QueueFamilies = stackalloc Vk.VkQueueFamilyProperties[(int)Backend.MaxQueueFamilies];
        uint QueueFamilyCount = availableQueueCount;
        api.vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &QueueFamilyCount, QueueFamilies);
        for (uint index = 0; index < QueueFamilyCount; ++index)
        {
            Vk.VkQueueFamilyProperties family = QueueFamilies[index];
            if (family.queueCount == 0)
                continue;
            Vk.VkQueueFlags flags = family.queueFlags;
            uint type = 0;
            if ((flags & Vk.VkQueueFlags.Graphics) != 0)
            {
                if ((flags & Vk.VkQueueFlags.Compute) == 0) continue;
            }
            else if ((flags & Vk.VkQueueFlags.Compute) != 0)
            {
                type = 1;
            }
            else if ((flags & Vk.VkQueueFlags.Transfer) != 0 &&
                     (flags & ~(Vk.VkQueueFlags.Transfer | Vk.VkQueueFlags.SparseBinding | Vk.VkQueueFlags.Protected)) == 0)
            {
                type = 2;
            }
            else
            {
                continue;
            }
            if (result.QueueCounts[type] != 0) continue;
            result.QueueFamilies[type] = index;
            result.QueueCounts[type] = family.queueCount;
            result.Timestamps[type] = desc.TimestampQueryCount != 0 && family.timestampValidBits == 64 && features.HostQueryReset;
            if (type == 2)
            {
                result.CopyTextureGranularity = new Extent3D(
                    family.minImageTransferGranularity.width,
                    family.minImageTransferGranularity.height,
                    family.minImageTransferGranularity.depth);
            }
        }
        if (result.QueueCounts[0] == 0 || (desc.DesiredComputeQueueCount != 0 && result.QueueCounts[1] == 0) ||
            (desc.DesiredCopyQueueCount != 0 && result.QueueCounts[2] == 0))
            return Error.Unsupported;

        output.PhysicalDevice = result.PhysicalDevice;
        output.QueueFamilies = result.QueueFamilies;
        output.QueueCounts = result.QueueCounts;
        output.Timestamps = result.Timestamps;
        output.CopyTextureGranularity = result.CopyTextureGranularity;
        output.Properties = result.Properties;
        output.MemoryProperties = result.MemoryProperties;
        output.UnifiedImageLayouts = result.UnifiedImageLayouts;
        output.AddressCommands = result.AddressCommands;
        output.ImageCubeArray = result.ImageCubeArray;
        output.TextureCompressionBc = result.TextureCompressionBc;
        output.TextureCompressionAstc = result.TextureCompressionAstc;
        output.TextureCompressionEtc2 = result.TextureCompressionEtc2;
        output.StorageInputOutput16 = result.StorageInputOutput16;
        output.KhrSwapchainMaintenance1 = result.KhrSwapchainMaintenance1;
        output.HeapProperties = result.HeapProperties;
        output.Vulkan12Properties = result.Vulkan12Properties;
        output.DeviceName = result.DeviceName;
        output.MissingExtensions = result.MissingExtensions;
        return Error.None;
    }

    internal static GPUException CreationFailure(Error error, string message)
    {
        Gpu.ReportError(message, false);
        return new GPUException(error, message);
    }

    internal static Vk.VkFormatFeatureFlags2 OptimalFormatFeatures(Vk.VkInstanceApi api, Vk.VkPhysicalDevice PhysicalDevice, Format format)
    {
        Vk.VkFormatProperties3 properties3 = new();
        Vk.VkFormatProperties2 properties2 = new() { pNext = &properties3 };
        api.vkGetPhysicalDeviceFormatProperties2(PhysicalDevice, Backend.ToVk(format), &properties2);
        return properties3.optimalTilingFeatures;
    }

    private static Vk.VkMemoryRequirements BufferMemoryRequirements(VulkanDeviceBase device, Vk.VkBufferUsageFlags usage)
    {
        fixed (uint* QueueFamilies = device.QueueFamilies)
        {
            Vk.VkBufferCreateInfo bufferInfo = new()
            {
                size = 1,
                usage = usage,
                sharingMode = device.QueueFamilyCount > 1 ? Vk.VkSharingMode.Concurrent : Vk.VkSharingMode.Exclusive,
                queueFamilyIndexCount = device.QueueFamilyCount,
                pQueueFamilyIndices = QueueFamilies,
            };
            Vk.VkDeviceBufferMemoryRequirements requirementsInfo = new() { pCreateInfo = &bufferInfo };
            Vk.VkMemoryRequirements2 requirements = new();
            device.Dispatch.GetDeviceBufferMemoryRequirements(device.VkDevice, &requirementsInfo, &requirements);
            return requirements.memoryRequirements;
        }
    }

    internal static bool SupportsGpuHeapMemory(VulkanDeviceBase device, bool compact)
    {
        Vk.VkMemoryRequirements ordinary = BufferMemoryRequirements(device, Backend.UniversalBufferUsage);
        if (!device.FindMemoryType(ordinary.memoryTypeBits, Backend.CpuVisibleMemoryProperties, 0, ordinary.size, out _) ||
            !device.FindMemoryType(ordinary.memoryTypeBits, Vk.VkMemoryPropertyFlags.DeviceLocal, 0, ordinary.size, out _,
                                     Vk.VkMemoryPropertyFlags.HostVisible))
        {
            return false;
        }
        if (compact) return true;
        Vk.VkMemoryRequirements descriptor = BufferMemoryRequirements(device, Backend.UniversalBufferUsage | Vk.VkBufferUsageFlags.DescriptorHeapEXT);
        return device.FindMemoryType(descriptor.memoryTypeBits, Backend.CpuVisibleMemoryProperties, 0, descriptor.size, out _);
    }

    private static bool FitsImageFormatProperties(in Vk.VkImageCreateInfo ImageInfo, in Vk.VkImageFormatProperties properties)
    {
        return ImageInfo.extent.width <= properties.maxExtent.width &&
               ImageInfo.extent.height <= properties.maxExtent.height &&
               ImageInfo.extent.depth <= properties.maxExtent.depth &&
               ImageInfo.mipLevels <= properties.maxMipLevels &&
               ImageInfo.arrayLayers <= properties.maxArrayLayers &&
               (properties.sampleCounts & ImageInfo.samples) == ImageInfo.samples;
    }

    internal static bool SupportsImageCreateInfo(VulkanDeviceBase device, in Vk.VkImageCreateInfo ImageInfo,
                                                out Vk.VkImageFormatProperties propertiesOutput, bool initializing = false)
    {
        Vk.VkPhysicalDeviceImageFormatInfo2 formatInfo = new()
        {
            format = ImageInfo.format,
            type = ImageInfo.imageType,
            tiling = ImageInfo.tiling,
            usage = ImageInfo.usage,
            flags = ImageInfo.flags,
        };
        Vk.VkImageFormatProperties2 properties = new();
        Vk.VkResult result = device.InstanceApi.vkGetPhysicalDeviceImageFormatProperties2(device.PhysicalDevice, &formatInfo, &properties);
        propertiesOutput = properties.imageFormatProperties;
        if (result == Vk.VkResult.ErrorFormatNotSupported)
            return false;
        if (initializing)
            Backend.CheckVk(result, "vkGetPhysicalDeviceImageFormatProperties2");
        else if (Backend.ErrorFromVk(result, "vkGetPhysicalDeviceImageFormatProperties2") != Error.None)
            return false;
        return FitsImageFormatProperties(in ImageInfo, in properties.imageFormatProperties);
    }

    internal static Vk.VkMemoryRequirements ImageMemoryRequirements(VulkanDeviceBase device, Vk.VkImageCreateInfo* ImageInfo)
    {
        Vk.VkDeviceImageMemoryRequirements requirementsInfo = new() { pCreateInfo = ImageInfo };
        Vk.VkMemoryRequirements2 requirements = new();
        device.Dispatch.GetDeviceImageMemoryRequirements(device.VkDevice, &requirementsInfo, &requirements);
        return requirements.memoryRequirements;
    }

    private static void IncludeTextureHeapAlignment(VulkanDeviceBase device, in Vk.VkMemoryRequirements requirements)
    {
        if (requirements.alignment > device.TextureHeapAlignment)
            device.TextureHeapAlignment = requirements.alignment;
    }

    internal static bool SelectTextureMemoryType(VulkanDeviceBase device)
    {
        Vk.VkFormatFeatureFlags2 colorFeatures = device.FormatFeatures[(int)Format.Rgba8Unorm];
        if ((colorFeatures & Vk.VkFormatFeatureFlags2.SampledImage) == 0)
            return false;
        // Probes cover DCC-capable color, broad 3D, and sampled depth layouts.
        uint probe2dSize = device.PhysicalProperties.limits.maxImageDimension2D < 2048
                                  ? device.PhysicalProperties.limits.maxImageDimension2D
                                  : 2048;
        Vk.VkImageUsageFlags colorUsage = Vk.VkImageUsageFlags.Sampled;
        if ((colorFeatures & Vk.VkFormatFeatureFlags2.ColorAttachment) != 0)
            colorUsage |= Vk.VkImageUsageFlags.ColorAttachment;
        uint memoryTypeBits = 0;
        fixed (uint* QueueFamilies = device.QueueFamilies)
        {
            Vk.VkImageCreateInfo ImageInfo = new()
            {
                imageType = Vk.VkImageType.Image2D,
                format = Vk.VkFormat.R8G8B8A8Unorm,
                extent = new Vk.VkExtent3D(probe2dSize, probe2dSize, 1),
                mipLevels = 1,
                arrayLayers = 1,
                samples = Vk.VkSampleCountFlags.Count1,
                tiling = Vk.VkImageTiling.Optimal,
                usage = colorUsage,
                sharingMode = device.QueueFamilyCount > 1 ? Vk.VkSharingMode.Concurrent : Vk.VkSharingMode.Exclusive,
                queueFamilyIndexCount = device.QueueFamilyCount,
                pQueueFamilyIndices = QueueFamilies,
                initialLayout = Vk.VkImageLayout.Undefined,
            };
            if (!SupportsImageCreateInfo(device, in ImageInfo, out _, initializing: true))
            {
                ImageInfo.usage = Vk.VkImageUsageFlags.Sampled;
                if (!SupportsImageCreateInfo(device, in ImageInfo, out _, initializing: true))
                    return false;
            }
            Vk.VkMemoryRequirements colorRequirements = ImageMemoryRequirements(device, &ImageInfo);
            memoryTypeBits = colorRequirements.memoryTypeBits;
            IncludeTextureHeapAlignment(device, in colorRequirements);

            const TextureUsage broadTextureUsage = TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.TransferDestination;
            Vk.VkFormatFeatureFlags2 broadFeatures = device.FormatFeatures[(int)Format.Rgba32Float];
            Vk.VkFormatFeatureFlags2 broadRequiredFeatures = Backend.RequiredFormatFeatures(broadTextureUsage);
            if ((broadFeatures & broadRequiredFeatures) == broadRequiredFeatures)
            {
                uint probe3dSize = device.PhysicalProperties.limits.maxImageDimension3D < 2048
                                          ? device.PhysicalProperties.limits.maxImageDimension3D
                                          : 2048;
                ImageInfo.imageType = Vk.VkImageType.Image3D;
                ImageInfo.format = Vk.VkFormat.R32G32B32A32Sfloat;
                ImageInfo.extent = new Vk.VkExtent3D(probe3dSize, probe3dSize, probe3dSize < 4 ? probe3dSize : 4);
                ImageInfo.mipLevels = 32u - (uint)BitOperations.LeadingZeroCount(probe3dSize);
                ImageInfo.usage = Vk.VkImageUsageFlags.Sampled | Vk.VkImageUsageFlags.Storage | Vk.VkImageUsageFlags.TransferDst;
                if (SupportsImageCreateInfo(device, in ImageInfo, out _, initializing: true))
                {
                    Vk.VkMemoryRequirements broadRequirements = ImageMemoryRequirements(device, &ImageInfo);
                    IncludeTextureHeapAlignment(device, in broadRequirements);
                }
            }

            Format[] depthStencilFormats =
            {
                Format.D16Unorm, Format.D24UnormS8Uint, Format.D32Float, Format.S8Uint, Format.D32FloatS8Uint,
            };
            Vk.VkFormatFeatureFlags2 storageFeatures = Backend.RequiredFormatFeatures(TextureUsage.Storage);
            foreach (Format format in depthStencilFormats)
            {
                Vk.VkFormatFeatureFlags2 features = device.FormatFeatures[(int)format];
                bool combined = Backend.HasDepthAspect(format) && Backend.HasStencilAspect(format);
                Vk.VkImageUsageFlags compatibilityUsage = 0;
                if ((features & Vk.VkFormatFeatureFlags2.SampledImage) != 0) compatibilityUsage = Vk.VkImageUsageFlags.Sampled;
                else if ((features & Vk.VkFormatFeatureFlags2.DepthStencilAttachment) != 0) compatibilityUsage = Vk.VkImageUsageFlags.DepthStencilAttachment;
                else if ((features & storageFeatures) == storageFeatures) compatibilityUsage = Vk.VkImageUsageFlags.Storage;
                else if (!combined && (features & Vk.VkFormatFeatureFlags2.TransferSrc) != 0) compatibilityUsage = Vk.VkImageUsageFlags.TransferSrc;
                else if (!combined && (features & Vk.VkFormatFeatureFlags2.TransferDst) != 0) compatibilityUsage = Vk.VkImageUsageFlags.TransferDst;
                if (compatibilityUsage != 0)
                {
                    ImageInfo.imageType = Vk.VkImageType.Image2D;
                    ImageInfo.format = Backend.ToVk(format);
                    ImageInfo.extent = new Vk.VkExtent3D(1, 1, 1);
                    ImageInfo.mipLevels = 1;
                    ImageInfo.usage = compatibilityUsage;
                    if (SupportsImageCreateInfo(device, in ImageInfo, out Vk.VkImageFormatProperties compatibilityProperties, initializing: true))
                    {
                        ImageInfo.extent = new Vk.VkExtent3D(512, 512, 1);
                        if ((features & Vk.VkFormatFeatureFlags2.SampledImage) != 0 && (features & Vk.VkFormatFeatureFlags2.DepthStencilAttachment) != 0)
                            ImageInfo.usage = Vk.VkImageUsageFlags.Sampled | Vk.VkImageUsageFlags.DepthStencilAttachment;

                        bool supported = ImageInfo.usage == compatibilityUsage
                                             ? FitsImageFormatProperties(in ImageInfo, in compatibilityProperties)
                                             : SupportsImageCreateInfo(device, in ImageInfo, out _, initializing: true);
                        if (!supported && ImageInfo.usage != compatibilityUsage)
                        {
                            ImageInfo.usage = compatibilityUsage;
                            supported = FitsImageFormatProperties(in ImageInfo, in compatibilityProperties);
                        }
                        if (!supported)
                        {
                            ImageInfo.extent = new Vk.VkExtent3D(1, 1, 1);
                            ImageInfo.usage = compatibilityUsage;
                        }

                        Vk.VkMemoryRequirements requirements = ImageMemoryRequirements(device, &ImageInfo);
                        memoryTypeBits &= requirements.memoryTypeBits;
                        IncludeTextureHeapAlignment(device, in requirements);
                    }
                }
            }
        }
        if (!device.FindMemoryType(memoryTypeBits, Vk.VkMemoryPropertyFlags.DeviceLocal, 0, 1, out device.TextureMemoryType,
                                     Vk.VkMemoryPropertyFlags.HostVisible))
        {
            return false;
        }
        device.TextureMemoryTypeValid = true;
        return true;
    }
}
