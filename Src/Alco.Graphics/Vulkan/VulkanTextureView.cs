using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

internal sealed unsafe class VulkanTextureView : GPUTextureView
{
    public VkImageView Native;
    public readonly VulkanTexture TextureRef;

    public readonly uint BaseMipLevel;
    public readonly uint MipLevels;
    public readonly uint BaseArrayLayer;
    public readonly uint ArrayLayers;
    public readonly VkImageAspectFlags AspectMask;

    protected override VulkanDevice Device { get; }

    public override GPUTexture Texture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TextureRef;
    }

    public VulkanTextureView(VulkanDevice device, in TextureViewDescriptor descriptor) : base(descriptor)
    {
        Device = device;
        TextureRef = (VulkanTexture)descriptor.Texture;

        BaseMipLevel = descriptor.BaseMipLevel;
        MipLevels = descriptor.MipLevelCount;
        BaseArrayLayer = descriptor.BaseArrayLayer;
        ArrayLayers = descriptor.ArrayLayerCount;
        AspectMask = VulkanUtility.AspectToVulkan(descriptor.Aspect, TextureRef.VkFormat);

        TextureViewDimension dimension = descriptor.Dimension;
        if (dimension == TextureViewDimension.Undefined)
        {
            dimension = TextureRef.Is3D ? TextureViewDimension.Texture3D : TextureViewDimension.Texture2D;
        }

        VkImageViewType viewType = dimension switch
        {
            TextureViewDimension.Texture1D => VkImageViewType.Image1D,
            TextureViewDimension.Texture2D => TextureRef.ArrayLayers > 1 ? VkImageViewType.Image2DArray : VkImageViewType.Image2D,
            TextureViewDimension.Texture2DArray => VkImageViewType.Image2DArray,
            TextureViewDimension.Texture3D => VkImageViewType.Image3D,
            TextureViewDimension.Cube => VkImageViewType.ImageCube,
            TextureViewDimension.CubeArray => VkImageViewType.ImageCubeArray,
            _ => VkImageViewType.Image2D,
        };

        VkImageViewCreateInfo createInfo = new()
        {
            image = TextureRef.Image,
            viewType = viewType,
            format = TextureRef.VkFormat,
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = AspectMask,
                baseMipLevel = BaseMipLevel,
                levelCount = MipLevels,
                baseArrayLayer = BaseArrayLayer,
                layerCount = ArrayLayers,
            },
        };

        VkImageView nativeView = default;
        VkResult result = vkCreateImageView(device.NativeDevice, &createInfo, null, &nativeView);
        Native = nativeView;
        VulkanException.ThrowIfFailed(result, $"Failed to create texture view '{descriptor.Name}'");

        device.SetDebugName(VkObjectType.ImageView, Native.Handle, descriptor.Name);
    }

    /// <summary>Wrapper around a foreign view (swapchain image views are managed by the swapchain).</summary>
    public VulkanTextureView(VulkanDevice device, VulkanTexture texture, VkImageView nativeView, string name) : base(name)
    {
        Device = device;
        TextureRef = texture;
        Native = nativeView;
        BaseMipLevel = 0;
        MipLevels = texture.MipLevelCount;
        BaseArrayLayer = 0;
        ArrayLayers = texture.ArrayLayers;
        AspectMask = VulkanUtility.AspectToVulkan(TextureAspect.All, texture.VkFormat);
    }

    protected override void Dispose(bool disposing)
    {
        if (Native.Handle == 0)
        {
            return;
        }

        Device.QueueNativeDestroy(Native);
        Native = default;
    }
}
