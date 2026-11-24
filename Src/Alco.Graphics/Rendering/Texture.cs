// Example-level bindless texture handle: a placed GPUTexture with sampled and per-mip storage descriptor indices.

using System.Diagnostics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>A placed texture with bindless descriptor slots and an optional owned render view.</summary>
public sealed class Texture : RenderingResource
{
    /// <summary>The sentinel index indicating that a descriptor role is absent.</summary>
    public const uint NoIndex = uint.MaxValue;

    private readonly TextureDesc _desc;
    private readonly Lock _viewLock = new();
    private PlacedTexture _allocation;
    private GPURenderView? _renderView;
    private List<(uint Mip, uint Slice, GPURenderView View)>? _extraRenderViews;
    private uint _sampledIndex;
    private uint _storageIndex;
    private uint[]? StorageMipIndices;
    internal List<(Format Format, uint Slot)>? AlternateSampledViews;
    internal readonly InitializationState Initialization;

    internal sealed class InitializationState
    {
        internal readonly GPUTexture Texture;
        internal int Index = -1;
        internal TimelinePoint Completion;

        internal InitializationState(GPUTexture texture)
        {
            Texture = texture;
        }
    }

    internal Texture(RenderingSystem system, in TextureDesc desc, PlacedTexture allocation, uint sampledIndex, uint storageIndex,
        uint[]? storageMipIndices) : base(system)
    {
        Debug.Assert(allocation.Texture != null);
        _desc = desc;
        _allocation = allocation;
        _sampledIndex = sampledIndex;
        _storageIndex = storageIndex;
        StorageMipIndices = storageMipIndices;
        Initialization = new InitializationState(allocation.Texture);
    }

    internal RenderingSystem System => _system;

    /// <summary>Gets texel-block geometry and aspect flags for <paramref name="format"/>.</summary>
    /// <param name="format">The texture format.</param>
    /// <returns>Block dimensions, bytes per block, and depth/stencil flags.</returns>
    public static TextureFormatInfo GetFormatInfo(Format format) => Gpu.GetTextureFormatInfo(format);

    /// <summary>The texture creation description.</summary>
    public TextureDesc Desc => _desc;

    internal GPUTexture LowLevelTexture
    {
        get
        {
            Debug.Assert(!IsDisposed && _allocation.Texture != null);
            return _allocation.Texture!;
        }
    }

    /// <summary>The sampled descriptor heap index, or <see cref="NoIndex"/> when the texture is not sampled.</summary>
    public uint SampledIndex
    {
        get
        {
            Debug.Assert(!IsDisposed);
            return _sampledIndex;
        }
    }

    /// <summary>Gets a sampled descriptor heap index interpreted with <paramref name="viewFormat"/>.</summary>
    /// <param name="viewFormat">The view format, or <see cref="Format.Undefined"/> for the texture's native format.</param>
    /// <returns>The sampled descriptor heap index.</returns>
    public uint GetSampledIndex(Format viewFormat)
    {
        Debug.Assert(!IsDisposed);
        if (viewFormat == Format.Undefined || viewFormat == _desc.Format)
        {
            if (_sampledIndex == NoIndex)
                throw new InvalidOperationException("Texture must be created with TextureUsage.Sampled.");
            return _sampledIndex;
        }
        return _system.GetOrCreateAlternateSampledIndex(this, viewFormat);
    }

    /// <summary>The mip-zero storage descriptor heap index, or <see cref="NoIndex"/> when storage is disabled.</summary>
    public uint StorageIndex
    {
        get
        {
            Debug.Assert(!IsDisposed);
            return _storageIndex;
        }
    }

    /// <summary>Gets the storage descriptor heap index for one mip level.</summary>
    /// <param name="mipLevel">The zero-based mip level, less than <see cref="MipLevels"/>.</param>
    /// <returns>The descriptor heap index, or <see cref="NoIndex"/> when storage is disabled.</returns>
    public uint GetStorageIndex(uint mipLevel)
    {
        Debug.Assert(!IsDisposed && mipLevel < _desc.MipLevels);
        return mipLevel == 0 || StorageMipIndices == null ? _storageIndex : StorageMipIndices[mipLevel - 1];
    }

    /// <summary>The sampled descriptor handle, or <see cref="GPUTextureId.Invalid"/> when the texture is not sampled.</summary>
    public GPUTextureId SampledId => new(SampledIndex);

    /// <summary>Gets a sampled descriptor handle interpreted with <paramref name="viewFormat"/>.</summary>
    /// <param name="viewFormat">The view format, or <see cref="Format.Undefined"/> for the texture's native format.</param>
    /// <returns>The sampled descriptor handle.</returns>
    public GPUTextureId GetSampledId(Format viewFormat) => new(GetSampledIndex(viewFormat));

