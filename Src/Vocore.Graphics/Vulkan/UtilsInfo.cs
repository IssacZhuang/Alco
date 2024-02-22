using System;
using Vortice.Vulkan;

using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe static partial class UtilsVulkan
{
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

    public static VkSurfaceFormatKHR[] GetDepthFormats(VkPhysicalDevice physicalDevice, ReadOnlySpan<VkSurfaceFormatKHR> formats)
    {
        List<VkSurfaceFormatKHR> depthFormats = new List<VkSurfaceFormatKHR>();
        foreach (VkSurfaceFormatKHR format in formats)
        {
            VkFormatProperties properties;
            vkGetPhysicalDeviceFormatProperties(physicalDevice, format.format, &properties);
            if ((properties.bufferFeatures & VkFormatFeatureFlags.DepthStencilAttachment) == VkFormatFeatureFlags.DepthStencilAttachment)
            {
                depthFormats.Add(format);
            }
        }

        return depthFormats.ToArray();
    }

    public static VkFormat? GetPreferredDepthFormat(VkPhysicalDevice physicalDevice, ReadOnlySpan<VkSurfaceFormatKHR> formats)
    {
        VkSurfaceFormatKHR[] depthFormats = GetDepthFormats(physicalDevice, formats);

        //prefer 32 bit format
        foreach (VkSurfaceFormatKHR format in depthFormats)
        {
            if (format.format == VkFormat.D24UnormS8Uint)
            {
                return format.format;
            }
        }

        if (depthFormats.Length > 0)
        {
            return depthFormats[0].format;
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