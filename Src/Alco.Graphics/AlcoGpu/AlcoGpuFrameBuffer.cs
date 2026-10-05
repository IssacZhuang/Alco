using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

internal sealed unsafe class AlcoGpuFrameBuffer : AlcoGpuFrameBufferBase
{
    #region Properties
    private readonly uint _width;
    private readonly uint _height;

    private readonly AlcoGpuTexture[] _colorTextures;
    private readonly AlcoGpuTextureView[] _colorViews;
    private readonly AlcoGpuTexture? _depthStencilTexture;
    private readonly AlcoGpuTextureView? _depthStencilView;
    private readonly AlcoGpuTextureView? _depthView;
    private readonly AlcoGpuTextureView? _stencilView;
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
        if (disposing)
        {
            foreach (var view in _colorViews)
            {
                view.Dispose();
            }

            _depthStencilView?.Dispose();
            _depthView?.Dispose();
            _stencilView?.Dispose();

            foreach (var texture in _colorTextures)
            {
                texture.Dispose();
            }

            _depthStencilTexture?.Dispose();
        }

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

    internal AlcoGpuFrameBuffer(AlcoGpuDevice device, in FrameBufferDescriptor descriptor) : base(descriptor)
    {
        Device = device;
        AlcoGpuAttachmentLayout attachmentLayout = (AlcoGpuAttachmentLayout)descriptor.AttachmentLayout;
        uint width = descriptor.Width;
        uint height = descriptor.Height;

        _attachmentLayout = attachmentLayout;

        _width = width;
        _height = height;

        _colorTextures = new AlcoGpuTexture[attachmentLayout.ColorInfos.Length];
        _colorViews = new AlcoGpuTextureView[attachmentLayout.ColorInfos.Length];
        _descriptor = new AlcoRenderPassDesc
        {
            ColorAttachmentCount = (uint)attachmentLayout.ColorInfos.Length,
        };

        for (int i = 0; i < attachmentLayout.ColorInfos.Length; i++)
        {
            AlcoColorAttachmentInfo colorInfo = attachmentLayout.ColorInfos[i];
            _colorTextures[i] = new AlcoGpuTexture(
                device,
                BuildColorTextureDescriptor(colorInfo.Format, width, height));

            _colorViews[i] = (AlcoGpuTextureView)device.CreateTextureView(new TextureViewDescriptor(_colorTextures[i]));
        }

        _colorAttachments = AllocColorAttachments(_colorViews, attachmentLayout.ColorInfos);

        if (attachmentLayout.DepthInfo.HasValue)
        {
            AlcoDepthAttachmentInfo depthInfo = attachmentLayout.DepthInfo.Value;

            _depthStencilTexture = new AlcoGpuTexture(
                device,
                BuildDepthTextureDescriptor(depthInfo.Format, width, height));

            _depthStencilView = (AlcoGpuTextureView)device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.None));
            _depthView = (AlcoGpuTextureView)device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.DepthOnly));
            if (PixelFormatUtility.HasStencil(_depthStencilTexture.PixelFormat))
            {
                _stencilView = (AlcoGpuTextureView)device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.StencilOnly));
            }

            _depthAttachment = AllocDepthAttachment(_depthStencilView, depthInfo);
        }

        _descriptor.ColorAttachments = _colorAttachments;
        _descriptor.DepthStencil = _depthAttachment;

        _colors = GetNativeColorFormats(attachmentLayout);

        if (attachmentLayout.DepthInfo.HasValue)
        {
            _depth = attachmentLayout.DepthInfo.Value.Format;
        }
    }

    #endregion
}
