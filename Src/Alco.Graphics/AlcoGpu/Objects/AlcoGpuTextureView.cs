using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

internal abstract class AlcoGpuTextureViewBase : GPUTextureView
{
    public abstract AlcoHandle Native { get; }

    protected AlcoGpuTextureViewBase(in TextureViewDescriptor descriptor) : base(descriptor)
    {
    }

    protected AlcoGpuTextureViewBase(string name) : base(name)
    {
    }
}

internal sealed unsafe class AlcoGpuTextureView : AlcoGpuTextureViewBase
{
    #region Properties
    private readonly AlcoHandle _native;
    private readonly AlcoGpuTextureBase _texture;

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
        if (!_native.IsNull)
        {
            AlcoGpuNative.TextureViewDestroy(((AlcoGpuDevice)Device).Native, _native);
        }
    }

    #endregion

    #region AlcoGpu Implementation
    public override AlcoHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    internal AlcoGpuTextureView(AlcoGpuDevice device, in TextureViewDescriptor descriptor) : base(descriptor)
    {
        Device = device;
        _texture = (AlcoGpuTextureBase)descriptor.Texture;

        ReadOnlySpan<byte> name = Name.Utf8Z();
        fixed (byte* ptrName = name)
        {
            AlcoTextureViewDesc desc = new()
            {
                Dimension = (uint)descriptor.Dimension,
                BaseMipLevel = descriptor.BaseMipLevel,
                MipLevelCount = descriptor.MipLevelCount,
                BaseArrayLayer = descriptor.BaseArrayLayer,
                ArrayLayerCount = descriptor.ArrayLayerCount,
                Aspect = (uint)descriptor.Aspect,
                // 0 (Undefined) inherits the texture format natively.
                Format = 0,
                Name = ptrName,
            };

            AlcoGpuNative.TextureCreateView(device.Native, _texture.Native, &desc, out _native);
        }
    }

    #endregion
}
