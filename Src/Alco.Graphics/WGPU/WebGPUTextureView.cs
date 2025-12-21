using WebGPU;
using System.Runtime.CompilerServices;

using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

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

        ReadOnlySpan<byte> name = descriptor.Name.GetUtf8Span();    
        fixed (byte* ptrName = name)
        {
            WGPUTextureViewDescriptor viewDescriptor = new WGPUTextureViewDescriptor()
            {
                nextInChain = null,
                label = new WGPUStringView(ptrName, name.Length),
                dimension = WebGPUUtility.TextureViewDimensionToWebGPU(descriptor.Dimension),
                baseMipLevel = descriptor.BaseMipLevel,
                mipLevelCount = descriptor.MipLevelCount,
                baseArrayLayer = descriptor.BaseArrayLayer,
                arrayLayerCount = descriptor.ArrayLayerCount,
                aspect = WebGPUUtility.TextureAspectToWebGPU(descriptor.Aspect),
            };
            _native = wgpuTextureCreateView(_texture.Native, &viewDescriptor);
        }
    }

    #endregion


}