using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public class NoDebugStatsRenderer : IDebugStatsRenderer
{
    public Vector2 MousePosition => new Vector2();

    public bool IsMouseClicked => false;

    public bool IsMousePressing => false;

    public void Begin()
    {
        
    }

    

    public void DrawQuad(Vector2 position, Vector2 size, ColorFloat color)
    {
        
    }

    public unsafe float DrawText(ReadOnlySpan<char> str, Vector2 position, Font font, float fontSize, ColorFloat color, Pivot pivot)
    {
        return 0;
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