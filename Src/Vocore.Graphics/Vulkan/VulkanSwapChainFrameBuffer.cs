using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe class VulkanSwapChainFrameBuffer : GPUFrameBuffer
{
    #region Members

    private readonly VkSwapchainKHR _native;
    private readonly VkDevice _nativeDevice;

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
        vkDestroySwapchainKHR(_nativeDevice, _native, null);
    }

    #endregion

    #region Vulkan Implementation


    //called by device
    internal VulkanSwapChainFrameBuffer(VkDevice nativeDevice, VkSwapchainCreateInfoKHR createInfo)
    {
        Name = "SwapChain FrameBuffer";
        _nativeDevice = nativeDevice;
        vkCreateSwapchainKHR(nativeDevice, &createInfo, null, out _native).CheckResult();
    }



    #endregion
}
