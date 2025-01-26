using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

internal unsafe class VulkanSwapChainFrameBuffer : VulkanFramebufferBase
{
    #region Members

    private readonly VulkanDevice _device;
    private readonly VulkanRenderPass _renderPass;

    private readonly VkFramebuffer[] _buffers;
    private readonly VkSwapchainKHR _swapChain;

    private readonly VulkanSwapChainTexture[] _colorTextures; //with default view

    private readonly VulkanTexture? _depthTexture;
    private readonly VkImageView _depthView = VkImageView.Null; //nullable
    private readonly uint _width;
    private readonly uint _height;

    private VkFramebuffer _currentBuffer;
    private int _frameIndex;

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
        //release textures
        for (int i = 0; i < _colorTextures.Length; i++)
        {
            _colorTextures[i].Dispose();
        }

        if (_depthTexture != null)
        {
            _depthTexture.Dispose();
            vkDestroyImageView(_device.Native, _depthView, null);
        }

        vkDestroySwapchainKHR(_device.Native, _swapChain, null);

        //release framebuffers
        for (int i = 0; i < _buffers.Length; i++)
        {
            vkDestroyFramebuffer(_device.Native, _buffers[i], null);
        }

        _renderPass.Dispose();
    }

    #endregion

    #region Vulkan Implementation

    private VkRenderPassBeginInfo _passBegineInfo;
    public override VkRenderPassBeginInfo PassBegineInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _passBegineInfo;
    }

    //called by device
    internal VulkanSwapChainFrameBuffer(VulkanDevice device, VulkanRenderPass renderPass, VkSwapchainCreateInfoKHR createInfo)
    {
        Name = "swapChain_frameBuffer";
        _device = device;
        _renderPass = renderPass;
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

        if (renderPass.Depth.HasValue)
        {

            PixelFormat depthFormat = renderPass.Depth.Value.Format;
            _depthTexture = new VulkanTexture(device, new TextureDescriptor
            {
                Dimension = TextureDimension.Texture2D,
                Format = depthFormat,
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
                format = UtilsVulkan.PixelFormatToVulkan(depthFormat),
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

            vkCreateImageView(device.Native, &depthViewInfo, null, out _depthView).CheckResult();
        }



        _buffers = new VkFramebuffer[count];

        for (int i = 0; i < count; i++)
        {
#pragma warning disable CA2014
            VkImageView* attachments = stackalloc VkImageView[2];
#pragma warning restore CA2014
            attachments[0] = _colorTextures[i].DefaultView;
            attachments[1] = _depthView;

            VkFramebufferCreateInfo frameBufferCreateInfo = new VkFramebufferCreateInfo
            {
                renderPass = renderPass.Native,
                attachmentCount = renderPass.Depth.HasValue ? 2u : 1u,
                pAttachments = attachments,
                width = createInfo.imageExtent.width,
                height = createInfo.imageExtent.height,
                layers = 1
            };

            vkCreateFramebuffer(device.Native, &frameBufferCreateInfo, null, out _buffers[i]).CheckResult();
        }


        SetFrame(0);
    }

    private void SetFrame(int index)
    {
        _frameIndex = index;
        _currentBuffer = _buffers[_frameIndex];
        _passBegineInfo = new VkRenderPassBeginInfo
        {
            renderPass = _renderPass.Native,
            framebuffer = _currentBuffer,
            renderArea = new VkRect2D
            {
                extent = new VkExtent2D(_width, _height),
                offset = new VkOffset2D(0, 0)
            }
        };
    }

    private void Swap()
    {
        SetFrame((_frameIndex + 1) % _buffers.Length);
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
