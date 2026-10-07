using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

/// <summary>
/// Fixed-size offscreen frame buffer that owns its color and depth-stencil attachment
/// textures and bakes a static render pass descriptor over them.
/// </summary>
internal sealed unsafe class AlcoGpuFrameBuffer : AlcoGpuFrameBufferBase
{
    #region Properties
    private readonly uint _width;
    private readonly uint _height;

    private readonly AlcoGpuTexture[] _colorTextures = [];
    private readonly AlcoGpuTextureView[] _colorViews = [];
    private readonly AlcoGpuTexture? _depthStencilTexture;
    private readonly AlcoGpuTextureView? _depthStencilView;
    private readonly AlcoGpuTextureView? _depthView;
    private readonly AlcoGpuTextureView? _stencilView;
    private readonly AlcoGpuAttachmentLayout _attachmentLayout;
    private readonly AlcoGPU.RenderPassDesc _descriptor;
    // native memory, need to be manually released
    private AlcoGPU.ColorAttachment* _colorAttachments;
    private AlcoGPU.DepthStencilAttachment* _depthAttachment;

    private readonly PixelFormat[] _colors;
    private readonly PixelFormat? _depth;

    #endregion

    #region Abstract Implementation

    /// <inheritdoc />
    public override GPUAttachmentLayout AttachmentLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _attachmentLayout;
    }

    protected override GPUDevice Device { get; }

    /// <inheritdoc />
    public override ReadOnlySpan<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    /// <inheritdoc />
    public override GPUTexture? DepthStencil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilTexture;
    }

    /// <inheritdoc />
    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _width;
    }

    /// <inheritdoc />
    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _height;
    }

    /// <inheritdoc />
    public override ReadOnlySpan<GPUTextureView> ColorViews
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorViews;
    }

    /// <inheritdoc />
    public override GPUTextureView? DepthStencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthStencilView;
    }

    /// <inheritdoc />
    public override GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthView;
    }

    /// <inheritdoc />
    public override GPUTextureView? StencilView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _stencilView;
    }

    protected override void Dispose(bool disposing)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        void Release(BaseGPUObject? resource)
        {
            try { resource?.Destroy(disposing); }
            catch (Exception error) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        }
        for (int i = 0; i < _colorViews.Length; i++) { Release(_colorViews[i]); }
        Release(_depthStencilView);
        Release(_depthView);
        Release(_stencilView);
        for (int i = 0; i < _colorTextures.Length; i++) { Release(_colorTextures[i]); }
        Release(_depthStencilTexture);

        AlcoGPU.ColorAttachment* colors = _colorAttachments;
        _colorAttachments = null;
        Free(colors);
        AlcoGPU.DepthStencilAttachment* depth = _depthAttachment;
        _depthAttachment = null;
        Free(depth);
        GC.KeepAlive(this);
        failure?.Throw();
    }

    #endregion

    #region AlcoGpu Implementation

    /// <inheritdoc />
    public override AlcoGPU.RenderPassDesc Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor;
    }

    /// <inheritdoc />
    public override ReadOnlySpan<PixelFormat> NativeColorFormats
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }

    /// <inheritdoc />
    public override PixelFormat? NativeDepthFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    internal AlcoGpuFrameBuffer(AlcoGpuDevice device, in FrameBufferDescriptor descriptor) : base(descriptor)
    {
        try
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
            _descriptor = new AlcoGPU.RenderPassDesc
            {
                ColorAttachmentCount = (uint)attachmentLayout.ColorInfos.Length,
            };

            for (int i = 0; i < attachmentLayout.ColorInfos.Length; i++)
            {
                ColorAttachmentInfo colorInfo = attachmentLayout.ColorInfos[i];
                _colorTextures[i] = new AlcoGpuTexture(
                    device,
                    BuildColorTextureDescriptor(colorInfo.Format, width, height), this);

                _colorViews[i] = (AlcoGpuTextureView)device.CreateTextureView(new TextureViewDescriptor(_colorTextures[i]));
            }

            _colorAttachments = AllocColorAttachments(_colorViews, attachmentLayout.ColorInfos);

            if (attachmentLayout.DepthInfo.HasValue)
            {
                DepthAttachmentInfo depthInfo = attachmentLayout.DepthInfo.Value;

                _depthStencilTexture = new AlcoGpuTexture(
                    device,
                    BuildDepthTextureDescriptor(depthInfo.Format, width, height), this);

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
        catch
        {
            try { Destroy(false); }
            catch { /* Preserve the construction failure. */ }
            throw;
        }
    }

    #endregion
}
