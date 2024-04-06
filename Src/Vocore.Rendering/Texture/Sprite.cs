namespace Vocore.Rendering;

public class Sprite
{
    private readonly Texture2D _texture;
    private readonly Rect _uvRect;
    private readonly bool _isInAtlas;

    internal Sprite(Texture2D texture, Rect uvRect, bool isInAtlas)
    {
        _texture = texture;
        _uvRect = uvRect;
        _isInAtlas = isInAtlas;
    }
}