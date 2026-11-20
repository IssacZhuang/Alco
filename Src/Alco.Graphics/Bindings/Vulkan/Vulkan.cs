// Minimal Windows/Linux/macOS Vulkan loader, global entry points, and constants.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vulkan;

internal static unsafe partial class Vulkan
{
    public const float VK_LOD_CLAMP_NONE = 1000.0F;
    public const uint VK_QUEUE_FAMILY_IGNORED = ~0u;
    public const uint VK_REMAINING_ARRAY_LAYERS = ~0u;
    public const uint VK_REMAINING_MIP_LEVELS = ~0u;
    public const uint VK_MAX_EXTENSION_NAME_SIZE = 256u;

    public static ReadOnlySpan<byte> VK_KHR_SURFACE_EXTENSION_NAME => "VK_KHR_surface"u8;
    public static ReadOnlySpan<byte> VK_KHR_SWAPCHAIN_EXTENSION_NAME => "VK_KHR_swapchain"u8;
    public static ReadOnlySpan<byte> VK_KHR_XLIB_SURFACE_EXTENSION_NAME => "VK_KHR_xlib_surface"u8;
    public static ReadOnlySpan<byte> VK_KHR_XCB_SURFACE_EXTENSION_NAME => "VK_KHR_xcb_surface"u8;
    public static ReadOnlySpan<byte> VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME => "VK_KHR_wayland_surface"u8;
    public static ReadOnlySpan<byte> VK_KHR_WIN32_SURFACE_EXTENSION_NAME => "VK_KHR_win32_surface"u8;
    public static ReadOnlySpan<byte> VK_KHR_DYNAMIC_RENDERING_EXTENSION_NAME => "VK_KHR_dynamic_rendering"u8;
    public static ReadOnlySpan<byte> VK_KHR_GET_SURFACE_CAPABILITIES_2_EXTENSION_NAME => "VK_KHR_get_surface_capabilities2"u8;
    public static ReadOnlySpan<byte> VK_KHR_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME => "VK_KHR_swapchain_maintenance1"u8;
    public static ReadOnlySpan<byte> VK_KHR_SURFACE_MAINTENANCE_1_EXTENSION_NAME => "VK_KHR_surface_maintenance1"u8;
    public static ReadOnlySpan<byte> VK_KHR_SYNCHRONIZATION_2_EXTENSION_NAME => "VK_KHR_synchronization2"u8;
    public static ReadOnlySpan<byte> VK_KHR_DEVICE_ADDRESS_COMMANDS_EXTENSION_NAME => "VK_KHR_device_address_commands"u8;
    public static ReadOnlySpan<byte> VK_KHR_COPY_COMMANDS_2_EXTENSION_NAME => "VK_KHR_copy_commands2"u8;
    public static ReadOnlySpan<byte> VK_KHR_FORMAT_FEATURE_FLAGS_2_EXTENSION_NAME => "VK_KHR_format_feature_flags2"u8;
    public static ReadOnlySpan<byte> VK_KHR_SHADER_UNTYPED_POINTERS_EXTENSION_NAME => "VK_KHR_shader_untyped_pointers"u8;
    public static ReadOnlySpan<byte> VK_KHR_MAINTENANCE_4_EXTENSION_NAME => "VK_KHR_maintenance4"u8;
    public static ReadOnlySpan<byte> VK_KHR_UNIFIED_IMAGE_LAYOUTS_EXTENSION_NAME => "VK_KHR_unified_image_layouts"u8;
    public static ReadOnlySpan<byte> VK_EXT_SWAPCHAIN_COLOR_SPACE_EXTENSION_NAME => "VK_EXT_swapchain_colorspace"u8;
    public static ReadOnlySpan<byte> VK_EXT_DEBUG_UTILS_EXTENSION_NAME => "VK_EXT_debug_utils"u8;
    public static ReadOnlySpan<byte> VK_EXT_DESCRIPTOR_HEAP_EXTENSION_NAME => "VK_EXT_descriptor_heap"u8;
    public static ReadOnlySpan<byte> VK_EXT_EXTENDED_DYNAMIC_STATE_EXTENSION_NAME => "VK_EXT_extended_dynamic_state"u8;
    public static ReadOnlySpan<byte> VK_EXT_SURFACE_MAINTENANCE_1_EXTENSION_NAME => "VK_EXT_surface_maintenance1"u8;
    public static ReadOnlySpan<byte> VK_EXT_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME => "VK_EXT_swapchain_maintenance1"u8;
    public static ReadOnlySpan<byte> VK_EXT_MESH_SHADER_EXTENSION_NAME => "VK_EXT_mesh_shader"u8;

    private static nint s_vulkanModule;
    private static readonly ConcurrentDictionary<VkInstance, VkInstanceApi> s_instanceTables = new();
    private static readonly ConcurrentDictionary<VkDevice, VkDeviceApi> s_deviceTables = new();

    public static delegate* unmanaged<VkInstance, byte*, PFN_vkVoidFunction> vkGetInstanceProcAddr_ptr;
    public static PFN_vkVoidFunction vkCreateInstance_ptr;
    public static PFN_vkVoidFunction vkEnumerateInstanceExtensionProperties_ptr;
    public static PFN_vkVoidFunction vkEnumerateInstanceLayerProperties_ptr;
    public static PFN_vkVoidFunction vkEnumerateInstanceVersion_ptr;

