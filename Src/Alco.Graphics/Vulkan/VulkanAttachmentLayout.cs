using System.Runtime.CompilerServices;
using Vortice.Vulkan;

namespace Alco.Graphics.Vulkan;

/// <summary>
/// Pure metadata attachment layout (no native objects): the color formats with
/// their clear colors and the optional depth attachment. All storage lives in the
/// abstract base; this subclass exposes the Vulkan-side accessors.
/// </summary>
internal sealed class VulkanAttachmentLayout : GPUAttachmentLayout
{
    private readonly VulkanDevice _device;

    public VulkanAttachmentLayout(VulkanDevice device, in AttachmentLayoutDescriptor descriptor) : base(descriptor)
    {
        _device = device;
    }

    public ReadOnlySpan<ColorAttachment> ColorAttachments
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Colors;
    }

    public DepthAttachment? DepthInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Depth;
    }

    protected override GPUDevice Device => _device;

    protected override void Dispose(bool disposing)
    {
        // metadata only
    }
}
