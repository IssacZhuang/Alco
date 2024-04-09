using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;
using static Vocore.Graphics.UtilsInterop;

namespace Vocore.Graphics.WebGPU;

internal unsafe class WebGPUFrameBuffer : WebGPUFrameBufferBase
{
    #region Properties
    private readonly uint _width;
    private readonly uint _height;

    private readonly WebGPUTexture[] _colorTextures;
    private readonly WGPUTextureView[] _colorViews;
    private readonly WebGPUTextureViewWrapper[] _colorViewsWrapper;
    private readonly WebGPUTexture? _depthTexture;
    private readonly WGPUTextureView _depthView = WGPUTextureView.Null;
    private readonly WebGPUTextureViewWrapper? _depthViewWrapper;
    private readonly WebGPURenderPass _renderPass;
    private readonly WGPURenderPassDescriptor _descriptor;
    // native memory, need to be manually released
    private readonly WGPURenderPassColorAttachment* _colorAttachments;
    private readonly WGPURenderPassDepthStencilAttachment* _depthAttachment;

    private readonly WGPUTextureFormat[] _colors;
    private readonly WGPUTextureFormat? _depth;

    #endregion

    #region Abstract Implementation
    public override string Name { get; }

    public override GPURenderPass RenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderPass;
    }

    public override IReadOnlyList<GPUTexture> Colors
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    public override GPUTexture? Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthTexture;
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

    public override IReadOnlyList<GPUTextureView> ColorViews
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorViewsWrapper;
    }

    public override GPUTextureView? DepthView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthViewWrapper;
    }

    protected override void Dispose(bool disposing)
    {

        foreach (var texture in _colorTextures)
        {
            texture.Dispose();
        }

        foreach (var view in _colorViews)
        {
            wgpuTextureViewRelease(view);
        }

        _depthTexture?.Dispose();
        
        if(_depthView != WGPUTextureView.Null)
        {
            wgpuTextureViewRelease(_depthView);
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

    public override IReadOnlyList<WGPUTextureFormat> NativeColorFormats
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colors;
    }
    public override WGPUTextureFormat? NativeDepthFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    internal WebGPUFrameBuffer(in FrameBufferDescriptor descriptor)
    {
        WebGPURenderPass renderPass = (WebGPURenderPass)descriptor.RenderPass;
        string name = descriptor.Name;
        uint width = descriptor.Width;
        uint height = descriptor.Height;

        Name = name;
        _renderPass = renderPass;

        _width = width;
        _height = height;

        _colorTextures = new WebGPUTexture[renderPass.Colors.Count];
        _colorViews = new WGPUTextureView[renderPass.Colors.Count];
        _colorViewsWrapper = new WebGPUTextureViewWrapper[renderPass.Colors.Count];
        _descriptor = new WGPURenderPassDescriptor
        {
            colorAttachmentCount = (uint)renderPass.Colors.Count,
        };

        _colorAttachments = Alloc<WGPURenderPassColorAttachment>(renderPass.Colors.Count);
        for (int i = 0; i < renderPass.WebGPUColorInfos.Count; i++)
        {
            WGPUColorAttachmentInfo colorInfo = renderPass.WebGPUColorInfos[i];

            _colorTextures[i] = new WebGPUTexture(
                renderPass.NativeDevice,
                BuildTextureDescriptor(colorInfo.format, width, height),
                $"Color Texture {i}");

            _colorViews[i] = wgpuTextureCreateView(_colorTextures[i].Native, null);
            _colorViewsWrapper[i] = new WebGPUTextureViewWrapper(_colorTextures[i], _colorViews[i]);

            _colorAttachments[i] = new WGPURenderPassColorAttachment
            {
                view = _colorViews[i],
                loadOp = WGPULoadOp.Clear,
                storeOp = WGPUStoreOp.Store,
                clearValue = colorInfo.clearColor,
            };
        }

        if (renderPass.WebGPUDepthInfo.HasValue)
        {
            WGPUDepthAttachmentInfo depthInfo = renderPass.WebGPUDepthInfo.Value;

            _depthTexture = new WebGPUTexture(
                renderPass.NativeDevice,
                BuildTextureDescriptor(depthInfo.format, width, height),
                "depth_texture");

            _depthView = wgpuTextureCreateView(_depthTexture.Native, null);
            _depthViewWrapper = new WebGPUTextureViewWrapper(_depthTexture, _depthView);

            _depthAttachment = Alloc<WGPURenderPassDepthStencilAttachment>(1);

            *_depthAttachment = new WGPURenderPassDepthStencilAttachment
            {
                view = _depthView,
                depthLoadOp = WGPULoadOp.Clear,
                depthStoreOp = WGPUStoreOp.Store,
                depthClearValue = depthInfo.clearDepth,
                stencilLoadOp = WGPULoadOp.Clear,
                stencilStoreOp = WGPUStoreOp.Store,
                stencilClearValue = depthInfo.clearStencil,
            };
        }

        _descriptor.colorAttachments = _colorAttachments;
        _descriptor.depthStencilAttachment = _depthAttachment;

        _colors = new WGPUTextureFormat[renderPass.WebGPUColorInfos.Count];
        for (int i = 0; i < renderPass.WebGPUColorInfos.Count; i++)
        {
            _colors[i] = renderPass.WebGPUColorInfos[i].format;
        }

        if (renderPass.WebGPUDepthInfo.HasValue)
        {
            _depth = renderPass.WebGPUDepthInfo.Value.format;
        }
    }

    private static WGPUTextureDescriptor BuildTextureDescriptor(in WGPUTextureFormat format, uint width, uint height)
    {
        return new WGPUTextureDescriptor
        {
            // the texture could be used as a render target, copied from, or sampled from a shader
            usage = WGPUTextureUsage.RenderAttachment | WGPUTextureUsage.TextureBinding | WGPUTextureUsage.CopySrc,
            dimension = WGPUTextureDimension._2D,
            size = new WGPUExtent3D
            {
                width = width,
                height = height,
                depthOrArrayLayers = 1,
            },
            format = format,
            mipLevelCount = 1,
            sampleCount = 1,
            viewFormatCount = 0,
        };
    }

    #endregion
}