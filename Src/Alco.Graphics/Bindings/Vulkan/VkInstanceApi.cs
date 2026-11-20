// Minimal Vulkan instance-level dispatch table.

using System.Runtime.CompilerServices;
using static Vulkan.Vulkan;

namespace Vulkan;

internal sealed unsafe partial class VkInstanceApi
{
    public VkInstance Instance { get; }

    public readonly PFN_vkVoidFunction vkDestroyInstance_ptr;
    public readonly PFN_vkVoidFunction vkEnumeratePhysicalDevices_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceProperties_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceQueueFamilyProperties_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceMemoryProperties_ptr;
    public readonly PFN_vkVoidFunction vkGetDeviceProcAddr_ptr;
    public readonly PFN_vkVoidFunction vkCreateDevice_ptr;
    public readonly PFN_vkVoidFunction vkEnumerateDeviceExtensionProperties_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceFeatures2_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceProperties2_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceFormatProperties2_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceImageFormatProperties2_ptr;
    public readonly PFN_vkVoidFunction vkDestroySurfaceKHR_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceSurfaceSupportKHR_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceSurfaceCapabilitiesKHR_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceSurfaceFormatsKHR_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceSurfacePresentModesKHR_ptr;
    public readonly PFN_vkVoidFunction vkGetPhysicalDeviceSurfaceCapabilities2KHR_ptr;
    public readonly PFN_vkVoidFunction vkCmdBeginDebugUtilsLabelEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdEndDebugUtilsLabelEXT_ptr;
    public readonly PFN_vkVoidFunction vkCmdInsertDebugUtilsLabelEXT_ptr;
    public readonly PFN_vkVoidFunction vkCreateDebugUtilsMessengerEXT_ptr;
    public readonly PFN_vkVoidFunction vkDestroyDebugUtilsMessengerEXT_ptr;
    public readonly PFN_vkVoidFunction vkCreateWaylandSurfaceKHR_ptr;
    public readonly PFN_vkVoidFunction vkCreateWin32SurfaceKHR_ptr;
    public readonly PFN_vkVoidFunction vkCreateXcbSurfaceKHR_ptr;
    public readonly PFN_vkVoidFunction vkCreateXlibSurfaceKHR_ptr;

    public VkInstanceApi(in VkInstance instance)
    {
        Instance = instance;
        vkDestroyInstance_ptr = vkGetInstanceProcAddr(instance.Handle, "vkDestroyInstance"u8);
        vkEnumeratePhysicalDevices_ptr = vkGetInstanceProcAddr(instance.Handle, "vkEnumeratePhysicalDevices"u8);
        vkGetPhysicalDeviceProperties_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceProperties"u8);
        vkGetPhysicalDeviceQueueFamilyProperties_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceQueueFamilyProperties"u8);
        vkGetPhysicalDeviceMemoryProperties_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceMemoryProperties"u8);
        vkGetDeviceProcAddr_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetDeviceProcAddr"u8);
        vkCreateDevice_ptr = vkGetInstanceProcAddr(instance.Handle, "vkCreateDevice"u8);
        vkEnumerateDeviceExtensionProperties_ptr = vkGetInstanceProcAddr(instance.Handle, "vkEnumerateDeviceExtensionProperties"u8);
        vkGetPhysicalDeviceFeatures2_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceFeatures2"u8);
        vkGetPhysicalDeviceProperties2_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceProperties2"u8);
        vkGetPhysicalDeviceFormatProperties2_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceFormatProperties2"u8);
        vkGetPhysicalDeviceImageFormatProperties2_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceImageFormatProperties2"u8);
        vkDestroySurfaceKHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkDestroySurfaceKHR"u8);
        vkGetPhysicalDeviceSurfaceSupportKHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceSurfaceSupportKHR"u8);
        vkGetPhysicalDeviceSurfaceCapabilitiesKHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR"u8);
        vkGetPhysicalDeviceSurfaceFormatsKHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceSurfaceFormatsKHR"u8);
        vkGetPhysicalDeviceSurfacePresentModesKHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceSurfacePresentModesKHR"u8);
        vkGetPhysicalDeviceSurfaceCapabilities2KHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkGetPhysicalDeviceSurfaceCapabilities2KHR"u8);
        vkCmdBeginDebugUtilsLabelEXT_ptr = vkGetInstanceProcAddr(instance.Handle, "vkCmdBeginDebugUtilsLabelEXT"u8);
        vkCmdEndDebugUtilsLabelEXT_ptr = vkGetInstanceProcAddr(instance.Handle, "vkCmdEndDebugUtilsLabelEXT"u8);
        vkCmdInsertDebugUtilsLabelEXT_ptr = vkGetInstanceProcAddr(instance.Handle, "vkCmdInsertDebugUtilsLabelEXT"u8);
        vkCreateDebugUtilsMessengerEXT_ptr = vkGetInstanceProcAddr(instance.Handle, "vkCreateDebugUtilsMessengerEXT"u8);
        vkDestroyDebugUtilsMessengerEXT_ptr = vkGetInstanceProcAddr(instance.Handle, "vkDestroyDebugUtilsMessengerEXT"u8);
        vkCreateWaylandSurfaceKHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkCreateWaylandSurfaceKHR"u8);
        vkCreateWin32SurfaceKHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkCreateWin32SurfaceKHR"u8);
        vkCreateXcbSurfaceKHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkCreateXcbSurfaceKHR"u8);
        vkCreateXlibSurfaceKHR_ptr = vkGetInstanceProcAddr(instance.Handle, "vkCreateXlibSurfaceKHR"u8);
    }

