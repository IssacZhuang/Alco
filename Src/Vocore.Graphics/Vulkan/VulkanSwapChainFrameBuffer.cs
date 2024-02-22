using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe class VulkanSwapChainFrameBuffer : GPUFrameBuffer
{
    #region Members

    private readonly VkFramebuffer _native;
    private readonly VkSwapchainKHR _swapChain;
    private readonly VkDevice _nativeDevice;

    private readonly VulkanSwapChainTexture[] _colorTextures; //with default view

    private readonly VulkanTexture? _depthTexture;
    private readonly VkImageView _depthView = VkImageView.Null; //nullable

    #endregion

    #region Abstract Implementation

    public override string Name { get; }

    public override uint Width => throw new NotImplementedException();

    public override uint Height => throw new NotImplementedException();
    public override GPURenderPass RenderPass => throw new NotImplementedException();

    public override IReadOnlyList<GPUTexture> Colors => throw new NotImplementedException();

    public override GPUTexture? Depth => throw new NotImplementedException();

    protected override void Dispose(bool disposing)
    {
        vkDestroySwapchainKHR(_nativeDevice, _swapChain, null);
    }

    #endregion

    #region Vulkan Implementation


    //called by device
    internal VulkanSwapChainFrameBuffer(VkDevice nativeDevice, VkSwapchainCreateInfoKHR createInfo)
    {
        Name = "SwapChain FrameBuffer";
        _nativeDevice = nativeDevice;
        vkCreateSwapchainKHR(nativeDevice, &createInfo, null, out _swapChain).CheckResult();

        //get swapchain images
        uint count = 0;
        vkGetSwapchainImagesKHR(nativeDevice, _swapChain, &count, null).CheckResult();
        VkImage* images = stackalloc VkImage[(int)count];
        vkGetSwapchainImagesKHR(nativeDevice, _swapChain, &count, images).CheckResult();

        _colorTextures = new VulkanSwapChainTexture[count];
        for (int i = 0; i < count; i++)
        {
            _colorTextures[i] = new VulkanSwapChainTexture(nativeDevice, images[i], createInfo.imageFormat);
        }

                
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
                components = new VkComponentMapping
                {
                    r = VkComponentSwizzle.Identity,
                    g = VkComponentSwizzle.Identity,
                    b = VkComponentSwizzle.Identity,
                    a = VkComponentSwizzle.Identity
                },
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
