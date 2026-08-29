using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

/// <summary>
/// Vulkan swapchain: owns the VkSurfaceKHR and VkSwapchainKHR, the per-image
/// texture wrappers/views and the acquire/present semaphore ring. The swapchain
/// framebuffer exposes the currently acquired image as the render target.
/// </summary>
internal sealed unsafe class VulkanSwapchain : GPUSwapchain
{
    private const int FlightSlots = 2;
    /// <summary>Swapchain frames in flight; the device's present-barrier one-shot
    /// retirement delays by this many frames (see VulkanDevice.ProcessOneShots).</summary>
    internal const int FlightSlotCount = FlightSlots;

    private readonly VulkanDevice _device;
    private readonly VulkanAttachmentLayout _attachmentLayout;

    public VkSurfaceKHR Surface;
    public VkSwapchainKHR Swapchain;
    public VkFormat VkSurfaceFormat;

    private VulkanTextureView[] _imageViews = Array.Empty<VulkanTextureView>();
    private VulkanTexture[] _images = Array.Empty<VulkanTexture>();

    private PixelFormat _colorFormat;
    private bool _isVSyncEnabled;
    private uint _width;
    private uint _height;
    private bool _needsRecreate;

    // frames-in-flight bookkeeping: two slots, each with an acquire semaphore
    // and the signal semaphores of the frame's submissions; the slot's end
    // timeline value throttles BeginFrame to two frames in flight
    private readonly VkSemaphore[] _acquireSemaphores = new VkSemaphore[FlightSlots];
    private readonly List<VkSemaphore>[] _slotSignalSemaphores = new List<VkSemaphore>[FlightSlots];
    private readonly List<VkSemaphore>[] _slotFreeSemaphores = new List<VkSemaphore>[FlightSlots];
    private readonly long[] _slotEndValues = new long[FlightSlots];
    private ulong _frameIndex;
    private bool _acquireSemaphorePending;

    private uint _currentImageIndex;
    private bool _imageAcquired;

    // set when a submission already appended the acquired image's
    // present-layout transition to its command buffer array (trail); Present
    // then skips the barrier submission entirely — wgpu does the same by
    // baking the transition into the frame's single submit
    private bool _presentTrailRecorded;

    private readonly VulkanSurfaceFrameBuffer _frameBuffer;

