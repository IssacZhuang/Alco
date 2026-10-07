namespace Alco.Graphics;

/// <summary>
/// Declares how a sampled texture binding appears to shaders.
/// </summary>
public struct TextureBindingInfo
{
    /// <summary>
    /// Initializes the info with view dimension and sample type.
    /// </summary>
    public TextureBindingInfo(TextureViewDimension viewDimension, TextureSampleType sampleType = TextureSampleType.Float)
    {
        ViewDimension = viewDimension;
        SampleType = sampleType;
    }

    /// <summary>
    /// How the texture is dimensioned in shaders.
    /// </summary>
    public TextureViewDimension ViewDimension { get; init; }
    /// <summary>
    /// The type shaders get when sampling the texture.
    /// </summary>
    public TextureSampleType SampleType { get; init; }

    /// <summary>
    /// A regular filterable 2D texture.
    /// </summary>
    public static readonly TextureBindingInfo Default2D = new(TextureViewDimension.Texture2D, TextureSampleType.Float);

    /// <summary>
    /// A 2D depth texture for comparison sampling (e.g. shadow maps).
    /// </summary>
    public static readonly TextureBindingInfo Depth2D = new(TextureViewDimension.Texture2D, TextureSampleType.Depth);

    /// <summary>
    /// Empty info marking "no texture"; used for non-sampled bindings.
    /// </summary>
    public static readonly TextureBindingInfo None = new()
    {
        ViewDimension = TextureViewDimension.Undefined
    };
}