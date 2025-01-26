using System;
using Vortice.Vulkan;

namespace Alco.Graphics.Vulkan;

internal ref struct SwapChainSupportDetails
{
    public VkSurfaceCapabilitiesKHR Capabilities;
    public ReadOnlySpan<VkSurfaceFormatKHR> Formats;
    public ReadOnlySpan<VkPresentModeKHR> PresentModes;

    public uint GetPrefferedImageCount(uint preffered)
    {
        return Capabilities.maxImageCount < preffered ? Capabilities.maxImageCount : preffered;
    }
}