    protected override VulkanDevice Device
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    public override GPUFrameBuffer FrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameBuffer;
    }

    public override bool IsVSyncEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isVSyncEnabled;
        set
        {
            if (_isVSyncEnabled == value)
            {
                return;
            }
            _isVSyncEnabled = value;
            _needsRecreate = true;
        }
    }

    internal VulkanAttachmentLayout SurfaceAttachmentLayout => _attachmentLayout;

    internal VulkanTexture GetImage(uint index) => _images[index];

    internal VulkanTextureView GetImageView(uint index) => _imageViews[index];

    internal uint SwapchainExtentWidth => _width;

    internal uint SwapchainExtentHeight => _height;

    public VulkanSwapchain(VulkanDevice device, in SwapchainDescriptor descriptor) : base(descriptor)
    {
        _device = device;
        _isVSyncEnabled = descriptor.IsVSyncEnabled;
        _width = descriptor.Width;
        _height = descriptor.Height;

        Surface = CreateSurface(device, descriptor.SurfaceSource);

        // the surface decides the actual format: prefer the requested one
        _colorFormat = PickSurfaceFormat(descriptor.ColorFormat);

        DepthAttachment? depth = descriptor.DepthFormat.HasValue
            ? new DepthAttachment(descriptor.DepthFormat.Value)
            : null;
        AttachmentLayoutDescriptor layoutDescriptor = new(
            new ColorAttachment[] { new ColorAttachment(_colorFormat, descriptor.ClearColor) },
            depth,
            $"{descriptor.Name}_attachment_layout");
        _attachmentLayout = device.Track(new VulkanAttachmentLayout(device, layoutDescriptor));

        for (int i = 0; i < FlightSlots; i++)
        {
            _slotSignalSemaphores[i] = new List<VkSemaphore>();
            _slotFreeSemaphores[i] = new List<VkSemaphore>();
            _acquireSemaphores[i] = CreateSemaphore();
        }

        CreateSwapchainNative();

        _frameBuffer = device.Track(new VulkanSurfaceFrameBuffer(device, this));
        _frameBuffer.NotifyImagesRecreated();
    }

    private VkSemaphore CreateSemaphore()
    {
        VkSemaphoreCreateInfo info = new()
        {
        };
        VkSemaphore semaphore = default;
        vkCreateSemaphore(_device.NativeDevice, &info, null, &semaphore).ThrowOnFailure();
        return semaphore;
    }

    internal static VkSurfaceKHR CreateSurface(VulkanDevice device, SurfaceSource source)
    {
        VkInstance instance = device.Instance;
        switch (source)
        {
            case Win32SurfaceSource win32:
            {
                VkWin32SurfaceCreateInfoKHR createInfo = new()
                {
                    hinstance = win32.HInstance,
                    hwnd = win32.Hwnd,
                };
                VkSurfaceKHR surface = default;
                vkCreateWin32SurfaceKHR(instance, &createInfo, null, &surface).ThrowOnFailure();
                return surface;
            }
            case WaylandSurfaceSource wayland:
            {
                VkWaylandSurfaceCreateInfoKHR createInfo = new()
                {
                    display = wayland.Display,
                    surface = wayland.Surface,
                };
                VkSurfaceKHR surface = default;
                vkCreateWaylandSurfaceKHR(instance, &createInfo, null, &surface).ThrowOnFailure();
                return surface;
            }
            case XcbWindowSurfaceSource xcb:
            {
                VkXcbSurfaceCreateInfoKHR createInfo = new()
                {
                    connection = xcb.Connection,
                    window = xcb.Window,
                };
                VkSurfaceKHR surface = default;
                vkCreateXcbSurfaceKHR(instance, &createInfo, null, &surface).ThrowOnFailure();
                return surface;
            }
            case XlibWindowSurfaceSource xlib:
            {
                VkXlibSurfaceCreateInfoKHR createInfo = new()
                {
                    display = xlib.Display,
                    window = (nuint)xlib.Window,
                };
                VkSurfaceKHR surface = default;
                vkCreateXlibSurfaceKHR(instance, &createInfo, null, &surface).ThrowOnFailure();
                return surface;
            }
            case AndroidWindowSurfaceSource android:
            {
                VkAndroidSurfaceCreateInfoKHR createInfo = new()
                {
                    window = android.Window,
                };
                VkSurfaceKHR surface = default;
                vkCreateAndroidSurfaceKHR(instance, &createInfo, null, &surface).ThrowOnFailure();
                return surface;
            }
            default:
                throw new GraphicsException($"Surface source {source.GetType().Name} is not supported by the Vulkan backend.");
        }
    }

    private PixelFormat PickSurfaceFormat(PixelFormat requested)
    {
        VkPhysicalDevice physicalDevice = _device.PhysicalDevice;
        uint formatCount = 0;
        vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, Surface, &formatCount, null).ThrowOnFailure();
        VkSurfaceFormatKHR* formats = stackalloc VkSurfaceFormatKHR[(int)formatCount];
        vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, Surface, &formatCount, formats).ThrowOnFailure();

        VkFormat requestedFormat = VulkanUtility.PixelFormatToVulkan(requested);
        VkFormat chosen = formats[0].format;
        for (uint i = 0; i < formatCount; i++)
        {
            if (formats[i].format == requestedFormat)
            {
                chosen = requestedFormat;
                break;
            }
        }

        PixelFormat chosenPixelFormat = VulkanUtility.VkFormatToPixelFormat(chosen);
        if (chosenPixelFormat != requested)
        {
            Device.LogWarning($"Swapchain format {requested} is not supported, using {chosenPixelFormat}");
        }
        return chosenPixelFormat;
    }

    private void CreateSwapchainNative()
    {
        VkPhysicalDevice physicalDevice = _device.PhysicalDevice;

        VkSurfaceCapabilitiesKHR capabilities;
        vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, Surface, &capabilities).ThrowOnFailure();

        // format chosen at creation (surface formats are constant for a surface)
        uint formatCount = 0;
        vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, Surface, &formatCount, null).ThrowOnFailure();
        VkSurfaceFormatKHR* formats = stackalloc VkSurfaceFormatKHR[(int)formatCount];
        vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, Surface, &formatCount, formats).ThrowOnFailure();

        VkFormat requestedFormat = VulkanUtility.PixelFormatToVulkan(_colorFormat);
        VkSurfaceFormatKHR surfaceFormat = formats[0];
        for (uint i = 0; i < formatCount; i++)
        {
            if (formats[i].format == requestedFormat)
            {
                surfaceFormat = formats[i];
                break;
            }
        }

        // present mode: FIFO when vsync is on (always available), otherwise
        // immediate when supported
        uint presentModeCount = 0;
        vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, Surface, &presentModeCount, null).ThrowOnFailure();
        VkPresentModeKHR* presentModes = stackalloc VkPresentModeKHR[(int)presentModeCount];
        vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, Surface, &presentModeCount, presentModes).ThrowOnFailure();

        VkPresentModeKHR presentMode = VkPresentModeKHR.Fifo;
        if (!_isVSyncEnabled)
        {
            for (uint i = 0; i < presentModeCount; i++)
            {
                if (presentModes[i] == VkPresentModeKHR.Immediate)
                {
                    presentMode = VkPresentModeKHR.Immediate;
                    break;
                }
            }
        }

        VkExtent2D extent = capabilities.currentExtent;
        if (extent.width == uint.MaxValue)
        {
            extent.width = Math.Clamp(_width, capabilities.minImageExtent.width, capabilities.maxImageExtent.width);
            extent.height = Math.Clamp(_height, capabilities.minImageExtent.height, capabilities.maxImageExtent.height);
        }
        _width = extent.width;
        _height = extent.height;

        uint imageCount = Math.Max(2u, capabilities.minImageCount);
        if (capabilities.maxImageCount > 0)
        {
            imageCount = Math.Min(imageCount, capabilities.maxImageCount);
        }

        VkSwapchainKHR oldSwapchain = Swapchain;
        VkSwapchainCreateInfoKHR createInfo = new()
        {
            surface = Surface,
            minImageCount = imageCount,
            imageFormat = surfaceFormat.format,
            imageColorSpace = surfaceFormat.colorSpace,
            imageExtent = extent,
            imageArrayLayers = 1,
            imageUsage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferSrc,
            imageSharingMode = VkSharingMode.Exclusive,
            preTransform = VkSurfaceTransformFlagsKHR.Identity,
            compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
            presentMode = presentMode,
            clipped = true,
            oldSwapchain = oldSwapchain,
        };

        VkSwapchainKHR newSwapchain = default;
        vkCreateSwapchainKHR(_device.NativeDevice, &createInfo, null, &newSwapchain).ThrowOnFailure();
        Swapchain = newSwapchain;
        VkSurfaceFormat = surfaceFormat.format;

        if (oldSwapchain.Handle != 0)
        {
            DestroyImageResources();
            vkDestroySwapchainKHR(_device.NativeDevice, oldSwapchain, null);
        }

        CreateImageResources();
    }

    private void CreateImageResources()
    {
        uint imageCount = 0;
        vkGetSwapchainImagesKHR(_device.NativeDevice, Swapchain, &imageCount, null).ThrowOnFailure();
        VkImage* images = stackalloc VkImage[(int)imageCount];
        vkGetSwapchainImagesKHR(_device.NativeDevice, Swapchain, &imageCount, images).ThrowOnFailure();

        _imageViews = new VulkanTextureView[imageCount];
        _images = new VulkanTexture[imageCount];

        for (uint i = 0; i < imageCount; i++)
        {
            _images[i] = _device.Track(new VulkanTexture(_device, images[i], VkSurfaceFormat, _width, _height, _colorFormat, $"swapchain_image_{i}"));

            VkImageViewCreateInfo viewInfo = new()
            {
                image = images[i],
                viewType = VkImageViewType.Image2D,
                format = VkSurfaceFormat,
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = VkImageAspectFlags.Color,
                    baseMipLevel = 0,
                    levelCount = 1,
                    baseArrayLayer = 0,
                    layerCount = 1,
                },
            };
            VkImageView nativeView = default;
            vkCreateImageView(_device.NativeDevice, &viewInfo, null, &nativeView).ThrowOnFailure();
            _imageViews[i] = _device.Track(new VulkanTextureView(_device, _images[i], nativeView, $"swapchain_view_{i}"));

            // swapchain images come back from acquire with undefined contents
            _device.Tracker.InvalidateTexture(_images[i]);
        }

        // the frame buffer does not exist during the first CreateSwapchainNative call
        // from the constructor; it initializes itself right after
        _frameBuffer?.NotifyImagesRecreated();
    }

    private void DestroyImageResources()
    {
        foreach (VulkanTextureView view in _imageViews)
        {
            view.Dispose();
        }
        _imageViews = Array.Empty<VulkanTextureView>();
        _images = Array.Empty<VulkanTexture>();
    }

    /// <summary>Called at the frame boundary: throttles to two frames in flight.
    /// Waits the device timeline value the previous cycle of this slot ended
    /// on — all of that frame's submissions (main buffers, riders and any
    /// present barrier) have completed by then, so slot resources (acquire
    /// semaphore, signal semaphore lists) are free to reuse.</summary>
    private void BeginFrame()
    {
        long endValue = _slotEndValues[Slot];
        if (endValue > 0)
        {
            _device.WaitTimeline(endValue);
        }
        _frameIndex++;
        _acquireSemaphorePending = false;
    }

    /// <summary>The acquire semaphore the next submit must wait on, if not consumed yet.</summary>
    internal VkSemaphore PendingAcquireSemaphore => _acquireSemaphorePending ? _acquireSemaphores[Slot] : default;

    internal void ConsumeAcquireSemaphore() => _acquireSemaphorePending = false;

    /// <summary>Takes a semaphore to signal at submit time; it returns to the free
    /// list after present has waited on it.</summary>
    internal VkSemaphore TakeSubmitSemaphore()
    {
        int slot = Slot;
        List<VkSemaphore> freeList = _slotFreeSemaphores[slot];
        VkSemaphore semaphore;
        if (freeList.Count > 0)
        {
            semaphore = freeList[^1];
            freeList.RemoveAt(freeList.Count - 1);
        }
        else
        {
            semaphore = CreateSemaphore();
        }
        _slotSignalSemaphores[slot].Add(semaphore);
        return semaphore;
    }

    /// <summary>The currently acquired swapchain image, or null when none is held.</summary>
    internal VulkanTexture? AcquiredImageTexture => _imageAcquired ? _images[_currentImageIndex] : null;

    /// <summary>Marks that a submission already recorded the present-layout
    /// transition for the acquired image; Present() will skip its barrier.</summary>
    internal void NotePresentTrailRecorded() => _presentTrailRecorded = true;

    private int Slot => (int)(_frameIndex % FlightSlots);

    public override bool RequestSurfaceTexture()
    {
        if (_needsRecreate)
        {
            _needsRecreate = false;
            RecreateSwapchain();
        }

        if (_imageAcquired)
        {
            // already holding an image from a skipped present
            return true;
        }

        BeginFrame();

        VkSemaphore acquireSemaphore = _acquireSemaphores[Slot];
        uint imageIndex = 0;
        VkResult result = vkAcquireNextImageKHR(
            _device.NativeDevice, Swapchain, ulong.MaxValue, acquireSemaphore, VkFence.Null, &imageIndex);

        if (result == VkResult.ErrorOutOfDateKHR)
        {
            RecreateSwapchain();
            result = vkAcquireNextImageKHR(
                _device.NativeDevice, Swapchain, ulong.MaxValue, acquireSemaphore, VkFence.Null, &imageIndex);
        }

        if (result != VkResult.Success && result != VkResult.SuboptimalKHR)
        {
            if (result == VkResult.Timeout || result == VkResult.NotReady)
            {
                return false;
            }
            VulkanException.ThrowIfFailed(result, "Failed to acquire swapchain image");
        }

        _currentImageIndex = imageIndex;
        _imageAcquired = true;
        _acquireSemaphorePending = true;
        // a fresh acquire: nothing has recorded a present trail for this image
        _presentTrailRecorded = false;
        _frameBuffer.OnImageAcquired(imageIndex);
        return true;
    }

    public override void Present()
    {
        if (!_imageAcquired)
        {
            return;
        }

        VulkanTexture texture = _images[_currentImageIndex];

        if (!_presentTrailRecorded)
        {
            // no submission this frame recorded the present transition (the
            // image was never touched, or nothing was submitted at all): fall
            // back to the standalone barrier submission, which consumes the
            // acquire wait and signals a semaphore present waits on
            _device.SubmitPresentBarrier(texture, this);
        }

        int slot = Slot;
        // the frame's queue work is done: record the timeline value the next
        // cycle of this slot waits before recycling its resources
        _slotEndValues[slot] = _device.CurrentTimelineValue;

        List<VkSemaphore> waitSemaphores = _slotSignalSemaphores[slot];

        int waitCount = waitSemaphores.Count;
        if (waitCount == 0)
        {
            // nothing was submitted this frame and no barrier ran; the image
            // cannot be presented, release it for the next acquire
            _imageAcquired = false;
            _device.Tracker.InvalidateTexture(texture);
            return;
        }

        VkSemaphore* waits = stackalloc VkSemaphore[waitCount];
        for (int i = 0; i < waitCount; i++)
        {
            waits[i] = waitSemaphores[i];
        }

        VkSwapchainKHR swapchain = Swapchain;
        uint imageIndex = _currentImageIndex;
        VkPresentInfoKHR presentInfo = new()
        {
            waitSemaphoreCount = (uint)waitCount,
            pWaitSemaphores = waits,
            swapchainCount = 1,
            pSwapchains = &swapchain,
            pImageIndices = &imageIndex,
        };

        VkResult result = _device.PresentLocked(&presentInfo);
        bool semaphoresRecyclable;
        if (result == VkResult.ErrorOutOfDateKHR)
        {
            _needsRecreate = true;
            // an errored present never waited its semaphores; they stay in an
            // indeterminate signaled state and cannot be recycled safely
            semaphoresRecyclable = false;
        }
        else
        {
            // SuboptimalKHR still presented the image, so the waits completed
            semaphoresRecyclable = true;
            if (result == VkResult.SuboptimalKHR)
            {
                _needsRecreate = true;
            }
            else if (result != VkResult.Success)
            {
                VulkanException.ThrowIfFailed(result, "Failed to present swapchain image");
            }
        }

        if (semaphoresRecyclable)
        {
            // the semaphores waited by this present return to the free list
            foreach (VkSemaphore semaphore in waitSemaphores)
            {
                _slotFreeSemaphores[slot].Add(semaphore);
            }
        }
        else
        {
            foreach (VkSemaphore semaphore in waitSemaphores)
            {
                vkDestroySemaphore(_device.NativeDevice, semaphore, null);
            }
        }
        waitSemaphores.Clear();

        _imageAcquired = false;
        _device.Tracker.InvalidateTexture(texture);
    }

    public override void Resize(uint width, uint height)
    {
        if (_width == width && _height == height)
        {
            return;
        }
        _width = width;
        _height = height;
        _needsRecreate = true;
    }

    internal void RecreateSwapchain()
    {
        vkDeviceWaitIdle(_device.NativeDevice);
        // after a wait idle every pending wait completed; signal semaphores that
        // were never waited (submitted but the present was skipped) would stay
        // signaled forever, so destroy them instead of clearing the list alone
        foreach (List<VkSemaphore> list in _slotSignalSemaphores)
        {
            foreach (VkSemaphore semaphore in list)
            {
                vkDestroySemaphore(_device.NativeDevice, semaphore, null);
            }
            list.Clear();
        }
        // an acquire semaphore may have been signaled but never waited (acquire
        // without a following submit); binary semaphores cannot be reset on the
        // host, so replace them with fresh ones
        for (int i = 0; i < FlightSlots; i++)
        {
            if (_acquireSemaphores[i].Handle != 0)
            {
                vkDestroySemaphore(_device.NativeDevice, _acquireSemaphores[i], null);
            }
            _acquireSemaphores[i] = CreateSemaphore();
        }
        _acquireSemaphorePending = false;
        _imageAcquired = false;
        _presentTrailRecorded = false;
        CreateSwapchainNative();
    }

    protected override void Dispose(bool disposing)
    {
        vkDeviceWaitIdle(_device.NativeDevice);

        DestroyImageResources();
        if (Swapchain.Handle != 0)
        {
            vkDestroySwapchainKHR(_device.NativeDevice, Swapchain, null);
            Swapchain = default;
        }
        if (Surface.Handle != 0)
        {
            vkDestroySurfaceKHR(_device.Instance, Surface, null);
            Surface = default;
        }

        for (int i = 0; i < FlightSlots; i++)
        {
            if (_acquireSemaphores[i].Handle != 0)
            {
                vkDestroySemaphore(_device.NativeDevice, _acquireSemaphores[i], null);
                _acquireSemaphores[i] = default;
            }
            foreach (VkSemaphore semaphore in _slotSignalSemaphores[i])
            {
                vkDestroySemaphore(_device.NativeDevice, semaphore, null);
            }
            _slotSignalSemaphores[i].Clear();
            foreach (VkSemaphore semaphore in _slotFreeSemaphores[i])
            {
                vkDestroySemaphore(_device.NativeDevice, semaphore, null);
            }
            _slotFreeSemaphores[i].Clear();
        }
    }
}

