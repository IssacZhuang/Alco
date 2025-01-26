using System;
using Vortice.Vulkan;

using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

internal unsafe static partial class UtilsVulkan
{
    public static readonly VkFormat[] AllDepthFormats = new VkFormat[]
    {
        VkFormat.D16Unorm,
        VkFormat.D16UnormS8Uint,
        VkFormat.D24UnormS8Uint,
        VkFormat.D32Sfloat,
        VkFormat.D32SfloatS8Uint,
    };

    public static string[] GetInstanceLayers()
    {
        uint count = 0;
        VkResult result = vkEnumerateInstanceLayerProperties(&count, null);
        if (result != VkResult.Success)
        {
            return Array.Empty<string>();
        }

        if (count == 0)
        {
            return Array.Empty<string>();
        }

        VkLayerProperties* properties = stackalloc VkLayerProperties[(int)count];
        vkEnumerateInstanceLayerProperties(&count, properties).CheckResult();

        string[] resultExt = new string[count];
        for (int i = 0; i < count; i++)
        {
            resultExt[i] = properties[i].GetLayerName();
        }

        return resultExt;
    }

    public static string[] GetInstanceExtensions()
    {
        uint count = 0;
        VkResult result = vkEnumerateInstanceExtensionProperties(&count, null);
        if (result != VkResult.Success)
        {
            return Array.Empty<string>();
        }

        if (count == 0)
        {
            return Array.Empty<string>();
        }

        VkExtensionProperties* props = stackalloc VkExtensionProperties[(int)count];
        vkEnumerateInstanceExtensionProperties(&count, props);

        string[] extensions = new string[count];
        for (int i = 0; i < count; i++)
        {
            extensions[i] = props[i].GetExtensionName();
        }

        return extensions;
    }

    public static string? GetSurfaceExtesion(SurfaceSource surfaceSource)
    {
        switch (surfaceSource)
        {
            case AndroidWindowSurfaceSource androidWindowSurface:
                return VK_KHR_ANDROID_SURFACE_EXTENSION_NAME;
            case MetalLayerSurfaceHandle metalLayerSurface:
                //TODO: Mac OS implementation
                return null;
            case Win32SurfaceSource win32Surface:
                return VK_KHR_WIN32_SURFACE_EXTENSION_NAME;
            case WaylandSurfaceSource waylandSurface:
                return VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME;
            case XcbWindowSurfaceSource xcbWindowSurface:
                return VK_KHR_XCB_SURFACE_EXTENSION_NAME;
            case XlibWindowSurfaceSource xlibWindowSurface:
                return VK_KHR_XLIB_SURFACE_EXTENSION_NAME;
            default:
                return null;
        }
    }

    public static string[] GetOptimalValidationLayers()
    {
        string[] availableLayers = GetInstanceLayers();
        // The preferred validation layer is "VK_LAYER_KHRONOS_validation"
        string[] validationLayers = new string[]
        {
            "VK_LAYER_KHRONOS_validation"
        };

        if (ValidateLayers(validationLayers, availableLayers))
        {
            return validationLayers;
        }

        // Otherwise we fallback to using the LunarG meta layer
        validationLayers = new string[]
        {
            "VK_LAYER_LUNARG_standard_validation"
        };

        if (ValidateLayers(validationLayers, availableLayers))
        {
            return validationLayers;
        }

        // Otherwise we attempt to enable the individual layers that compose the LunarG meta layer since it doesn't exist
        validationLayers = new string[]
        {
            "VK_LAYER_GOOGLE_threading",
            "VK_LAYER_LUNARG_parameter_validation",
            "VK_LAYER_LUNARG_object_tracker",
            "VK_LAYER_LUNARG_core_validation",
            "VK_LAYER_GOOGLE_unique_objects",
        };

        if (ValidateLayers(validationLayers, availableLayers))
        {
            return validationLayers;
        }

        // Otherwise as a last resort we fallback to attempting to enable the LunarG core layer
        validationLayers = new string[]
        {
            "VK_LAYER_LUNARG_core_validation"
        };

        if (ValidateLayers(validationLayers, availableLayers))
        {
            return validationLayers;
        }

        return Array.Empty<string>();
    }

