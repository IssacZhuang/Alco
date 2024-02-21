using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe class VulkanTexture : GPUTexture
{
    #region Members

    private readonly VkImage _native;
    private readonly VkDevice _nativeDevice;

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
        vkDestroyImage(_nativeDevice, _native, null);
    }

    #endregion

    #region Vulkan Implementation

    public VkImage Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    public VulkanTexture(VkDevice _nativeDevice, TextureDescriptor descriptor)
    {
        Name = descriptor.Name;
        _extent = new VkExtent3D(descriptor.Width, descriptor.Height, descriptor.DepthOrArrayLayer);
        _mipLevelCount = descriptor.MipLevels;

        VkImageCreateInfo createInfo = new VkImageCreateInfo
        {
            
        };
    }

    #endregion
}