using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// High-level encapsulation of a 3D <see cref="GPUTexture"/> with full-chain and per-mip resource groups.
/// </summary>
public sealed class Texture3D : Texture
{
    private GPUResourceGroup? _resourcesRead;

    private GPUBindGroup? _bindGroupStorage;
    private readonly GPUResourceGroup?[] _resourcesStorage;
    private readonly GPUResourceGroup?[] _resourcesReadMip;
    private readonly GPUTextureView?[] _mipViews;

    /// <summary>
    /// The number of mip levels of the texture.
    /// </summary>
    public uint MipLevels { get; }

    /// <inheritdoc />
    public override GPUResourceGroup EntryReadonly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_resourcesRead == null)
            {
                _resourcesRead = CreateResourceGroupRead();
            }

            return _resourcesRead;
        }
    }

    /// <inheritdoc />
    public override GPUResourceGroup EntryWriteable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return EntryStorage(0);
        }
    }

    internal Texture3D(
        GPUDevice device,
        GPUTexture texture,
        GPUTextureView textureView
        ) :
        base(device, texture, textureView)
    {
        MipLevels = texture.MipLevelCount;
        _resourcesStorage = new GPUResourceGroup?[MipLevels];
        _resourcesReadMip = new GPUResourceGroup?[MipLevels];
        _mipViews = new GPUTextureView?[MipLevels];
    }

    /// <summary>
    /// The read-only resource group bound to the single-mip view of the given mip level.
    /// Use it instead of <see cref="EntryReadonly"/> when the same dispatch writes another mip.
    /// </summary>
    /// <param name="mipLevel">The mip level to read (0 = full resolution).</param>
    /// <returns>The read-only resource group for the mip level.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mip level is out of range.</exception>
    public GPUResourceGroup EntryReadonlyMip(uint mipLevel)
    {
        CheckMipLevel(mipLevel);

        if (_resourcesReadMip[mipLevel] == null)
        {
            ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
                _device.BindGroupTexture3DRead,
                new ResourceBindingEntry[]{
                    new ResourceBindingEntry(0, GetMipView(mipLevel)),
                }
            );

            _resourcesReadMip[mipLevel] = _device.CreateResourceGroup(descriptor);
        }

        return _resourcesReadMip[mipLevel]!;
    }

    /// <summary>
    /// The storage resource group bound to the single-mip view of the given mip
    /// level, for compute passes that write that mip.
    /// </summary>
    /// <param name="mipLevel">The mip level to write (0 = full resolution).</param>
    /// <returns>The storage resource group for the mip level.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mip level is out of range.</exception>
    public GPUResourceGroup EntryStorage(uint mipLevel)
    {
        CheckMipLevel(mipLevel);

        if (_bindGroupStorage == null)
        {
            _bindGroupStorage = _device.CreateBindGroup(new BindGroupDescriptor
            {
                Name = $"{Name}_bind_group_storage_texture",
                Bindings = new BindGroupEntry[]{
                    new BindGroupEntry(
                        0,
                        ShaderStage.Standard,
                        BindingType.StorageTexture,
                        null,
                        new StorageTextureBindingInfo(AccessMode.ReadWrite, TextureViewDimension.Texture3D, _texture.PixelFormat)),
                }
            });
        }

        if (_resourcesStorage[mipLevel] == null)
        {
            ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
                _bindGroupStorage,
                new ResourceBindingEntry[]{
                    new ResourceBindingEntry(0, GetMipView(mipLevel)),
                }
            );

            _resourcesStorage[mipLevel] = _device.CreateResourceGroup(descriptor);
        }

        return _resourcesStorage[mipLevel]!;
    }

    internal GPUTextureView GetMipView(uint mipLevel)
    {
        if (_mipViews[mipLevel] == null)
        {
            _mipViews[mipLevel] = _device.CreateTextureView(new TextureViewDescriptor(
                _texture,
                TextureViewDimension.Texture3D,
                mipLevel,
                1,
                name: $"{Name}_mip_view_{mipLevel}"));
        }

        return _mipViews[mipLevel]!;
    }

    private void CheckMipLevel(uint mipLevel)
    {
        if (mipLevel >= MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel), mipLevel, "The mip level is out of range.");
        }
    }

    private GPUResourceGroup CreateResourceGroupRead()
    {
        ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture3DRead,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _textureView),
            }
        );

        return _device.CreateResourceGroup(descriptor);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            //dispose non-private managed resources
            _resourcesRead?.Dispose();
            _bindGroupStorage?.Dispose();
            for (int i = 0; i < _resourcesStorage.Length; i++)
            {
                _resourcesStorage[i]?.Dispose();
                _resourcesReadMip[i]?.Dispose();
                _mipViews[i]?.Dispose();
            }
        }
    }
}
