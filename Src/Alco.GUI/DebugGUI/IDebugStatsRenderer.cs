using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public interface IDebugStatsRenderer
{
    public Vector2 MousePosition { get; }
    public bool IsMouseClicked { get; }
    public bool IsMousePressing { get; }

    public void SetResolution(float width, float height);
    public void Begin();
    public void End();
    public void DrawQuad(Vector2 position, Vector2 size, ColorFloat color);

    public unsafe float DrawText(ReadOnlySpan<char> str, Vector2 position, Font font, float fontSize, ColorFloat color, Pivot pivot);

    public void DrawTexture(Vector2 position, Vector2 size, Texture2D texture, ColorFloat color);
}