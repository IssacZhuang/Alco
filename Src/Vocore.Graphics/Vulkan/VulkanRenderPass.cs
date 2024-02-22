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

    public override IReadOnlyList<ColorAttachment> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor.Colors;
    }

    public override DepthAttachment? Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor.Depth;
    }


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
                stencilLoadOp = VkAttachmentLoadOp.Clear,
                stencilStoreOp = VkAttachmentStoreOp.Store,
                initialLayout = VkImageLayout.Undefined,
                finalLayout = VkImageLayout.PresentSrcKHR
            };
        }

        if (descriptor.Depth.HasValue)
        {
            colors[descriptor.Colors.Length] = new VkAttachmentDescription
            {
                format = UtilsVulkan.PixelFormatToVulkan(descriptor.Depth.Value.Format),
                samples = VkSampleCountFlags.Count1,
                loadOp = VkAttachmentLoadOp.Clear,
                storeOp = VkAttachmentStoreOp.Store,
                stencilLoadOp = VkAttachmentLoadOp.DontCare,
                stencilStoreOp = VkAttachmentStoreOp.DontCare,
                initialLayout = VkImageLayout.Undefined,
                finalLayout = VkImageLayout.DepthStencilAttachmentOptimal
            };
        }

        VkAttachmentReference* colorAttachmentRefs = stackalloc VkAttachmentReference[descriptor.Colors.Length];
        for (int i = 0; i < descriptor.Colors.Length; i++)
        {
            colorAttachmentRefs[i] = new VkAttachmentReference
            {
                attachment = (uint)i,
                layout = VkImageLayout.ColorAttachmentOptimal
            };
        }

        VkAttachmentReference depthAttachmentRef = default;
        if (descriptor.Depth.HasValue)
        {
            depthAttachmentRef = new VkAttachmentReference
            {
                attachment = (uint)descriptor.Colors.Length,
                layout = VkImageLayout.DepthStencilAttachmentOptimal
            };
        }

        VkSubpassDescription subpass = new VkSubpassDescription
        {
            pipelineBindPoint = VkPipelineBindPoint.Graphics,
            colorAttachmentCount = (uint)descriptor.Colors.Length,
            pColorAttachments = colorAttachmentRefs,
        };

        if (descriptor.Depth.HasValue)
        {
            subpass.pDepthStencilAttachment = &depthAttachmentRef;
        }

        //TODO: set correct dependency
        VkSubpassDependency dependency = new VkSubpassDependency
        {
            srcSubpass = 0,
            dstSubpass = 1,
            srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            srcAccessMask = 0,
            dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite
        };

        VkRenderPassCreateInfo renderPassCreateInfo = new VkRenderPassCreateInfo
        {
            attachmentCount = (uint)(descriptor.Colors.Length + (descriptor.Depth.HasValue ? 1 : 0)),
            pAttachments = colors,
            subpassCount = 1,
            pSubpasses = &subpass,
        };

        vkCreateRenderPass(_nativeDevice, &renderPassCreateInfo, null, out _native).CheckResult("Failed to create render pass");
    }

    #endregion
}
