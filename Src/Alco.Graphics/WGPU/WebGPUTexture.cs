using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed class WebGPUTexture : WebGPUTextureBase
{
    #region Properties
    private readonly WGPUTexture _nativeTexture;
    private readonly WGPUExtent3D _size;
    private readonly uint _mipLevelCount;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _size.width;
    }

    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _size.height;
    }

    public override uint Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _size.depthOrArrayLayers;
    }

    public override PixelFormat PixelFormat { get; }

    protected override void Dispose(bool disposing)
    {
        wgpuTextureDestroy(_nativeTexture);
        wgpuTextureRelease(_nativeTexture);
    }

    #endregion


    #region WebGPU Implementation
    public override WGPUTexture Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _nativeTexture;
    }

    public override uint MipLevelCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mipLevelCount;
    }

    internal unsafe WebGPUTexture(WebGPUDevice device, in TextureDescriptor descriptor): base(descriptor)
    {
        Device = device;
        WGPUDevice nativeDevice = device.Native;

        _mipLevelCount = descriptor.MipLevels;
        _size = new WGPUExtent3D
        {
            width = descriptor.Width,
            height = descriptor.Height,
            depthOrArrayLayers = descriptor.DepthOrArrayLayer,
        };

        WGPUTextureDescriptor textureDescriptor = new WGPUTextureDescriptor
        {
            usage = WebGPUUtility.ConvertTextureUsage(descriptor.Usage),
            dimension = WebGPUUtility.TextureDimensionToWebGPU(descriptor.Dimension),
            size = _size,
            format = WebGPUUtility.PixelFormatToWebGPU(descriptor.Format),
            mipLevelCount = descriptor.MipLevels,
            sampleCount = descriptor.SampleCount,
        };

        _nativeTexture = wgpuDeviceCreateTexture(nativeDevice, &textureDescriptor);

        PixelFormat = descriptor.Format;
    }

    #endregion
}