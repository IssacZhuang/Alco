// Per-surface Vulkan swapchains, presentation contexts, image retirement, and acquire/present transitions.

using System.Diagnostics;
using Vk = Vulkan;
using static global::Alco.Graphics.LowLevel.VulkanBackend;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed class PresentContext
{
    internal Vk.VkSemaphore Acquired;
    internal Vk.VkSemaphore Rendered;
    internal Vk.VkFence Presented;
    internal Vk.VkCommandBuffer Tail;
    internal Vk.VkSwapchainKHR Swapchain;
    internal ulong RetirementValue;
    internal bool PresentPending;
}

internal sealed class RetiredSwapchain
{
    internal Vk.VkSwapchainKHR Handle;
    internal Vk.VkImageView[] Views = new Vk.VkImageView[Backend.MaxSwapchainImages];
    internal uint ViewCount;
    internal ulong RetirementValue;
}

/// <summary>A Vulkan presentation surface and its independently acquired images.</summary>
internal sealed unsafe class VulkanSwapchain : GPUSwapchain
{
    private const Vk.VkImageUsageFlags ImageUsage = Vk.VkImageUsageFlags.ColorAttachment | Vk.VkImageUsageFlags.TransferSrc | Vk.VkImageUsageFlags.TransferDst;

    internal VulkanDeviceBase? Owner;
    internal Vk.VkSurfaceKHR Surface;
    internal Vk.VkSwapchainKHR Handle;
    internal Vk.VkImage[] Images = new Vk.VkImage[Backend.MaxSwapchainImages];
    internal VulkanTexture?[] Textures = new VulkanTexture?[Backend.MaxSwapchainImages];
    internal VulkanRenderView?[] RenderViews = new VulkanRenderView?[Backend.MaxSwapchainImages];
    internal bool[] Initialized = new bool[Backend.MaxSwapchainImages];
    internal uint ImageCount;
    internal uint ImageIndex;
    internal uint Width;
    internal uint Height;
    internal PresentContext? Context;
    internal VulkanCommandBuffer? AcquireCommands;
    internal bool Acquired;
    internal bool AcquireWaitPending;
    internal bool RecreateRequired;
    internal bool SurfaceLost;
    internal Vk.VkSemaphore RenderRetirement;
    internal ulong RetirementValue;
    internal ulong CompletedRetirement;
    internal string? InitializationFailure;

    private PresentContext[] PresentContexts = [];
    private RetiredSwapchain[] RetiredSwapchains = [];
    private Vk.VkCommandPool TailPool;
    private uint NextPresentContext;
    private uint RetiredCount;
    private uint DesiredImageCount;
    private Extent2D RequestedExtent;
    private Format RequestedFormat;
    private Format ActualFormat;
    private ColorSpace PresentationColorSpace;
    private bool VSyncEnabled;
    private bool Registered;
    private Vk.VkPresentModeKHR PresentMode;
    private Vk.VkSurfaceTransformFlagsKHR Transform;
    private Vk.VkCompositeAlphaFlagsKHR CompositeAlpha;

    private struct Configuration
    {
        internal Vk.VkExtent2D Extent;
        internal Format Format;
        internal Vk.VkColorSpaceKHR ColorSpace;
        internal Vk.VkPresentModeKHR PresentMode;
        internal uint ImageCount;
        internal Vk.VkSurfaceTransformFlagsKHR Transform;
        internal Vk.VkCompositeAlphaFlagsKHR CompositeAlpha;
    }

