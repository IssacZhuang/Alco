using System.Runtime.CompilerServices;
using Alco.Graphics;



namespace Alco.Rendering;

/// <summary>
/// High level encapsulation of a GPUTexture with a TextureView which the dimension is 2D
/// </summary>
public abstract class Texture : AutoDisposable
{
    protected readonly GPUDevice _device;
    // internal
    protected GPUTexture _texture;
    protected GPUTextureView _textureView;

    // from outside
    protected GPUSampler _sampler;

    // Whether this wrapper owns _texture and _textureView. Wrappers created over
    // externally owned GPU resources (e.g. frame buffer attachments or render
    // graph pooled textures) are non-owning: their lifetime is managed by the
    // creator, the same rule as the externally supplied sampler.
    private readonly bool _ownsResources;

    // Bind groups that bind one view of this texture as the only resource of a
    // shader group, keyed by the group layout and the bound view (the full-chain
    // view or a per-mip view). A single-resource group is fully determined by
    // (view, sampler, layout), so one group per combination is created for the
    // texture's lifetime and shared across materials and frames instead of being
    // rebuilt on every slot change.
    private Dictionary<(GPUBindGroup Layout, GPUTextureView View), GPUResourceGroup>? _layoutResourceGroups;

    public string Name { get; }

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
    /// The sampler used when the texture is bound to a texture-and-sampler slot.
    /// </summary>
    public GPUSampler Sampler
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _sampler;
    }

    /// <summary>
    /// The resource group containing the texture view and the sampler, for
    /// texture-and-sampler shader bind groups.
    /// </summary>
    public abstract GPUResourceGroup EntrySample { get; }

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
        GPUSampler sampler,
        bool ownsResources = true)
    {
        _device = device;

        _texture = texture;
        _textureView = textureView;
        _sampler = sampler;
        _ownsResources = ownsResources;

        Name = texture.Name;
    }

    public unsafe void SetPixels<T>(T[] data) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            SetPixels(ptr, (uint)data.Length);
        }
    }

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

    public unsafe void SetPixels(byte* data, uint size)
    {
        if (!IsWriteable)
        {
            throw new InvalidOperationException("Can not set pixels to a readonly texture");
        }
        _device.WriteTexture(_texture, data, size);
    }

    public virtual void SetSampler(GPUSampler sampler)
    {
        _sampler = sampler;
        // Cached layout groups embed the old sampler. They are dropped without
        // disposal: recorded commands and material caches may still reference
        // them until their slots change, and the finalizer releases the native
        // objects (the same policy as the texture hot reload).
        DiscardLayoutResourceGroups();
    }

    /// <summary>
    /// Returns the bind group that binds the given view of this texture (plus
    /// its sampler for texture-and-sampler groups) as the only resource of a
    /// shader bind group with the given layout, creating it on first use. The
    /// group is cached on the texture for its lifetime and shared across all
    /// materials and frames, so cycling textures or mip views through a
    /// material does not allocate a new bind group per change.
    /// </summary>
    /// <param name="layout">The bind group layout of the consuming shader's group.</param>
    /// <param name="view">The texture view to bind (full-chain or single-mip).</param>
    /// <param name="sampler">The companion sampler, or null when the group has no sampler binding.</param>
    /// <param name="binding">The binding number of the texture view inside the group.</param>
    /// <param name="samplerBinding">The binding number of the sampler inside the group (used when <paramref name="sampler"/> is not null).</param>
    /// <returns>The cached or newly created resource group.</returns>
    internal GPUResourceGroup GetOrCreateResourceGroup(GPUBindGroup layout, GPUTextureView view, GPUSampler? sampler, uint binding, uint samplerBinding)
    {
        Dictionary<(GPUBindGroup Layout, GPUTextureView View), GPUResourceGroup> cache = _layoutResourceGroups ??= new Dictionary<(GPUBindGroup Layout, GPUTextureView View), GPUResourceGroup>();
        if (cache.TryGetValue((layout, view), out GPUResourceGroup? group))
        {
            return group;
        }

        ResourceBindingEntry[] entries = sampler != null
            ? new ResourceBindingEntry[] { new(binding, view), new(samplerBinding, sampler) }
            : new ResourceBindingEntry[] { new(binding, view) };
        group = _device.CreateResourceGroup(new ResourceGroupDescriptor(layout, entries, $"{Name}_layout_bind_group"));
        cache[(layout, view)] = group;
        return group;
    }

    /// <summary>
    /// Drops the cached per-layout bind groups, e.g. after the native texture
    /// or the sampler was replaced in place. The groups are not disposed:
    /// recorded commands and material caches may still reference them; the
    /// finalizer releases the native objects.
    /// </summary>
    internal void DiscardLayoutResourceGroups()
    {
        _layoutResourceGroups = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsResources)
        {
            //dispose non-private managed resources
            _texture?.Dispose();
            _textureView?.Dispose();
        }

        if (disposing && _layoutResourceGroups != null)
        {
            foreach (GPUResourceGroup group in _layoutResourceGroups.Values)
            {
                group.Dispose();
            }

            _layoutResourceGroups = null;
        }
    }


    #region Texture Creation

    #endregion


}