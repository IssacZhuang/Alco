namespace Vocore.Graphics;

public struct TextureViewDescriptor
{
    public TextureViewDescriptor(
        GPUTexture texture,
        TextureViewDimension dimension,
        uint baseMipLevel = 0,
        uint mipLevelCount = 1,
        uint baseArrayLayer = 0,
        uint arrayLayerCount = 1,
        string name = "Unnamed texture view")
    {
        Name = name;
        Dimension = dimension;
        BaseMipLevel = baseMipLevel;
        MipLevelCount = mipLevelCount;
        BaseArrayLayer = baseArrayLayer;
        ArrayLayerCount = arrayLayerCount;
        Texture = texture;
    }

    public TextureViewDimension Dimension { get; init; }
    public uint BaseMipLevel { get; init; } = 0;
    public uint MipLevelCount { get; init; } = 1;
    public uint BaseArrayLayer { get; init; } = 0;
    public uint ArrayLayerCount { get; init; } = 1;
    public GPUTexture Texture { get; init; }
    public string Name { get; init; } = "unnamed_texture_view";
}