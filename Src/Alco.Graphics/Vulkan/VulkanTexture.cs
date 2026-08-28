using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Vortice.Vulkan.Vma;

namespace Alco.Graphics.Vulkan;

internal sealed unsafe class VulkanTexture : GPUTexture
{
    public VkImage Image;
    public VmaAllocation Allocation;

    public VkFormat VkFormat { get; }
    public VkImageType ImageType { get; }
    public VkExtent3D Extent { get; }
    /// <summary>Array layers for 2D/1D textures, 1 for 3D.</summary>
    public uint ArrayLayers { get; }
    public bool Is3D { get; }

    private readonly uint _width;
    private readonly uint _height;
    private readonly uint _depth;
    private readonly uint _mipLevelCount;

    protected override VulkanDevice Device { get; }

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

    public override uint Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    public override uint MipLevelCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mipLevelCount;
    }

    public override PixelFormat PixelFormat { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MipWidth(uint mipLevel) => uint.Max(1, _width >> (int)mipLevel);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MipHeight(uint mipLevel) => Is3D || ImageType != VkImageType.Image1D
        ? uint.Max(1, _height >> (int)mipLevel)
        : 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint MipDepth(uint mipLevel) => Is3D
        ? uint.Max(1, _depth >> (int)mipLevel)
        : ArrayLayers;

    public VulkanTexture(VulkanDevice device, in TextureDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        _width = descriptor.Width;
        _height = descriptor.Height;
        _mipLevelCount = descriptor.MipLevels;
        PixelFormat = descriptor.Format;
        VkFormat = VulkanUtility.PixelFormatToVulkan(descriptor.Format);
        Is3D = descriptor.Dimension == TextureDimension.Texture3D;
        ImageType = descriptor.Dimension switch
        {
            TextureDimension.Texture1D => VkImageType.Image1D,
            TextureDimension.Texture2D => VkImageType.Image2D,
            TextureDimension.Texture3D => VkImageType.Image3D,
            _ => VkImageType.Image2D,
        };
        ArrayLayers = Is3D ? 1u : descriptor.DepthOrArrayLayer;
        _depth = descriptor.DepthOrArrayLayer;
        Extent = new VkExtent3D
        {
            width = _width,
            height = ImageType == VkImageType.Image1D ? 1u : _height,
            depth = Is3D ? _depth : 1u,
        };

        VkImageUsageFlags usage = VulkanUtility.ConvertTextureUsage(descriptor.Usage);
        if (VulkanUtility.IsDepthFormat(VkFormat))
        {
            // callers request attachment usage generically; depth-stencil formats
            // (including stencil-only, which still uses the DEPTH_STENCIL usage
            // bit in Vulkan) reject COLOR_ATTACHMENT in
            // vkGetPhysicalDeviceImageFormatProperties2
            usage &= ~VkImageUsageFlags.ColorAttachment;
            usage |= VkImageUsageFlags.DepthStencilAttachment;
        }
        // All copies (upload, readback, mipmap blits) are enabled regardless of the
        // declared usage to keep the automatic copy paths working.
        usage |= VkImageUsageFlags.TransferSrc | VkImageUsageFlags.TransferDst;

        // wgpu sets CUBE_COMPATIBLE for any 2D image with at least six layers so
        // cube views stay valid; the flag is harmless for plain arrays
        bool cubeCompatible = ImageType == VkImageType.Image2D && ArrayLayers >= 6;

        VkImageCreateInfo imageInfo = new()
        {
            flags = cubeCompatible ? VkImageCreateFlags.CubeCompatible : VkImageCreateFlags.None,
            imageType = ImageType,
            format = VkFormat,
            extent = Extent,
            mipLevels = _mipLevelCount,
            arrayLayers = ArrayLayers,
            samples = (VkSampleCountFlags)descriptor.SampleCount,
            tiling = VkImageTiling.Optimal,
            usage = usage,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined,
        };

        VmaAllocationCreateInfo allocInfo = new()
        {
            usage = VmaMemoryUsage.AutoPreferDevice,
        };

        VkImage image = default;
        VmaAllocation allocation = default;
        VkResult result = vmaCreateImage(device.Allocator, &imageInfo, &allocInfo, &image, &allocation, null);
        VulkanException.ThrowIfFailed(result, $"Failed to create texture '{descriptor.Name}'");
        Image = image;
        Allocation = allocation;

        device.SetDebugName(VkObjectType.Image, Image.Handle, descriptor.Name);
        device.InitializeTextureLayout(this);
    }

    /// <summary>Constructor for swapchain images (memory owned by the presentation engine).</summary>
    public VulkanTexture(VulkanDevice device, VkImage image, VkFormat format, uint width, uint height, PixelFormat pixelFormat, string name)
        : base(new TextureDescriptor(TextureDimension.Texture2D, pixelFormat, width, height, 1, 1, TextureUsage.None, 1, name))
    {
        Device = device;
        Image = image;
        Allocation = default;
        VkFormat = format;
        ImageType = VkImageType.Image2D;
        Extent = new VkExtent3D { width = width, height = height, depth = 1 };
        ArrayLayers = 1;
        Is3D = false;
        _width = width;
        _height = height;
        _depth = 1;
        _mipLevelCount = 1;
        PixelFormat = pixelFormat;
        // the surface framebuffer's pass is recorded in its own command buffer
        // that interleaves with (and executes after) the frame's main command
        // buffer, so the tracker's record-order state machine cannot safely move
        // this image through optimal layouts — keep it in GENERAL for everything
        // except the present transition
        PreferGeneralLayout = true;
    }

    /// <summary>Keep this image in GENERAL layout for every state except present
    /// (set for swapchain images, whose attachment barriers are recorded in a
    /// command buffer that interleaves with the main frame buffer).</summary>
    public bool PreferGeneralLayout { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (Image.Handle == 0)
        {
            return;
        }

        if (Allocation.Handle != 0)
        {
            Device.QueueNativeDestroy(Image, Allocation);
        }
        Image = default;
        Allocation = default;
    }
}