/// <summary>
/// The swapchain-owned frame buffer: the color attachment is the currently
/// acquired swapchain image; the depth texture is owned and resized with the
/// surface.
/// </summary>
internal sealed class VulkanSurfaceFrameBuffer : VulkanFrameBufferBase
{
    private readonly VulkanSwapchain _swapchain;
    private readonly VulkanDevice _device;
    private readonly VulkanAttachmentLayout _attachmentLayout;
    private uint _width;
    private uint _height;

    private readonly VulkanTexture?[] _depthStencilTexture = new VulkanTexture?[1];
    private readonly VulkanTextureView?[] _depthStencilView = new VulkanTextureView?[1];
    private readonly VulkanTextureView?[] _depthView = new VulkanTextureView?[1];
    private readonly VulkanTextureView?[] _stencilView = new VulkanTextureView?[1];

    private readonly VulkanTexture[] _currentColor = new VulkanTexture[1];
    private readonly VulkanTextureView[] _currentColorView = new VulkanTextureView[1];

    public override GPUAttachmentLayout AttachmentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _attachmentLayout;
    }

    public override ReadOnlySpan<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _currentColor;
    }

    public override ReadOnlySpan<GPUTextureView> ColorViews
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _currentColorView;
    }

    public override GPUTexture? DepthStencil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilTexture[0];
    }

    public override GPUTextureView? DepthStencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilView[0];
    }

    public override GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthView[0];
    }

    public override GPUTextureView? StencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _stencilView[0];
    }

    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _width;
    }

    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _height;
    }

    protected override VulkanDevice Device
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    public VulkanSurfaceFrameBuffer(VulkanDevice device, VulkanSwapchain swapchain) : base("swapchain_frame_buffer")
    {
        _device = device;
        _swapchain = swapchain;
        _attachmentLayout = swapchain.SurfaceAttachmentLayout;
    }

    internal void NotifyImagesRecreated()
    {
        _width = _swapchain.SwapchainExtentWidth;
        _height = _swapchain.SwapchainExtentHeight;

        if (_attachmentLayout.DepthInfo.HasValue)
        {
            _depthStencilTexture[0]?.Dispose();
            _depthStencilView[0]?.Dispose();
            _depthView[0]?.Dispose();
            _stencilView[0]?.Dispose();

            DepthAttachment depth = _attachmentLayout.DepthInfo.Value;
            _depthStencilTexture[0] = (VulkanTexture)_device.CreateTexture(
                BuildDepthTextureDescriptor(depth.Format, _width, _height, "swapchain_depth"));
            _depthStencilView[0] = (VulkanTextureView)CreateDepthStencilView(_device, _depthStencilTexture[0]);
            _depthView[0] = (VulkanTextureView)CreateDepthView(_device, _depthStencilTexture[0]);
            _stencilView[0] = (VulkanTextureView?)CreateStencilView(_device, _depthStencilTexture[0]);
        }

        OnImageAcquired(0);
    }

    internal void OnImageAcquired(uint imageIndex)
    {
        _currentColor[0] = _swapchain.GetImage(imageIndex);
        _currentColorView[0] = _swapchain.GetImageView(imageIndex);
    }

    protected override void Dispose(bool disposing)
    {
        _depthStencilTexture[0]?.Dispose();
        _depthStencilView[0]?.Dispose();
        _depthView[0]?.Dispose();
        _stencilView[0]?.Dispose();
    }
}
