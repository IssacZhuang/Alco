using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Alco.Graphics;



namespace Alco.Rendering;

/// <summary>
/// High level encapsulation of a GPUTexture with a TextureView which the dimension is 2D
/// </summary>
public abstract class Texture : AutoDisposable
{
    protected readonly GPUDevice _device;
    protected GPUTexture _texture;
    protected GPUTextureView _textureView;

    // Whether this wrapper owns _texture and _textureView. Wrappers created over
    // externally owned GPU resources (e.g. frame buffer attachments or render
    // graph pooled textures) are non-owning: their lifetime is managed by the
    // creator.
    private readonly bool _ownsResources;

    // Bind groups that bind one view of this texture as the only resource of a
    // shader group, keyed by the group layout and the bound view (the full-chain
    // view or a per-mip view). A single-resource group is fully determined by
    // (view, layout), so one group per combination is created for the texture's
    // lifetime and shared across materials and frames instead of being
    // rebuilt on every slot change. Samplers never appear here: they are
    // independent resources bound by the consuming shader's sampler entries.
    // Thread safety: reads are lock free; the first creation per key serializes
    // on _createGroupLock, so materials on multiple threads may bind the same
    // texture concurrently.
    private readonly ConcurrentDictionary<(GPUBindGroup Layout, GPUTextureView View), GPUResourceGroup> _layoutResourceGroups = new();
    private readonly Lock _createGroupLock = new();

    /// <summary>
    /// The debug name of the underlying GPU texture.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether the texture can be written to on the CPU side.
    /// </summary>
    public bool IsWriteable => _texture.IsWriteable;


    public uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture.Width;
    }

    public uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture.Height;
    }

    public uint Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture.Depth;
    }

    /// <summary>
    /// The underlying GPU texture.
    /// </summary>
    public GPUTexture NativeTexture => _texture;

    /// <summary>
    /// The full-chain texture view of the texture.
    /// </summary>
    public GPUTextureView View
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _textureView;
    }

    /// <summary>
    /// The resource group containing only the texture view, for read-only texture
    /// shader bind groups.
    /// </summary>
    public abstract GPUResourceGroup EntryReadonly { get; }

    /// <summary>
    /// The resource group containing only the texture view, for storage texture
    /// shader bind groups.
    /// </summary>
    public abstract GPUResourceGroup EntryWriteable { get; }

    internal Texture(
        GPUDevice device,
        GPUTexture texture,
        GPUTextureView textureView,
        bool ownsResources = true)
    {
        _device = device;

        _texture = texture;
        _textureView = textureView;
        _ownsResources = ownsResources;

        Name = texture.Name;
    }

    /// <summary>
    /// Uploads one pixel per element from the array to the texture.
    /// </summary>
    /// <param name="data">The pixel data; its length must equal width times height.</param>
    public unsafe void SetPixels<T>(T[] data) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            SetPixels(ptr, (uint)data.Length);
        }
    }

    /// <summary>
    /// Uploads pixel data from an unmanaged buffer to the texture.
    /// </summary>
    /// <param name="data">The pointer to the pixel data.</param>
    /// <param name="length">The pixel count; must equal width times height.</param>
    public unsafe void SetPixels<T>(T* data, uint length) where T : unmanaged
    {
        if (!IsWriteable)
        {
            throw new InvalidOperationException("Can not set pixels to a readonly texture");

        }

        if (length != _texture.Width * _texture.Height)
        {
            throw new ArgumentException($"The pxiel count {length} is not equal to the texture size(width*height)");
        }

        _device.WriteTexture(_texture, (byte*)data, length * (uint)sizeof(T));
    }

    /// <summary>
    /// Uploads raw bytes to the texture without validating the size.
    /// </summary>
    /// <param name="data">The pointer to the raw pixel bytes.</param>
    /// <param name="size">The size of the data in bytes.</param>
    public unsafe void SetPixels(byte* data, uint size)
    {
        if (!IsWriteable)
        {
            throw new InvalidOperationException("Can not set pixels to a readonly texture");
        }
        _device.WriteTexture(_texture, data, size);
    }

    /// <summary>
    /// Returns the cached bind group that binds the given view of this texture as the only
    /// resource of the given layout, creating it on first use.
    /// </summary>
    /// <param name="layout">The bind group layout of the consuming shader's group.</param>
    /// <param name="view">The texture view to bind (full-chain or single-mip).</param>
    /// <param name="binding">The binding number of the texture view inside the group.</param>
    /// <returns>The cached or newly created resource group.</returns>
    internal GPUResourceGroup GetOrCreateResourceGroup(GPUBindGroup layout, GPUTextureView view, uint binding)
    {
        if (_layoutResourceGroups.TryGetValue((layout, view), out GPUResourceGroup? group))
        {
            return group;
        }

        lock (_createGroupLock)
        {
            if (_layoutResourceGroups.TryGetValue((layout, view), out group))
            {
                return group;
            }

            ResourceBindingEntry[] entries = new ResourceBindingEntry[] { new(binding, view) };
            group = _device.CreateResourceGroup(new ResourceGroupDescriptor(layout, entries, $"{Name}_layout_bind_group"));
            _layoutResourceGroups[(layout, view)] = group;
            return group;
        }
    }

    /// <summary>
    /// Drops the cached per-layout bind groups, e.g. after the native texture
    /// was replaced in place. The groups are not disposed: recorded commands
    /// and material caches may still reference them; the finalizer releases
    /// the native objects.
    /// </summary>
    internal void DiscardLayoutResourceGroups()
    {
        _layoutResourceGroups.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsResources)
        {
            //dispose non-private managed resources
            _texture?.Dispose();
            _textureView?.Dispose();
        }

        if (disposing)
        {
            foreach (GPUResourceGroup group in _layoutResourceGroups.Values)
            {
                group.Dispose();
            }

            _layoutResourceGroups.Clear();
        }
    }


    #region Texture Creation

    #endregion


}