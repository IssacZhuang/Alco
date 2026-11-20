// Render graph handle, usage, descriptor, target, and diagnostic report types.

using System.Diagnostics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Semantic GPU resource access flags used by render graph passes to drive explicit synchronization and usage validation.</summary>
[Flags]
public enum RGUsage : uint
{
    /// <summary>No access.</summary>
    None = 0,

    // Read usages.
    /// <summary>Shader read in the vertex stage.</summary>
    VertexRead = 1u << 0,
    /// <summary>Shader read in task or mesh stages.</summary>
    MeshRead = 1u << 1,
    /// <summary>Shader read in the pixel stage.</summary>
    PixelRead = 1u << 2,
    /// <summary>Shader read in the compute stage.</summary>
    ComputeRead = 1u << 3,
    /// <summary>Color attachment read (load or blending) in the color output stage.</summary>
    ColorAttachmentRead = 1u << 4,
    /// <summary>Depth or stencil attachment read (load or depth/stencil test).</summary>
    DepthStencilRead = 1u << 5,
    /// <summary>Transfer copy source read.</summary>
    TransferRead = 1u << 6,
    /// <summary>Index buffer read during vertex assembly.</summary>
    IndexRead = 1u << 7,
    /// <summary>Indirect draw or dispatch argument read.</summary>
    IndirectRead = 1u << 8,
    /// <summary>Host readback visibility after transfer completion (exit barriers only).</summary>
    HostRead = 1u << 9,

    // Write usages.
    /// <summary>Storage shader write in the pixel stage.</summary>
    PixelWrite = 1u << 16,
    /// <summary>Storage shader write in the compute stage.</summary>
    ComputeWrite = 1u << 17,
    /// <summary>Color attachment write (clear, store, or MSAA resolve).</summary>
    ColorAttachmentWrite = 1u << 18,
    /// <summary>Depth or stencil attachment write (clear or depth/stencil write).</summary>
    DepthStencilWrite = 1u << 19,
    /// <summary>Transfer copy destination write.</summary>
    TransferWrite = 1u << 20,

    // Common combined shorthands.
    /// <summary>Compute shader read and storage write.</summary>
    ComputeReadWrite = ComputeRead | ComputeWrite,
    /// <summary>Color attachment read and write (blending or loading existing color).</summary>
    ColorAttachment = ColorAttachmentRead | ColorAttachmentWrite,
    /// <summary>Depth/stencil attachment read and write (depth test with depth write).</summary>
    DepthStencil = DepthStencilRead | DepthStencilWrite,
    /// <summary>Shader read in both vertex and pixel stages.</summary>
    GraphicsRead = VertexRead | PixelRead,
}

internal static class RGUsageInfo
{
    internal const RGUsage AllReads =
        RGUsage.VertexRead | RGUsage.MeshRead | RGUsage.PixelRead | RGUsage.ComputeRead |
        RGUsage.ColorAttachmentRead | RGUsage.DepthStencilRead | RGUsage.TransferRead |
        RGUsage.IndexRead | RGUsage.IndirectRead | RGUsage.HostRead;

    internal const RGUsage AllWrites =
        RGUsage.PixelWrite | RGUsage.ComputeWrite | RGUsage.ColorAttachmentWrite |
        RGUsage.DepthStencilWrite | RGUsage.TransferWrite;

    internal const RGUsage TextureOnly =
        RGUsage.ColorAttachmentRead | RGUsage.ColorAttachmentWrite |
        RGUsage.DepthStencilRead | RGUsage.DepthStencilWrite;

    internal const RGUsage BufferOnly = RGUsage.IndexRead | RGUsage.IndirectRead;

    internal static RGUsage ReadBits(RGUsage usage) => usage & AllReads;

    internal static RGUsage WriteBits(RGUsage usage) => usage & AllWrites;

    internal static bool HasRead(RGUsage usage) => (usage & AllReads) != 0;

    internal static bool HasWrite(RGUsage usage) => (usage & AllWrites) != 0;

