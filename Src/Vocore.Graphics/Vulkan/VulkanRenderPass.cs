using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe class VulkanRenderPass : GPURenderPass
{
    #region Members

    private readonly VkRenderPass _native;
    private readonly VkDevice _nativeDevice;

    private readonly RenderPassDescriptor _descriptor;

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
        _nativeDevice = nativeDevice;
        _descriptor = descriptor;

        VkAttachmentDescription* colors = stackalloc VkAttachmentDescription[descriptor.Colors.Length + (descriptor.Depth.HasValue ? 1 : 0)];

        for (int i = 0; i < descriptor.Colors.Length; i++)
        {
            colors[i] = new VkAttachmentDescription
            {
                format = UtilsVulkan.PixelFormatToVulkan(descriptor.Colors[i].Format),
                samples = VkSampleCountFlags.Count1,
                loadOp = VkAttachmentLoadOp.Clear,
                storeOp = VkAttachmentStoreOp.Store,
            };
        }

        VkAttachmentReference* colorRefs = stackalloc VkAttachmentReference[descriptor.Colors.Length];

        
    }

    #endregion
}
