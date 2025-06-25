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

    

    public void DrawQuad(Vector2 position, float depth, Vector2 size, ColorFloat color)
    {
        
    }

    public unsafe float DrawText(Vector2 position, float depth, Font font, char* str, int strLength, float fontSize, ColorFloat color, Pivot pivot)
    {
        return 0;
    }

    public void DrawTexture(Vector2 position, float depth, Vector2 size, Texture2D texture, ColorFloat color)
    {
        
    }

    public void End()
    {
        
    }

    public void Blit(GPUFrameBuffer frameBuffer)
    {

    }

    public void SetResolution(float width, float height)
    {
        
    }
}