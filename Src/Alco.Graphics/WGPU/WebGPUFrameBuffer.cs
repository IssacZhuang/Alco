using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;
using static Alco.Graphics.UtilsInterop;

namespace Alco.Graphics.WebGPU;

internal unsafe sealed class WebGPUFrameBuffer : WebGPUFrameBufferBase
{
    #region Properties
    private readonly uint _width;
    private readonly uint _height;

    private readonly WebGPUTexture[] _colorTextures;
    private readonly WebGPUTextureView[] _colorViews;
    private readonly WebGPUTexture? _depthStencilTexture;
    private readonly WebGPUTextureView? _depthStencilView;
    private readonly WebGPUTextureView? _depthView;
    private readonly WebGPUTextureView? _stencilView;
    private readonly WebGPURenderPass _renderPass;
    private readonly WGPURenderPassDescriptor _descriptor;
    // native memory, need to be manually released
    private readonly WGPURenderPassColorAttachment* _colorAttachments;
    private readonly WGPURenderPassDepthStencilAttachment* _depthAttachment;

    private readonly WGPUTextureFormat[] _colors;
    private readonly WGPUTextureFormat? _depth;

    #endregion

    #region Abstract Implementation

    public override GPURenderPass RenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderPass;
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

    internal WebGPUFrameBuffer(WebGPUDevice device, in FrameBufferDescriptor descriptor): base(descriptor)
    {
        Device = device;
        WebGPURenderPass renderPass = (WebGPURenderPass)descriptor.RenderPass;
        uint width = descriptor.Width;
        uint height = descriptor.Height;

        _renderPass = renderPass;

        _width = width;
        _height = height;

        _colorTextures = new WebGPUTexture[renderPass.Colors.Length];
        _colorViews = new WebGPUTextureView[renderPass.Colors.Length];
        _descriptor = new WGPURenderPassDescriptor
        {
            colorAttachmentCount = (uint)renderPass.Colors.Length,
        };

        _colorAttachments = Alloc<WGPURenderPassColorAttachment>(renderPass.Colors.Length);
        for (int i = 0; i < renderPass.WebGPUColorInfos.Length; i++)
        {
            WGPUColorAttachmentInfo colorInfo = renderPass.WebGPUColorInfos[i];
            _colorTextures[i] = new WebGPUTexture(
                device,
                BuildColorTextureDescriptor(colorInfo.format, width, height)
                );

            _colorViews[i] = (WebGPUTextureView)device.CreateTextureView(new TextureViewDescriptor(_colorTextures[i]));



            _colorAttachments[i] = new WGPURenderPassColorAttachment
            {
                view = _colorViews[i].Native,
                loadOp = WGPULoadOp.Load,
                storeOp = WGPUStoreOp.Store,
                clearValue = colorInfo.clearColor,
                depthSlice = WGPU_DEPTH_SLICE_UNDEFINED,
            };
        }

        if (renderPass.WebGPUDepthInfo.HasValue)
        {
            WGPUDepthAttachmentInfo depthInfo = renderPass.WebGPUDepthInfo.Value;

            _depthStencilTexture = new WebGPUTexture(
                device,
                BuildDepthTextureDescriptor(depthInfo.format, width, height)
                );

            _depthStencilView = (WebGPUTextureView)device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture));
            _depthView = (WebGPUTextureView)device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.DepthOnly));
            if(UtilsPixelFormat.HasStencil(_depthStencilTexture.PixelFormat))
            {
                _stencilView = (WebGPUTextureView)device.CreateTextureView(new TextureViewDescriptor(_depthStencilTexture, aspect: TextureAspect.StencilOnly));
            }

            _depthAttachment = Alloc<WGPURenderPassDepthStencilAttachment>(1);

            *_depthAttachment = new WGPURenderPassDepthStencilAttachment
            {
                view = _depthStencilView.Native,
                depthLoadOp = WGPULoadOp.Load,
                depthStoreOp = WGPUStoreOp.Store,
                depthClearValue = depthInfo.clearDepth,
                stencilLoadOp = WGPULoadOp.Load,
                stencilStoreOp = WGPUStoreOp.Store,
                stencilClearValue = depthInfo.clearStencil,
            };
        }

        _descriptor.colorAttachments = _colorAttachments;
        _descriptor.depthStencilAttachment = _depthAttachment;

        _colors = new WGPUTextureFormat[renderPass.WebGPUColorInfos.Length];
        for (int i = 0; i < renderPass.WebGPUColorInfos.Length; i++)
        {
            _colors[i] = renderPass.WebGPUColorInfos[i].format;
        }

        if (renderPass.WebGPUDepthInfo.HasValue)
        {
            _depth = renderPass.WebGPUDepthInfo.Value.format;
        }
    }

    #endregion
}