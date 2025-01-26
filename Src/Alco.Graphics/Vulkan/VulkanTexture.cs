using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Vortice.Vulkan.Vma;

namespace Alco.Graphics.Vulkan;

internal unsafe class VulkanTexture : VulkanTextureBase
{
    #region Members

    private readonly VulkanDevice _device;

    private readonly VkImage _native;
    private readonly VmaAllocation _allocation;


    private readonly VkExtent3D _extent;
    private readonly uint _mipLevelCount;

    #endregion

    #region Abstract Implementation

    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _extent.width;
    }

    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _extent.height;
    }

    public override uint Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _extent.depth;
    }

    public override uint MipLevelCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mipLevelCount;
    }

    public override string Name { get; }

    protected override void Dispose(bool disposing)
    {
        //vkDestroyImage(_device.Native, _native, null);
        vmaDestroyImage(_device.Allocator, _native, _allocation);
    }

    #endregion

    #region Vulkan Implementation

    public override VkImage Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    public VulkanTexture(VulkanDevice device, TextureDescriptor descriptor)
    {
        Name = descriptor.Name;
        _extent = new VkExtent3D(descriptor.Width, descriptor.Height, descriptor.DepthOrArrayLayer);
        _mipLevelCount = descriptor.MipLevels;
        _device = device;

        VkImageCreateInfo createInfo = new VkImageCreateInfo
        {
            format = UtilsVulkan.PixelFormatToVulkan(descriptor.Format),
            imageType = UtilsVulkan.TextureDimensionToVulkan(descriptor.Dimension),
            usage = UtilsVulkan.ConvertTextureUsage(descriptor.Usage),
            extent = _extent,
            mipLevels = _mipLevelCount,
            arrayLayers = descriptor.DepthOrArrayLayer,
            samples = (VkSampleCountFlags)descriptor.SampleCount,
            tiling = VkImageTiling.Optimal,
            initialLayout = VkImageLayout.Undefined,
            sharingMode = VkSharingMode.Exclusive,
        };

        VmaAllocationInfo allocationInfo = default;
        VmaAllocationCreateInfo memoryInfo = new()
        {
            usage = VmaMemoryUsage.Auto
        };

        vmaCreateImage(device.Allocator,
            &createInfo,
            &memoryInfo,
            out _native,
            out _allocation,
            &allocationInfo).CheckResult("Failed to create image.");
    }

    #endregion
}