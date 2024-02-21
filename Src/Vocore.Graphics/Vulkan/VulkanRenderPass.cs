using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe class VulkanRenderPass : GPURenderPass
{
    #region Members

    private readonly VkRenderPass _native;
    private readonly VkDevice _nativeDevice;

    #endregion

    #region Abstract Implementation

    public override string Name { get; }

    public override IReadOnlyList<ColorAttachment> Colors => throw new NotImplementedException();

    public override DepthAttachment? Depth => throw new NotImplementedException();

    
    public override GPUFrameBuffer CreateFrameBuffer(uint width, uint height, string? name = null)
    {
        throw new NotImplementedException();
    }

    protected override void Dispose(bool disposing)
    {
        vkDestroyRenderPass(_nativeDevice, _native, null);
    }



    #endregion

    #region Vulkan Implementation

    public VkRenderPass Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    

    public VulkanRenderPass(VkDevice nativeDevice, RenderPassDescriptor descriptor)
    {
        Name = descriptor.Name;
        
    }

    #endregion
}