    internal static void ToStageAccess(RGUsage usage, out Stage stages, out Access access)
    {
        stages = Stage.None;
        access = Access.None;
        if ((usage & RGUsage.VertexRead) != 0)
        {
            stages |= Stage.Vertex;
            access |= Access.ShaderRead;
        }
        if ((usage & RGUsage.MeshRead) != 0)
        {
            stages |= Stage.Task | Stage.Mesh;
            access |= Access.ShaderRead;
        }
        if ((usage & RGUsage.PixelRead) != 0)
        {
            stages |= Stage.Pixel;
            access |= Access.ShaderRead;
        }
        if ((usage & RGUsage.ComputeRead) != 0)
        {
            stages |= Stage.Compute;
            access |= Access.ShaderRead;
        }
        if ((usage & RGUsage.ColorAttachmentRead) != 0)
        {
            stages |= Stage.ColorOutput;
            access |= Access.ColorRead;
        }
        if ((usage & RGUsage.DepthStencilRead) != 0)
        {
            stages |= Stage.DepthStencilTests;
            access |= Access.DepthStencilRead;
        }
        if ((usage & RGUsage.TransferRead) != 0)
        {
            stages |= Stage.Transfer;
            access |= Access.TransferRead;
        }
        if ((usage & RGUsage.IndexRead) != 0)
        {
            stages |= Stage.IndexInput;
            access |= Access.IndexRead;
        }
        if ((usage & RGUsage.IndirectRead) != 0)
        {
            stages |= Stage.Indirect;
            access |= Access.IndirectRead;
        }
        if ((usage & RGUsage.HostRead) != 0)
        {
            stages |= Stage.Host;
            access |= Access.HostRead;
        }
        if ((usage & RGUsage.PixelWrite) != 0)
        {
            stages |= Stage.Pixel;
            access |= Access.ShaderWrite;
        }
        if ((usage & RGUsage.ComputeWrite) != 0)
        {
            stages |= Stage.Compute;
            access |= Access.ShaderWrite;
        }
        if ((usage & RGUsage.ColorAttachmentWrite) != 0)
        {
            stages |= Stage.ColorOutput;
            access |= Access.ColorWrite;
        }
        if ((usage & RGUsage.DepthStencilWrite) != 0)
        {
            stages |= Stage.DepthStencilTests;
            access |= Access.DepthStencilWrite;
        }
        if ((usage & RGUsage.TransferWrite) != 0)
        {
            stages |= Stage.Transfer;
            access |= Access.TransferWrite;
        }
    }

    // Returns every read usage flag whose stage and access bits are fully covered by the given barrier destination scope.
    internal static RGUsage CoveredReadUsages(Stage afterStages, Access afterAccess)
    {
        RGUsage covered = RGUsage.None;
        bool all = (afterStages & Stage.AllCommands) != 0;
        if ((afterAccess & Access.ShaderRead) != 0)
        {
            if (all || (afterStages & Stage.Vertex) != 0) covered |= RGUsage.VertexRead;
            if (all || (afterStages & (Stage.Task | Stage.Mesh)) == (Stage.Task | Stage.Mesh)) covered |= RGUsage.MeshRead;
            if (all || (afterStages & Stage.Pixel) != 0) covered |= RGUsage.PixelRead;
            if (all || (afterStages & Stage.Compute) != 0) covered |= RGUsage.ComputeRead;
        }
        if ((afterAccess & Access.ColorRead) != 0 && (all || (afterStages & Stage.ColorOutput) != 0))
            covered |= RGUsage.ColorAttachmentRead;
        if ((afterAccess & Access.DepthStencilRead) != 0 && (all || (afterStages & Stage.DepthStencilTests) != 0))
            covered |= RGUsage.DepthStencilRead;
        if ((afterAccess & Access.TransferRead) != 0 && (all || (afterStages & Stage.Transfer) != 0))
            covered |= RGUsage.TransferRead;
        if ((afterAccess & Access.IndexRead) != 0 && (all || (afterStages & Stage.IndexInput) != 0))
            covered |= RGUsage.IndexRead;
        if ((afterAccess & Access.IndirectRead) != 0 && (all || (afterStages & Stage.Indirect) != 0))
            covered |= RGUsage.IndirectRead;
        if ((afterAccess & Access.HostRead) != 0 && (afterStages & Stage.Host) != 0)
            covered |= RGUsage.HostRead;
        return covered;
    }

