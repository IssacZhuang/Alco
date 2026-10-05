using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

internal abstract class AlcoGpuTextureBase : GPUTexture
{
    public abstract AlcoHandle Native { get; }

    protected AlcoGpuTextureBase(in TextureDescriptor descriptor) : base(descriptor)
    {
    }
}

internal sealed unsafe class AlcoGpuTexture : AlcoGpuTextureBase
{
    #region Properties
    private readonly AlcoHandle _nativeTexture;
    private readonly uint _width;
    private readonly uint _height;
    private readonly uint _depth;
    private readonly uint _mipLevelCount;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

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

    public override uint Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    public override PixelFormat PixelFormat { get; }

    protected override void Dispose(bool disposing)
    {
        if (!_nativeTexture.IsNull && ((AlcoGpuDevice)Device).IsNativeAlive)
        {
            AlcoGpuNative.TextureDestroy(((AlcoGpuDevice)Device).Native, _nativeTexture);
        }
    }

    #endregion

    #region AlcoGpu Implementation
    public override AlcoHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _nativeTexture;
    }

    public override uint MipLevelCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mipLevelCount;
    }

    internal AlcoGpuTexture(AlcoGpuDevice device, in TextureDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        _width = descriptor.Width;
        _height = descriptor.Height;
        _depth = descriptor.DepthOrArrayLayer;
        _mipLevelCount = descriptor.MipLevels;
        PixelFormat = descriptor.Format;

        ReadOnlySpan<byte> name = Name.Utf8Z();
        fixed (byte* ptrName = name)
        {
            AlcoTextureDesc desc = new()
            {
                Dimension = (uint)descriptor.Dimension,
                Format = (uint)descriptor.Format,
                Usage = (uint)descriptor.Usage,
                Width = descriptor.Width,
                Height = descriptor.Height,
                DepthOrArrayLayers = descriptor.DepthOrArrayLayer,
                MipLevelCount = descriptor.MipLevels,
                SampleCount = descriptor.SampleCount,
                Name = ptrName,
            };

            AlcoGpuNative.TextureCreate(device.Native, in desc, out _nativeTexture);
        }
    }

    #endregion
}
