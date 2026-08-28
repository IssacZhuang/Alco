using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

internal sealed unsafe class VulkanSampler : GPUSampler
{
    public VkSampler Native;

    protected override VulkanDevice Device { get; }

    public VulkanSampler(VulkanDevice device, in SamplerDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        // clamp instead of disabling: partially supported anisotropy still beats
        // silently falling back to isotropic filtering
        float maxAnisotropy = Math.Clamp(descriptor.MaxAnisotropy, 1.0f, device.Features.MaxSamplerAnisotropy);
        if (maxAnisotropy > 1.0f && !device.Features.SamplerAnisotropy)
        {
            maxAnisotropy = 1.0f;
        }

        bool compareEnabled = descriptor.Compare != CompareFunction.Undefined;

        VkSamplerCreateInfo createInfo = new()
        {
            magFilter = VulkanUtility.FilterModeToVulkan(descriptor.MagFilter),
            minFilter = VulkanUtility.FilterModeToVulkan(descriptor.MinFilter),
            mipmapMode = VulkanUtility.MipmapFilterModeToVulkan(descriptor.MipFilter),
            addressModeU = VulkanUtility.AddressModeToVulkan(descriptor.AddressModeU),
            addressModeV = VulkanUtility.AddressModeToVulkan(descriptor.AddressModeV),
            addressModeW = VulkanUtility.AddressModeToVulkan(descriptor.AddressModeW),
            mipLodBias = 0.0f,
            maxAnisotropy = maxAnisotropy,
            anisotropyEnable = maxAnisotropy > 1.0f,
            compareEnable = compareEnabled,
            compareOp = VulkanUtility.CompareFunctionToVulkan(descriptor.Compare),
            minLod = descriptor.LodMinClamp,
            maxLod = descriptor.LodMaxClamp,
            borderColor = VkBorderColor.FloatTransparentBlack,
            unnormalizedCoordinates = false,
        };

        VkSampler nativeSampler = default;
        VkResult result = vkCreateSampler(device.NativeDevice, &createInfo, null, &nativeSampler);
        VulkanException.ThrowIfFailed(result, $"Failed to create sampler '{descriptor.Name}'");
        Native = nativeSampler;

        device.SetDebugName(VkObjectType.Sampler, Native.Handle, descriptor.Name);
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
