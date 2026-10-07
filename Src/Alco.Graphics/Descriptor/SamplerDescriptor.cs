namespace Alco.Graphics;

/// <summary>
/// The creation information for a texture sampler: filtering, address modes,
/// LOD clamps and comparison behavior.
/// </summary>
public struct SamplerDescriptor
{
    /// <summary>
    /// A general-purpose default: trilinear filtering, clamp-to-edge addressing,
    /// no comparison sampling, no anisotropy.
    /// </summary>
    public static readonly SamplerDescriptor Default = new(
        FilterMode.Linear,
        FilterMode.Linear,
        FilterMode.Linear,
        AddressMode.ClampToEdge,
        AddressMode.ClampToEdge,
        AddressMode.ClampToEdge,
        0,
        32f,
        CompareFunction.Undefined,
        1,
        "default_sampler"
        );

    /// <summary>
    /// Initializes the descriptor with filtering, address modes, LOD clamps,
    /// comparison and anisotropy settings.
    /// </summary>
    public SamplerDescriptor(
        FilterMode minFilter,
        FilterMode magFilter,
        FilterMode mipFilter,
        AddressMode addressModeU,
        AddressMode addressModeV,
        AddressMode addressModeW,
        float lodMinClamp = 0,
        float lodMaxClamp = 32f,
        CompareFunction compare = CompareFunction.Undefined,
        ushort maxAnisotropy = 1,
        string name = "unamed_sampler"
        )
    {
        MinFilter = minFilter;
        MagFilter = magFilter;
        MipFilter = mipFilter;
        AddressModeU = addressModeU;
        AddressModeV = addressModeV;
        AddressModeW = addressModeW;
        LodMinClamp = lodMinClamp;
        LodMaxClamp = lodMaxClamp;
        Compare = compare;
        MaxAnisotropy = maxAnisotropy;
        Name = name;
    }

    /// <summary>
    /// Filtering applied when a texel covers less than one pixel (shrinking).
    /// </summary>
    public FilterMode MinFilter { get; init; }
    /// <summary>
    /// Filtering applied when a texel covers more than one pixel (magnifying).
    /// </summary>
    public FilterMode MagFilter { get; init; }
    /// <summary>
    /// Filtering applied between mip levels.
    /// </summary>
    public FilterMode MipFilter { get; init; }
    /// <summary>
    /// Addressing of out-of-range U (first) texture coordinates.
    /// </summary>
    public AddressMode AddressModeU { get; init; }
    /// <summary>
    /// Addressing of out-of-range V (second) texture coordinates.
    /// </summary>
    public AddressMode AddressModeV { get; init; }
    /// <summary>
    /// Addressing of out-of-range W (third) texture coordinates.
    /// </summary>
    public AddressMode AddressModeW { get; init; }
    /// <summary>
    /// The minimum mip level that can be sampled, in mip levels.
    /// </summary>
    public float LodMinClamp { get; init; } = 0;
    /// <summary>
    /// The maximum mip level that can be sampled, in mip levels.
    /// </summary>
    public float LodMaxClamp { get; init; } = 32f;
    /// <summary>
    /// Undefined = plain sampling; any other value enables comparison sampling
    /// (e.g. shadow-map depth comparison).
    /// </summary>
    public CompareFunction Compare { get; init; } = CompareFunction.Undefined;
    /// <summary>
    /// Anisotropy clamp; 1 = anisotropic filtering disabled, 16 = maximum.
    /// </summary>
    public ushort MaxAnisotropy { get; init; } = 1;
    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unamed_sampler";
}