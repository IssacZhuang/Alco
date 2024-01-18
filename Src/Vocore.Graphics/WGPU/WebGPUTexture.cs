using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUTexture : GPUTexture
{
    #region Properties
    private readonly WGPUDevice _nativeDevice;
    private readonly WGPUTexture _nativeTexture;
    private readonly WGPUExtent3D _size;

    #endregion

    #region Abstract Implementation
    public override string Name { get; }
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

    protected override void Dispose(bool disposing)
    {
        wgpuTextureRelease(_nativeTexture);
    }

    #endregion


    #region WebGPU Implementation
    internal WGPUTexture Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _nativeTexture;
    }

    internal unsafe WebGPUTexture(WGPUDevice nativeDevice, in WGPUTextureDescriptor descriptor, string name)
    {
        Name = name;
        WGPUTextureDescriptor textureDescriptor = descriptor;
        _nativeDevice = nativeDevice;
        _size = descriptor.size;
        _nativeTexture = wgpuDeviceCreateTexture(nativeDevice, &textureDescriptor);
    }

    internal unsafe WebGPUTexture(WGPUDevice nativeDevice, in TextureDescriptor descriptor)
    {
        Name = descriptor.Name;
        _nativeDevice = nativeDevice;

        _size = new WGPUExtent3D
        {
            width = descriptor.Width,
            height = descriptor.Height,
            depthOrArrayLayers = descriptor.DepthOrArrayLayer,
        };

        WGPUTextureDescriptor textureDescriptor = new WGPUTextureDescriptor
        {
            usage = UtilsWebGPU.ConvertTextureUsage(descriptor.Usage),
            dimension = UtilsWebGPU.TextureDimensionToWebGPU(descriptor.Dimension),
            size = _size,
            format = UtilsWebGPU.PixelFormatToWebGPU(descriptor.Format),
            mipLevelCount = descriptor.MipLevels,
            sampleCount = descriptor.SampleCount,
        };

        _nativeTexture = wgpuDeviceCreateTexture(nativeDevice, &textureDescriptor);
    }

    public unsafe WGPUTextureView CreateView(in WGPUTextureViewDescriptor descriptor)
    {
        fixed (WGPUTextureViewDescriptor* descriptorPtr = &descriptor)
        {
            return wgpuTextureCreateView(_nativeTexture, descriptorPtr);
        }
    }

    #endregion
}