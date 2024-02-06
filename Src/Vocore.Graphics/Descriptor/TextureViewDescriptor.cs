namespace Vocore.Graphics;

public struct TextureViewDescriptor
{
    public TextureViewDescriptor(
        GPUTexture texture,
        TextureViewDimension dimension, 
        string name = "Unnamed texture view")
    {
        Name = name;
        Dimension = dimension;
        Texture = texture;
    }

    public TextureViewDimension Dimension { get; init; }
    public GPUTexture Texture { get; init; }
    public string Name { get; init; } = "Unnamed texture view";
}