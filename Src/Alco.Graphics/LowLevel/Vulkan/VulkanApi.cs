// Vulkan device creation, presentation, resource management, and command recording for the Vulkan backend.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vk = Vulkan;
using static global::Alco.Graphics.LowLevel.VulkanBackend;
using static Vulkan.Vulkan;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal static unsafe partial class VulkanBackend
{

    internal static unsafe string PhysicalDeviceName(Vk.VkInstanceApi api, Vk.VkPhysicalDevice PhysicalDevice)
    {
        api.vkGetPhysicalDeviceProperties(PhysicalDevice, out Vk.VkPhysicalDeviceProperties properties);
        return Backend.ReadVkString(properties.deviceName);
    }

    internal static unsafe byte* CopyExtensionName(ReadOnlySpan<byte> name, byte* storage, ref int writtenNames)
    {
        byte* destination = storage + writtenNames * VK_MAX_EXTENSION_NAME_SIZE;
        name.CopyTo(new Span<byte>(destination, (int)(VK_MAX_EXTENSION_NAME_SIZE - 1)));
        destination[name.Length] = 0;
        ++writtenNames;
        return destination;
    }

    internal static bool HasLayerName(ReadOnlySpan<Vk.VkLayerProperties> values, ReadOnlySpan<byte> name)
    {
        foreach (ref readonly Vk.VkLayerProperties value in values)
        {
            fixed (byte* layerName = value.layerName)
            {
                nint index = 0;
                while (index < name.Length && layerName[index] == name[(int)index]) ++index;
                if (index == name.Length && layerName[index] == 0) return true;
            }
        }
        return false;
    }

}

internal sealed unsafe partial class VulkanDevice : VulkanDeviceBase
{
    private VulkanDevice() : base(GraphicsBackend.Vulkan) { }

    internal static VulkanDevice Create(DeviceDesc desc)
    {
        VulkanDevice device = new();
        device.Initialize(desc);
        return device;
    }
}

internal abstract unsafe partial class VulkanDeviceBase
{
    protected void Initialize(DeviceDesc desc)
    {
        Debug.Assert(desc.DesiredQueueCount != 0, "createDevice requires at least one queue");
        bool compact = ActiveBackend == GraphicsBackend.VulkanCompact;
        Vk.VkVersion requiredVersion = compact ? Vk.VkVersion.Version_1_2 : Vk.VkVersion.Version_1_4;

        lock (VulkanInitializationLock)
        {
            if (!VulkanInitialized)
            {
                Backend.CheckVk(Vk.Vulkan.vkInitialize(), "vkInitialize");
                VulkanInitialized = true;
            }
        }

        uint loaderVersion = Vk.VkVersion.Version_1_0;
        if (Vk.Vulkan.vkEnumerateInstanceVersion_ptr != default)
        {
            Backend.CheckVk(Vk.Vulkan.vkEnumerateInstanceVersion(&loaderVersion), "vkEnumerateInstanceVersion");
        }
        if (new Vk.VkVersion(loaderVersion) < requiredVersion)
        {
            throw CreationFailure(Error.Unsupported,
                $"Vulkan {requiredVersion.Major}.{requiredVersion.Minor} or newer is required. Update the graphics driver.");
        }

        TimestampQueryCount = desc.TimestampQueryCount;
        PresentationEnabled = !desc.Headless;
        bool initialized = false;
        try
        {
            Initialize(this, desc, compact, requiredVersion);
            initialized = true;
        }
        finally
        {
            if (!initialized) Destroy();
        }
    }