    public PFN_vkVoidFunction vkGetDeviceProcAddr(nint device, ReadOnlySpan<byte> name)
    {
        fixed (byte* pName = name)
            return ((delegate* unmanaged<VkDevice, byte*, PFN_vkVoidFunction>)vkGetDeviceProcAddr_ptr.Value)(device, pName);
    }

    public void vkDestroyInstance(VkAllocationCallbacks* allocator)
    {
        ((delegate* unmanaged<VkInstance, VkAllocationCallbacks*, void>)vkDestroyInstance_ptr.Value)(Instance, allocator);
        _ = Vulkan.RemoveInstanceTable(Instance);
    }

    public VkResult vkEnumeratePhysicalDevices(uint* physicalDeviceCount, VkPhysicalDevice* physicalDevices) =>
        ((delegate* unmanaged<VkInstance, uint*, VkPhysicalDevice*, VkResult>)vkEnumeratePhysicalDevices_ptr.Value)(Instance, physicalDeviceCount, physicalDevices);

    public void vkGetPhysicalDeviceProperties(VkPhysicalDevice physicalDevice, out VkPhysicalDeviceProperties properties)
    {
        Unsafe.SkipInit(out properties);

        fixed (VkPhysicalDeviceProperties* propertiesPtr = &properties)
        {
            ((delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties*, void>)vkGetPhysicalDeviceProperties_ptr.Value)(physicalDevice, propertiesPtr);
        }
    }

    public void vkGetPhysicalDeviceQueueFamilyProperties(VkPhysicalDevice physicalDevice, uint* queueFamilyPropertyCount, VkQueueFamilyProperties* queueFamilyProperties) =>
        ((delegate* unmanaged<VkPhysicalDevice, uint*, VkQueueFamilyProperties*, void>)vkGetPhysicalDeviceQueueFamilyProperties_ptr.Value)(physicalDevice, queueFamilyPropertyCount, queueFamilyProperties);

    public void vkGetPhysicalDeviceMemoryProperties(VkPhysicalDevice physicalDevice, out VkPhysicalDeviceMemoryProperties memoryProperties)
    {
        Unsafe.SkipInit(out memoryProperties);

        fixed (VkPhysicalDeviceMemoryProperties* memoryPropertiesPtr = &memoryProperties)
        {
            ((delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties*, void>)vkGetPhysicalDeviceMemoryProperties_ptr.Value)(physicalDevice, memoryPropertiesPtr);
        }
    }

    public VkResult vkCreateDevice(VkPhysicalDevice physicalDevice, VkDeviceCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkDevice device)
    {
        Unsafe.SkipInit(out device);

        fixed (VkDevice* devicePtr = &device)
        {
            return ((delegate* unmanaged<VkPhysicalDevice, VkDeviceCreateInfo*, VkAllocationCallbacks*, VkDevice*, VkResult>)vkCreateDevice_ptr.Value)(physicalDevice, createInfo, allocator, devicePtr);
        }
    }

