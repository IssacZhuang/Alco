using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Describes a transient texture resource created by
/// <see cref="RenderGraph.CreateTransient"/>. Transient resources are backed by the
/// graph's texture pool: their lifetime is computed per frame from the declared
/// reads/writes, and resources whose lifetimes do not overlap may alias the same
/// underlying GPU texture.
/// </summary>
public struct RenderGraphTextureDescriptor
{
    /// <summary>
    /// The attachment layout of the resource (color attachment formats and optional
    /// depth attachment). The same layout drives both the pooled textures and the
    /// composed frame buffer.
    /// </summary>
    public required GPUAttachmentLayout Layout { get; init; }

    /// <summary>
    /// The absolute width in pixels, or 0 to derive the size from the graph's
    /// viewport (<see cref="RenderGraph"/> width × <see cref="ResolutionScale"/>).
    /// </summary>
    public uint Width { get; init; }

    /// <summary>
    /// The absolute height in pixels, or 0 to derive the size from the graph's
    /// viewport (<see cref="RenderGraph"/> height × <see cref="ResolutionScale"/>).
    /// </summary>
    public uint Height { get; init; }

    /// <summary>
    /// The absolute height in pixels, or 0 to derive the size from the graph's
    /// viewport (<see cref="RenderGraph"/> height × <see cref="ResolutionScale"/>).
    /// </summary>
    public float ResolutionScale { get; init; } = 1.0f;

    /// <summary>
    /// Optional transient resource whose depth attachment this resource shares.
    /// Both layouts must declare a depth attachment with the same format. Every node
    /// writing this resource implicitly reads <see cref="DepthSource"/>, so the
    /// source's lifetime always covers the usage. Typically used to render a scene
    /// color target against the G-buffer's depth without a depth copy.
    /// </summary>
    public RenderGraphTexture? DepthSource { get; init; }

    /// <summary>
    /// The sampler filter mode used by the <see cref="RenderTexture"/> facade.
    /// </summary>
    public FilterMode Filter { get; init; } = FilterMode.Linear;

    /// <summary>
    /// The diagnostic name of the resource.
    /// </summary>
    public string Name { get; init; } = "unnamed_graph_texture";

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderGraphTextureDescriptor"/> struct.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RenderGraphTextureDescriptor(
        GPUAttachmentLayout layout,
        uint width = 0,
        uint height = 0,
        float resolutionScale = 1.0f,
        RenderGraphTexture? depthSource = null,
        FilterMode filter = FilterMode.Linear,
        string name = "unnamed_graph_texture")
    {
        Layout = layout;
        Width = width;
        Height = height;
        ResolutionScale = resolutionScale;
        DepthSource = depthSource;
        Filter = filter;
        Name = name;
    }
}
