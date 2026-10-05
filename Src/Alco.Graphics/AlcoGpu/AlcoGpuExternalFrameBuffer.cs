using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

/// <summary>
/// The frame buffer composed of externally owned textures and views.
/// The textures and views are not disposed with the frame buffer; the caller owns their lifetime.
/// </summary>
internal sealed unsafe class AlcoGpuExternalFrameBuffer : AlcoGpuFrameBufferBase
{
    #region Properties
    private readonly uint _width;
    private readonly uint _height;

    // externally owned resources, never disposed by this frame buffer
    private readonly GPUTexture[] _colorTextures;
    private readonly GPUTextureView[] _colorViews;
    private readonly GPUTexture? _depthStencilTexture;
    private readonly GPUTextureView? _depthStencilView;
    private readonly GPUTextureView? _depthView;
    private readonly GPUTextureView? _stencilView;

    private readonly AlcoGpuAttachmentLayout _attachmentLayout;
    private readonly AlcoRenderPassDesc _descriptor;
    // native memory, need to be manually released
    private readonly AlcoColorAttachment* _colorAttachments;
    private readonly AlcoDepthStencilAttachment* _depthAttachment;

    private readonly PixelFormat[] _colors;
    private readonly PixelFormat? _depth;

    #endregion

    #region Abstract Implementation

    public override GPUAttachmentLayout AttachmentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _attachmentLayout;
    }

    protected override GPUDevice Device { get; }

    public override ReadOnlySpan<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    public override GPUTexture? DepthStencil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilTexture;
    }

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

    public override ReadOnlySpan<GPUTextureView> ColorViews
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorViews;
    }

    public override GPUTextureView? DepthStencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilView;
    }

    public override GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthView;
    }

    public override GPUTextureView? StencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _stencilView;
    }

    protected override void Dispose(bool disposing)
    {
        // the externally owned textures and views are not disposed here
        Free(_colorAttachments);
        if (_depthAttachment != null)
        {
            Free(_depthAttachment);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    public override AlcoRenderPassDesc Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor;
    }

    public override ReadOnlySpan<PixelFormat> NativeColorFormats
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }

    public override PixelFormat? NativeDepthFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    internal AlcoGpuExternalFrameBuffer(AlcoGpuDevice device, in ExternalFrameBufferDescriptor descriptor)
        : base(new FrameBufferDescriptor(descriptor.AttachmentLayout, descriptor.Width, descriptor.Height, descriptor.Name))
    {
        Device = device;
        AlcoGpuAttachmentLayout attachmentLayout = (AlcoGpuAttachmentLayout)descriptor.AttachmentLayout;

        _attachmentLayout = attachmentLayout;

        _width = descriptor.Width;
        _height = descriptor.Height;

        _colorTextures = descriptor.Colors;
        _colorViews = descriptor.ColorViews;
        _depthStencilTexture = descriptor.DepthStencil;
        _depthStencilView = descriptor.DepthStencilView;
        _depthView = descriptor.DepthView;
        _stencilView = descriptor.StencilView;

        _colorAttachments = AllocColorAttachments(descriptor.ColorViews, attachmentLayout.ColorInfos);
        _descriptor = new AlcoRenderPassDesc
        {
            ColorAttachmentCount = (uint)descriptor.ColorViews.Length,
            ColorAttachments = _colorAttachments,
        };

        if (attachmentLayout.DepthInfo.HasValue)
        {
            _depthAttachment = AllocDepthAttachment(descriptor.DepthStencilView!, attachmentLayout.DepthInfo.Value);
            _descriptor.DepthStencil = _depthAttachment;
        }

        _colors = GetNativeColorFormats(attachmentLayout);

        if (attachmentLayout.DepthInfo.HasValue)
        {
            _depth = attachmentLayout.DepthInfo.Value.Format;
        }
    }

    #endregion
}