    private static void Initialize(VulkanDeviceBase device, DeviceDesc desc, bool compact, Vk.VkVersion requiredVersion)
    {
        bool presentation = device.PresentationEnabled;
        Vk.VkExtensionProperties[] instanceExtensions = new Vk.VkExtensionProperties[Backend.MaxInstanceExtensions];
        uint instanceExtensionCount = EnumerateInstanceExtensions(instanceExtensions);
        ReadOnlySpan<Vk.VkExtensionProperties> instanceAvailable = instanceExtensions.AsSpan(0, (int)instanceExtensionCount);

        device.SwapchainColorSpaceEnabled = presentation && Backend.HasName(instanceAvailable, VK_EXT_SWAPCHAIN_COLOR_SPACE_EXTENSION_NAME);
        device.Win32SurfaceEnabled = presentation && OperatingSystem.IsWindows() && Backend.HasName(instanceAvailable, VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
        device.XlibSurfaceEnabled = presentation && OperatingSystem.IsLinux() && Backend.HasName(instanceAvailable, VK_KHR_XLIB_SURFACE_EXTENSION_NAME);
        device.XcbSurfaceEnabled = presentation && OperatingSystem.IsLinux() && Backend.HasName(instanceAvailable, VK_KHR_XCB_SURFACE_EXTENSION_NAME);
        device.WaylandSurfaceEnabled = presentation && OperatingSystem.IsLinux() && Backend.HasName(instanceAvailable, VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME);
        bool khrSurfaceMaintenance1 = presentation && Backend.HasName(instanceAvailable, VK_KHR_SURFACE_MAINTENANCE_1_EXTENSION_NAME);
        bool extSurfaceMaintenance1 = presentation && Backend.HasName(instanceAvailable, VK_EXT_SURFACE_MAINTENANCE_1_EXTENSION_NAME);
        if (presentation)
        {
            System.Collections.Generic.List<string> missing = new();
            Backend.NoteMissingExtension(instanceAvailable, VK_KHR_SURFACE_EXTENSION_NAME, missing);
            if (OperatingSystem.IsWindows() && !device.Win32SurfaceEnabled)
                missing.Add("VK_KHR_win32_surface");
            if (!device.Win32SurfaceEnabled && !device.XlibSurfaceEnabled && !device.XcbSurfaceEnabled && !device.WaylandSurfaceEnabled)
                missing.Add("a supported Win32, Xlib, Xcb, or Wayland surface extension");
            Backend.NoteMissingExtension(instanceAvailable, VK_KHR_GET_SURFACE_CAPABILITIES_2_EXTENSION_NAME, missing);
            if (!khrSurfaceMaintenance1 && !extSurfaceMaintenance1)
                missing.Add("VK_KHR_surface_maintenance1 or VK_EXT_surface_maintenance1");
            if (missing.Count != 0)
            {
                StringBuilder message = new("Cannot initialize Vulkan presentation.");
                AppendCapabilityDiagnostic(ref message, "Vulkan instance", missing.ToArray(), compact);
                throw CreationFailure(Error.Unsupported, message.ToString());
            }
        }

        bool debugUtilsAvailable = Backend.HasName(instanceAvailable, VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
#if DEBUG
        Vk.VkLayerProperties[] layers = new Vk.VkLayerProperties[Backend.MaxInstanceLayers];
        uint LayerCount = EnumerateInstanceLayers(layers);
        ReadOnlySpan<Vk.VkLayerProperties> layerAvailable = layers.AsSpan(0, (int)LayerCount);
        // Validation builds that predate VK_EXT_descriptor_heap crash inside vkCreateGraphicsPipelines as soon as a
        // shader-descriptor mapping covers a live resource, so the layer is opt-in via ALCO_GRAPHICS_VALIDATION=1.
        bool validationAvailable = Environment.GetEnvironmentVariable("ALCO_GRAPHICS_VALIDATION") == "1" &&
                                    HasLayerName(layerAvailable, "VK_LAYER_KHRONOS_validation"u8);
#endif

        byte* nameStorage = stackalloc byte[(int)(12 * VK_MAX_EXTENSION_NAME_SIZE)];
        byte** enabledInstanceExtensions = stackalloc byte*[12];
        int instanceNameCursor = 0;
        uint enabledInstanceExtensionCount = 0;
        if (debugUtilsAvailable)
            enabledInstanceExtensions[enabledInstanceExtensionCount++] =
                CopyExtensionName(VK_EXT_DEBUG_UTILS_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
        if (presentation)
        {
            if (device.SwapchainColorSpaceEnabled)
                enabledInstanceExtensions[enabledInstanceExtensionCount++] =
                    CopyExtensionName(VK_EXT_SWAPCHAIN_COLOR_SPACE_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
            enabledInstanceExtensions[enabledInstanceExtensionCount++] = CopyExtensionName(VK_KHR_SURFACE_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
            if (device.Win32SurfaceEnabled)
                enabledInstanceExtensions[enabledInstanceExtensionCount++] =
                    CopyExtensionName(VK_KHR_WIN32_SURFACE_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
            if (device.XlibSurfaceEnabled)
                enabledInstanceExtensions[enabledInstanceExtensionCount++] =
                    CopyExtensionName(VK_KHR_XLIB_SURFACE_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
            if (device.XcbSurfaceEnabled)
                enabledInstanceExtensions[enabledInstanceExtensionCount++] =
                    CopyExtensionName(VK_KHR_XCB_SURFACE_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
            if (device.WaylandSurfaceEnabled)
                enabledInstanceExtensions[enabledInstanceExtensionCount++] =
                    CopyExtensionName(VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
            enabledInstanceExtensions[enabledInstanceExtensionCount++] =
                CopyExtensionName(VK_KHR_GET_SURFACE_CAPABILITIES_2_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
            if (khrSurfaceMaintenance1)
                enabledInstanceExtensions[enabledInstanceExtensionCount++] =
                    CopyExtensionName(VK_KHR_SURFACE_MAINTENANCE_1_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
            if (extSurfaceMaintenance1)
                enabledInstanceExtensions[enabledInstanceExtensionCount++] =
                    CopyExtensionName(VK_EXT_SURFACE_MAINTENANCE_1_EXTENSION_NAME, nameStorage, ref instanceNameCursor);
        }

        byte* applicationName = Backend.utf8("Alco application", stackalloc byte[64]);
        byte* engineName = Backend.utf8("Alco.Graphics", stackalloc byte[32]);
        Vk.VkApplicationInfo appInfo = new()
        {
            pApplicationName = applicationName,
            pEngineName = engineName,
            apiVersion = requiredVersion,
        };
        byte** enabledLayers = null;
#if DEBUG
        byte* validationLayer = Backend.utf8("VK_LAYER_KHRONOS_validation", stackalloc byte[64]);
        if (validationAvailable)
        {
            enabledLayers = &validationLayer;
        }
#endif
        Vk.VkInstanceCreateInfo instanceInfo = new()
        {
            pApplicationInfo = &appInfo,
            enabledLayerCount = enabledLayers != null ? 1u : 0u,
            ppEnabledLayerNames = enabledLayers,
            enabledExtensionCount = enabledInstanceExtensionCount,
            ppEnabledExtensionNames = enabledInstanceExtensions,
        };
        Backend.CheckVk(Vk.Vulkan.vkCreateInstance(&instanceInfo, null, out device.Instance), "vkCreateInstance");
        device.InstanceCreated = true;
        device.InstanceApi = Vk.Vulkan.GetApi(device.Instance);
        device.DebugMarkers = debugUtilsAvailable;

#if DEBUG
        if (debugUtilsAvailable)
        {
            if (device.InstanceApi.vkCreateDebugUtilsMessengerEXT_ptr == default || device.InstanceApi.vkDestroyDebugUtilsMessengerEXT_ptr == default)
                throw CreationFailure(Error.DriverError, "The graphics driver did not provide the enabled Vulkan debug messenger commands.");
            Vk.VkDebugUtilsMessengerCreateInfoEXT debugInfo = new()
            {
                messageSeverity = Vk.VkDebugUtilsMessageSeverityFlagsEXT.Warning | Vk.VkDebugUtilsMessageSeverityFlagsEXT.Error,
                messageType = Vk.VkDebugUtilsMessageTypeFlagsEXT.General | Vk.VkDebugUtilsMessageTypeFlagsEXT.Validation |
                              Vk.VkDebugUtilsMessageTypeFlagsEXT.Performance,
                pfnUserCallback = &DebugCallback,
            };
            Backend.CheckVk(device.InstanceApi.vkCreateDebugUtilsMessengerEXT(&debugInfo, null, out Vk.VkDebugUtilsMessengerEXT messenger),
                            "vkCreateDebugUtilsMessengerEXT");
            device.DebugMessenger = messenger;
            device.DebugMessengerCreated = true;
        }
#endif

        Vk.VkPhysicalDevice[] physicalDevices = new Vk.VkPhysicalDevice[Backend.MaxPhysicalDevices];
        uint physicalDeviceCount;
        fixed (Vk.VkPhysicalDevice* pointer = physicalDevices)
        {
            physicalDeviceCount = (uint)physicalDevices.Length;
            Vk.VkResult result = device.InstanceApi.vkEnumeratePhysicalDevices(&physicalDeviceCount, pointer);
            if (result == Vk.VkResult.Incomplete)
                throw CreationFailure(Error.Unsupported, "The Vulkan physical device list exceeds the supported capacity.");
            Backend.CheckVk(result, "vkEnumeratePhysicalDevices");
        }

        Candidate? selected = null;
        StringBuilder candidateDiagnostics = new("No compatible GPU found.");
        for (uint index = 0; index < physicalDeviceCount; ++index)
        {
            Vk.VkPhysicalDevice PhysicalDevice = physicalDevices[index];
            Candidate candidate = new();
            Error error = InspectCandidate(device.InstanceApi, PhysicalDevice, compact, khrSurfaceMaintenance1, extSurfaceMaintenance1, desc, candidate);
            if (error == Error.Unsupported)
            {
                string name = candidate.DeviceName.Length != 0
                    ? candidate.DeviceName
                    : PhysicalDeviceName(device.InstanceApi, PhysicalDevice);
                AppendCapabilityDiagnostic(ref candidateDiagnostics, name, candidate.MissingExtensions, compact);
                continue;
            }
            Debug.Assert(error == Error.None);
            if (selected == null || candidate.Properties.deviceType == Vk.VkPhysicalDeviceType.DiscreteGpu)
                selected = candidate;
            if (candidate.Properties.deviceType == Vk.VkPhysicalDeviceType.DiscreteGpu)
                break;
        }
        if (selected == null)
            throw CreationFailure(Error.Unsupported, candidateDiagnostics.ToString());

        device.PhysicalDevice = selected.PhysicalDevice;
        device.PhysicalProperties = selected.Properties;
        device.HeapProperties = selected.HeapProperties;
        device.Vulkan12Properties = selected.Vulkan12Properties;
        device.MaxTimelineValueDifference = selected.Vulkan12Properties.maxTimelineSemaphoreValueDifference;
        device.MemoryProperties = selected.MemoryProperties;
        device.KhrSwapchainMaintenance1 = selected.KhrSwapchainMaintenance1;
        for (int value = 0; value < Backend.FormatCount; ++value)
            device.FormatFeatures[value] = OptimalFormatFeatures(device.InstanceApi, device.PhysicalDevice, (Format)value);

        // Rebuild the feature chain to enable the requested feature set on the created device.
        Vk.VkPhysicalDeviceFeatures2 enabledFeatures = new();
        Vk.VkPhysicalDeviceVulkan11Features enabledVulkan11 = new();
        Vk.VkPhysicalDeviceVulkan12Features enabledVulkan12 = new();
        Vk.VkPhysicalDeviceVulkan13Features enabledVulkan13 = new() { synchronization2 = true, dynamicRendering = true, maintenance4 = true };
        Vk.VkPhysicalDeviceVulkan14Features enabledVulkan14 = new() { maintenance5 = true };
        Vk.VkPhysicalDeviceSynchronization2Features synchronization2 = new() { synchronization2 = true };
        Vk.VkPhysicalDeviceDynamicRenderingFeatures dynamicRendering = new() { dynamicRendering = true };
        Vk.VkPhysicalDeviceMaintenance4Features maintenance4 = new() { maintenance4 = true };
        Vk.VkPhysicalDeviceExtendedDynamicStateFeaturesEXT extendedDynamicState = new() { extendedDynamicState = true };
        Vk.VkPhysicalDeviceDescriptorHeapFeaturesEXT enabledDescriptorHeap = new() { descriptorHeap = true };
        Vk.VkPhysicalDeviceDeviceAddressCommandsFeaturesKHR enabledAddressCommands = new() { deviceAddressCommands = true };
        Vk.VkPhysicalDeviceShaderUntypedPointersFeaturesKHR enabledUntypedPointers = new() { shaderUntypedPointers = true };
        Vk.VkPhysicalDeviceUnifiedImageLayoutsFeaturesKHR enabledUnifiedImageLayouts = new() { unifiedImageLayouts = true };
        Vk.VkPhysicalDeviceMeshShaderFeaturesEXT enabledMeshShader = new() { taskShader = true, meshShader = true };
        Vk.VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR enabledSwapchainMaintenance1 = new() { swapchainMaintenance1 = true };
        void* presentationFeatures = presentation ? &enabledSwapchainMaintenance1 : null;
        enabledFeatures.pNext = &enabledVulkan11;
        enabledVulkan11.pNext = &enabledVulkan12;
        if (compact)
        {
            enabledVulkan12.pNext = &synchronization2;
            synchronization2.pNext = &dynamicRendering;
            dynamicRendering.pNext = &maintenance4;
            maintenance4.pNext = &extendedDynamicState;
            extendedDynamicState.pNext = presentationFeatures;

            enabledFeatures.features.shaderSampledImageArrayDynamicIndexing = true;
            enabledFeatures.features.shaderStorageImageArrayDynamicIndexing = true;
            enabledVulkan12.descriptorIndexing = true;
            enabledVulkan12.runtimeDescriptorArray = true;
            enabledVulkan12.shaderSampledImageArrayNonUniformIndexing = true;
            enabledVulkan12.shaderStorageImageArrayNonUniformIndexing = true;
            enabledVulkan12.descriptorBindingSampledImageUpdateAfterBind = true;
            enabledVulkan12.descriptorBindingStorageImageUpdateAfterBind = true;
            enabledVulkan12.descriptorBindingPartiallyBound = true;
            enabledVulkan12.descriptorBindingUpdateUnusedWhilePending = true;
            enabledVulkan12.descriptorBindingVariableDescriptorCount = true;
        }
        else
        {
            enabledVulkan12.pNext = &enabledVulkan13;
            enabledVulkan13.pNext = &enabledVulkan14;
            enabledVulkan14.pNext = &enabledDescriptorHeap;
            enabledDescriptorHeap.pNext = selected.AddressCommands ? &enabledAddressCommands : (void*)&enabledUntypedPointers;
            enabledAddressCommands.pNext = &enabledUntypedPointers;
            enabledUntypedPointers.pNext = selected.UnifiedImageLayouts ? &enabledUnifiedImageLayouts : (void*)&enabledMeshShader;
            enabledUnifiedImageLayouts.pNext = &enabledMeshShader;
            enabledMeshShader.pNext = presentationFeatures;

            enabledFeatures.features.shaderInt16 = true;
            enabledVulkan11.storageBuffer16BitAccess = true;
            enabledVulkan11.storagePushConstant16 = true;
            enabledVulkan11.storageInputOutput16 = selected.StorageInputOutput16;
            enabledVulkan12.shaderFloat16 = true;
        }
        device.TextureCompressionEtc2 = selected.TextureCompressionEtc2;
        enabledFeatures.features.imageCubeArray = selected.ImageCubeArray;
        enabledFeatures.features.samplerAnisotropy = true;
        enabledFeatures.features.shaderInt64 = true;
        enabledFeatures.features.depthBiasClamp = true;
        enabledFeatures.features.fillModeNonSolid = true;
        enabledFeatures.features.independentBlend = true;
        enabledFeatures.features.textureCompressionBC = selected.TextureCompressionBc;
        enabledFeatures.features.textureCompressionASTC_LDR = selected.TextureCompressionAstc;
        enabledFeatures.features.textureCompressionETC2 = selected.TextureCompressionEtc2;
        enabledFeatures.features.fragmentStoresAndAtomics = true;
        enabledFeatures.features.vertexPipelineStoresAndAtomics = true;
        enabledFeatures.features.shaderStorageImageReadWithoutFormat = true;
        enabledFeatures.features.shaderStorageImageWriteWithoutFormat = true;
        enabledFeatures.features.multiDrawIndirect = true;
        enabledFeatures.features.drawIndirectFirstInstance = true;
        enabledVulkan11.shaderDrawParameters = true;
        enabledVulkan12.scalarBlockLayout = true;
        enabledVulkan12.timelineSemaphore = true;
        enabledVulkan12.hostQueryReset = selected.Timestamps[0] || selected.Timestamps[1] || selected.Timestamps[2];
        enabledVulkan12.bufferDeviceAddress = true;

        uint[] requestedCounts = { desc.DesiredQueueCount, desc.DesiredComputeQueueCount, desc.DesiredCopyQueueCount };
        uint[] QueueCounts = new uint[Backend.QueueTypeCount];
        for (uint type = 0; type < Backend.QueueTypeCount; ++type)
        {
            QueueCounts[type] = requestedCounts[type] < selected.QueueCounts[type] ? requestedCounts[type] : selected.QueueCounts[type];
            device.QueueCount += QueueCounts[type];
        }
        device.Queues = new Queue[device.QueueCount];
        for (uint index = 0; index < device.QueueCount; ++index)
            device.Queues[index] = new Queue();
        float* queuePriorities = stackalloc float[(int)device.QueueCount];
        for (uint index = 0; index < device.QueueCount; ++index)
            queuePriorities[index] = 1.0f;
        Vk.VkDeviceQueueCreateInfo* queueInfos = stackalloc Vk.VkDeviceQueueCreateInfo[(int)Backend.QueueTypeCount];
        uint firstQueue = 0;
        for (uint type = 0; type < Backend.QueueTypeCount; ++type)
        {
            if (QueueCounts[type] == 0) continue;
            device.QueueFamilies[device.QueueFamilyCount] = selected.QueueFamilies[type];
            queueInfos[device.QueueFamilyCount++] = new Vk.VkDeviceQueueCreateInfo
            {
                queueFamilyIndex = selected.QueueFamilies[type],
                queueCount = QueueCounts[type],
                pQueuePriorities = queuePriorities + firstQueue,
            };
            firstQueue += QueueCounts[type];
        }

        const int maxDeviceExtensionCount = 8;
        byte* deviceNameStorage = stackalloc byte[(int)(maxDeviceExtensionCount * VK_MAX_EXTENSION_NAME_SIZE)];
        byte** enabledDeviceExtensions = stackalloc byte*[maxDeviceExtensionCount];
        int deviceNameCursor = 0;
        uint enabledDeviceExtensionCount = 0;
        if (compact)
        {
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_KHR_SYNCHRONIZATION_2_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_KHR_DYNAMIC_RENDERING_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_KHR_COPY_COMMANDS_2_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_KHR_MAINTENANCE_4_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_KHR_FORMAT_FEATURE_FLAGS_2_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_EXT_EXTENDED_DYNAMIC_STATE_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
        }
        else
        {
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_EXT_DESCRIPTOR_HEAP_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            if (selected.AddressCommands)
                enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                    CopyExtensionName(VK_KHR_DEVICE_ADDRESS_COMMANDS_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_KHR_SHADER_UNTYPED_POINTERS_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            if (selected.UnifiedImageLayouts)
                enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                    CopyExtensionName(VK_KHR_UNIFIED_IMAGE_LAYOUTS_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_EXT_MESH_SHADER_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
        }
        if (presentation)
        {
            enabledDeviceExtensions[enabledDeviceExtensionCount++] =
                CopyExtensionName(VK_KHR_SWAPCHAIN_EXTENSION_NAME, deviceNameStorage, ref deviceNameCursor);
            enabledDeviceExtensions[enabledDeviceExtensionCount++] = CopyExtensionName(
                selected.KhrSwapchainMaintenance1 ? VK_KHR_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME : VK_EXT_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME,
                deviceNameStorage, ref deviceNameCursor);
        }

        Vk.VkDeviceCreateInfo deviceInfo = new()
        {
            pNext = &enabledFeatures,
            queueCreateInfoCount = device.QueueFamilyCount,
            pQueueCreateInfos = queueInfos,
            enabledExtensionCount = enabledDeviceExtensionCount,
            ppEnabledExtensionNames = enabledDeviceExtensions,
        };
        Backend.CheckVk(device.InstanceApi.vkCreateDevice(device.PhysicalDevice, &deviceInfo, null, out device.VkDevice), "vkCreateDevice");
        device.DeviceCreated = true;
        device.Api = Vk.Vulkan.GetApi(device.Instance, device.VkDevice);
        device.Dispatch = new VulkanNativeDispatch(device.Api, compact);
        uint firstCreatedQueue = 0;
        for (uint type = 0; type < Backend.QueueTypeCount; ++type)
        {
            for (uint index = 0; index < QueueCounts[type]; ++index)
            {
                Queue queue = device.Queues[firstCreatedQueue++];
                queue.FamilyIndex = selected.QueueFamilies[type];
                queue.Timestamps = selected.Timestamps[type];
                device.Api.vkGetDeviceQueue(queue.FamilyIndex, index, out queue.Handle);
            }
        }
        if (!SupportsGpuHeapMemory(device, compact) || !SelectTextureMemoryType(device))
            throw CreationFailure(Error.Unsupported, "Required Vulkan device, memory, or display capabilities are unavailable.");

        if (!compact)
        {
            if (device.Api.vkWriteSamplerDescriptorsEXT_ptr == default || device.Api.vkWriteResourceDescriptorsEXT_ptr == default ||
                device.Api.vkCmdBindSamplerHeapEXT_ptr == default || device.Api.vkCmdBindResourceHeapEXT_ptr == default ||
                device.Api.vkCmdPushDataEXT_ptr == default || device.Api.vkCmdDrawMeshTasksEXT_ptr == default)
            {
                throw CreationFailure(Error.DriverError, "The graphics driver did not provide the required Vulkan command entry points.");
            }
            if (selected.AddressCommands)
            {
                if (device.Api.vkCmdBindIndexBuffer3KHR_ptr == default || device.Api.vkCmdDrawIndirect2KHR_ptr == default ||
                    device.Api.vkCmdDrawIndexedIndirect2KHR_ptr == default || device.Api.vkCmdDispatchIndirect2KHR_ptr == default ||
                    device.Api.vkCmdDrawMeshTasksIndirect2EXT_ptr == default || device.Api.vkCmdCopyMemoryKHR_ptr == default ||
                    device.Api.vkCmdCopyMemoryToImageKHR_ptr == default || device.Api.vkCmdCopyImageToMemoryKHR_ptr == default)
                {
                    throw CreationFailure(Error.DriverError, "The graphics driver did not provide the enabled Vulkan device address commands.");
                }
            }
            else if (device.Api.vkCmdDrawMeshTasksIndirectEXT_ptr == default)
            {
                throw CreationFailure(Error.DriverError, "The graphics driver did not provide vkCmdDrawMeshTasksIndirectEXT.");
            }
            device.AddressCommands = selected.AddressCommands;
            device.DescriptorReadAccess = Vk.VkAccessFlags2.ResourceHeapReadEXT | Vk.VkAccessFlags2.SamplerHeapReadEXT;
        }

        if (presentation && (device.Api.vkCreateSwapchainKHR_ptr == default || device.Api.vkAcquireNextImageKHR_ptr == default ||
            device.Api.vkQueuePresentKHR_ptr == default || (selected.KhrSwapchainMaintenance1
                ? device.Api.vkReleaseSwapchainImagesKHR_ptr == default : device.Api.vkReleaseSwapchainImagesEXT_ptr == default)))
        {
            throw CreationFailure(Error.DriverError, "The graphics driver did not provide the enabled Vulkan presentation commands.");
        }

        device.InitializePipelineCache(desc.PipelineCacheDirectory);

        device.BufferViewTable = device.CreateBackingBuffer(BufferViewSlots.Capacity * sizeof(ulong),
            Vk.VkBufferUsageFlags.ShaderDeviceAddress | Vk.VkBufferUsageFlags.StorageBuffer, Backend.CpuVisibleMemoryProperties, 0);
        NativeMemory.Clear(device.BufferViewTable.mapped, BufferViewSlots.Capacity * sizeof(ulong));
        device.BufferViews = new BufferViewSlots((ulong*)device.BufferViewTable.mapped);

        fixed (byte* namePointer = device.PhysicalProperties.deviceName)
        {
            device.Capabilities = new DeviceCaps
            {
                DeviceName = Backend.ReadVkString(namePointer),
                QueueCount = device.QueueCount,
                GeneralQueueCount = QueueCounts[0],
                ComputeQueueCount = QueueCounts[1],
                CopyQueueCount = QueueCounts[2],
                CopyTextureGranularity = selected.CopyTextureGranularity,
                MaxPushDataSize = compact ? uint.MaxValue : device.HeapProperties.maxPushDataSize,
                TextureHeapAlignment = device.TextureHeapAlignment,
                TimestampPeriodNs = selected.Properties.limits.timestampPeriod,
                SubTexelPrecisionBits = selected.Properties.limits.subTexelPrecisionBits,
                TextureCompressionBc = selected.TextureCompressionBc,
                TextureCompressionAstc = selected.TextureCompressionAstc,
                StorageInputOutput16 = selected.StorageInputOutput16,
                IndirectMeshDraw = !compact,
                MaxIndirectDrawCount = device.PhysicalProperties.limits.maxDrawIndirectCount,
            };
        }
        if (!compact)
        {
            Vk.VkPhysicalDeviceDescriptorHeapPropertiesEXT heapProperties = device.HeapProperties;
            device.Capabilities.TextureDescriptorHeapCapacity = Math.Min(Backend.BindlessTextureDescriptorCapacity,
                (uint)((heapProperties.maxResourceHeapSize - heapProperties.minResourceHeapReservedRange) / heapProperties.imageDescriptorSize));
            device.Capabilities.SamplerDescriptorHeapCapacity = Math.Min(Backend.BindlessSamplerDescriptorCapacity,
                (uint)((heapProperties.maxSamplerHeapSize - heapProperties.minSamplerHeapReservedRange) / heapProperties.samplerDescriptorSize));
        }
        device.ShaderBindingStarted = true;
        device.InitializeShaderBinding();
    }

    /// <summary>Returns whether textures of the given format and usage can be created.</summary>
    /// <param name="format">The texture format.</param>
    /// <param name="usage">The required texture usage.</param>
    /// <returns>Whether the requested textures are supported.</returns>
    public override bool SupportsTextureFormat(Format format, TextureUsage usage)
    {
        Debug.Assert((uint)format < Backend.FormatCount && Backend.HasValidTextureUsageBits(usage));

        TextureFormatInfo info = Gpu.GetTextureFormatInfo(format);
        if ((info.Depth || info.Stencil) && Backend.HasFlag(usage, TextureUsage.ColorAttachment))
            return false;
        if (!info.Depth && !info.Stencil && Backend.HasFlag(usage, TextureUsage.DepthStencilAttachment))
            return false;
        if (info.Depth && info.Stencil && (Backend.HasFlag(usage, TextureUsage.TransferSource) || Backend.HasFlag(usage, TextureUsage.TransferDestination)))
            return false;
        switch (Backend.FormatCompression(format))
        {
            case TextureCompression.None: break;
            case TextureCompression.Etc2:
                if (!this.TextureCompressionEtc2)
                    return false;
                break;
            case TextureCompression.Astc:
                if (!this.Capabilities.TextureCompressionAstc)
                    return false;
                break;
            case TextureCompression.Bc:
                if (!this.Capabilities.TextureCompressionBc)
                    return false;
                break;
        }

        Vk.VkFormatFeatureFlags2 available = this.FormatFeatures[(int)format];
        Vk.VkFormatFeatureFlags2 required = Backend.RequiredFormatFeatures(usage);
        return (available & required) == required;
    }

    /// <summary>Creates a timeline semaphore.</summary>
    /// <param name="initialValue">The initial timeline value.</param>
    /// <returns>The semaphore.</returns>
    public override GPUTimelineSemaphore CreateTimelineSemaphore(ulong initialValue = 0)
    {

        Vk.VkSemaphoreTypeCreateInfo timelineInfo = new() { semaphoreType = Vk.VkSemaphoreType.Timeline, initialValue = initialValue };
        Vk.VkSemaphoreCreateInfo createInfo = new() { pNext = &timelineInfo };
        VulkanTimelineSemaphore result = new() { Owner = this };
        Backend.CheckVk(this.Api.vkCreateSemaphore(&createInfo, null, out result.Semaphore), "vkCreateSemaphore (timeline)");
        return result;
    }

    /// <summary>Blocks until every submitted command has completed.</summary>
    public override void WaitIdle()
    {
        Backend.RequireVk(this.Api.vkDeviceWaitIdle(), "vkDeviceWaitIdle");
    }
}

internal sealed unsafe partial class VulkanTimelineSemaphore
{
    /// <summary>Releases the semaphore.</summary>
    public override void Destroy()
    {
        this.Owner.Api.vkDestroySemaphore(this.Semaphore, null);
    }

    /// <summary>The latest completed value signaled by the GPU.</summary>
    public override ulong CompletedValue
    {
        get
        {
            Debug.Assert(this.Owner != null && !this.Semaphore.IsNull, "timelineCompletedValue received an invalid semaphore");
            ulong value;
            Backend.AssertVk(this.Owner.Api.vkGetSemaphoreCounterValue(this.Semaphore, &value));
            return value;
        }
    }

    internal override void Wait(ulong value)
    {
        Debug.Assert(this.Owner != null && !this.Semaphore.IsNull, "waitTimeline requires a live timeline semaphore");

        Vk.VkSemaphore handle = this.Semaphore;
        Vk.VkSemaphoreWaitInfo waitInfo = new()
        {
            semaphoreCount = 1,
            pSemaphores = &handle,
            pValues = &value,
        };
        Backend.AssertVk(this.Owner.Api.vkWaitSemaphores(&waitInfo, ulong.MaxValue));
    }
}

internal sealed unsafe partial class VulkanGpuHeapOwner
{
    internal override void Destroy()
    {
        Debug.Assert(this.Device != null);
        if (ViewIndex != 0) Device.BufferViews!.Free(ViewIndex);
        Vk.VkDeviceApi vk = this.Device.Api;
        BackingBuffer backing = this.Backing;
        if (backing.mapped != null) vk.vkUnmapMemory(backing.Memory);
        vk.vkDestroyBuffer(backing.Buffer, null);
        vk.vkFreeMemory(backing.Memory, null);
    }
}