    internal static TextureUsage RequiredTextureUsage(RGUsage usage)
    {
        TextureUsage flags = TextureUsage.None;
        if ((usage & (RGUsage.VertexRead | RGUsage.MeshRead | RGUsage.PixelRead | RGUsage.ComputeRead)) != 0)
            flags |= TextureUsage.Sampled;
        if ((usage & (RGUsage.PixelWrite | RGUsage.ComputeWrite)) != 0)
            flags |= TextureUsage.Storage;
        if ((usage & (RGUsage.ColorAttachmentRead | RGUsage.ColorAttachmentWrite)) != 0)
            flags |= TextureUsage.ColorAttachment;
        if ((usage & (RGUsage.DepthStencilRead | RGUsage.DepthStencilWrite)) != 0)
            flags |= TextureUsage.DepthStencilAttachment;
        if ((usage & RGUsage.TransferRead) != 0)
            flags |= TextureUsage.TransferSource;
        if ((usage & RGUsage.TransferWrite) != 0)
            flags |= TextureUsage.TransferDestination;
        return flags;
    }

    // Checks whether an explicit TextureUsage satisfies the requested RGUsage (storage textures can also be read in shaders).
    internal static bool IsTextureUsageCompatible(TextureUsage available, RGUsage usage)
    {
        if ((usage & (RGUsage.VertexRead | RGUsage.MeshRead | RGUsage.PixelRead | RGUsage.ComputeRead)) != 0 &&
            (available & (TextureUsage.Sampled | TextureUsage.Storage)) == 0)
            return false;
        if ((usage & (RGUsage.PixelWrite | RGUsage.ComputeWrite)) != 0 && (available & TextureUsage.Storage) == 0)
            return false;
        if ((usage & (RGUsage.ColorAttachmentRead | RGUsage.ColorAttachmentWrite)) != 0 && (available & TextureUsage.ColorAttachment) == 0)
            return false;
        if ((usage & (RGUsage.DepthStencilRead | RGUsage.DepthStencilWrite)) != 0 && (available & TextureUsage.DepthStencilAttachment) == 0)
            return false;
        if ((usage & RGUsage.TransferRead) != 0 && (available & TextureUsage.TransferSource) == 0)
            return false;
        if ((usage & RGUsage.TransferWrite) != 0 && (available & TextureUsage.TransferDestination) == 0)
            return false;
        return true;
    }

    // Detects illegal simultaneous feedback on one texture within the same synchronization scope.
    internal static bool HasTextureSamePassConflict(RGUsage usage)
    {
        bool colorWrite = (usage & RGUsage.ColorAttachmentWrite) != 0;
        bool depthWrite = (usage & RGUsage.DepthStencilWrite) != 0;
        bool shaderRead = (usage & (RGUsage.VertexRead | RGUsage.MeshRead | RGUsage.PixelRead | RGUsage.ComputeRead)) != 0;
        bool shaderWrite = (usage & (RGUsage.PixelWrite | RGUsage.ComputeWrite)) != 0;
        bool transfer = (usage & (RGUsage.TransferRead | RGUsage.TransferWrite)) != 0;
        bool attachment = (usage & TextureOnly) != 0;
        if ((colorWrite || depthWrite) && shaderRead)
            return true;
        if (attachment && shaderWrite)
            return true;
        if (transfer && (attachment || shaderRead || shaderWrite))
            return true;
        if ((usage & RGUsage.TransferRead) != 0 && (usage & RGUsage.TransferWrite) != 0)
            return true;
        return false;
    }
}

