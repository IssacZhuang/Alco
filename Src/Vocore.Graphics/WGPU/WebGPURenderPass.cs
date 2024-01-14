
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPURenderPass : GPURenderPass
{
    private RenderPassDescriptor _abstractDescriptor;
    private WGPUDevice _nativeDevice;
    private WGPURenderPassDescriptor _descriptor;

    private WebGPUTexture[] _colorTextures;
    private WGPUTextureView[] _colorViews;
    private WebGPUTexture? _depthTexture;
    private WGPUTextureView _depthView;

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
        get => _abstractDescriptor.Width;
    }

    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _abstractDescriptor.Height;
    }

    public override string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _abstractDescriptor.Name ?? string.Empty;
    }

    public unsafe WebGPURenderPass(WGPUDevice nativeDevice, in RenderPassDescriptor descriptor)
    {
        int colorCount = descriptor.Colors.Length;
        _colorTextures = new WebGPUTexture[colorCount];
        _colorViews = new WGPUTextureView[colorCount];

        _abstractDescriptor = descriptor;
        _nativeDevice = nativeDevice;

        CreateTextures();
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseTextures();
    }

    private void ReleaseTextures()
    {
        for (int i = 0; i < _colorViews.Length; i++)
        {
            _colorTextures[i].Dispose();
            wgpuTextureViewRelease(_colorViews[i]);
        }

        if (_depthTexture != null)
        {
            _depthTexture.Dispose();
            wgpuTextureViewRelease(_depthView);
        }
    }

    private unsafe void CreateTextures()
    {
        int colorCount = _abstractDescriptor.Colors.Length;
        RenderPassDescriptor descriptor = _abstractDescriptor;


        for (int i = 0; i < colorCount; i++)
        {
            _colorTextures[i] = new WebGPUTexture(_nativeDevice, BuildTextureDescriptor(descriptor.Colors[i].Format, descriptor.Width, descriptor.Height));
            _colorViews[i] = wgpuTextureCreateView(_colorTextures[i].NativeTexture, null);
        }

        _depthView = WGPUTextureView.Null;
        if (descriptor.Depth.HasValue)
        {
            _depthTexture = new WebGPUTexture(_nativeDevice, BuildTextureDescriptor(descriptor.Depth.Value.Format, descriptor.Width, descriptor.Height));
            _depthView = wgpuTextureCreateView(_depthTexture.NativeTexture, null);
        }

        WGPURenderPassColorAttachment* colorAttachments = stackalloc WGPURenderPassColorAttachment[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            colorAttachments[i] = new WGPURenderPassColorAttachment
            {
                view = _colorViews[i],
                resolveTarget = WGPUTextureView.Null,
                loadOp = WGPULoadOp.Clear,
                storeOp = WGPUStoreOp.Store,
                clearValue = new WGPUColor
                {
                    r = 0,
                    g = 0,
                    b = 0,
                    a = 0,
                },
            };
        }

        _descriptor = new WGPURenderPassDescriptor
        {
            colorAttachmentCount = (uint)colorCount,
            colorAttachments = colorAttachments,
            depthStencilAttachment = null,
        };

        if (descriptor.Depth.HasValue)
        {
            WGPURenderPassDepthStencilAttachment depthStencilAttachment = new WGPURenderPassDepthStencilAttachment
            {
                view = _depthView,
                depthLoadOp = WGPULoadOp.Clear,
                depthStoreOp = WGPUStoreOp.Store,
                depthClearValue = descriptor.Depth.Value.ClearDepth,
                stencilLoadOp = WGPULoadOp.Clear,
                stencilStoreOp = WGPUStoreOp.Store,
                stencilClearValue = descriptor.Depth.Value.ClearStencil,
            };
            _descriptor.depthStencilAttachment = &depthStencilAttachment;
        }
    }

    public override void Resize(uint width, uint height)
    {
        ReleaseTextures();
        _abstractDescriptor.Width = width;
        _abstractDescriptor.Height = height;
        CreateTextures();
    }

    private TextureDescriptor BuildTextureDescriptor(in PixelFormat format, uint width, uint height)
    {
        return new TextureDescriptor
        {
            // the texture could be used as a render target, copied from, or sampled from a shader
            Usage = TextureUsage.RenderAttachment | TextureUsage.Read | TextureUsage.TextureBinding,
            Dimension = TextureDimension.Texture2D,
            Width = width,
            Height = height,
            DepthOrArrayLayer = 1,
            Format = format,
            MipLevels = 1,
            SampleCount = 1,
        };
    }
}