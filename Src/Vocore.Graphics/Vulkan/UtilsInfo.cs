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