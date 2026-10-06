namespace Alco.Graphics;

/// <summary>
/// The creation information for a texture view: which sub-range of a texture's
/// mips, layers and aspects the view exposes, and how it is dimensioned.
/// </summary>
public struct TextureViewDescriptor
{
    /// <summary>
    /// Initializes the descriptor with the texture to view and its sub-range.
    /// </summary>
    public TextureViewDescriptor(
        GPUTexture texture,
        TextureViewDimension dimension = TextureViewDimension.Texture2D,
        uint baseMipLevel = 0,
        uint mipLevelCount = 1,
        uint baseArrayLayer = 0,
        uint arrayLayerCount = 1,
        TextureAspect aspect = TextureAspect.All,
        string name = "unnamed_texture_view")
    {
        Name = name;
        Dimension = dimension;
        BaseMipLevel = baseMipLevel;
        MipLevelCount = mipLevelCount;
        BaseArrayLayer = baseArrayLayer;
        ArrayLayerCount = arrayLayerCount;
        Aspect = aspect;
        Texture = texture;
    }


    /// <summary>
    /// How the view is dimensioned for shaders (2D, cube, 2D array, ...); must be
    /// compatible with the texture's dimension and layer count.
    /// </summary>
    public TextureViewDimension Dimension { get; init; } = TextureViewDimension.Texture2D;
    /// <summary>
    /// The first mip level the view exposes.
    /// </summary>
    public uint BaseMipLevel { get; init; } = 0;
    /// <summary>
    /// How many mip levels the view exposes; 0 exposes all remaining levels from
    /// <see cref="BaseMipLevel"/>.
    /// </summary>
    public uint MipLevelCount { get; init; } = 1;
    /// <summary>
    /// The first array layer the view exposes.
    /// </summary>
    public uint BaseArrayLayer { get; init; } = 0;
    /// <summary>
    /// How many array layers the view exposes; 0 exposes all remaining layers from
    /// <see cref="BaseArrayLayer"/>.
    /// </summary>
    public uint ArrayLayerCount { get; init; } = 1;
    /// <summary>
    /// Which aspect (color, depth or stencil) of the texture the view exposes.
    /// </summary>
    public TextureAspect Aspect { get; init; } = TextureAspect.All;
    /// <summary>
    /// The texture the view is created on.
    /// </summary>
    public GPUTexture Texture { get; init; }
    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unnamed_texture_view";
}