using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe class VulkanSwapChainFrameBuffer : GPUFrameBuffer
{
    #region Members

    private readonly VulkanDevice _device;
    private readonly VulkanRenderPass _renderPass;

    private readonly VkFramebuffer _native;
    private readonly VkSwapchainKHR _swapChain;


    private readonly VulkanSwapChainTexture[] _colorTextures; //with default view

    private readonly VulkanTexture? _depthTexture;
    private readonly VkImageView _depthView = VkImageView.Null; //nullable
    private readonly uint _width;
    private readonly uint _height;

    #endregion

    #region Abstract Implementation

    public override string Name { get; }

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
    public override GPURenderPass RenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderPass;
    }

    public override IReadOnlyList<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    public override GPUTexture? Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthTexture;
    }

    protected override void Dispose(bool disposing)
    {
        vkDestroySwapchainKHR(_device.Native, _swapChain, null);
    }

    #endregion

    #region Vulkan Implementation


    //called by device
    internal VulkanSwapChainFrameBuffer(VulkanDevice device, VkSwapchainCreateInfoKHR createInfo, PixelFormat colorFormat, PixelFormat? depth)
    {
        Name = "SwapChain FrameBuffer";
        _device = device;
        _width = createInfo.imageExtent.width;
        _height = createInfo.imageExtent.height;
        vkCreateSwapchainKHR(device.Native, &createInfo, null, out _swapChain).CheckResult();

        //get swapchain images
        uint count = 0;
        vkGetSwapchainImagesKHR(device.Native, _swapChain, &count, null).CheckResult();
        VkImage* images = stackalloc VkImage[(int)count];
        vkGetSwapchainImagesKHR(device.Native, _swapChain, &count, images).CheckResult();

        _colorTextures = new VulkanSwapChainTexture[count];
        for (int i = 0; i < count; i++)
        {
            _colorTextures[i] = new VulkanSwapChainTexture(device.Native, images[i], createInfo.imageFormat);
        }

        if (depth.HasValue)
        {
            _depthTexture = new VulkanTexture(device, new TextureDescriptor
            {
                Dimension = TextureDimension.Texture2D,
                Format = depth.Value,
                Width = createInfo.imageExtent.width,
                Height = createInfo.imageExtent.height,
                DepthOrArrayLayer = 1,
                MipLevels = 1,
                Usage = TextureUsage.DepthAttachment,
                SampleCount = 1
            });

            VkImageViewCreateInfo depthViewInfo = new VkImageViewCreateInfo
            {
                image = _depthTexture.Native,
                viewType = VkImageViewType.Image2D,
                format = UtilsVulkan.PixelFormatToVulkan(depth.Value),
                components = VkComponentMapping.Identity,
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = VkImageAspectFlags.Depth,
                    baseMipLevel = 0,
                    levelCount = 1,
                    baseArrayLayer = 0,
                    layerCount = 1
                }
            };
        }

        RenderPassDescriptor renderPassDescriptor = new RenderPassDescriptor(
            new ColorAttachment[]
            {
                new ColorAttachment(colorFormat)
            },
            depth.HasValue ? new DepthAttachment(depth.Value) : null
        );

        _renderPass = new VulkanRenderPass(device.Native, renderPassDescriptor);

    }



    #endregion

    //managed swapchain image
    internal class VulkanSwapChainTexture : VulkanTextureBase
    {
        #region Members

        private readonly VkImage _native;
        private readonly VkDevice _nativeDevice;
        private readonly VkImageView _defaultView;

        #endregion

        #region Abstract Implementation

        public override string Name { get; }

        public override uint Width => throw new NotImplementedException();

        public override uint Height => throw new NotImplementedException();

        public override uint Depth => throw new NotImplementedException();

        public override uint MipLevelCount => throw new NotImplementedException();

        protected override void Dispose(bool disposing)
        {
            vkDestroyImage(_nativeDevice, _native, null);
            vkDestroyImageView(_nativeDevice, _defaultView, null);
        }

        #endregion

        #region Vulkan Implementation

        public override VkImage Native
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _native;
        }

        public VkImageView DefaultView
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _defaultView;
        }

        public VulkanSwapChainTexture(VkDevice nativeDevice, VkImage image, VkFormat swapChainFormat)
        {
            Name = "SwapChain Texture";
            _nativeDevice = nativeDevice;
            _native = image;
            VkImageViewCreateInfo viewCreateInfo = new VkImageViewCreateInfo
            {
                image = image,
                viewType = VkImageViewType.Image2D,
                format = swapChainFormat,
                components = VkComponentMapping.Identity,
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = VkImageAspectFlags.Color,
                    baseMipLevel = 0,
                    levelCount = 1,
                    baseArrayLayer = 0,
                    layerCount = 1
                }
            };

            vkCreateImageView(nativeDevice, &viewCreateInfo, null, out _defaultView).CheckResult();

        }

        #endregion
    }
}
