using Alco.Graphics;

namespace Alco.Rendering;

public class Sprite
{
    public string Name { get; }
    public Texture2D Texture { get; }
    public Rect UVRect { get; }
    public bool IsInAtlas { get; }

    internal Sprite(string name, Texture2D texture, Rect uvRect, bool isInAtlas)
    {
        Name = name;
        Texture = texture;
        UVRect = uvRect;
        IsInAtlas = isInAtlas;
    }
}