/// <summary>A lightweight handle identifying a texture resource managed or imported by a <see cref="RenderGraph"/>.</summary>
public readonly struct RGTexture : IEquatable<RGTexture>
{
    internal readonly ushort Index;
    internal readonly ushort Generation;

    internal RGTexture(ushort index, ushort generation)
    {
        Index = index;
        Generation = generation;
    }

    /// <summary>Whether this handle refers to a registered resource slot rather than <see langword="default"/>.</summary>
    public bool IsValid => Generation != 0;

    /// <inheritdoc />
    public bool Equals(RGTexture other) => Index == other.Index && Generation == other.Generation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RGTexture other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (Generation << 16) | Index;

    /// <summary>Tests whether two texture handles refer to the same slot and generation.</summary>
    public static bool operator ==(RGTexture left, RGTexture right) => left.Equals(right);

    /// <summary>Tests whether two texture handles differ.</summary>
    public static bool operator !=(RGTexture left, RGTexture right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => IsValid ? $"RGTexture(#{Index}v{Generation})" : "RGTexture(Invalid)";
}

/// <summary>A lightweight handle identifying a buffer resource managed or imported by a <see cref="RenderGraph"/>.</summary>
public readonly struct RGBuffer : IEquatable<RGBuffer>
{
    internal readonly ushort Index;
    internal readonly ushort Generation;

    internal RGBuffer(ushort index, ushort generation)
    {
        Index = index;
        Generation = generation;
    }

    /// <summary>Whether this handle refers to a registered resource slot rather than <see langword="default"/>.</summary>
    public bool IsValid => Generation != 0;

    /// <inheritdoc />
    public bool Equals(RGBuffer other) => Index == other.Index && Generation == other.Generation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RGBuffer other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (Generation << 16) | Index;

    /// <summary>Tests whether two buffer handles refer to the same slot and generation.</summary>
    public static bool operator ==(RGBuffer left, RGBuffer right) => left.Equals(right);

    /// <summary>Tests whether two buffer handles differ.</summary>
    public static bool operator !=(RGBuffer left, RGBuffer right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => IsValid ? $"RGBuffer(#{Index}v{Generation})" : "RGBuffer(Invalid)";
}

/// <summary>Describes a transient texture resource pooled and lifetime-aliased by a <see cref="RenderGraph"/>.</summary>
public struct RGTextureDesc
{
    /// <summary>Diagnostic name for validation errors and profiling reports.</summary>
    public string Name;

    /// <summary>Texture dimensionality; defaults to <see cref="TextureType.TwoD"/>.</summary>
    public TextureType Type;

    /// <summary>Explicit texel extent; zero X or Y derives that axis from the graph viewport multiplied by <see cref="Scale"/>.</summary>
    public Extent3D Extent;

    /// <summary>Viewport scale applied when <see cref="Extent"/> leaves X or Y at zero; defaults to 1.</summary>
    public float Scale;

    /// <summary>Pixel format; defaults to <see cref="Format.Rgba8Unorm"/>.</summary>
    public Format Format;

    /// <summary>Explicit texture usage flags, or <see cref="TextureUsage.None"/> to infer minimal usage from frame pass declarations.</summary>
    public TextureUsage Usage;

    /// <summary>Number of mip levels; defaults to 1.</summary>
    public uint MipLevels;

    /// <summary>Number of array layers; defaults to 1.</summary>
    public uint LayerCount;

    /// <summary>Sample count (1 or 4); defaults to 1.</summary>
    public uint SampleCount;

    /// <summary>Whether format-compatible descriptor views are enabled.</summary>
    public bool MutableFormat;

    /// <summary>Initializes a viewport-sized single-sample 2D RGBA8 texture descriptor with inferred usage.</summary>
    public RGTextureDesc()
    {
        Name = "unnamed_texture";
        Type = TextureType.TwoD;
        Extent = new Extent3D(0, 0, 1);
        Scale = 1.0f;
        Format = Format.Rgba8Unorm;
        Usage = TextureUsage.None;
        MipLevels = 1;
        LayerCount = 1;
        SampleCount = 1;
        MutableFormat = false;
    }

    internal readonly Extent3D ResolveExtent(Extent2D viewport)
    {
        float scale = float.IsFinite(Scale) && Scale > 0.0f ? Scale : 1.0f;
        uint width = Extent.X != 0 ? Extent.X : Math.Max(1u, (uint)(viewport.X * scale));
        uint height = Extent.Y != 0 ? Extent.Y : Math.Max(1u, (uint)(viewport.Y * scale));
        uint depth = Extent.Z != 0 ? Extent.Z : 1u;
        return new Extent3D(width, height, depth);
    }

    internal readonly TextureDesc ResolveTextureDesc(Extent2D viewport, TextureUsage resolvedUsage)
    {
        return new TextureDesc
        {
            Type = Type,
            Extent = ResolveExtent(viewport),
            MipLevels = MipLevels != 0 ? MipLevels : 1u,
            LayerCount = LayerCount != 0 ? LayerCount : 1u,
            Format = Format,
            MutableFormat = MutableFormat,
            Usage = resolvedUsage,
            SampleCount = SampleCount != 0 ? SampleCount : 1u,
        };
    }
}

/// <summary>Describes a transient buffer resource pooled and lifetime-aliased by a <see cref="RenderGraph"/>.</summary>
public struct RGBufferDesc
{
    /// <summary>Diagnostic name for validation errors and profiling reports.</summary>
    public string Name;

    /// <summary>Required minimum capacity in bytes; must be positive.</summary>
    public ulong SizeBytes;

    /// <summary>Initializes a buffer descriptor.</summary>
    public RGBufferDesc()
    {
        Name = "unnamed_buffer";
    }
}

/// <summary>A borrowed color output target for <see cref="RenderGraph.Execute"/>: either an acquired swapchain frame or a caller-owned texture.</summary>
public readonly struct RenderGraphTarget
{
    /// <summary>The color attachment view, or null when headless/minimized.</summary>
    public GPURenderView? RenderView { get; }

    /// <summary>The underlying native texture for attachment and transfer operations, or null when headless.</summary>
    public GPUTexture? NativeTexture { get; }

    /// <summary>The caller-owned texture wrapper when backed by a <see cref="Rendering.Texture"/>, or null for swapchain frames.</summary>
    public Texture? Texture { get; }

    /// <summary>The target extent in pixels.</summary>
    public Extent2D Extent { get; }

    /// <summary>The target color format.</summary>
    public Format Format { get; }

    /// <summary>Whether this target refers to a valid drawable output.</summary>
    public bool IsValid => RenderView != null;

    private RenderGraphTarget(GPURenderView renderView, GPUTexture? nativeTexture, Texture? texture, Extent2D extent, Format format)
    {
        RenderView = renderView;
        NativeTexture = nativeTexture;
        Texture = texture;
        Extent = extent;
        Format = format;
    }

    /// <summary>Wraps an acquired swapchain frame; returns <see langword="default"/> when the frame is unavailable.</summary>
    /// <param name="frame">The acquired swapchain frame.</param>
    /// <param name="format">The swapchain's color format.</param>
    /// <returns>A target referencing the borrowed swapchain image, or an invalid target when minimized.</returns>
    public static RenderGraphTarget FromSwapchain(SwapchainFrame frame, Format format)
    {
        if (frame.RenderView == null)
            return default;
        return new RenderGraphTarget(frame.RenderView, frame.Texture, null, frame.Extent, format);
    }

    /// <summary>Wraps a caller-owned offscreen texture as a graph output target.</summary>
    /// <param name="texture">The live color-attachment texture.</param>
    /// <returns>A target referencing the texture's mip-zero render view.</returns>
    public static RenderGraphTarget FromTexture(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Debug.Assert(!texture.IsDestroyed && (texture.Desc.Usage & TextureUsage.ColorAttachment) != 0);
        return new RenderGraphTarget(texture.RenderView, texture.NativeTexture, texture,
            new Extent2D(texture.Extent.X, texture.Extent.Y), texture.Format);
    }

    internal static RenderGraphTarget CreateDryRun(Extent2D extent, Format format)
    {
        return new RenderGraphTarget(null!, null, null, extent, format);
    }

    internal bool IsDryRunTarget => RenderView == null && Extent.X != 0 && Extent.Y != 0 && Format != Format.Undefined;
}

/// <summary>A planned global execution and memory barrier emitted before a render graph pass or at graph exit.</summary>
public readonly struct RGBarrier : IEquatable<RGBarrier>
{
    /// <summary>The source pipeline stages.</summary>
    public Stage Before { get; init; }

    /// <summary>The source memory accesses.</summary>
    public Access BeforeAccess { get; init; }

    /// <summary>The destination pipeline stages.</summary>
    public Stage After { get; init; }

    /// <summary>The destination memory accesses.</summary>
    public Access AfterAccess { get; init; }

    /// <summary>Whether no barrier is needed.</summary>
    public bool IsEmpty => Before == Stage.None || After == Stage.None;

    /// <inheritdoc />
    public bool Equals(RGBarrier other) =>
        Before == other.Before && BeforeAccess == other.BeforeAccess && After == other.After && AfterAccess == other.AfterAccess;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RGBarrier other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Before, BeforeAccess, After, AfterAccess);

    /// <summary>Tests whether two barrier records are identical.</summary>
    public static bool operator ==(RGBarrier left, RGBarrier right) => left.Equals(right);

    /// <summary>Tests whether two barrier records differ.</summary>
    public static bool operator !=(RGBarrier left, RGBarrier right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => IsEmpty ? "None" : $"({Before} [{BeforeAccess}] -> {After} [{AfterAccess}])";
}

/// <summary>Per-pass compilation, barrier, and timing diagnostics produced by a <see cref="RenderGraph"/> frame.</summary>
public readonly struct RenderGraphPassReport
{
    /// <summary>The pass name declared during setup.</summary>
    public string Name { get; init; }

    /// <summary>The owning node's index in <see cref="RenderGraph.Nodes"/>.</summary>
    public int NodeIndex { get; init; }

    /// <summary>The node-local sub-pass identifier.</summary>
    public int PassId { get; init; }

    /// <summary>Whether the pass survived dead-pass culling.</summary>
    public bool Alive { get; init; }

    /// <summary>The merged barrier emitted immediately before this pass, or empty when none was required.</summary>
    public RGBarrier Barrier { get; init; }

    /// <summary>The number of resource reads declared on this pass.</summary>
    public int ReadCount { get; init; }

    /// <summary>The number of resource writes declared on this pass.</summary>
    public int WriteCount { get; init; }

    /// <summary>CPU recording time in milliseconds when profiling is enabled; zero otherwise.</summary>
    public double CpuMilliseconds { get; init; }

    /// <summary>Most recently resolved GPU execution time in milliseconds when profiling is enabled; zero before first readback.</summary>
    public double GpuMilliseconds { get; init; }
}

/// <summary>Frame-level summary diagnostics for a <see cref="RenderGraph"/> execution.</summary>
public readonly struct RenderGraphStatistics
{
    /// <summary>Total passes declared by enabled nodes this frame.</summary>
    public int TotalPassCount { get; init; }

    /// <summary>Number of passes that survived culling and executed.</summary>
    public int AlivePassCount { get; init; }

    /// <summary>Number of declared passes culled as unreachable from outputs.</summary>
    public int CulledPassCount { get; init; }

    /// <summary>Number of non-empty barriers emitted this frame (including any exit barrier).</summary>
    public int EmittedBarrierCount { get; init; }

    /// <summary>Number of logical transient textures materialized this frame.</summary>
    public int LogicalTransientTextureCount { get; init; }

    /// <summary>Number of distinct physical pooled textures used this frame.</summary>
    public int ActivePhysicalTextureCount { get; init; }

    /// <summary>Total physical textures currently retained in the graph pool.</summary>
    public int TotalPooledTextureCount { get; init; }

    /// <summary>Number of logical transient buffers materialized this frame.</summary>
    public int LogicalTransientBufferCount { get; init; }

    /// <summary>Number of distinct physical pooled buffers used this frame.</summary>
    public int ActivePhysicalBufferCount { get; init; }

    /// <summary>Total physical buffers currently retained in the graph pool.</summary>
    public int TotalPooledBufferCount { get; init; }

    /// <summary>Number of retired pooled resources waiting for GPU completion before destruction.</summary>
    public int RetiredResourceCount { get; init; }

    /// <summary>Total CPU execution time of the graph frame in milliseconds when profiling is enabled.</summary>
    public double TotalCpuMilliseconds { get; init; }

    /// <summary>Sum of resolved per-pass GPU times in milliseconds when profiling is enabled.</summary>
    public double TotalGpuMilliseconds { get; init; }
}
