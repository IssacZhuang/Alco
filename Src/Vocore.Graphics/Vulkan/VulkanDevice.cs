using System.Runtime.InteropServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

public unsafe class VulkanDevice : GPUDevice
{
    #region Members
    private readonly VkDebugUtilsMessengerEXT _debugMessenger = VkDebugUtilsMessengerEXT.Null;
    private readonly VkInstance _instance;
    private readonly VkSurfaceKHR _surface;
    private readonly VkPhysicalDevice _physicalDevice = VkPhysicalDevice.Null;
    private readonly VkDevice _native;


    #endregion

    #region Abstract Implementations
    public override GPURenderPass SwapChainRenderPass => throw new NotImplementedException();

    public override GPUFrameBuffer SwapChainFrameBuffer => throw new NotImplementedException();

    public override PixelFormat PrefferedSurfaceFomat => throw new NotImplementedException();

    public override PixelFormat? PrefferedDepthStencilFormat => throw new NotImplementedException();

    public override bool VSync { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public override GPUSampler SamplerNearestRepeat => throw new NotImplementedException();

    public override GPUSampler SamplerLinearRepeat => throw new NotImplementedException();

    public override GPUSampler SamplerNearestClamp => throw new NotImplementedException();

    public override GPUSampler SamplerLinearClamp => throw new NotImplementedException();

    public override GPUSampler SamplerNearestMirrorRepeat => throw new NotImplementedException();

    public override GPUSampler SamplerLinearMirrorRepeat => throw new NotImplementedException();

    public override GPUBindGroup BindGroupBuffer => throw new NotImplementedException();

    public override GPUBindGroup BindGroupTexture2DSampled => throw new NotImplementedException();

    public override GPUBindGroup BindGroupTexture2DRead => throw new NotImplementedException();

    public override GPUBindGroup BindGroupStorageTexture2D => throw new NotImplementedException();

    public override string Name => throw new NotImplementedException();

    protected override GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
    {
        throw new NotImplementedException();
    }

    protected override GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override GPURenderPass CreateRenderPassCore(in RenderPassDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyBindGroupCore(GPUBindGroup bindGroup)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyBufferCore(GPUBuffer buffer)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyCommandBufferCore(GPUCommandBuffer commandBuffer)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyComputePipelineCore(GPUPipeline pipeline)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyGraphicsPipelineCore(GPUPipeline pipeline)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyRenderPassCore(GPURenderPass renderPass)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyResourceGroupCore(GPUResourceGroup resourceGroup)
    {
        throw new NotImplementedException();
    }

    protected override void DestroySamplerCore(GPUSampler sampler)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyTextureCore(GPUTexture texture)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyTextureViewCore(GPUTextureView textureView)
    {
        throw new NotImplementedException();
    }

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }

    protected override void ResizeSurfaceCore(uint width, uint height)
    {
        throw new NotImplementedException();
    }

    protected override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        throw new NotImplementedException();
    }

    protected override void SwapBuffersCore()
    {
        throw new NotImplementedException();
    }

