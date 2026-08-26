namespace Alco.Graphics;

/// <summary>
/// One sampled-texture slot of a shader library: the slot's bare field name
/// (its material-domain identity — the name an asset binds by) plus the
/// texture shape the declaration requires. The library-level counterpart of
/// the linked view's <c>TextureBindingInfo</c> on a bind-group entry: the
/// same shape facts, correlated by name after linking, with no set/binding
/// numbers (a set is a composition product, not an input).
/// </summary>
public sealed class ShaderTextureSlot
{
    /// <summary>Creates a slot view from module reflection data.</summary>
    /// <param name="name">The slot's bare field name.</param>
    /// <param name="viewDimension">The texture dimension the declaration requires.</param>
    /// <param name="sampleType">The texel sample type the declaration requires.</param>
    public ShaderTextureSlot(string name, TextureViewDimension viewDimension, TextureSampleType sampleType)
    {
        Name = name;
        ViewDimension = viewDimension;
        SampleType = sampleType;
    }

    /// <summary>The slot's bare field name — the identity an asset binds by.</summary>
    public string Name { get; }

    /// <summary>The texture dimension the declaration requires (1D/2D/3D/cube).</summary>
    public TextureViewDimension ViewDimension { get; }

    /// <summary>The texel sample type the declaration requires (float/unfilterable float/uint...).</summary>
    public TextureSampleType SampleType { get; }
}

/// <summary>
/// One sampler slot of a shader library: a sampler member the module declares
/// (e.g. a surface's custom <c>SamplerState _mySampler;</c>), by its bare field
/// name — the identity a material binds through <c>SetSampler</c>. The
/// library-level counterpart of the linked view's sampler bind-group entries.
/// The engine's shared sampler bank is deliberately NOT a slot: its members
/// are engine-owned state, never bound by a material.
/// </summary>
public sealed class ShaderSamplerSlot
{
    /// <summary>Creates a slot view from module reflection data.</summary>
    /// <param name="name">The slot's bare field name.</param>
    /// <param name="isComparison">Whether the member is a <c>SamplerComparisonState</c> (depth comparison) rather than a plain <c>SamplerState</c>.</param>
    public ShaderSamplerSlot(string name, bool isComparison)
    {
        Name = name;
        IsComparison = isComparison;
    }

    /// <summary>The slot's bare field name — the identity a material binds by.</summary>
    public string Name { get; }

    /// <summary>True for <c>SamplerComparisonState</c> (depth comparison), false for plain <c>SamplerState</c>.</summary>
    public bool IsComparison { get; }
}

/// <summary>
/// The reflection of a shader library — one module's own declarations before
/// any composition or link: its uniform blocks (the shared
/// <see cref="ShaderUniformBlock"/> vocabulary, with their user-defined
/// attributes and members) and its texture slots (bare field names with their
/// required shape, no set numbers — a set is a composition product, not an
/// input). Sibling of <see cref="ShaderReflection"/> (the linked program's
/// view of the same block vocabulary plus its pipeline interface); deliberately
/// not a base class of it. Cached per (module, defines) by the module system
/// and invalidated with the module.
/// </summary>
public sealed class ShaderLibraryReflection
{
    /// <summary>Creates the module-level reflection view.</summary>
    /// <param name="uniformBlocks">Every uniform/parameter block the module declares, in declaration order.</param>
    /// <param name="textureSlots">Every sampled-texture member of every block, in declaration order.</param>
    /// <param name="samplerSlots">Every sampler member of every block, in declaration order.</param>
    public ShaderLibraryReflection(
        IReadOnlyList<ShaderUniformBlock> uniformBlocks,
        IReadOnlyList<ShaderTextureSlot> textureSlots,
        IReadOnlyList<ShaderSamplerSlot> samplerSlots)
    {
        UniformBlocks = uniformBlocks;
        TextureSlots = textureSlots;
        SamplerSlots = samplerSlots;
    }

    /// <summary>Every uniform/parameter block the module declares, in declaration order.</summary>
    public IReadOnlyList<ShaderUniformBlock> UniformBlocks { get; }

    /// <summary>
    /// Every sampled-texture member of every block, in declaration order.
    /// Storage images and depth textures are pass bindings, not material slots, and are absent.
    /// </summary>
    public IReadOnlyList<ShaderTextureSlot> TextureSlots { get; }

    /// <summary>
    /// Every sampler member of every block, in declaration order, with the
    /// comparison flag. A custom sampler is an explicit binding target
    /// (<c>SetSampler</c> by name); the shared sampler bank is engine-owned
    /// state and never appears here.
    /// </summary>
    public IReadOnlyList<ShaderSamplerSlot> SamplerSlots { get; }
}