    /// <summary>The current drawable size, or zero while suspended.</summary>
    public override Extent2D DrawableExtent
    {
        get
        {
            VulkanDeviceBase device = Owner!;
            Debug.Assert(device != null);
            if (SurfaceLost)
                return default;
            Error error = CheckSurfaceResult(
                device.InstanceApi.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(device.PhysicalDevice, Surface, out Vk.VkSurfaceCapabilitiesKHR capabilities),
                "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
            if (error != Error.None)
            {
                if (!SurfaceLost)
                    Backend.RequireError(error, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
                return default;
            }
            Vk.VkExtent2D extent = ResolveExtent(in capabilities, RequestedExtent);
            if (extent.width != Width || extent.height != Height || capabilities.currentTransform != Transform ||
                ChooseCompositeAlpha(capabilities.supportedCompositeAlpha) != CompositeAlpha)
            {
                RecreateRequired = true;
            }
            return new Extent2D(extent.width, extent.height);
        }
    }

    /// <summary>The resolved presentation image format.</summary>
    public override Format Format => ActualFormat;

    /// <summary>The presentation color space.</summary>
    public override ColorSpace ColorSpace => PresentationColorSpace;

    /// <summary>Whether presentation waits for vertical synchronization.</summary>
    public override bool VSync => VSyncEnabled;

    /// <summary>Changes the drawable size, cancelling an acquired frame after its GPU use has completed.</summary>
    /// <param name="extent">The requested size; a zero dimension suspends acquisition.</param>
    /// <param name="error">None on success, or the configuration error.</param>
    /// <returns>true if the swapchain was reconfigured; otherwise false.</returns>
    public override bool TryResize(Extent2D extent, out Error error)
    {
        error = Configure(extent, RequestedFormat, PresentationColorSpace, VSyncEnabled);
        return error == Error.None;
    }

    /// <summary>Changes the presentation format, cancelling an acquired frame after its GPU use has completed.</summary>
    /// <param name="format">The requested format, or Undefined to select the default non-sRGB presentation format for the color space.</param>
    /// <param name="colorSpace">The presentation color space.</param>
    /// <param name="error">None on success, or the configuration error without changing an unsupported configuration.</param>
    /// <returns>true if the configuration was applied; otherwise false.</returns>
    public override bool TrySetFormat(Format format, ColorSpace colorSpace, out Error error)
    {
        error = Configure(RequestedExtent, format, colorSpace, VSyncEnabled);
        return error == Error.None;
    }

    /// <summary>Changes vertical synchronization; unsupported unsynchronized modes leave the current configuration unchanged.</summary>
    /// <param name="enabled">Whether vertical synchronization is enabled.</param>
    /// <param name="error">None on success, or the configuration error.</param>
    /// <returns>true if the mode was applied; otherwise false.</returns>
    public override bool TrySetVSync(bool enabled, out Error error)
    {
        error = Configure(RequestedExtent, RequestedFormat, PresentationColorSpace, enabled);
        return error == Error.None;
    }

    internal Error Initialize(in SwapchainDesc desc)
    {
        VulkanDeviceBase device = Owner!;
        DesiredImageCount = desc.DesiredImageCount;
        Error error = CreateSurface(desc.Surface);
        if (error != Error.None)
            return error;
        error = CheckSurfaceResult(
            device.InstanceApi.vkGetPhysicalDeviceSurfaceSupportKHR(device.PhysicalDevice, device.Queues![0].FamilyIndex, Surface, out Vk.VkBool32 supported),
            "vkGetPhysicalDeviceSurfaceSupportKHR");
        if (error != Error.None)
            return error;
        if (!supported)
        {
            InitializationFailure = "Queue 0 cannot present to the requested Vulkan surface.";
            return Error.Unsupported;
        }

        PresentContexts = new PresentContext[DesiredImageCount];
        for (int index = 0; index < PresentContexts.Length; ++index)
            PresentContexts[index] = new PresentContext();
        RetiredSwapchains = new RetiredSwapchain[Backend.MaxSwapchainImages];
        for (int index = 0; index < RetiredSwapchains.Length; ++index)
            RetiredSwapchains[index] = new RetiredSwapchain();

        Vk.VkSemaphoreTypeCreateInfo typeInfo = new() { semaphoreType = Vk.VkSemaphoreType.Timeline };
        Vk.VkSemaphoreCreateInfo retirementInfo = new() { pNext = &typeInfo };
        error = CheckResult(device.Api.vkCreateSemaphore(&retirementInfo, null, out RenderRetirement), "vkCreateSemaphore (swapchain retirement)");
        if (error != Error.None)
            return error;
        Vk.VkCommandPoolCreateInfo poolInfo = new()
        {
            flags = Vk.VkCommandPoolCreateFlags.Transient | Vk.VkCommandPoolCreateFlags.ResetCommandBuffer,
            queueFamilyIndex = device.Queues[0].FamilyIndex,
        };
        error = CheckResult(device.Api.vkCreateCommandPool(&poolInfo, null, out TailPool), "vkCreateCommandPool (presentation tail)");
        if (error != Error.None)
            return error;
        Vk.VkCommandBufferAllocateInfo allocateInfo = new()
        {
            commandPool = TailPool,
            level = Vk.VkCommandBufferLevel.Primary,
            commandBufferCount = DesiredImageCount,
        };
        Vk.VkCommandBuffer* tails = stackalloc Vk.VkCommandBuffer[(int)DesiredImageCount];
        error = CheckResult(device.Api.vkAllocateCommandBuffers(&allocateInfo, tails), "vkAllocateCommandBuffers (presentation tail)");
        if (error != Error.None)
            return error;
        for (int index = 0; index < PresentContexts.Length; ++index)
        {
            PresentContext context = PresentContexts[index];
            context.Tail = tails[index];
            Vk.VkSemaphoreCreateInfo semaphoreInfo = new();
            error = CheckResult(device.Api.vkCreateSemaphore(&semaphoreInfo, null, out context.Acquired), "vkCreateSemaphore (acquire)");
            if (error != Error.None)
                return error;
            error = CheckResult(device.Api.vkCreateSemaphore(&semaphoreInfo, null, out context.Rendered), "vkCreateSemaphore (render)");
            if (error != Error.None)
                return error;
            Vk.VkFenceCreateInfo fenceInfo = new();
            error = CheckResult(device.Api.vkCreateFence(&fenceInfo, null, out context.Presented), "vkCreateFence (present)");
            if (error != Error.None)
                return error;
        }
        error = Configure(desc.Extent, desc.Format, desc.ColorSpace, desc.VSync);
        if (error != Error.None)
            return error;
        Interlocked.Increment(ref device.SwapchainCount);
        Registered = true;
        return Error.None;
    }

    private Error CreateSurface(SurfaceSource source)
    {
        VulkanDeviceBase device = Owner!;
        switch (source.Kind)
        {
            case SurfaceKind.Win32 when device.Win32SurfaceEnabled && device.InstanceApi.vkCreateWin32SurfaceKHR_ptr != default:
            {
                Debug.Assert(source.Handle != 0);
                Vk.VkWin32SurfaceCreateInfoKHR info = new()
                {
                    hinstance = source.Display != 0 ? source.Display : GetModuleHandleW(0),
                    hwnd = source.Handle,
                };
                return CheckResult(device.InstanceApi.vkCreateWin32SurfaceKHR(&info, null, out Surface), "vkCreateWin32SurfaceKHR");
            }
            case SurfaceKind.Xlib when device.XlibSurfaceEnabled && device.InstanceApi.vkCreateXlibSurfaceKHR_ptr != default:
            {
                Debug.Assert(source.Display != 0 && source.WindowId != 0);
                Vk.VkXlibSurfaceCreateInfoKHR info = new() { dpy = source.Display, window = source.WindowId };
                return CheckResult(device.InstanceApi.vkCreateXlibSurfaceKHR(&info, null, out Surface), "vkCreateXlibSurfaceKHR");
            }
            case SurfaceKind.Xcb when device.XcbSurfaceEnabled && device.InstanceApi.vkCreateXcbSurfaceKHR_ptr != default:
            {
                Debug.Assert(source.Display != 0 && source.WindowId != 0);
                Vk.VkXcbSurfaceCreateInfoKHR info = new() { connection = source.Display, window = (uint)source.WindowId };
                fixed (Vk.VkSurfaceKHR* surface = &Surface)
                    return CheckResult(device.InstanceApi.vkCreateXcbSurfaceKHR(&info, null, surface), "vkCreateXcbSurfaceKHR");
            }
            case SurfaceKind.Wayland when device.WaylandSurfaceEnabled && device.InstanceApi.vkCreateWaylandSurfaceKHR_ptr != default:
            {
                Debug.Assert(source.Display != 0 && source.Handle != 0);
                Vk.VkWaylandSurfaceCreateInfoKHR info = new() { display = source.Display, surface = source.Handle };
                return CheckResult(device.InstanceApi.vkCreateWaylandSurfaceKHR(&info, null, out Surface), "vkCreateWaylandSurfaceKHR");
            }
            default:
                InitializationFailure = $"The Vulkan {source.Kind} surface source is unavailable on this device.";
                return Error.Unsupported;
        }
    }

    private static Vk.VkExtent2D ResolveExtent(in Vk.VkSurfaceCapabilitiesKHR capabilities, Extent2D requested)
    {
        if (requested.X == 0 || requested.Y == 0)
            return default;
        return new Vk.VkExtent2D
        {
            width = capabilities.currentExtent.width != uint.MaxValue ? capabilities.currentExtent.width :
                Math.Clamp(requested.X, capabilities.minImageExtent.width, capabilities.maxImageExtent.width),
            height = capabilities.currentExtent.height != uint.MaxValue ? capabilities.currentExtent.height :
                Math.Clamp(requested.Y, capabilities.minImageExtent.height, capabilities.maxImageExtent.height),
        };
    }

    private static Vk.VkCompositeAlphaFlagsKHR ChooseCompositeAlpha(Vk.VkCompositeAlphaFlagsKHR supported)
    {
        ReadOnlySpan<Vk.VkCompositeAlphaFlagsKHR> choices =
        [
            Vk.VkCompositeAlphaFlagsKHR.Opaque, Vk.VkCompositeAlphaFlagsKHR.PreMultiplied,
            Vk.VkCompositeAlphaFlagsKHR.PostMultiplied, Vk.VkCompositeAlphaFlagsKHR.Inherit,
        ];
        foreach (Vk.VkCompositeAlphaFlagsKHR choice in choices)
        {
            if ((supported & choice) != 0)
                return choice;
        }
        return 0;
    }

    private Error CheckResult(Vk.VkResult result, string operation)
    {
        if (Registered)
            return Backend.ErrorFromVk(result, operation);
        if (result != Vk.VkResult.Success)
            InitializationFailure = Backend.VkFailureMessage(result, operation);
        return Backend.VkError(result);
    }

    internal Error CheckSurfaceResult(Vk.VkResult result, string operation)
    {
        if (result != Vk.VkResult.ErrorSurfaceLostKHR)
            return CheckResult(result, operation);
        if (!SurfaceLost)
        {
            SurfaceLost = true;
            string message = $"{operation}: the native presentation surface was lost; destroy and recreate this swapchain.";
            if (Registered)
                Gpu.ReportError(message, false);
            else
                InitializationFailure = Backend.VkFailureMessage(result, operation) + " Destroy and recreate the native presentation surface.";
        }
        return Error.DriverError;
    }

    private Error UnsupportedConfiguration(string message)
    {
        if (!Registered) InitializationFailure = message;
        return Error.Unsupported;
    }

    private Error PrepareConfiguration(Extent2D extent, Format format, ColorSpace colorSpace, bool vsync, out Configuration configuration)
    {
        VulkanDeviceBase device = Owner!;
        configuration = default;
        if (colorSpace != ColorSpace.Srgb && (colorSpace != ColorSpace.ExtendedSrgbLinear || !device.SwapchainColorSpaceEnabled))
            return UnsupportedConfiguration("The requested Vulkan presentation color space is unsupported.");
        Vk.VkColorSpaceKHR nativeColorSpace = colorSpace == ColorSpace.ExtendedSrgbLinear
            ? Vk.VkColorSpaceKHR.ExtendedSrgbLinearEXT : Vk.VkColorSpaceKHR.SrgbNonLinear;
        Vk.VkSurfaceFormatKHR* formats = stackalloc Vk.VkSurfaceFormatKHR[(int)Backend.MaxSurfaceFormats];
        uint formatCount = Backend.MaxSurfaceFormats;
        Vk.VkResult result = device.InstanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(device.PhysicalDevice, Surface, &formatCount, formats);
        if (result == Vk.VkResult.Incomplete)
            return UnsupportedConfiguration(Backend.VkFailureMessage(result, "vkGetPhysicalDeviceSurfaceFormatsKHR"));
        if (result == Vk.VkResult.Success && formatCount == 0)
            return UnsupportedConfiguration("vkGetPhysicalDeviceSurfaceFormatsKHR returned no supported presentation formats.");
        Error error = CheckSurfaceResult(result, "vkGetPhysicalDeviceSurfaceFormatsKHR");
        if (error != Error.None)
            return error;
        ReadOnlySpan<Format> defaultFormats = colorSpace == ColorSpace.ExtendedSrgbLinear
            ? [Format.Rgba16Float]
            : [Format.Bgra8Unorm, Format.Rgba8Unorm];
        bool found = false;
        Format selectedFormat = format;
        for (int candidate = 0; candidate < (format == Format.Undefined ? defaultFormats.Length : 1) && !found; ++candidate)
        {
            selectedFormat = format == Format.Undefined ? defaultFormats[candidate] : format;
            Vk.VkFormat nativeFormat = Backend.ToVk(selectedFormat);
            for (uint index = 0; index < formatCount; ++index)
            {
                if (nativeFormat != Vk.VkFormat.Undefined && formats[index].colorSpace == nativeColorSpace &&
                    (formats[index].format == nativeFormat || formats[index].format == Vk.VkFormat.Undefined))
                {
                    found = true;
                    break;
                }
            }
        }
        if (!found)
            return UnsupportedConfiguration("The requested Vulkan presentation format and color space are unsupported on this surface.");

        const uint maxPresentModes = 64;
        Vk.VkPresentModeKHR* modes = stackalloc Vk.VkPresentModeKHR[(int)maxPresentModes];
        uint modeCount = maxPresentModes;
        result = device.InstanceApi.vkGetPhysicalDeviceSurfacePresentModesKHR(device.PhysicalDevice, Surface, &modeCount, modes);
        if (result == Vk.VkResult.Incomplete)
            return UnsupportedConfiguration(Backend.VkFailureMessage(result, "vkGetPhysicalDeviceSurfacePresentModesKHR"));
        error = CheckSurfaceResult(result, "vkGetPhysicalDeviceSurfacePresentModesKHR");
        if (error != Error.None)
            return error;
        Vk.VkPresentModeKHR selectedMode = vsync ? Vk.VkPresentModeKHR.Fifo : Vk.VkPresentModeKHR.Immediate;
        found = false;
        for (uint index = 0; index < modeCount; ++index)
        {
            if (modes[index] == selectedMode)
            {
                found = true;
                break;
            }
        }
        if (!found)
            return UnsupportedConfiguration("The requested Vulkan presentation mode is unsupported on this surface.");

        Vk.VkSurfacePresentModeKHR presentModeInfo = new() { presentMode = selectedMode };
        Vk.VkPhysicalDeviceSurfaceInfo2KHR surfaceInfo = new() { pNext = &presentModeInfo, surface = Surface };
        Vk.VkSurfaceCapabilities2KHR capabilitiesInfo = new();
        error = CheckSurfaceResult(
            device.InstanceApi.vkGetPhysicalDeviceSurfaceCapabilities2KHR(device.PhysicalDevice, &surfaceInfo, &capabilitiesInfo),
            "vkGetPhysicalDeviceSurfaceCapabilities2KHR");
        if (error != Error.None)
            return error;
        Vk.VkSurfaceCapabilitiesKHR capabilities = capabilitiesInfo.surfaceCapabilities;
        Vk.VkCompositeAlphaFlagsKHR alpha = ChooseCompositeAlpha(capabilities.supportedCompositeAlpha);
        if ((capabilities.supportedUsageFlags & ImageUsage) != ImageUsage || alpha == 0)
            return UnsupportedConfiguration("The Vulkan surface requires color-attachment and bidirectional transfer usage with supported composite alpha.");
        uint imageCount = Math.Max(DesiredImageCount, capabilities.minImageCount);
        if (capabilities.maxImageCount != 0)
            imageCount = Math.Min(imageCount, capabilities.maxImageCount);
        if (imageCount == 0 || imageCount > Backend.MaxSwapchainImages)
            return UnsupportedConfiguration("The Vulkan surface requires an unsupported swapchain image count.");
        configuration = new Configuration
        {
            Extent = ResolveExtent(in capabilities, extent),
            Format = selectedFormat,
            ColorSpace = nativeColorSpace,
            PresentMode = selectedMode,
            ImageCount = imageCount,
            Transform = capabilities.currentTransform,
            CompositeAlpha = alpha,
        };
        return Error.None;
    }

    internal Error Configure(Extent2D extent, Format format, ColorSpace colorSpace, bool vsync)
    {
        VulkanDeviceBase device = Owner!;
        Debug.Assert(device != null);
        if (SurfaceLost)
            return Error.DriverError;
        Error error = PrepareConfiguration(extent, format, colorSpace, vsync, out Configuration configuration);
        if (error != Error.None)
            return error;
        CancelAcquired();
        if (SurfaceLost)
            return Error.DriverError;
        bool suspended = configuration.Extent.width == 0 || configuration.Extent.height == 0;
        // A suspended surface keeps its native swapchain association until restoration can supply oldSwapchain.
        if (!suspended && (Handle.IsNull || RecreateRequired || Width != configuration.Extent.width || Height != configuration.Extent.height ||
            ActualFormat != configuration.Format || PresentationColorSpace != colorSpace || PresentMode != configuration.PresentMode ||
            Transform != configuration.Transform || CompositeAlpha != configuration.CompositeAlpha))
        {
            Vk.VkPresentModeKHR presentMode = configuration.PresentMode;
            Vk.VkSwapchainPresentModesCreateInfoKHR presentModesInfo = new() { presentModeCount = 1, pPresentModes = &presentMode };
            Vk.VkSwapchainCreateInfoKHR createInfo = new()
            {
                pNext = &presentModesInfo,
                surface = Surface,
                minImageCount = configuration.ImageCount,
                imageFormat = Backend.ToVk(configuration.Format),
                imageColorSpace = configuration.ColorSpace,
                imageExtent = configuration.Extent,
                imageArrayLayers = 1,
                imageUsage = ImageUsage,
                imageSharingMode = Vk.VkSharingMode.Exclusive,
                preTransform = configuration.Transform,
                compositeAlpha = configuration.CompositeAlpha,
                presentMode = presentMode,
                oldSwapchain = Handle,
            };
            error = CheckSurfaceResult(device.Api.vkCreateSwapchainKHR(&createInfo, null, out Vk.VkSwapchainKHR replacement), "vkCreateSwapchainKHR");
            if (error != Error.None)
            {
                // Passing oldSwapchain retires it even when creation fails; retain the previous requested configuration for retry.
                RetireCurrentHandle();
                RecreateRequired = true;
                return error;
            }
            Vk.VkImage* images = stackalloc Vk.VkImage[(int)Backend.MaxSwapchainImages];
            uint imageCount = Backend.MaxSwapchainImages;
            Vk.VkResult result = device.Api.vkGetSwapchainImagesKHR(replacement, &imageCount, images);
            if (result != Vk.VkResult.Success || imageCount == 0)
            {
                device.Api.vkDestroySwapchainKHR(replacement, null);
                RetireCurrentHandle();
                RecreateRequired = true;
                if (result == Vk.VkResult.Incomplete)
                    return UnsupportedConfiguration(Backend.VkFailureMessage(result, "vkGetSwapchainImagesKHR"));
                return result == Vk.VkResult.Success
                    ? UnsupportedConfiguration("vkGetSwapchainImagesKHR returned no presentation images.")
                    : CheckSurfaceResult(result, "vkGetSwapchainImagesKHR");
            }
            Vk.VkImageView* views = stackalloc Vk.VkImageView[(int)Backend.MaxSwapchainImages];
            for (uint index = 0; index < imageCount; ++index)
            {
                Vk.VkImageViewCreateInfo viewInfo = new()
                {
                    image = images[index],
                    viewType = Vk.VkImageViewType.Image2D,
                    format = createInfo.imageFormat,
                    subresourceRange = new Vk.VkImageSubresourceRange
                    {
                        aspectMask = Vk.VkImageAspectFlags.Color,
                        levelCount = 1,
                        layerCount = 1,
                    },
                };
                result = device.Api.vkCreateImageView(&viewInfo, null, out views[index]);
                if (result != Vk.VkResult.Success)
                {
                    for (uint created = 0; created < index; ++created)
                        device.Api.vkDestroyImageView(views[created], null);
                    device.Api.vkDestroySwapchainKHR(replacement, null);
                    RetireCurrentHandle();
                    RecreateRequired = true;
                    return CheckResult(result, "vkCreateImageView (swapchain)");
                }
            }
            RetireCurrentHandle();
            Handle = replacement;
            ImageCount = imageCount;
            for (uint index = 0; index < imageCount; ++index)
            {
                Images[index] = images[index];
                VulkanTexture texture = new()
                {
                    Owner = device,
                    Swapchain = this,
                    Image = images[index],
                    Width = configuration.Extent.width,
                    Height = configuration.Extent.height,
                    Depth = 1,
                    LayerCount = 1,
                    SampleCount = 1,
                    Type = TextureType.TwoD,
                    Format = configuration.Format,
                    Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource | TextureUsage.TransferDestination,
                };
                Textures[index] = texture;
                RenderViews[index] = new VulkanRenderView
                {
                    Owner = device,
                    Source = texture,
                    View = views[index],
                    Width = configuration.Extent.width,
                    Height = configuration.Extent.height,
                    SwapchainView = true,
                };
            }
        }
        RequestedExtent = extent;
        RequestedFormat = format;
        ActualFormat = configuration.Format;
        PresentationColorSpace = colorSpace;
        VSyncEnabled = vsync;
        PresentMode = configuration.PresentMode;
        Width = configuration.Extent.width;
        Height = configuration.Extent.height;
        Transform = configuration.Transform;
        CompositeAlpha = configuration.CompositeAlpha;
        RecreateRequired = suspended;
        return Error.None;
    }

    internal PresentContext? BeginAcquire()
    {
        if (SurfaceLost)
            return null;
        if (Handle.IsNull || RecreateRequired)
        {
            Error error = Configure(RequestedExtent, RequestedFormat, PresentationColorSpace, VSyncEnabled);
            if (SurfaceLost)
                return null;
            Backend.RequireError(error, "GPUSwapchain recreation");
            if (Handle.IsNull || Width == 0 || Height == 0)
                return null;
        }
        CollectRetired();
        PresentContext context = PresentContexts[NextPresentContext];
        WaitRenderRetirement(context.RetirementValue);
        WaitPresentContext(context);
        return context;
    }

    internal ulong NextRetirementValue()
    {
        ulong next = RetirementValue + 1;
        if (next - CompletedRetirement > Owner!.MaxTimelineValueDifference)
        {
            PollRenderRetirement();
            if (next - CompletedRetirement > Owner.MaxTimelineValueDifference)
                WaitRenderRetirement(next - Owner.MaxTimelineValueDifference);
        }
        RetirementValue = next;
        return next;
    }

    private void PollRenderRetirement()
    {
        if (RenderRetirement.IsNull || CompletedRetirement == RetirementValue)
            return;
        ulong completed;
        Backend.RequireVk(Owner!.Api.vkGetSemaphoreCounterValue(RenderRetirement, &completed), "vkGetSemaphoreCounterValue (swapchain retirement)");
        CompletedRetirement = completed;
    }

    private void WaitRenderRetirement(ulong value)
    {
        if (value <= CompletedRetirement)
            return;
        Vk.VkSemaphore semaphore = RenderRetirement;
        Vk.VkSemaphoreWaitInfo waitInfo = new() { semaphoreCount = 1, pSemaphores = &semaphore, pValues = &value };
        Backend.RequireVk(Owner!.Api.vkWaitSemaphores(&waitInfo, ulong.MaxValue), "vkWaitSemaphores (swapchain retirement)");
        CompletedRetirement = value;
    }

    private void WaitPresentContext(PresentContext context)
    {
        if (!context.PresentPending)
            return;
        Vk.VkFence fence = context.Presented;
        Backend.RequireVk(Owner!.Api.vkWaitForFences(1, &fence, true, ulong.MaxValue), "vkWaitForFences (present)");
        context.PresentPending = false;
        context.Swapchain = default;
    }

    private void CollectRetired()
    {
        PollRenderRetirement();
        if (RetiredCount == 0)
            return;
        foreach (PresentContext context in PresentContexts)
        {
            if (!context.PresentPending)
                continue;
            Vk.VkResult result = Owner!.Api.vkGetFenceStatus(context.Presented);
            if (result == Vk.VkResult.NotReady)
                continue;
            Backend.RequireVk(result, "vkGetFenceStatus (present)");
            context.PresentPending = false;
            context.Swapchain = default;
        }
        foreach (RetiredSwapchain retired in RetiredSwapchains)
        {
            if (retired.Handle.IsNull || retired.RetirementValue > CompletedRetirement)
                continue;
            bool pending = false;
            foreach (PresentContext context in PresentContexts)
                pending |= context.PresentPending && context.Swapchain == retired.Handle;
            if (!pending)
                DestroyRetired(retired);
        }
    }

    private void DestroyRetired(RetiredSwapchain retired)
    {
        for (uint index = 0; index < retired.ViewCount; ++index)
        {
            Owner!.Api.vkDestroyImageView(retired.Views[index], null);
            retired.Views[index] = default;
        }
        Owner!.Api.vkDestroySwapchainKHR(retired.Handle, null);
        retired.Handle = default;
        retired.ViewCount = 0;
        retired.RetirementValue = 0;
        --RetiredCount;
    }

    private void RetireCurrentHandle()
    {
        Debug.Assert(!Acquired);
        if (Handle.IsNull)
            return;
        CollectRetired();
        RetiredSwapchain? available = null;
        foreach (RetiredSwapchain retired in RetiredSwapchains)
        {
            if (retired.Handle.IsNull)
            {
                available = retired;
                break;
            }
        }
        if (available == null)
        {
            available = RetiredSwapchains[0];
            foreach (RetiredSwapchain retired in RetiredSwapchains)
            {
                if (retired.RetirementValue < available.RetirementValue)
                    available = retired;
            }
            WaitRenderRetirement(available.RetirementValue);
            foreach (PresentContext context in PresentContexts)
            {
                if (context.PresentPending && context.Swapchain == available.Handle)
                    WaitPresentContext(context);
            }
            DestroyRetired(available);
        }
        ++RetiredCount;
        available.Handle = Handle;
        available.RetirementValue = RetirementValue;
        available.ViewCount = ImageCount;
        for (uint index = 0; index < ImageCount; ++index)
        {
            available.Views[index] = RenderViews[index]!.View;
            RenderViews[index] = null;
            Textures[index] = null;
            Images[index] = default;
            Initialized[index] = false;
        }
        Handle = default;
        ImageCount = 0;
        Width = 0;
        Height = 0;
        CollectRetired();
    }

    private void CancelAcquired()
    {
        if (!Acquired)
            return;
        VulkanDeviceBase device = Owner!;
        PresentContext context = Context!;
        if (AcquireWaitPending)
        {
            // A cancelled, unsubmitted acquisition still owns a pending binary semaphore signal.
            ulong value = NextRetirementValue();
            Vk.VkSemaphoreSubmitInfo wait = new() { semaphore = context.Acquired, stageMask = Vk.VkPipelineStageFlags2.AllCommands };
            Vk.VkSemaphoreSubmitInfo signal = new()
            {
                semaphore = RenderRetirement,
                value = value,
                stageMask = Vk.VkPipelineStageFlags2.AllCommands,
            };
            Vk.VkSubmitInfo2 submit = new()
            {
                waitSemaphoreInfoCount = 1,
                pWaitSemaphoreInfos = &wait,
                signalSemaphoreInfoCount = 1,
                pSignalSemaphoreInfos = &signal,
            };
            Backend.RequireVk(device.Dispatch.QueueSubmit2(device.Queues![0].Handle, 1, &submit, default), "vkQueueSubmit2 (cancel acquire)");
            context.RetirementValue = value;
            WaitRenderRetirement(value);
            AcquireWaitPending = false;
        }
        uint imageIndex = ImageIndex;
        Vk.VkReleaseSwapchainImagesInfoKHR releaseInfo = new() { swapchain = Handle, imageIndexCount = 1, pImageIndices = &imageIndex };
        Vk.VkResult result = device.KhrSwapchainMaintenance1 ? device.Api.vkReleaseSwapchainImagesKHR(&releaseInfo) :
            device.Api.vkReleaseSwapchainImagesEXT(&releaseInfo);
        if (result == Vk.VkResult.ErrorSurfaceLostKHR)
            _ = CheckSurfaceResult(result, "vkReleaseSwapchainImages");
        else
            Backend.RequireVk(result, "vkReleaseSwapchainImages");
        Initialized[imageIndex] = false;
        if (AcquireCommands != null)
            AcquireCommands.Swapchain = null;
        AcquireCommands = null;
        Context = null;
        Acquired = false;
        NextPresentContext = (NextPresentContext + 1) % (uint)PresentContexts.Length;
    }

    internal void RecordTail()
    {
        VulkanDeviceBase device = Owner!;
        Vk.VkCommandBuffer tail = Context!.Tail;
        Backend.RequireVk(device.Api.vkResetCommandBuffer(tail, 0), "vkResetCommandBuffer (presentation tail)");
        Vk.VkCommandBufferBeginInfo beginInfo = new() { flags = Vk.VkCommandBufferUsageFlags.OneTimeSubmit };
        Backend.RequireVk(device.Api.vkBeginCommandBuffer(tail, &beginInfo), "vkBeginCommandBuffer (presentation tail)");
        Vk.VkImageMemoryBarrier2 barrier = new()
        {
            srcStageMask = Vk.VkPipelineStageFlags2.AllCommands,
            srcAccessMask = Vk.VkAccessFlags2.MemoryWrite,
            oldLayout = Vk.VkImageLayout.General,
            newLayout = Vk.VkImageLayout.PresentSrcKHR,
            srcQueueFamilyIndex = VkQueueFamilyIgnored,
            dstQueueFamilyIndex = VkQueueFamilyIgnored,
            image = Images[ImageIndex],
            subresourceRange = new Vk.VkImageSubresourceRange
            {
                aspectMask = Vk.VkImageAspectFlags.Color,
                levelCount = 1,
                layerCount = 1,
            },
        };
        RecordImageBarriers(device, tail, new ReadOnlySpan<Vk.VkImageMemoryBarrier2>(in barrier));
        Backend.RequireVk(device.Api.vkEndCommandBuffer(tail), "vkEndCommandBuffer (presentation tail)");
    }

    internal void Present()
    {
        VulkanDeviceBase device = Owner!;
        PresentContext context = Context!;
        Vk.VkFence fence = context.Presented;
        Backend.RequireVk(device.Api.vkResetFences(1, &fence), "vkResetFences (present)");
        Vk.VkSwapchainPresentFenceInfoKHR fenceInfo = new() { swapchainCount = 1, pFences = &fence };
        Vk.VkSemaphore rendered = context.Rendered;
        Vk.VkSwapchainKHR handle = Handle;
        uint imageIndex = ImageIndex;
        Vk.VkPresentInfoKHR presentInfo = new()
        {
            pNext = &fenceInfo,
            waitSemaphoreCount = 1,
            pWaitSemaphores = &rendered,
            swapchainCount = 1,
            pSwapchains = &handle,
            pImageIndices = &imageIndex,
        };
        Vk.VkResult result = device.Api.vkQueuePresentKHR(device.Queues![0].Handle, &presentInfo);
        if (result != Vk.VkResult.Success && result != Vk.VkResult.SuboptimalKHR && result != Vk.VkResult.ErrorOutOfDateKHR &&
            result != Vk.VkResult.ErrorSurfaceLostKHR)
            Backend.AbortVkFailure(result, "vkQueuePresentKHR");
        context.PresentPending = true;
        context.Swapchain = Handle;
        Initialized[imageIndex] = true;
        Context = null;
        Acquired = false;
        NextPresentContext = (NextPresentContext + 1) % (uint)PresentContexts.Length;
        RecreateRequired |= result == Vk.VkResult.ErrorOutOfDateKHR;
        if (result == Vk.VkResult.ErrorSurfaceLostKHR)
            _ = CheckSurfaceResult(result, "vkQueuePresentKHR");
        else if (result == Vk.VkResult.SuboptimalKHR)
            _ = DrawableExtent;
    }

    /// <summary>Releases this surface after all recorded and submitted image uses have finished; an acquired frame is cancelled.</summary>
    public override void Destroy()
    {
        VulkanDeviceBase device = Owner!;
        Debug.Assert(device != null);
        CancelAcquired();
        WaitRenderRetirement(RetirementValue);
        foreach (PresentContext context in PresentContexts)
            WaitPresentContext(context);
        RetireCurrentHandle();
        CollectRetired();
        foreach (RetiredSwapchain retired in RetiredSwapchains)
            Debug.Assert(retired.Handle.IsNull);
        foreach (PresentContext context in PresentContexts)
        {
            if (!context.Acquired.IsNull) device.Api.vkDestroySemaphore(context.Acquired, null);
            if (!context.Rendered.IsNull) device.Api.vkDestroySemaphore(context.Rendered, null);
            if (!context.Presented.IsNull) device.Api.vkDestroyFence(context.Presented, null);
        }
        if (!TailPool.IsNull) device.Api.vkDestroyCommandPool(TailPool, null);
        if (!RenderRetirement.IsNull) device.Api.vkDestroySemaphore(RenderRetirement, null);
        if (!Surface.IsNull) device.InstanceApi.vkDestroySurfaceKHR(Surface, null);
        if (Registered) Interlocked.Decrement(ref device.SwapchainCount);
        Owner = null;
    }
}

internal abstract unsafe partial class VulkanDeviceBase
{
    /// <summary>Creates an independent swapchain for the supplied native surface.</summary>
    /// <param name="desc">The surface and presentation configuration.</param>
    /// <returns>The swapchain.</returns>
    public override GPUSwapchain CreateSwapchain(in SwapchainDesc desc)
    {
        if (!PresentationEnabled)
            throw CreationFailure(Error.Unsupported, "Create a Vulkan device with Headless=false before creating swapchains.");
        Debug.Assert(desc.DesiredImageCount != 0 && desc.DesiredImageCount <= Backend.MaxSwapchainImages);
        if (desc.DesiredImageCount == 0 || desc.DesiredImageCount > Backend.MaxSwapchainImages)
            throw CreationFailure(Error.Unsupported, "The requested Vulkan swapchain image count is unsupported.");
        VulkanSwapchain swapchain = new() { Owner = this };
        Error error;
        bool initialized = false;
        try
        {
            error = swapchain.Initialize(in desc);
            initialized = error == Error.None;
        }
        finally
        {
            if (!initialized) swapchain.Destroy();
        }
        if (error != Error.None)
        {
            throw CreationFailure(error, swapchain.InitializationFailure ??
                "The requested Vulkan swapchain surface, format, image count, or presentation mode is unsupported.");
        }
        return swapchain;
    }

    /// <summary>Submits work on queue 0 and presents one acquired swapchain image.</summary>
    /// <param name="desc">The commands, explicit waits, and required completion point; commands may be empty after earlier submissions.</param>
    /// <param name="swapchain">The acquired swapchain to present.</param>
    public override void SubmitAndPresent(SubmitDesc desc, GPUSwapchain swapchain)
    {
        VulkanSwapchain target = (VulkanSwapchain)swapchain;
        Debug.Assert(target.Owner == this && target.Acquired && target.Context != null);
#if DEBUG
        bool acquisitionSubmitted = !target.AcquireWaitPending;
        for (int index = 0; index < desc.Commands.Length; ++index)
            acquisitionSubmitted |= desc.Commands[index] == target.AcquireCommands;
        Debug.Assert(acquisitionSubmitted, "submit the acquisition command buffer before presenting its image");
#endif
        target.RecordTail();
        SubmitCommands(this, desc, 0, target);
        Debug.Assert(!target.AcquireWaitPending && target.AcquireCommands == null);
        target.Present();
    }
}

internal abstract unsafe partial class VulkanCommandBuffer
{
    /// <summary>Acquires one image for recording on queue 0; the render view is null while the swapchain is suspended.</summary>
    /// <param name="swapchain">The swapchain to acquire.</param>
    /// <returns>The acquired image and its actual extent, or an empty frame while unavailable.</returns>
    public override SwapchainFrame Acquire(GPUSwapchain swapchain)
    {
        VulkanSwapchain target = (VulkanSwapchain)swapchain;
        Debug.Assert(Owner == target.Owner && QueueIndex == 0);
        if (target.SurfaceLost)
            return default;
        Debug.Assert(Swapchain == null && !target.Acquired);
        for (;;)
        {
            PresentContext? context = target.BeginAcquire();
            if (context == null)
                return default;
            Vk.VkResult result = Owner.Api.vkAcquireNextImageKHR(target.Handle, ulong.MaxValue, context.Acquired, default, out uint imageIndex);
            if (result == Vk.VkResult.ErrorOutOfDateKHR)
            {
                target.RecreateRequired = true;
                continue;
            }
            if (result == Vk.VkResult.ErrorSurfaceLostKHR)
            {
                _ = target.CheckSurfaceResult(result, "vkAcquireNextImageKHR");
                return default;
            }
            if (result != Vk.VkResult.Success && result != Vk.VkResult.SuboptimalKHR)
                Backend.AbortVkFailure(result, "vkAcquireNextImageKHR");
            Debug.Assert(imageIndex < target.ImageCount);
            target.ImageIndex = imageIndex;
            target.Context = context;
            target.Acquired = true;
            target.AcquireWaitPending = true;
            target.AcquireCommands = this;
            Swapchain = target;
            if (result == Vk.VkResult.SuboptimalKHR)
                _ = target.DrawableExtent;
            if (target.SurfaceLost)
                return default;
            Vk.VkImageMemoryBarrier2 barrier = new()
            {
                dstStageMask = Vk.VkPipelineStageFlags2.AllCommands,
                dstAccessMask = Vk.VkAccessFlags2.MemoryRead | Vk.VkAccessFlags2.MemoryWrite,
                oldLayout = target.Initialized[imageIndex] ? Vk.VkImageLayout.PresentSrcKHR : Vk.VkImageLayout.Undefined,
                newLayout = Vk.VkImageLayout.General,
                srcQueueFamilyIndex = VkQueueFamilyIgnored,
                dstQueueFamilyIndex = VkQueueFamilyIgnored,
                image = target.Images[imageIndex],
                subresourceRange = new Vk.VkImageSubresourceRange
                {
                    aspectMask = Vk.VkImageAspectFlags.Color,
                    levelCount = 1,
                    layerCount = 1,
                },
            };
            RecordImageBarriers(Owner, Handle, new ReadOnlySpan<Vk.VkImageMemoryBarrier2>(in barrier));
            return new SwapchainFrame
            {
                RenderView = target.RenderViews[imageIndex],
                Extent = new Extent2D(target.Width, target.Height),
                Texture = target.Textures[imageIndex],
            };
        }
    }
}