    protected override unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        throw new NotImplementedException();
    }

    protected override unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint pixelSize, uint mipLevel)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Vulkan Specific

    public VulkanDevice(DeviceDescriptor descriptor)
    {
        if (!IsVulkanSupported())
        {
            throw new NotSupportedException("Vulkan 1.1 or higher is required");
        }

        //native string
        using VkString appName = new VkString(descriptor.Name);
        using VkString engineName = new VkString("Vocore");

        //create instance

        VkApplicationInfo applicationInfo = new VkApplicationInfo
        {
            pApplicationName = appName,
            pEngineName = engineName,
            applicationVersion = new VkVersion(1, 0, 0),
            engineVersion = new VkVersion(1, 0, 0),
            apiVersion = VkVersion.Version_1_3
        };

        string[] validationLayers = Array.Empty<string>();
        if (descriptor.Debug)
        {
            //enable validation layers
            validationLayers = GetOptimalValidationLayers(EnumerateInstanceLayers());
            if (validationLayers.Length == 0)
            {
                GraphicsLogger.Warning("Validation layers are requested but not available found.");
            }
        }

        using VkStringArray layers = new VkStringArray(validationLayers);

        string[] instanceExtensions = GetInstanceExtensions();
        using VkStringArray extensions = new VkStringArray(instanceExtensions);

        VkInstanceCreateInfo instanceInfo = new VkInstanceCreateInfo
        {
            pApplicationInfo = &applicationInfo,
            enabledLayerCount = (uint)validationLayers.Length,
            ppEnabledLayerNames = layers,
            enabledExtensionCount = (uint)instanceExtensions.Length,
            ppEnabledExtensionNames = extensions
        };

        VkDebugUtilsMessengerCreateInfoEXT debugUtilsCreateInfo = new();

        bool hasValidationLayer = validationLayers.Length > 0;

        if (hasValidationLayer)
        {
            debugUtilsCreateInfo.messageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.Error | VkDebugUtilsMessageSeverityFlagsEXT.Warning;
            debugUtilsCreateInfo.messageType = VkDebugUtilsMessageTypeFlagsEXT.Validation | VkDebugUtilsMessageTypeFlagsEXT.Performance;
            debugUtilsCreateInfo.pfnUserCallback = &DebugMessengerCallback;
            instanceInfo.pNext = &debugUtilsCreateInfo;
        }

        vkCreateInstance(&instanceInfo, null, out _instance).CheckResult("Failed to create Vulkan instance");
        vkLoadInstanceOnly(_instance);

        //create debug messenger

        if (hasValidationLayer)
        {
            vkCreateDebugUtilsMessengerEXT(_instance, &debugUtilsCreateInfo, null, out _debugMessenger).CheckResult("Failed to create debug messenger");
        }

        //create surface

        _surface = UtilsVulkan.CreateSurface(_instance, descriptor.SurfaceSource);

        //create physical device
        uint physicalDeviceCount = 0;
        vkEnumeratePhysicalDevices(_instance, &physicalDeviceCount, null).CheckResult("Failed to enumerate physical devices");

        if (physicalDeviceCount == 0)
        {
            throw new GraphicsException("Vulkan: Failed to find GPUs with Vulkan support");
        }

        VkPhysicalDevice* physicalDevices = stackalloc VkPhysicalDevice[(int)physicalDeviceCount];
        vkEnumeratePhysicalDevices(_instance, &physicalDeviceCount, physicalDevices).CheckResult("Failed to enumerate physical devices");

        for (int i = 0; i < physicalDeviceCount; i++)
        {
            VkPhysicalDevice physicalDevice = physicalDevices[i];

            if (IsDeviceSuitable(physicalDevice, _surface) == false)
                continue;

            vkGetPhysicalDeviceProperties(physicalDevice, out VkPhysicalDeviceProperties checkProperties);
            bool discrete = checkProperties.deviceType == VkPhysicalDeviceType.DiscreteGpu;

            if (discrete || _physicalDevice.IsNull)
            {
                _physicalDevice = physicalDevice;
                if (discrete)
                {
                    // If this is discrete GPU, look no further (prioritize discrete GPU)
                    break;
                }
            }
        }

        var (graphicsFamily, presentFamily) = FindQueueFamilies(_physicalDevice, _surface);
        int queueCount = graphicsFamily == presentFamily ? 1 : 2;
        VkDeviceQueueCreateInfo* queueCreateInfo = stackalloc VkDeviceQueueCreateInfo[queueCount];
        

        float priority = 1.0f;
        queueCreateInfo[0] = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = graphicsFamily,
            queueCount = 1,
            pQueuePriorities = &priority
        };

        if (queueCount >= 2)
        {
            queueCreateInfo[1] = new VkDeviceQueueCreateInfo
            {
                queueFamilyIndex = presentFamily,
                queueCount = 1,
                pQueuePriorities = &priority
            };
        }

        VkDeviceCreateInfo deviceCreateInfo = new VkDeviceCreateInfo
        {
            queueCreateInfoCount = 2,
            pQueueCreateInfos = queueCreateInfo
        };
    }

    private static bool IsDeviceSuitable(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface)
    {
        var (graphicsFamily, presentFamily) = FindQueueFamilies(physicalDevice, surface);
        if (graphicsFamily == VK_QUEUE_FAMILY_IGNORED)
            return false;

        if (presentFamily == VK_QUEUE_FAMILY_IGNORED)
            return false;

        VkSurfaceCapabilitiesKHR vkSurfaceCapabilities; 
        vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, surface, &vkSurfaceCapabilities).CheckResult();
        ReadOnlySpan<VkSurfaceFormatKHR> formats = vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface);
        ReadOnlySpan<VkPresentModeKHR> presentModes = vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, surface);
        return !formats.IsEmpty && !presentModes.IsEmpty;
    }

    private static (uint graphicsFamily, uint presentFamily) FindQueueFamilies(
        VkPhysicalDevice device, VkSurfaceKHR surface)
    {
        ReadOnlySpan<VkQueueFamilyProperties> queueFamilies = vkGetPhysicalDeviceQueueFamilyProperties(device);

        uint graphicsFamily = VK_QUEUE_FAMILY_IGNORED;
        uint presentFamily = VK_QUEUE_FAMILY_IGNORED;
        uint i = 0;
        foreach (VkQueueFamilyProperties queueFamily in queueFamilies)
        {
            if ((queueFamily.queueFlags & VkQueueFlags.Graphics) != VkQueueFlags.None)
            {
                graphicsFamily = i;
            }

            vkGetPhysicalDeviceSurfaceSupportKHR(device, i, surface, out VkBool32 presentSupport);
            if (presentSupport)
            {
                presentFamily = i;
            }

            if (graphicsFamily != VK_QUEUE_FAMILY_IGNORED
                && presentFamily != VK_QUEUE_FAMILY_IGNORED)
            {
                break;
            }

            i++;
        }

        return (graphicsFamily, presentFamily);
    }

    private static string[] EnumerateInstanceLayers()
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

    private static bool IsVulkanSupported()
    {
        try
        {
            VkResult result = vkInitialize();
            if (result != VkResult.Success)
                return false;

            // We require Vulkan 1.1 or higher
            VkVersion version = vkEnumerateInstanceVersion();
            if (version < VkVersion.Version_1_1)
                return false;

            // TODO: Enumerate physical devices and try to create instance.

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string[] GetInstanceExtensions()
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

    private static string[] GetOptimalValidationLayers(string[] availableLayers)
    {
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
                //Log.Warn("Validation Layer '{}' not found", layer);
                return false;
            }
        }

        return true;
    }

    [UnmanagedCallersOnly]
    private static uint DebugMessengerCallback(VkDebugUtilsMessageSeverityFlagsEXT messageSeverity,
        VkDebugUtilsMessageTypeFlagsEXT messageTypes,
        VkDebugUtilsMessengerCallbackDataEXT* pCallbackData,
        void* userData)
    {
        string message = new(pCallbackData->pMessage);
        if (messageTypes == VkDebugUtilsMessageTypeFlagsEXT.Validation)
        {
            if (messageSeverity == VkDebugUtilsMessageSeverityFlagsEXT.Error)
            {
                GraphicsLogger.Error($"[Vulkan]: Validation: {messageSeverity} - {message}");
            }
            else if (messageSeverity == VkDebugUtilsMessageSeverityFlagsEXT.Warning)
            {
                GraphicsLogger.Warning($"[Vulkan]: Validation: {messageSeverity} - {message}");
            }

            GraphicsLogger.Info($"[Vulkan]: Validation: {messageSeverity} - {message}");
        }
        else
        {
            if (messageSeverity == VkDebugUtilsMessageSeverityFlagsEXT.Error)
            {
                GraphicsLogger.Error($"[Vulkan]: {messageSeverity} - {message}");
            }
            else if (messageSeverity == VkDebugUtilsMessageSeverityFlagsEXT.Warning)
            {
                GraphicsLogger.Warning($"[Vulkan]: {messageSeverity} - {message}");
            }

            GraphicsLogger.Info($"[Vulkan]: {messageSeverity} - {message}");
        }

        return VK_FALSE;
    }

    #endregion
}