    public static VkResult vkInitialize(string? libraryName = default)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!string.IsNullOrEmpty(libraryName))
                s_vulkanModule = LoadNativeLibrary(libraryName);

            if (s_vulkanModule == 0)
                s_vulkanModule = LoadNativeLibrary("vulkan-1.dll");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (!string.IsNullOrEmpty(libraryName))
                s_vulkanModule = LoadNativeLibrary(libraryName);

            if (s_vulkanModule == 0)
            {
                s_vulkanModule = LoadNativeLibrary("libvulkan.dylib");
                if (s_vulkanModule == 0)
                    s_vulkanModule = LoadNativeLibrary("libvulkan.1.dylib");
                if (s_vulkanModule == 0)
                    s_vulkanModule = LoadNativeLibrary("libMoltenVK.dylib");
                if (s_vulkanModule == 0)
                    s_vulkanModule = LoadNativeLibrary("vulkan.framework/vulkan");
                if (s_vulkanModule == 0)
                    s_vulkanModule = LoadNativeLibrary("MoltenVK.framework/MoltenVK");
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(libraryName))
                s_vulkanModule = LoadNativeLibrary(libraryName);

            if (s_vulkanModule == 0)
            {
                s_vulkanModule = LoadNativeLibrary("libvulkan.so.1");
                if (s_vulkanModule == 0)
                    s_vulkanModule = LoadNativeLibrary("libvulkan.so");
            }
        }

        if (s_vulkanModule == 0)
            return VkResult.ErrorInitializationFailed;

        vkGetInstanceProcAddr_ptr = (delegate* unmanaged<VkInstance, byte*, PFN_vkVoidFunction>)NativeLibrary.GetExport(s_vulkanModule, nameof(vkGetInstanceProcAddr));
        vkCreateInstance_ptr = vkGetGlobalProcAddr("vkCreateInstance"u8);
        vkEnumerateInstanceExtensionProperties_ptr = vkGetGlobalProcAddr("vkEnumerateInstanceExtensionProperties"u8);
        vkEnumerateInstanceLayerProperties_ptr = vkGetGlobalProcAddr("vkEnumerateInstanceLayerProperties"u8);
        vkEnumerateInstanceVersion_ptr = vkGetGlobalProcAddr("vkEnumerateInstanceVersion"u8);
        return VkResult.Success;
    }

    public static void vkShutdown()
    {
        if (s_vulkanModule != 0)
        {
            NativeLibrary.Free(s_vulkanModule);
            s_vulkanModule = 0;
        }
    }

    public static VkInstanceApi GetApi(VkInstance instance) =>
        s_instanceTables.GetOrAdd(instance, instance => new VkInstanceApi(instance));

    public static VkDeviceApi GetApi(VkInstance instance, VkDevice device) =>
        s_deviceTables.GetOrAdd(device, device => new VkDeviceApi(GetApi(instance), device));

    public static bool RemoveInstanceTable(VkInstance instance) =>
        s_instanceTables.TryRemove(instance, out _);

    public static bool RemoveDeviceTable(VkDevice device) =>
        s_deviceTables.TryRemove(device, out _);

    public static PFN_vkVoidFunction vkGetGlobalProcAddr(ReadOnlySpan<byte> name)
    {
        fixed (byte* pName = name)
            return vkGetInstanceProcAddr_ptr(default, pName);
    }

    public static PFN_vkVoidFunction vkGetInstanceProcAddr(nint instance, ReadOnlySpan<byte> name)
    {
        fixed (byte* pName = name)
            return vkGetInstanceProcAddr_ptr(instance, pName);
    }

    public static VkResult vkCreateInstance(VkInstanceCreateInfo* createInfo, VkAllocationCallbacks* allocator, out VkInstance instance)
    {
        Unsafe.SkipInit(out instance);
        fixed (VkInstance* instancePtr = &instance)
            return ((delegate* unmanaged<VkInstanceCreateInfo*, VkAllocationCallbacks*, VkInstance*, VkResult>)vkCreateInstance_ptr.Value)(createInfo, allocator, instancePtr);
    }

    public static VkResult vkEnumerateInstanceExtensionProperties(uint* propertyCount, VkExtensionProperties* properties) =>
        ((delegate* unmanaged<byte*, uint*, VkExtensionProperties*, VkResult>)vkEnumerateInstanceExtensionProperties_ptr.Value)(null, propertyCount, properties);

    public static VkResult vkEnumerateInstanceLayerProperties(uint* propertyCount, VkLayerProperties* properties) =>
        ((delegate* unmanaged<uint*, VkLayerProperties*, VkResult>)vkEnumerateInstanceLayerProperties_ptr.Value)(propertyCount, properties);

    public static VkResult vkEnumerateInstanceVersion(uint* apiVersion) =>
        ((delegate* unmanaged<uint*, VkResult>)vkEnumerateInstanceVersion_ptr.Value)(apiVersion);

    private static nint LoadNativeLibrary(string name) =>
        NativeLibrary.TryLoad(name, out nint lib) ? lib : 0;
}
