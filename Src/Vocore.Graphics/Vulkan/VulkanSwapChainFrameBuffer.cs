using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe class VulkanSwapChainFrameBuffer : GPUFrameBuffer
{
    #region Members

    private readonly VkFramebuffer _native;
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
        vkDestroyFramebuffer(_nativeDevice, _native, null);
    }

    #endregion

    #region Vulkan Implementation

    public VkFramebuffer Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    public VulkanSwapChainFrameBuffer()
    {
        Name = "SwapChain FrameBuffer";
    }



    #endregion
}
