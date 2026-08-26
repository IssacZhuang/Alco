using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A handle to a texture resource managed by a <see cref="RenderGraph"/>.
/// <list type="bullet">
/// <item><b>Transient</b> resources (<see cref="RenderGraph.CreateTransient"/>) are
/// backed by the graph's texture pool. Their lifetime is computed per frame from the
/// declared reads/writes, and non-overlapping lifetimes may alias the same pooled
/// texture.</item>
/// <item><b>Imported</b> resources (<see cref="RenderGraph.Import"/>) wrap a
/// caller-owned <see cref="RenderTexture"/> (e.g. cross-frame history); the
/// graph references but never pools or rebinds them.</item>
/// </list>
/// The <see cref="Texture"/> facade keeps a stable object identity across backing
/// changes: materials bind it once and rebuild their bind groups automatically through
/// the <see cref="RenderTexture.Version"/> check whenever the backing
/// changes. In steady state (same enabled node set) the pool hands out the identical
/// textures every frame, so no rebinding occurs.
/// </summary>
public sealed class RenderGraphTexture
{
    /// <summary>The backing kind of the resource.</summary>
    internal enum ResourceKind
    {
        Transient,
        Imported,
    }

    /// <summary>The index of the resource in the owning graph's resource table; -1 while unregistered.</summary>
    internal int Id = -1;

    /// <summary>
    /// Whether the resource was destroyed via <see cref="RenderGraph.DestroyTransient"/>.
    /// A destroyed transient is a tombstone: it stays in the graph's resource table
    /// (keeping ids stable) but is skipped by the allocation walk, resize
    /// rematerialization and disposal.
    /// </summary>
    internal bool IsDestroyed;

    /// <summary>The backing kind.</summary>
    internal ResourceKind Kind { get; private set; }

    /// <summary>The attachment layout of the resource, or null for imported resources.</summary>
    internal GPUAttachmentLayout? Layout { get; }

    /// <summary>The declared absolute width, or 0 when the size is graph-relative.</summary>
    internal uint AbsoluteWidth { get; }

    /// <summary>The declared absolute height, or 0 when the size is graph-relative.</summary>
    internal uint AbsoluteHeight { get; }

    /// <summary>The scale applied to the graph viewport when <see cref="AbsoluteWidth"/> is 0.</summary>
    internal float ResolutionScale { get; }

    /// <summary>The transient resource whose depth attachment this resource shares, or null.</summary>
    internal RenderGraphTexture? DepthSource { get; }

    /// <summary>The facade sampler filter mode.</summary>
    internal FilterMode Filter { get; }

    /// <summary>The resolved (current frame) width in pixels.</summary>
    internal uint ResolvedWidth { get; set; }

    /// <summary>The resolved (current frame) height in pixels.</summary>
    internal uint ResolvedHeight { get; set; }

    /// <summary>The current pool assignment of the color attachments (transient only).</summary>
    /// <remarks>
    /// The composed frame buffer and facade stay valid between frames (materials may
    /// keep referencing them); the underlying pooled textures are only guaranteed
    /// inside the resource's computed lifetime — outside it they may alias another
    /// transient.
    /// </remarks>
    internal PooledAttachment[]? ColorAttachments;
    internal PooledAttachment? DepthAttachment;
    internal GPUFrameBuffer? ComposedFrameBuffer;

    /// <summary>The pool keys of the color attachment slots, or null when not yet computed.</summary>
    internal TexturePoolKey[]? ColorKeys;

    /// <summary>The pool key of the own depth attachment slot, or null when the
    /// resource has no depth attachment or shares another transient's depth.</summary>
    internal TexturePoolKey? OwnDepthKey;

    private RenderTexture? _facade;
    private readonly string _name;

    private RenderGraphTexture(string name, GPUAttachmentLayout layout, in RenderGraphTextureDescriptor descriptor)
    {
        _name = name;
        Kind = ResourceKind.Transient;
        Layout = layout;
        AbsoluteWidth = descriptor.Width;
        AbsoluteHeight = descriptor.Height;
        ResolutionScale = descriptor.ResolutionScale;
        DepthSource = descriptor.DepthSource;
        Filter = descriptor.Filter;
    }

    private RenderGraphTexture(RenderTexture imported)
    {
        _name = imported.Name;
        Kind = ResourceKind.Imported;
        _facade = imported;
        ResolvedWidth = imported.Width;
        ResolvedHeight = imported.Height;
    }

    /// <summary>The diagnostic name of the resource.</summary>
    public string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _name;
    }

    /// <summary>Whether the resource wraps a caller-owned persistent texture.</summary>
    public bool IsImported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == ResourceKind.Imported;
    }

    /// <summary>The current width in pixels.</summary>
    public uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ResolvedWidth;
    }

    /// <summary>The current height in pixels.</summary>
    public uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ResolvedHeight;
    }

    /// <summary>
    /// The <see cref="RenderTexture"/> facade of this resource, for material
    /// binding and as a render target. Materializes at creation time, so the facade is
    /// always available once the resource is registered on a graph.
    /// </summary>
    public RenderTexture Texture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _facade ?? throw new InvalidOperationException(
            $"The render graph texture '{_name}' is not materialized: it was not created through RenderGraph.CreateTransient/Import.");
    }

    /// <summary>The facade, or null when not yet materialized (internal scheduling paths).</summary>
    internal RenderTexture? Facade
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _facade;
    }

    /// <summary>
    /// Computes the pool keys of every attachment slot from the layout and the
    /// resolved size. Called by the graph on creation and after every resize.
    /// </summary>
    internal void ComputeSlotKeys()
    {
        GPUAttachmentLayout layout = Layout!;
        TexturePoolKey[] keys = ColorKeys ?? new TexturePoolKey[layout.Colors.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = new TexturePoolKey(
                ResolvedWidth, ResolvedHeight,
                layout.Colors[i].Format, GPUFrameBuffer.ColorAttachmentUsage);
        }
        ColorKeys = keys;
        OwnDepthKey = DepthSource == null && layout.Depth.HasValue
            ? new TexturePoolKey(ResolvedWidth, ResolvedHeight, layout.Depth.Value.Format, GPUFrameBuffer.DepthAttachmentUsage)
            : null;
    }

    /// <summary>Creates a transient resource from its descriptor with the resolved size.</summary>
    internal static RenderGraphTexture CreateTransient(in RenderGraphTextureDescriptor descriptor, uint resolvedWidth, uint resolvedHeight)
    {
        RenderGraphTexture texture = new RenderGraphTexture(descriptor.Name, descriptor.Layout, descriptor);
        texture.ResolvedWidth = resolvedWidth;
        texture.ResolvedHeight = resolvedHeight;
        return texture;
    }

    /// <summary>Creates an imported resource wrapping a caller-owned render texture.</summary>
    internal static RenderGraphTexture CreateImported(RenderTexture texture)
    {
        return new RenderGraphTexture(texture);
    }

    /// <summary>Assigns the materialized facade (called by the graph on first acquisition).</summary>
    internal void SetFacade(RenderTexture facade)
    {
        _facade = facade;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{_name} ({Kind}, {ResolvedWidth}x{ResolvedHeight})";
    }
}
