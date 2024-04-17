using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public class NoImGuiRenderer : IImGuiRenderer
{
    public Vector2 MousePosition => new Vector2();

    public bool IsMouseClicked => false;

    public void Begin()
    {
        
    }

    public void DrawQuad(Vector2 position, Vector2 size, ColorFloat color)
    {
        
    }

    public void DrawText(Vector2 position, Font font, string text, float fontSize, ColorFloat color, Pivot pivot)
    {
        
    }

    public void DrawTexture(Vector2 position, Vector2 size, Texture2D texture, ColorFloat color)
    {
        
    }

    public void End()
    {
        
    }

    public void SetResolution(float width, float height)
    {
        
    }
}