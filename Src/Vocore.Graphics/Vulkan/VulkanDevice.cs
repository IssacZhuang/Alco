using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Vortice.Vulkan.Vma;

namespace Vocore.Graphics.Vulkan;

internal unsafe class VulkanDevice : GPUDevice
{
    #region Members
    private static readonly string[] RequiredInstanceExtension = new string[]
    {
        VK_KHR_SURFACE_EXTENSION_NAME, //VK_KHR_surface
        VK_EXT_DEBUG_UTILS_EXTENSION_NAME, //VK_EXT_debug_utils
        VK_EXT_SWAPCHAIN_COLOR_SPACE_EXTENSION_NAME, //VK_EXT_swapchain_colorspace
    };

    private static readonly string[] RequiredDeviceExtension = new string[]
    {
        VK_KHR_SWAPCHAIN_EXTENSION_NAME, //VK_KHR_swapchain
    };

    private readonly VkDebugUtilsMessengerEXT _debugMessenger = VkDebugUtilsMessengerEXT.Null;
    private readonly VkInstance _instance;
    private readonly VkSurfaceKHR _surface;
    private readonly VkPhysicalDevice _physicalDevice = VkPhysicalDevice.Null;
    private readonly VkDevice _native;
    private readonly VkQueue _graphicsQueue;
    private readonly VkQueue _presentQueue;

    private readonly PhysicalDeviceExtensions _physicalExtensions;
    private readonly VmaAllocator _allocator;

    private readonly PixelFormat _prefferedSurfaceFomat;
    private readonly PixelFormat? _prefferedDepthStencilFormat;

    private readonly uint _graphicsQueueIndex;
    private readonly uint _presentQueueIndex;

    //managed
    private readonly VulkanRenderPass _swapChainRenderPass;
    private VulkanSwapChainFrameBuffer _swapChainFrameBuffer; // recreate on resize


    #endregion

    #region Abstract Implementations
    public override GPURenderPass SwapChainRenderPass => throw new NotImplementedException();

    public override GPUFrameBuffer SwapChainFrameBuffer => throw new NotImplementedException();

