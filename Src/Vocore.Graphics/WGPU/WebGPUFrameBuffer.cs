using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUFrameBuffer : WebGPUFrameBufferBase
{
    #region Properties
    private readonly uint _width;
    private readonly uint _height;
    private readonly WebGPUTexture[] _colorTextures;
    private readonly WebGPUTexture? _depthTexture;
    private readonly WebGPURenderPass _renderPass;

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
    }

    #endregion

    #region WebGPU Implementation

    public override IReadOnlyList<WebGPUTextureBase> WebGPUColorTextures
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorTextures;
    }

    public override WebGPUTextureBase? WebGPUDepthTexture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depthTexture;
    }

    internal WebGPUFrameBuffer(WebGPURenderPass renderPass, uint width, uint height, string name)
    {
        Name = name;
        _renderPass = renderPass;

        _width = width;
        _height = height;

        _colorTextures = new WebGPUTexture[renderPass.Colors.Count];
        for (int i = 0; i < renderPass.Colors.Count; i++)
        {
            _colorTextures[i] = new WebGPUTexture(
                renderPass.NativeDevice,
                BuildTextureDescriptor(renderPass.WebGPUColorInfos[i].format, width, height),
                $"Color Texture {i}");
        }

        if (renderPass.WebGPUDepthInfo.HasValue)
        {
            _depthTexture = new WebGPUTexture(
                renderPass.NativeDevice,
                BuildTextureDescriptor(renderPass.WebGPUDepthInfo.Value.format, width, height),
                "Depth Texture");
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