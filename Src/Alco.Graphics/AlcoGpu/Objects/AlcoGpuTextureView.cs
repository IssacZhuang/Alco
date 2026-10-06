using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Base class for alco-gpu texture views, exposing the native view handle.</summary>
internal abstract class AlcoGpuTextureViewBase : GPUTextureView
{
    /// <summary>Gets the native view handle, borrowed for native calls; ownership is defined by the concrete type.</summary>
    public abstract AlcoGPU.TextureViewHandle Native { get; }

    protected AlcoGpuTextureViewBase(in TextureViewDescriptor descriptor) : base(descriptor)
    {
    }

    protected AlcoGpuTextureViewBase(string name) : base(name)
    {
    }
}

/// <summary>A texture view over a selected mip/array/aspect range of a texture; owns its native view handle.</summary>
internal sealed unsafe class AlcoGpuTextureView : AlcoGpuTextureViewBase
{
    #region Properties
    private AlcoGPU.TextureViewHandle _native;
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
            AlcoGPU.TextureViewHandle handle = _native;
            _native = AlcoGPU.TextureViewHandle.Null;
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
    /// <summary>Gets the native view handle; owned by this object and destroyed on dispose.</summary>
    public override AlcoGPU.TextureViewHandle Native
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
                AlcoGPU.TextureViewDesc desc = new()
                {
                    Dimension = descriptor.Dimension,
                    BaseMipLevel = descriptor.BaseMipLevel,
                    MipLevelCount = descriptor.MipLevelCount,
                    BaseArrayLayer = descriptor.BaseArrayLayer,
                    ArrayLayerCount = descriptor.ArrayLayerCount,
                    Aspect = descriptor.Aspect,
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
