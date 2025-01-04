using Vocore.Graphics;

namespace Vocore.Rendering;

public class Sprite
{
    private readonly RenderTexture _renderTexture;
    public string Name { get; }
    public GPUResourceGroup EntrySample { get; }
    public Rect UVRect { get; }
    public bool IsInAtlas { get; }

    internal Sprite(string name, RenderTexture renderTexture, Rect uvRect, bool isInAtlas)
    {
        Name = name;
        _renderTexture = renderTexture;
        EntrySample = _renderTexture.EntriesColorSample[0];
        UVRect = uvRect;
        IsInAtlas = isInAtlas;
    }
}