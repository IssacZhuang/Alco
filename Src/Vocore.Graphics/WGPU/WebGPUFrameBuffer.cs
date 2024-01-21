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
    private readonly WebGPUTexture? _depthTexture;
    private readonly WebGPURenderPass _renderPass;
    private readonly WGPURenderPassDescriptor _descriptor;
    // native memory, need to be manually released
    private readonly WGPURenderPassColorAttachment* _colorAttachments;
    private readonly WGPURenderPassDepthStencilAttachment* _depthAttachment;

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

    protected override void Dispose(bool disposing)
    {

        foreach (var texture in _colorTextures)
        {
            texture.Dispose();
        }

        _depthTexture?.Dispose();

        Free(_colorAttachments);
        Free(_depthAttachment);
    }

    #endregion

    #region WebGPU Implementation

    public override WGPURenderPassDescriptor Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _descriptor;
    }

    internal WebGPUFrameBuffer(WebGPURenderPass renderPass, uint width, uint height, string name)
    {
        Name = name;
        _renderPass = renderPass;

        _width = width;
        _height = height;

        _colorTextures = new WebGPUTexture[renderPass.Colors.Count];
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

            // TODO: fiil the texure view
            _colorAttachments[i] = new WGPURenderPassColorAttachment
            {
                view = _colorTextures[i].DefaultView,
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
                "Depth Texture");
            _depthAttachment = Alloc<WGPURenderPassDepthStencilAttachment>(1);

            // TODO: fiil the texure view
            *_depthAttachment = new WGPURenderPassDepthStencilAttachment
            {
                view = _depthTexture.DefaultView,
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