    public static SwapChainSupportDetails GetSwapChainSupportDetails(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface)
    {
        SwapChainSupportDetails details = new SwapChainSupportDetails();
        vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, surface, &details.Capabilities).CheckResult();
        details.Formats = vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface);
        details.PresentModes = vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, surface);
        return details;
    }

    public static VkSurfaceFormatKHR GetPreferredSurfaceFormat(ReadOnlySpan<VkSurfaceFormatKHR> formats)
    {
        foreach (VkSurfaceFormatKHR format in formats)
        {
            if (format.format == VkFormat.B8G8R8A8Srgb && format.colorSpace == VkColorSpaceKHR.SrgbNonLinear)
            {
                return format;
            }
        }

        return formats[0];
    }

    public static VkFormat? GetPreferredDepthFormat(VkPhysicalDevice physicalDevice, VkImageTiling tiling)
    {

        //prefer 32 bit format
        VkFormat? found = null;
        foreach (VkFormat format in AllDepthFormats)
        {
            VkFormatProperties props;
            vkGetPhysicalDeviceFormatProperties(physicalDevice, format, &props);
            if (tiling == VkImageTiling.Linear && (props.linearTilingFeatures & VkFormatFeatureFlags.DepthStencilAttachment) == VkFormatFeatureFlags.DepthStencilAttachment)
            {
                found = format;
            }
            else if (tiling == VkImageTiling.Optimal && (props.optimalTilingFeatures & VkFormatFeatureFlags.DepthStencilAttachment) == VkFormatFeatureFlags.DepthStencilAttachment)
            {
                found = format;
            }

            if (found.HasValue && found.Value == VkFormat.D24UnormS8Uint)
            {
                return found;
            }
        }

        return null;
    }


    public static VkPresentModeKHR GetPreferredPresentMode(ReadOnlySpan<VkPresentModeKHR> presentModes, bool vSync)
    {
        foreach (VkPresentModeKHR mode in presentModes)
        {
            if (vSync && mode == VkPresentModeKHR.Fifo)
            {
                return mode;
            }

            //prefer immediate than mailbox when vSync is off
            if (!vSync && mode == VkPresentModeKHR.Immediate)
            {
                return mode;
            }

            if (!vSync && mode == VkPresentModeKHR.Mailbox)
            {
                return mode;
            }
        }

        return VkPresentModeKHR.Fifo;
    }

    public static PhysicalDeviceExtensions QueryPhysicalDeviceExtensions(VkPhysicalDevice physicalDevice)
    {
        uint count = 0;
        VkResult result = vkEnumerateDeviceExtensionProperties(physicalDevice, null, &count, null);
        if (result != VkResult.Success)
            return default;

        VkExtensionProperties* vk_extensions = stackalloc VkExtensionProperties[(int)count];
        vkEnumerateDeviceExtensionProperties(physicalDevice, null, &count, vk_extensions);

        PhysicalDeviceExtensions extensions = new();

        for (int i = 0; i < count; ++i)
        {
            string extensionName = vk_extensions[i].GetExtensionName();

            if (extensionName == VK_KHR_MAINTENANCE_4_EXTENSION_NAME)
            {
                extensions.Maintenance4 = true;
            }
            else if (extensionName == VK_KHR_DYNAMIC_RENDERING_EXTENSION_NAME)
            {
                extensions.DynamicRendering = true;
            }
            else if (extensionName == VK_KHR_SYNCHRONIZATION_2_EXTENSION_NAME)
            {
                extensions.Synchronization2 = true;
            }
            else if (extensionName == VK_EXT_EXTENDED_DYNAMIC_STATE_EXTENSION_NAME)
            {
                extensions.ExtendedDynamicState = true;
            }
            else if (extensionName == VK_EXT_EXTENDED_DYNAMIC_STATE_2_EXTENSION_NAME)
            {
                extensions.ExtendedDynamicState2 = true;
            }
            else if (extensionName == VK_EXT_PIPELINE_CREATION_CACHE_CONTROL_EXTENSION_NAME)
            {
                extensions.PipelineCreationCacheControl = true;
            }
            else if (extensionName == VK_KHR_FORMAT_FEATURE_FLAGS_2_EXTENSION_NAME)
            {
                extensions.FormatFeatureFlags2 = true;
            }
            else if (extensionName == VK_KHR_SWAPCHAIN_EXTENSION_NAME)
            {
                extensions.Swapchain = true;
            }
            else if (extensionName == VK_EXT_DEPTH_CLIP_ENABLE_EXTENSION_NAME)
            {
                extensions.DepthClipEnable = true;
            }
            else if (extensionName == VK_EXT_MEMORY_BUDGET_EXTENSION_NAME)
            {
                extensions.MemoryBudget = true;
            }
            else if (extensionName == VK_AMD_DEVICE_COHERENT_MEMORY_EXTENSION_NAME)
            {
                extensions.AMD_DeviceCoherentMemory = true;
            }
            else if (extensionName == VK_EXT_MEMORY_PRIORITY_EXTENSION_NAME)
            {
                extensions.MemoryPriority = true;
            }
            else if (extensionName == VK_KHR_PERFORMANCE_QUERY_EXTENSION_NAME)
            {
                extensions.performance_query = true;
            }
            else if (extensionName == VK_EXT_HOST_QUERY_RESET_EXTENSION_NAME)
            {
                extensions.host_query_reset = true;
            }
            else if (extensionName == VK_KHR_DEFERRED_HOST_OPERATIONS_EXTENSION_NAME)
            {
                extensions.deferred_host_operations = true;
            }
            else if (extensionName == VK_KHR_PORTABILITY_SUBSET_EXTENSION_NAME)
            {
                extensions.PortabilitySubset = true;
            }
            else if (extensionName == VK_KHR_ACCELERATION_STRUCTURE_EXTENSION_NAME)
            {
                extensions.accelerationStructure = true;
            }
            else if (extensionName == VK_KHR_RAY_TRACING_PIPELINE_EXTENSION_NAME)
            {
                extensions.raytracingPipeline = true;
            }
            else if (extensionName == VK_KHR_RAY_QUERY_EXTENSION_NAME)
            {
                extensions.rayQuery = true;
            }
            else if (extensionName == VK_KHR_FRAGMENT_SHADING_RATE_EXTENSION_NAME)
            {
                extensions.FragmentShadingRate = true;
            }
            else if (extensionName == VK_EXT_MESH_SHADER_EXTENSION_NAME)
            {
                extensions.MeshShader = true;
            }
            else if (extensionName == VK_EXT_CONDITIONAL_RENDERING_EXTENSION_NAME)
            {
                extensions.ConditionalRendering = true;
            }
            else if (extensionName == VK_KHR_VIDEO_QUEUE_EXTENSION_NAME)
            {
                extensions.Video.Queue = true;
            }
            else if (extensionName == VK_KHR_VIDEO_DECODE_QUEUE_EXTENSION_NAME)
            {
                extensions.Video.DecodeQueue = true;
            }
            else if (extensionName == VK_KHR_VIDEO_DECODE_H264_EXTENSION_NAME)
            {
                extensions.Video.DecodeH264 = true;
            }
            else if (extensionName == VK_KHR_VIDEO_DECODE_H265_EXTENSION_NAME)
            {
                extensions.Video.DecodeH265 = true;
            }
            else if (extensionName == VK_KHR_VIDEO_ENCODE_QUEUE_EXTENSION_NAME)
            {
                extensions.Video.EncodeQueue = true;
            }
            // else if (extensionName == VK_EXT_VIDEO_ENCODE_H264_EXTENSION_NAME)
            // {
            //     extensions.Video.EncodeH264 = true;
            // }
            // else if (extensionName == VK_EXT_VIDEO_ENCODE_H265_EXTENSION_NAME)
            // {
            //     extensions.Video.EncodeH265 = true;
            // }

            if (OperatingSystem.IsWindows())
            {
                if (extensionName == VK_EXT_FULL_SCREEN_EXCLUSIVE_EXTENSION_NAME)
                {
                    extensions.win32_full_screen_exclusive = true;
                }
                else if (extensionName == VK_KHR_EXTERNAL_SEMAPHORE_WIN32_EXTENSION_NAME)
                {
                    extensions.SupportsExternalSemaphore = true;
                }
                else if (extensionName == VK_KHR_EXTERNAL_MEMORY_WIN32_EXTENSION_NAME)
                {
                    extensions.SupportsExternalMemory = true;
                }
            }
            else
            {
                if (extensionName == VK_KHR_EXTERNAL_SEMAPHORE_FD_EXTENSION_NAME)
                {
                    extensions.SupportsExternalSemaphore = true;
                }
                else if (extensionName == VK_KHR_EXTERNAL_MEMORY_FD_EXTENSION_NAME)
                {
                    extensions.SupportsExternalMemory = true;
                }
            }
        }

        VkPhysicalDeviceProperties gpuProps;
        vkGetPhysicalDeviceProperties(physicalDevice, &gpuProps);

        // Core 1.3
        if (gpuProps.apiVersion >= VkVersion.Version_1_3)
        {
            extensions.Maintenance4 = true;
            extensions.DynamicRendering = true;
            extensions.Synchronization2 = true;
            extensions.ExtendedDynamicState = true;
            extensions.ExtendedDynamicState2 = true;
            extensions.PipelineCreationCacheControl = true;
            extensions.FormatFeatureFlags2 = true;
        }

        return extensions;
    }

    private static bool ValidateLayers(string[] required, string[] availableLayers)
    {
        foreach (string layer in required)
        {
            bool found = false;
            foreach (string availableLayer in availableLayers)
            {
                if (availableLayer == layer)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

}