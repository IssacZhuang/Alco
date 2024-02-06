using WebGPU;
using System.Runtime.CompilerServices;

using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

public class WebGPUTextureView : GPUTextureView
{

    #region Properties

    private readonly WGPUTextureView _native;
    private readonly TextureViewDimension _dimension;
    private readonly WebGPUTexture _texture;

    #endregion

    #region Abstract Implementation
    public override GPUTexture Texture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture;
    }

    public override TextureViewDimension Dimension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _dimension;
    }

    public override string Name { get; }

    protected override void Dispose(bool disposing)
    {
        wgpuTextureViewRelease(_native);
    }

    #endregion

    #region WebGPU Implementation

    public WGPUTextureView Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    public unsafe WebGPUTextureView(WGPUDevice device, in TextureViewDescriptor descriptor)
    {
        Name = descriptor.Name;

        _texture = (WebGPUTexture)descriptor.Texture;
        _dimension = descriptor.Dimension;

        fixed (sbyte* ptrName = descriptor.Name.GetUtf8Span())
        {
            WGPUTextureViewDescriptor viewDescriptor = new WGPUTextureViewDescriptor()
            {
                nextInChain = null,
                label = ptrName,
                dimension = UtilsWebGPU.TextureViewDimensionToWebGPU(descriptor.Dimension),
            };
            _native = wgpuTextureCreateView(_texture.Native, &viewDescriptor);
        }
    }

    #endregion


}