    public override PixelFormat PrefferedSurfaceFomat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedSurfaceFomat;
    }

    public override PixelFormat? PrefferedDepthStencilFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefferedDepthStencilFormat;
    }

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
        return new VulkanCommandBuffer(this, descriptor);
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



    protected override void ResizeSurfaceCore(uint width, uint height)
    {
        throw new NotImplementedException();
    }

    protected override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        VulkanCommandBuffer vulkanCommandBuffer = (VulkanCommandBuffer)commandBuffer;
        
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

    protected override void Dispose(bool disposing)
    {
        //destroy managed
        _swapChainFrameBuffer.Dispose();
        _swapChainRenderPass.Dispose();

        //destroy native
        vkDestroyDevice(_native, null);
        vmaDestroyAllocator(_allocator);
        vkDestroySurfaceKHR(_instance, _surface, null);
        vkDestroyDebugUtilsMessengerEXT(_instance, _debugMessenger, null);
        vkDestroyInstance(_instance, null);
    }

    #endregion

    #region Vulkan Specific

    public VkDevice Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    public VmaAllocator Allocator
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _allocator;
    }

    public uint GraphicsQueueIndex
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _graphicsQueueIndex;
    }

    public uint PresentQueueIndex
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _presentQueueIndex;
    }

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
            validationLayers = UtilsVulkan.GetOptimalValidationLayers();
            if (validationLayers.Length == 0)
            {
                GraphicsLogger.Warning("Validation layers are requested but not available found.");
            }
            else
            {
                GraphicsLogger.Info("Validation layers enabled: " + string.Join(",", validationLayers));
            }
        }

        using VkStringArray layers = new VkStringArray(validationLayers);

        string[] availableInstanceExtensions = UtilsVulkan.GetInstanceExtensions();
        List<string> instanceExtensions = new List<string>();

        foreach (string extension in availableInstanceExtensions)
        {
            for (int i = 0; i < RequiredInstanceExtension.Length; i++)
            {
                if (extension == RequiredInstanceExtension[i])
                {
                    instanceExtensions.Add(extension);
                    break;
                }
            }
        }

        string? surfaceExtension = UtilsVulkan.GetSurfaceExtesion(descriptor.SurfaceSource);
        if (surfaceExtension != null)
        {
            instanceExtensions.Add(surfaceExtension);
        }

        using VkStringArray extensions = new VkStringArray(instanceExtensions);

        VkInstanceCreateInfo instanceInfo = new VkInstanceCreateInfo
        {
            pApplicationInfo = &applicationInfo,
            enabledLayerCount = (uint)validationLayers.Length,
            ppEnabledLayerNames = layers,
            enabledExtensionCount = (uint)instanceExtensions.Count,
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

        foreach (string extension in instanceExtensions)
        {
            GraphicsLogger.Info("Instance extension: " + extension);
        }
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
            throw new GraphicsException("Failed to find physical GPUs with Vulkan support");
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

        _physicalExtensions = UtilsVulkan.QueryPhysicalDeviceExtensions(_physicalDevice);

        var (graphicsQueueIndex, presentQueueIndex) = FindQueueIndex(_physicalDevice, _surface);
        int queueCount = graphicsQueueIndex == presentQueueIndex ? 1 : 2;
        bool isQueuesSame = graphicsQueueIndex == presentQueueIndex;
        uint* queueFamilyIndices = stackalloc uint[queueCount];
        float priority = 1.0f;

        _graphicsQueueIndex = graphicsQueueIndex;
        _presentQueueIndex = presentQueueIndex;

        VkDeviceQueueCreateInfo* queueCreateInfo = stackalloc VkDeviceQueueCreateInfo[2];
        
        queueCreateInfo[0] = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = graphicsQueueIndex,
            queueCount = 1,
            pQueuePriorities = &priority
        };
        queueFamilyIndices[0] = graphicsQueueIndex;

        if (queueCount == 2)
        {
            queueCreateInfo[1] = new VkDeviceQueueCreateInfo
            {
                queueFamilyIndex = presentQueueIndex,
                queueCount = 1,
                pQueuePriorities = &priority
            };
            queueFamilyIndices[1] = presentQueueIndex;
        }

        List<string> enabledDeviceExtensions = new List<string>(RequiredDeviceExtension);

        using VkStringArray deviceExtensions = new VkStringArray(enabledDeviceExtensions);

        //use no extensions for now
        VkDeviceCreateInfo deviceCreateInfo = new VkDeviceCreateInfo
        {
            queueCreateInfoCount = (uint)queueCount,
            pQueueCreateInfos = queueCreateInfo,
            enabledExtensionCount = deviceExtensions.Length,
            ppEnabledExtensionNames = deviceExtensions,
            pEnabledFeatures = null
        };

        vkCreateDevice(_physicalDevice, &deviceCreateInfo, null, out _native).CheckResult("Failed to create logical device");
        vkLoadDevice(_native);

        //get queues
        vkGetDeviceQueue(_native, graphicsQueueIndex, 0, out _graphicsQueue);
        vkGetDeviceQueue(_native, presentQueueIndex, 0, out _presentQueue);

        GraphicsLogger.Info("Device created");

        //create allocator
        VmaAllocatorCreateInfo allocatorCreateInfo;
        allocatorCreateInfo.vulkanApiVersion = VkVersion.Version_1_3;
        allocatorCreateInfo.physicalDevice = _physicalDevice;
        allocatorCreateInfo.device = _native;
        allocatorCreateInfo.instance = _instance;

        // Core in 1.1
        allocatorCreateInfo.flags = VmaAllocatorCreateFlags.KHRDedicatedAllocation | VmaAllocatorCreateFlags.KHRBindMemory2;

        if (_physicalExtensions.MemoryBudget)
        {
            allocatorCreateInfo.flags |= VmaAllocatorCreateFlags.EXTMemoryBudget;
        }

        if (_physicalExtensions.AMD_DeviceCoherentMemory)
        {
            allocatorCreateInfo.flags |= VmaAllocatorCreateFlags.AMDDeviceCoherentMemory;
        }

        // if (PhysicalDeviceFeatures1_2.bufferDeviceAddress)
        // {
        //     allocatorCreateInfo.flags = VmaAllocatorCreateFlags.BufferDeviceAddress;
        // }

        if (_physicalExtensions.MemoryPriority)
        {
            allocatorCreateInfo.flags |= VmaAllocatorCreateFlags.EXTMemoryPriority;
        }

        vmaCreateAllocator(&allocatorCreateInfo, out _allocator).CheckResult();

        //create swap chain
        SwapChainSupportDetails swapChainSupportDetails = UtilsVulkan.GetSwapChainSupportDetails(_physicalDevice, _surface);
        VkSurfaceFormatKHR surfaceFormat = UtilsVulkan.GetPreferredSurfaceFormat(swapChainSupportDetails.Formats);


        //get preffered surface format
        VkFormat nativePrefferedSurfaceFormat = UtilsVulkan.GetPreferredSurfaceFormat(swapChainSupportDetails.Formats).format;
        _prefferedSurfaceFomat = UtilsVulkan.PixelFormatToAbstract(nativePrefferedSurfaceFormat);

        //get preffered depth format
        VkFormat? nativePrefferedDepthFormat = UtilsVulkan.GetPreferredDepthFormat(_physicalDevice, VkImageTiling.Optimal);
       
        if (nativePrefferedDepthFormat.HasValue)
        {
            _prefferedDepthStencilFormat = UtilsVulkan.PixelFormatToAbstract(nativePrefferedDepthFormat.Value);
        }
        else if (descriptor.DepthFormat.HasValue)
        {
            GraphicsLogger.Warning("Requested depth format is not supported, falling back to default");
        }


        RenderPassDescriptor renderPassDescriptor = new RenderPassDescriptor(
            new ColorAttachment[]
            {
                new ColorAttachment(_prefferedSurfaceFomat)
            },
            descriptor.DepthFormat.HasValue ? new DepthAttachment(descriptor.DepthFormat.Value) : null
        );

        _swapChainRenderPass = new VulkanRenderPass(_native, renderPassDescriptor);

        VkPresentModeKHR presentMode = UtilsVulkan.GetPreferredPresentMode(swapChainSupportDetails.PresentModes, descriptor.VSync);
        VkSwapchainCreateInfoKHR swapChainCreateInfo = new VkSwapchainCreateInfoKHR
        {
            surface = _surface,
            minImageCount = swapChainSupportDetails.GetPrefferedImageCount(2),
            imageFormat = surfaceFormat.format,
            imageColorSpace = surfaceFormat.colorSpace,
            imageExtent = new VkExtent2D(descriptor.InitialSurfaceSizeWidth, descriptor.InitialSurfaceSizeHeight),
            imageArrayLayers = 1,
            imageUsage = VkImageUsageFlags.ColorAttachment,
            imageSharingMode = isQueuesSame? VkSharingMode.Exclusive : VkSharingMode.Concurrent,
            queueFamilyIndexCount = (uint)(isQueuesSame ? 0 : 2),
            pQueueFamilyIndices = isQueuesSame ? null : queueFamilyIndices,
            presentMode = presentMode,
            preTransform = swapChainSupportDetails.Capabilities.currentTransform,
            compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
            clipped = true,
            oldSwapchain = VkSwapchainKHR.Null
        };

        _swapChainFrameBuffer = new VulkanSwapChainFrameBuffer(this, _swapChainRenderPass, swapChainCreateInfo);

        GraphicsLogger.Info("Swap chain created");
        
    }

    private static bool IsDeviceSuitable(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface)
    {
        var (graphicsFamily, presentFamily) = FindQueueIndex(physicalDevice, surface);
        if (graphicsFamily == VK_QUEUE_FAMILY_IGNORED)
            return false;

        if (presentFamily == VK_QUEUE_FAMILY_IGNORED)
            return false;

        SwapChainSupportDetails supportDetails = UtilsVulkan.GetSwapChainSupportDetails(physicalDevice, surface);
        return !supportDetails.Formats.IsEmpty && !supportDetails.PresentModes.IsEmpty;
    }

    private static (uint graphicsFamily, uint presentFamily) FindQueueIndex(
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