    public VkResult vkEnumerateDeviceExtensionProperties(VkPhysicalDevice physicalDevice, byte* layerName, uint* propertyCount, VkExtensionProperties* properties) =>
        ((delegate* unmanaged<VkPhysicalDevice, byte*, uint*, VkExtensionProperties*, VkResult>)vkEnumerateDeviceExtensionProperties_ptr.Value)(physicalDevice, layerName, propertyCount, properties);

    public void vkGetPhysicalDeviceFeatures2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceFeatures2* features) =>
        ((delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceFeatures2*, void>)vkGetPhysicalDeviceFeatures2_ptr.Value)(physicalDevice, features);

    public void vkGetPhysicalDeviceProperties2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceProperties2* properties) =>
        ((delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceProperties2*, void>)vkGetPhysicalDeviceProperties2_ptr.Value)(physicalDevice, properties);

    public void vkGetPhysicalDeviceFormatProperties2(VkPhysicalDevice physicalDevice, VkFormat format, VkFormatProperties2* formatProperties) =>
        ((delegate* unmanaged<VkPhysicalDevice, VkFormat, VkFormatProperties2*, void>)vkGetPhysicalDeviceFormatProperties2_ptr.Value)(physicalDevice, format, formatProperties);

    public VkResult vkGetPhysicalDeviceImageFormatProperties2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceImageFormatInfo2* imageFormatInfo, VkImageFormatProperties2* imageFormatProperties) =>
        ((delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceImageFormatInfo2*, VkImageFormatProperties2*, VkResult>)vkGetPhysicalDeviceImageFormatProperties2_ptr.Value)(physicalDevice, imageFormatInfo, imageFormatProperties);

    public void vkDestroySurfaceKHR(VkSurfaceKHR surface, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkInstance, VkSurfaceKHR, VkAllocationCallbacks*, void>)vkDestroySurfaceKHR_ptr.Value)(Instance, surface, allocator);

    public VkResult vkGetPhysicalDeviceSurfaceSupportKHR(VkPhysicalDevice physicalDevice, uint queueFamilyIndex, VkSurfaceKHR surface, out VkBool32 supported)
    {
        Unsafe.SkipInit(out supported);

        fixed (VkBool32* supportedPtr = &supported)
        {
            return ((delegate* unmanaged<VkPhysicalDevice, uint, VkSurfaceKHR, VkBool32*, VkResult>)vkGetPhysicalDeviceSurfaceSupportKHR_ptr.Value)(physicalDevice, queueFamilyIndex, surface, supportedPtr);
        }
    }

    public VkResult vkGetPhysicalDeviceSurfaceCapabilitiesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, out VkSurfaceCapabilitiesKHR surfaceCapabilities)
    {
        Unsafe.SkipInit(out surfaceCapabilities);

        fixed (VkSurfaceCapabilitiesKHR* surfaceCapabilitiesPtr = &surfaceCapabilities)
        {
            return ((delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, VkSurfaceCapabilitiesKHR*, VkResult>)vkGetPhysicalDeviceSurfaceCapabilitiesKHR_ptr.Value)(physicalDevice, surface, surfaceCapabilitiesPtr);
        }
    }

    public VkResult vkGetPhysicalDeviceSurfaceFormatsKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, uint* surfaceFormatCount, VkSurfaceFormatKHR* surfaceFormats) =>
        ((delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, uint*, VkSurfaceFormatKHR*, VkResult>)vkGetPhysicalDeviceSurfaceFormatsKHR_ptr.Value)(physicalDevice, surface, surfaceFormatCount, surfaceFormats);

    public VkResult vkGetPhysicalDeviceSurfacePresentModesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, uint* presentModeCount, VkPresentModeKHR* presentModes) =>
        ((delegate* unmanaged<VkPhysicalDevice, VkSurfaceKHR, uint*, VkPresentModeKHR*, VkResult>)vkGetPhysicalDeviceSurfacePresentModesKHR_ptr.Value)(physicalDevice, surface, presentModeCount, presentModes);

    public VkResult vkGetPhysicalDeviceSurfaceCapabilities2KHR(VkPhysicalDevice physicalDevice, VkPhysicalDeviceSurfaceInfo2KHR* surfaceInfo, VkSurfaceCapabilities2KHR* surfaceCapabilities) =>
        ((delegate* unmanaged<VkPhysicalDevice, VkPhysicalDeviceSurfaceInfo2KHR*, VkSurfaceCapabilities2KHR*, VkResult>)vkGetPhysicalDeviceSurfaceCapabilities2KHR_ptr.Value)(physicalDevice, surfaceInfo, surfaceCapabilities);

    public void vkCmdBeginDebugUtilsLabelEXT(VkCommandBuffer commandBuffer, VkDebugUtilsLabelEXT* labelInfo) =>
        ((delegate* unmanaged<VkCommandBuffer, VkDebugUtilsLabelEXT*, void>)vkCmdBeginDebugUtilsLabelEXT_ptr.Value)(commandBuffer, labelInfo);

    public void vkCmdEndDebugUtilsLabelEXT(VkCommandBuffer commandBuffer) =>
        ((delegate* unmanaged<VkCommandBuffer, void>)vkCmdEndDebugUtilsLabelEXT_ptr.Value)(commandBuffer);

    public void vkCmdInsertDebugUtilsLabelEXT(VkCommandBuffer commandBuffer, VkDebugUtilsLabelEXT* labelInfo) =>
        ((delegate* unmanaged<VkCommandBuffer, VkDebugUtilsLabelEXT*, void>)vkCmdInsertDebugUtilsLabelEXT_ptr.Value)(commandBuffer, labelInfo);

    public VkResult vkCreateDebugUtilsMessengerEXT(VkDebugUtilsMessengerCreateInfoEXT* createInfo, VkAllocationCallbacks* allocator, out VkDebugUtilsMessengerEXT messenger)
    {
        Unsafe.SkipInit(out messenger);

        fixed (VkDebugUtilsMessengerEXT* messengerPtr = &messenger)
        {
            return ((delegate* unmanaged<VkInstance, VkDebugUtilsMessengerCreateInfoEXT*, VkAllocationCallbacks*, VkDebugUtilsMessengerEXT*, VkResult>)vkCreateDebugUtilsMessengerEXT_ptr.Value)(Instance, createInfo, allocator, messengerPtr);
        }
    }

    public void vkDestroyDebugUtilsMessengerEXT(VkDebugUtilsMessengerEXT messenger, VkAllocationCallbacks* allocator) =>
        ((delegate* unmanaged<VkInstance, VkDebugUtilsMessengerEXT, VkAllocationCallbacks*, void>)vkDestroyDebugUtilsMessengerEXT_ptr.Value)(Instance, messenger, allocator);

    public VkResult vkCreateWaylandSurfaceKHR(VkWaylandSurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, out VkSurfaceKHR surface)
    {
        Unsafe.SkipInit(out surface);

        fixed (VkSurfaceKHR* surfacePtr = &surface)
        {
            return ((delegate* unmanaged<VkInstance, VkWaylandSurfaceCreateInfoKHR*, VkAllocationCallbacks*, VkSurfaceKHR*, VkResult>)vkCreateWaylandSurfaceKHR_ptr.Value)(Instance, createInfo, allocator, surfacePtr);
        }
    }

    public VkResult vkCreateWin32SurfaceKHR(VkWin32SurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, out VkSurfaceKHR surface)
    {
        Unsafe.SkipInit(out surface);

        fixed (VkSurfaceKHR* surfacePtr = &surface)
        {
            return ((delegate* unmanaged<VkInstance, VkWin32SurfaceCreateInfoKHR*, VkAllocationCallbacks*, VkSurfaceKHR*, VkResult>)vkCreateWin32SurfaceKHR_ptr.Value)(Instance, createInfo, allocator, surfacePtr);
        }
    }

    public VkResult vkCreateXcbSurfaceKHR(VkXcbSurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface) =>
        ((delegate* unmanaged<VkInstance, VkXcbSurfaceCreateInfoKHR*, VkAllocationCallbacks*, VkSurfaceKHR*, VkResult>)vkCreateXcbSurfaceKHR_ptr.Value)(Instance, createInfo, allocator, surface);

    public VkResult vkCreateXlibSurfaceKHR(VkXlibSurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, out VkSurfaceKHR surface)
    {
        Unsafe.SkipInit(out surface);

        fixed (VkSurfaceKHR* surfacePtr = &surface)
        {
            return ((delegate* unmanaged<VkInstance, VkXlibSurfaceCreateInfoKHR*, VkAllocationCallbacks*, VkSurfaceKHR*, VkResult>)vkCreateXlibSurfaceKHR_ptr.Value)(Instance, createInfo, allocator, surfacePtr);
        }
    }
}
