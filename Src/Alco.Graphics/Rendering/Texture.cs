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
    private PlacedTexture _allocation;
    private GPURenderView? _renderView;
    private uint _sampledIndex;
    private uint _storageIndex;
    private uint[]? StorageMipIndices;
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

    /// <summary>The texture creation description.</summary>
    public TextureDesc Desc => _desc;

    /// <summary>The placed texture; non-context submissions must follow initialization and synchronize its uses explicitly.</summary>
    public GPUTexture NativeTexture
    {
        get
        {
            Debug.Assert(!IsDestroyed && _allocation.Texture != null);
            return _allocation.Texture!;
        }
    }

    /// <summary>The sampled descriptor heap index, or <see cref="NoIndex"/> when the texture is not sampled.</summary>
    public uint SampledIndex
    {
        get
        {
            Debug.Assert(!IsDestroyed);
            return _sampledIndex;
        }
    }

    /// <summary>The mip-zero storage descriptor heap index, or <see cref="NoIndex"/> when storage is disabled.</summary>
    public uint StorageIndex
    {
        get
        {
            Debug.Assert(!IsDestroyed);
            return _storageIndex;
        }
    }

    /// <summary>Gets the storage descriptor heap index for one mip level.</summary>
    /// <param name="mipLevel">The zero-based mip level, less than <see cref="MipLevels"/>.</param>
    /// <returns>The descriptor heap index, or <see cref="NoIndex"/> when storage is disabled.</returns>
    public uint GetStorageIndex(uint mipLevel)
    {
        Debug.Assert(!IsDestroyed && mipLevel < _desc.MipLevels);
        return mipLevel == 0 || StorageMipIndices == null ? _storageIndex : StorageMipIndices[mipLevel - 1];
    }

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

    /// <summary>The owned mip-zero, slice-zero attachment view; synchronize direct attachment commands and uploads explicitly.</summary>
    public GPURenderView RenderView
    {
        get
        {
            Debug.Assert(!IsDestroyed);
            _renderView ??= NativeTexture.CreateRenderView(new RenderViewDesc());
            return _renderView;
        }
    }

    /// <summary>Releases the render view and retires texture storage after its pending initialization completes.</summary>
    protected override void Release()
    {
        _renderView?.Destroy();
        _renderView = null;
        _system.ReleaseTexture(ref _allocation, _sampledIndex, _storageIndex, StorageMipIndices, Initialization);
        _sampledIndex = NoIndex;
        _storageIndex = NoIndex;
        StorageMipIndices = null;
    }
}
