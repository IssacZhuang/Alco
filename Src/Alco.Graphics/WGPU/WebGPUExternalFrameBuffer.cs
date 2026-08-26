using System.Runtime.CompilerServices;
using WebGPU;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.WebGPU;

/// <summary>
/// The frame buffer composed of externally owned textures and views.
/// The textures and views are not disposed with the frame buffer; the caller owns their lifetime.
/// </summary>
internal unsafe sealed class WebGPUExternalFrameBuffer : WebGPUFrameBufferBase
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

    private readonly WebGPUAttachmentLayout _attachmentLayout;
    private readonly WGPURenderPassDescriptor _descriptor;
    // native memory, need to be manually released
    private readonly WGPURenderPassColorAttachment* _colorAttachments;
    private readonly WGPURenderPassDepthStencilAttachment* _depthAttachment;

    private readonly WGPUTextureFormat[] _colors;
    private readonly WGPUTextureFormat? _depth;

    #endregion

    #region Abstract Implementation

    public override GPUAttachmentLayout AttachmentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _attachmentLayout;
    }

    protected override WebGPUDevice Device { get; }

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

    #region WebGPU Implementation

    public override WGPURenderPassDescriptor Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor;
    }

    public override ReadOnlySpan<WGPUTextureFormat> NativeColorFormats
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }
    public override WGPUTextureFormat? NativeDepthFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    internal WebGPUExternalFrameBuffer(WebGPUDevice device, in ExternalFrameBufferDescriptor descriptor)
        : base(new FrameBufferDescriptor(descriptor.AttachmentLayout, descriptor.Width, descriptor.Height, descriptor.Name))
    {
        Device = device;
        WebGPUAttachmentLayout attachmentLayout = (WebGPUAttachmentLayout)descriptor.AttachmentLayout;

        _attachmentLayout = attachmentLayout;

        _width = descriptor.Width;
        _height = descriptor.Height;

        _colorTextures = descriptor.Colors;
        _colorViews = descriptor.ColorViews;
        _depthStencilTexture = descriptor.DepthStencil;
        _depthStencilView = descriptor.DepthStencilView;
        _depthView = descriptor.DepthView;
        _stencilView = descriptor.StencilView;

        _colorAttachments = AllocColorAttachments(descriptor.ColorViews, attachmentLayout.WebGPUColorInfos);
        _descriptor = new WGPURenderPassDescriptor
        {
            colorAttachmentCount = (uint)descriptor.ColorViews.Length,
            colorAttachments = _colorAttachments,
        };

        if (attachmentLayout.WebGPUDepthInfo.HasValue)
        {
            _depthAttachment = AllocDepthAttachment(descriptor.DepthStencilView!, attachmentLayout.WebGPUDepthInfo.Value);
            _descriptor.depthStencilAttachment = _depthAttachment;
        }

        _colors = GetNativeColorFormats(attachmentLayout);

        if (attachmentLayout.WebGPUDepthInfo.HasValue)
        {
            _depth = attachmentLayout.WebGPUDepthInfo.Value.format;
        }
    }

    #endregion
}
