// CPU regressions for the handwritten internal Vulkan bindings, ABI layouts, and sType defaults.

using System.Reflection;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;
using Vk = Vulkan;

internal static unsafe class VulkanBindingTests
{
    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("Vulkan bindings (CPU):");
        Assembly binding = typeof(Vk.Vulkan).Assembly;
        check(binding == typeof(GPUDevice).Assembly,
            "Vulkan bindings live inside the Alco.Graphics assembly");
        check(!typeof(Vk.Vulkan).IsPublic && !typeof(Vk.VkInstanceApi).IsPublic && !typeof(Vk.VkDeviceApi).IsPublic &&
            !typeof(Vk.VkDevice).IsPublic && !typeof(Vk.VkFormat).IsPublic && !typeof(Vk.VkApplicationInfo).IsPublic,
            "Vulkan binding types stay internal to Alco.Graphics");

        RunHandleAndEnumChecks(check);
        RunStructureLayoutChecks(check);
        RunDefaultStructureTypeChecks(check);
    }

    private static void RunHandleAndEnumChecks(Action<bool, string> check)
    {
        check(sizeof(Vk.VkInstance) == sizeof(nint) && sizeof(Vk.VkPhysicalDevice) == sizeof(nint) &&
            sizeof(Vk.VkDevice) == sizeof(nint) && sizeof(Vk.VkQueue) == sizeof(nint) &&
            sizeof(Vk.VkCommandBuffer) == sizeof(nint),
            "dispatchable Vulkan handles match native pointer size");
        check(sizeof(Vk.VkBuffer) == sizeof(ulong) && sizeof(Vk.VkImage) == sizeof(ulong) &&
            sizeof(Vk.VkImageView) == sizeof(ulong) && sizeof(Vk.VkSemaphore) == sizeof(ulong) &&
            sizeof(Vk.VkPipeline) == sizeof(ulong) && sizeof(Vk.VkSwapchainKHR) == sizeof(ulong),
            "non-dispatchable Vulkan handles remain 64-bit values");
        check(Vk.VkBuffer.Null.IsNull && !new Vk.VkBuffer(42).IsNull && new Vk.VkBuffer(42).IsNotNull &&
            new Vk.VkBuffer(42) == new Vk.VkBuffer(42) && new Vk.VkBuffer(42) != Vk.VkBuffer.Null,
            "typed handles preserve null checks and value equality");
        check(sizeof(Vk.VkAccessFlags2) == 8 && sizeof(Vk.VkPipelineStageFlags2) == 8 &&
            sizeof(Vk.VkFormatFeatureFlags2) == 8 && sizeof(Vk.VkPipelineCreateFlags2) == 8 &&
            (ulong)Vk.VkAccessFlags2.ResourceHeapReadEXT == 0x400000000000000UL,
            "64-bit Vulkan flag enums retain ulong width and exact bit values");
        check((int)Vk.VkResult.ErrorOutOfDeviceMemory == -2 && (int)Vk.VkResult.ErrorSurfaceLostKHR == -1000000000 &&
            (int)Vk.VkFormat.R8G8B8A8Unorm == 37 && (int)Vk.VkFormat.B8G8R8A8Srgb == 50,
            "Vulkan result and format enums preserve spec integer values");
    }

    private static void RunStructureLayoutChecks(Action<bool, string> check)
    {
        bool is64Bit = sizeof(nint) == 8;
        check(sizeof(Vk.VkDescriptorMappingSourceDataEXT) == (is64Bit ? 56 : 48) &&
            (int)Marshal.OffsetOf<Vk.VkDescriptorMappingSourceDataEXT>(nameof(Vk.VkDescriptorMappingSourceDataEXT.pushAddressOffset)) == 0 &&
            sizeof(Vk.VkDescriptorSetAndBindingMappingEXT) == (is64Bit ? 96 : 76) &&
            (int)Marshal.OffsetOf<Vk.VkDescriptorSetAndBindingMappingEXT>(nameof(Vk.VkDescriptorSetAndBindingMappingEXT.source)) == (is64Bit ? 32 : 24) &&
            (int)Marshal.OffsetOf<Vk.VkDescriptorSetAndBindingMappingEXT>(nameof(Vk.VkDescriptorSetAndBindingMappingEXT.sourceData)) == (is64Bit ? 40 : 28),
            "Vulkan descriptor mappings retain the native union layout and array stride for the current pointer width");
        CheckPointerAlignment<Vk.VkDescriptorMappingSourceDataEXT>(check);

        if (!is64Bit)
        {
            Console.WriteLine("  [SKIP] 64-bit Vulkan ABI offset checks");
            return;
        }

        check(sizeof(Vk.VkApplicationInfo) == 48 &&
            (int)Marshal.OffsetOf<Vk.VkApplicationInfo>(nameof(Vk.VkApplicationInfo.pApplicationName)) == 16 &&
            (int)Marshal.OffsetOf<Vk.VkApplicationInfo>(nameof(Vk.VkApplicationInfo.apiVersion)) == 44,
            "VkApplicationInfo retains its 48-byte 64-bit layout");
        check(sizeof(Vk.VkInstanceCreateInfo) == 64 &&
            (int)Marshal.OffsetOf<Vk.VkInstanceCreateInfo>(nameof(Vk.VkInstanceCreateInfo.pApplicationInfo)) == 24 &&
            (int)Marshal.OffsetOf<Vk.VkInstanceCreateInfo>(nameof(Vk.VkInstanceCreateInfo.ppEnabledExtensionNames)) == 56,
            "VkInstanceCreateInfo retains its 64-byte 64-bit layout");
        check(sizeof(Vk.VkPhysicalDeviceProperties) == 824 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceProperties>(nameof(Vk.VkPhysicalDeviceProperties.deviceName)) == 20 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceProperties>(nameof(Vk.VkPhysicalDeviceProperties.pipelineCacheUUID)) == 276 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceProperties>(nameof(Vk.VkPhysicalDeviceProperties.limits)) == 296 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceProperties>(nameof(Vk.VkPhysicalDeviceProperties.sparseProperties)) == 800,
            "VkPhysicalDeviceProperties retains its 824-byte layout with embedded limits");
        check(sizeof(Vk.VkPhysicalDeviceMemoryProperties) == 520 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceMemoryProperties>(nameof(Vk.VkPhysicalDeviceMemoryProperties.memoryTypes)) == 4 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceMemoryProperties>(nameof(Vk.VkPhysicalDeviceMemoryProperties.memoryHeapCount)) == 260 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceMemoryProperties>(nameof(Vk.VkPhysicalDeviceMemoryProperties.memoryHeaps)) == 264,
            "VkPhysicalDeviceMemoryProperties retains its 520-byte inline-array layout");
        check(sizeof(Vk.VkPhysicalDeviceVulkan12Properties) == 736 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceVulkan12Properties>(nameof(Vk.VkPhysicalDeviceVulkan12Properties.maxUpdateAfterBindDescriptorsInAllPools)) == 604 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceVulkan12Properties>(nameof(Vk.VkPhysicalDeviceVulkan12Properties.maxTimelineSemaphoreValueDifference)) == 720,
            "VkPhysicalDeviceVulkan12Properties retains its 736-byte layout");
        check(sizeof(Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT) == 152 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT>(nameof(Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT.imageDescriptorSize)) == 80 &&
            (int)Marshal.OffsetOf<Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT>(nameof(Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT.maxPushDataSize)) == 120,
            "VkPhysicalDeviceDescriptorHeapPropertiesEXT retains its 152-byte layout");
        check(sizeof(Vk.VkGraphicsPipelineCreateInfo) == 144 &&
            (int)Marshal.OffsetOf<Vk.VkGraphicsPipelineCreateInfo>(nameof(Vk.VkGraphicsPipelineCreateInfo.pStages)) == 24 &&
            (int)Marshal.OffsetOf<Vk.VkGraphicsPipelineCreateInfo>(nameof(Vk.VkGraphicsPipelineCreateInfo.layout)) == 104 &&
            (int)Marshal.OffsetOf<Vk.VkGraphicsPipelineCreateInfo>(nameof(Vk.VkGraphicsPipelineCreateInfo.basePipelineIndex)) == 136,
            "VkGraphicsPipelineCreateInfo retains its 144-byte layout");
        check(sizeof(Vk.VkResourceDescriptorDataEXT) == 8 && sizeof(Vk.VkResourceDescriptorInfoEXT) == 32 &&
            (int)Marshal.OffsetOf<Vk.VkResourceDescriptorInfoEXT>(nameof(Vk.VkResourceDescriptorInfoEXT.data)) == 24,
            "VkResourceDescriptorInfoEXT retains its 32-byte layout and 8-byte union at offset 24");
        CheckPointerAlignment<Vk.VkResourceDescriptorDataEXT>(check);
        check(sizeof(Vk.VkImageMemoryBarrier2) == 96 &&
            (int)Marshal.OffsetOf<Vk.VkImageMemoryBarrier2>(nameof(Vk.VkImageMemoryBarrier2.oldLayout)) == 48 &&
            (int)Marshal.OffsetOf<Vk.VkImageMemoryBarrier2>(nameof(Vk.VkImageMemoryBarrier2.image)) == 64 &&
            (int)Marshal.OffsetOf<Vk.VkImageMemoryBarrier2>(nameof(Vk.VkImageMemoryBarrier2.subresourceRange)) == 72,
            "VkImageMemoryBarrier2 retains its 96-byte layout");
        check(sizeof(Vk.VkRenderingInfo) == 72 && sizeof(Vk.VkRenderingAttachmentInfo) == 72 &&
            (int)Marshal.OffsetOf<Vk.VkRenderingAttachmentInfo>(nameof(Vk.VkRenderingAttachmentInfo.clearValue)) == 52 &&
            sizeof(Vk.VkClearValue) == 16,
            "VkRenderingInfo and VkRenderingAttachmentInfo retain their 72-byte layouts");
        check(sizeof(Vk.VkSubmitInfo2) == 64 && sizeof(Vk.VkSwapchainCreateInfoKHR) == 104 &&
            sizeof(Vk.VkDeviceMemoryImageCopyKHR) == 88 && sizeof(Vk.VkBindIndexBuffer3InfoKHR) == 40 &&
            sizeof(Vk.VkPushDataInfoEXT) == 40,
            "submission, swapchain, and device-address command structs retain exact 64-bit sizes");
    }

    private static void RunDefaultStructureTypeChecks(Action<bool, string> check)
    {
        check(new Vk.VkApplicationInfo().sType == Vk.VkStructureType.ApplicationInfo &&
            new Vk.VkInstanceCreateInfo().sType == Vk.VkStructureType.InstanceCreateInfo &&
            new Vk.VkDeviceCreateInfo().sType == Vk.VkStructureType.DeviceCreateInfo &&
            new Vk.VkGraphicsPipelineCreateInfo().sType == Vk.VkStructureType.GraphicsPipelineCreateInfo &&
            new Vk.VkComputePipelineCreateInfo().sType == Vk.VkStructureType.ComputePipelineCreateInfo &&
            new Vk.VkPipelineCacheCreateInfo().sType == Vk.VkStructureType.PipelineCacheCreateInfo &&
            new Vk.VkDescriptorSetAndBindingMappingEXT().sType == Vk.VkStructureType.DescriptorSetAndBindingMappingEXT &&
            new Vk.VkRenderingInfo().sType == Vk.VkStructureType.RenderingInfo &&
            new Vk.VkImageMemoryBarrier2().sType == Vk.VkStructureType.ImageMemoryBarrier2 &&
            new Vk.VkSubmitInfo2().sType == Vk.VkStructureType.SubmitInfo2 &&
            new Vk.VkSwapchainCreateInfoKHR().sType == Vk.VkStructureType.SwapchainCreateInfoKHR &&
            new Vk.VkPushDataInfoEXT().sType == Vk.VkStructureType.PushDataInfoEXT &&
            new Vk.VkBindIndexBuffer3InfoKHR().sType == Vk.VkStructureType.BindIndexBuffer3InfoKHR,
            "parameterless constructors auto-initialize Vulkan sType fields");
    }

    private static void CheckPointerAlignment<T>(Action<bool, string> check) where T : unmanaged
    {
        AlignmentProbe<T> probe = new();
        int pointerSize = sizeof(nint);
        check((byte*)&probe.Value - (byte*)&probe == pointerSize && sizeof(AlignmentProbe<T>) == sizeof(T) + pointerSize,
            $"{typeof(T).Name} preserves native {pointerSize}-byte alignment when embedded after a byte");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AlignmentProbe<T> where T : unmanaged
    {
        internal byte Prefix;
        internal T Value;
    }
}
