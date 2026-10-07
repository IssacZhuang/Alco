using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Base class for alco-gpu textures, exposing the native texture handle; implemented by owned textures and the per-frame surface texture.</summary>
internal abstract class AlcoGpuTextureBase : GPUTexture
{
    /// <summary>Gets the native texture handle, borrowed for native calls; ownership is defined by the concrete type.</summary>
    public abstract AlcoGPU.TextureHandle Native { get; }

    protected AlcoGpuTextureBase(in TextureDescriptor descriptor) : base(descriptor)
    {
    }
}

/// <summary>A texture that owns its native handle; attachment textures retain the frame buffer that created them.</summary>
internal sealed unsafe class AlcoGpuTexture : AlcoGpuTextureBase
{
    #region Properties
    private AlcoGPU.TextureHandle _nativeTexture;
    // A borrowed owned attachment must retain its parent through managed native calls.
    private readonly BaseGPUObject? _owner;
    private readonly uint _width;
    private readonly uint _height;
    private readonly uint _depth;
    private readonly uint _mipLevelCount;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    /// <inheritdoc />
    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _width;
    }

    /// <inheritdoc />
    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _height;
    }

    /// <inheritdoc />
    public override uint Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _depth;
    }

    /// <inheritdoc />
    public override PixelFormat PixelFormat { get; }

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoGPU.TextureHandle handle = _nativeTexture;
            _nativeTexture = AlcoGPU.TextureHandle.Null;
            if (!handle.IsNull)
            {
                try
                {
                    AlcoGpuNative.TextureDestroy(handle);
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
    /// <summary>Gets the native texture handle; owned by this object and destroyed on dispose.</summary>
    public override AlcoGPU.TextureHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _nativeTexture;
    }

    /// <inheritdoc />
    public override uint MipLevelCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mipLevelCount;
    }

    internal AlcoGpuTexture(AlcoGpuDevice device, in TextureDescriptor descriptor, BaseGPUObject? owner = null) : base(descriptor)
    {
        try
        {
            _owner = owner;
            Device = device;

            _width = descriptor.Width;
            _height = descriptor.Height;
            _depth = descriptor.DepthOrArrayLayer;
            _mipLevelCount = descriptor.MipLevels;
            PixelFormat = descriptor.Format;

            ReadOnlySpan<byte> name = Name.Utf8Z();
            fixed (byte* ptrName = name)
            {
                AlcoGPU.TextureDesc desc = new()
                {
                    Dimension = descriptor.Dimension,
                    Format = descriptor.Format,
                    Usage = descriptor.Usage,
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
