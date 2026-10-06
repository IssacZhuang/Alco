using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Describes AlcoGpuTextureViewBase.</summary>
internal abstract class AlcoGpuTextureViewBase : GPUTextureView
{
    /// <summary>Gets the borrowed native pointer; ownership stays with this object.</summary>
    public abstract AlcoGpuAbi.TextureViewHandle Native { get; }

    protected AlcoGpuTextureViewBase(in TextureViewDescriptor descriptor) : base(descriptor)
    {
    }

    protected AlcoGpuTextureViewBase(string name) : base(name)
    {
    }
}

/// <summary>Describes AlcoGpuTextureView.</summary>
internal sealed unsafe class AlcoGpuTextureView : AlcoGpuTextureViewBase
{
    #region Properties
    private AlcoGpuAbi.TextureViewHandle _native;
    private readonly AlcoGpuTextureBase _texture;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    /// <inheritdoc />
    public override GPUTexture Texture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoGpuAbi.TextureViewHandle handle = _native;
            _native = AlcoGpuAbi.TextureViewHandle.Null;
            if (!handle.IsNull)
            {
                try
                {
                    AlcoGpuNative.TextureViewDestroy(handle);
                }
                finally
                {
                }
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    #endregion

    #region AlcoGpu Implementation
    /// <inheritdoc />
    public override AlcoGpuAbi.TextureViewHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    internal AlcoGpuTextureView(AlcoGpuDevice device, in TextureViewDescriptor descriptor) : base(descriptor)
    {
        try
        {
            Device = device;
            _texture = (AlcoGpuTextureBase)descriptor.Texture;

            ReadOnlySpan<byte> name = Name.Utf8Z();
            fixed (byte* ptrName = name)
            {
                AlcoGpuAbi.TextureViewDesc desc = new()
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

                AlcoGpuNative.TextureCreateView(_texture.Native, &desc, out _native);
            }

        }
        catch
        {
            try { Destroy(false); }
            catch { /* Preserve the construction failure. */ }
            throw;
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(device);
        }
    }

    #endregion
}
