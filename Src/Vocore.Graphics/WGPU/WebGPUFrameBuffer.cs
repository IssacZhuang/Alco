using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUFrameBuffer : GPUFrameBuffer
{
    #region Properties
    private readonly uint _width;
    private readonly uint _height;
    private readonly bool _isColorTextureOwner;
    private readonly WebGPUTexture[] _colorTextures;
    private readonly WebGPUTexture? _depthTexture;

    #endregion

    #region Abstract Implementation
    public override string Name { get; }
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
        if (_isColorTextureOwner)
        {
            foreach (var texture in _colorTextures)
            {
                texture.Dispose();
            }
        }

        _depthTexture?.Dispose();
    }

    #endregion

    #region WebGPU Implementation

    internal WebGPUFrameBuffer(WebGPURenderPass renderPass, uint width, uint height, string name)
    {
        Name = name;

        _width = width;
        _height = height;
        _isColorTextureOwner = true;

        _colorTextures = new WebGPUTexture[renderPass.Colors.Count];
        for (int i = 0; i < renderPass.Colors.Count; i++)
        {
            _colorTextures[i] = new WebGPUTexture(
                renderPass.NativeDevice,
                BuildTextureDescriptor(UtilsWebGPU.PixelFormatToWebGPU(renderPass.Colors[i].Format), width, height),
                $"Color Texture {i}");
        }

        if (renderPass.Depth.HasValue)
        {
            _depthTexture = new WebGPUTexture(
                renderPass.NativeDevice,
                BuildTextureDescriptor(UtilsWebGPU.PixelFormatToWebGPU(renderPass.Depth.Value.Format), width, height),
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