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
/// This list excludes the engine-owned shared sampler bank; materials cannot
/// bind it directly.
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
/// The scalar kinds a generic value parameter may carry for the material
/// specialization domain — the set the literal formatter can emit (slang
/// itself rejects floating-point value parameters, so integer and bool are
/// the whole universe).
/// </summary>
public enum ShaderSpecScalarType
{
    /// <summary>A <c>bool</c> value parameter; literals are <c>true</c>/<c>false</c>.</summary>
    Bool,

    /// <summary>An <c>int</c> value parameter; literals are decimal.</summary>
    Int32,

    /// <summary>A <c>uint</c> value parameter; literals are decimal.</summary>
    UInt32,
}

/// <summary>
/// One generic value parameter of a shader module's entry points: the axis a
/// material specializes by name (e.g. <c>&lt;let isFacade : bool&gt;</c>).
/// Axes enumerate in specialization argument order — entry definition order,
/// each entry's value parameters in declaration order — the same order link-time
/// specialization consumes them, so a positional argument list is a projection
/// of this list.
/// </summary>
public sealed record ShaderSpecializationAxis(string Name, ShaderSpecScalarType ScalarType);

/// <summary>
/// Declarations view of one shader module before any composition or link:
/// its uniform blocks and texture/sampler slots, keyed by bare field names
/// with no set numbers. Cached per module by the module system and
/// invalidated with it.
/// </summary>
public sealed class ShaderLibraryReflection
{
    /// <summary>Creates the module-level reflection view.</summary>
    /// <param name="uniformBlocks">Every uniform/parameter block the module declares, in declaration order.</param>
    /// <param name="textureSlots">Every sampled-texture member of every block, in declaration order.</param>
    /// <param name="samplerSlots">Every sampler member of every block, in declaration order.</param>
    /// <param name="specializationAxes">Every generic value parameter of the module's entry points, in specialization argument order.</param>
    public ShaderLibraryReflection(
        IReadOnlyList<ShaderUniformBlock> uniformBlocks,
        IReadOnlyList<ShaderTextureSlot> textureSlots,
        IReadOnlyList<ShaderSamplerSlot> samplerSlots,
        IReadOnlyList<ShaderSpecializationAxis> specializationAxes)
    {
        UniformBlocks = uniformBlocks;
        TextureSlots = textureSlots;
        SamplerSlots = samplerSlots;
        SpecializationAxes = specializationAxes;
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

    /// <summary>
    /// Every generic value parameter of the module's generic entry points, in
    /// specialization argument order (entry definition order, each entry's
    /// value parameters in declaration order) — the named axes a material's
    /// specialization table binds by. A repeated name (the vertex and fragment
    /// entries both declaring <c>fadeInOut</c>) appears once per position; one
    /// named value feeds every position of that name. Modules whose entries
    /// take no value parameters have an empty list.
    /// </summary>
    public IReadOnlyList<ShaderSpecializationAxis> SpecializationAxes { get; }
}