    /// <summary>The mip-zero storage descriptor handle, or <see cref="GPUTextureId.Invalid"/> when storage is disabled.</summary>
    public GPUTextureId StorageId => new(StorageIndex);

    /// <summary>Gets the storage descriptor handle for one mip level.</summary>
    /// <param name="mipLevel">The zero-based mip level, less than <see cref="MipLevels"/>.</param>
    /// <returns>The storage descriptor handle, or <see cref="GPUTextureId.Invalid"/> when storage is disabled.</returns>
    public GPUTextureId GetStorageId(uint mipLevel) => new(GetStorageIndex(mipLevel));

    /// <summary>The texture extent in texels.</summary>
    public Extent3D Extent => _desc.Extent;

    /// <summary>The texture format.</summary>
    public Format Format => _desc.Format;

    /// <summary>The number of mip levels.</summary>
    public uint MipLevels => _desc.MipLevels;

    /// <summary>The number of array layers; cube faces are individual layers.</summary>
    public uint LayerCount => _desc.LayerCount;

    /// <summary>Converts the texture to its sampled descriptor heap index.</summary>
    /// <param name="texture">The texture; it must be alive and have a sampled descriptor slot.</param>
    /// <returns>The sampled descriptor heap index.</returns>
    public static implicit operator uint(Texture texture)
    {
        Debug.Assert(texture != null);
        Debug.Assert(texture.SampledIndex != NoIndex, "The texture has no sampled descriptor slot; use StorageIndex.");
        return texture.SampledIndex;
    }

    internal GPURenderView LowLevelRenderView
    {
        get
        {
            Debug.Assert(!IsDisposed);
            lock (_viewLock)
            {
                _renderView ??= LowLevelTexture.CreateRenderView(new RenderViewDesc());
                return _renderView;
            }
        }
    }

    /// <summary>Gets a render target referencing mip 0, array slice 0 of this texture.</summary>
    /// <returns>The mip-zero, slice-zero render target.</returns>
    public RenderTarget GetRenderTarget()
    {
        Debug.Assert(!IsDisposed);
        return new RenderTarget(LowLevelRenderView, this, new Extent2D(_desc.Extent.X, _desc.Extent.Y));
    }

    /// <summary>Gets a render target referencing a specific mip level and array slice of this texture.</summary>
    /// <param name="mipLevel">The zero-based mip level.</param>
    /// <param name="arraySlice">The zero-based physical array slice or cubemap face.</param>
    /// <returns>The render target view.</returns>
    public RenderTarget GetRenderTarget(uint mipLevel, uint arraySlice = 0)
    {
        Debug.Assert(!IsDisposed);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipLevel, _desc.MipLevels);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(arraySlice, _desc.LayerCount);
        if (mipLevel == 0 && arraySlice == 0)
            return GetRenderTarget();

        GPURenderView view;
        lock (_viewLock)
        {
            GPURenderView? cached = null;
            if (_extraRenderViews != null)
            {
                for (int index = 0; index < _extraRenderViews.Count; ++index)
                {
                    if (_extraRenderViews[index].Mip == mipLevel && _extraRenderViews[index].Slice == arraySlice)
                    {
                        cached = _extraRenderViews[index].View;
                        break;
                    }
                }
            }
            if (cached == null)
            {
                cached = LowLevelTexture.CreateRenderView(new RenderViewDesc { MipLevel = mipLevel, Slice = arraySlice });
                _extraRenderViews ??= [];
                _extraRenderViews.Add((mipLevel, arraySlice, cached));
            }
            view = cached;
        }
        uint width = Math.Max(1u, _desc.Extent.X >> (int)mipLevel);
        uint height = Math.Max(1u, _desc.Extent.Y >> (int)mipLevel);
        return new RenderTarget(view, this, new Extent2D(width, height));
    }

    /// <summary>Releases the render view and retires texture storage after its pending initialization completes.</summary>
    protected override void Release()
    {
        lock (_viewLock)
        {
            _renderView?.Destroy();
            _renderView = null;
            if (_extraRenderViews != null)
            {
                for (int index = 0; index < _extraRenderViews.Count; ++index)
                    _extraRenderViews[index].View.Destroy();
                _extraRenderViews = null;
            }
        }
        uint[]? alternateSlots = null;
        if (AlternateSampledViews is { Count: > 0 } views)
        {
            alternateSlots = new uint[views.Count];
            for (int index = 0; index < views.Count; ++index)
                alternateSlots[index] = views[index].Slot;
            AlternateSampledViews = null;
        }
        _system.ReleaseTexture(ref _allocation, _sampledIndex, _storageIndex, StorageMipIndices, alternateSlots, Initialization);
        _sampledIndex = NoIndex;
        _storageIndex = NoIndex;
        StorageMipIndices = null;
    }
}
