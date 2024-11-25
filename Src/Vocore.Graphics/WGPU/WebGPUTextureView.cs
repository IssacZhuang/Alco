using WebGPU;
using System.Runtime.CompilerServices;

using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal sealed class WebGPUTextureView : WebGPUTextureViewBase
{

    #region Properties

    private readonly WGPUTextureView _native;
    private readonly WebGPUTextureBase _texture;

    #endregion

    #region Abstract Implementation

    protected override GPUDevice Device { get; }
    public override GPUTexture Texture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture;
    }

    protected override void Dispose(bool disposing)
    {
        wgpuTextureViewRelease(_native);
    }

    #endregion

    #region WebGPU Implementation

    public override WGPUTextureView Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    

    public unsafe WebGPUTextureView(WebGPUDevice device, in TextureViewDescriptor descriptor): base(descriptor)    
    {
        Device = device;

        _texture = (WebGPUTextureBase)descriptor.Texture;

        fixed (byte* ptrName = descriptor.Name.GetUtf8Span())
        {
            WGPUTextureViewDescriptor viewDescriptor = new WGPUTextureViewDescriptor()
            {
                nextInChain = null,
                label = ptrName,
                dimension = UtilsWebGPU.TextureViewDimensionToWebGPU(descriptor.Dimension),
                baseMipLevel = descriptor.BaseMipLevel,
                mipLevelCount = descriptor.MipLevelCount,
                baseArrayLayer = descriptor.BaseArrayLayer,
                arrayLayerCount = descriptor.ArrayLayerCount,
                aspect = WGPUTextureAspect.All,
            };
            _native = wgpuTextureCreateView(_texture.Native, &viewDescriptor);
        }
    }

    #endregion


}