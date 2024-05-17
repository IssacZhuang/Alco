using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public interface IDebugGUIRenderer
{
    public Vector2 MousePosition { get; }
    public bool IsMouseClicked { get; }
    public bool IsMousePressing { get; }

    public void SetResolution(float width, float height);
    public void Begin();
    public void End();
    public void Blit();
    public void DrawQuad(Vector2 position, float depth, Vector2 size, ColorFloat color);

    public unsafe float DrawText(Vector2 position, float depth, Font font, char* str, int strLength, float fontSize, ColorFloat color, Pivot pivot);

    public void DrawTexture(Vector2 position, float depth, Vector2 size, Texture2D texture, ColorFloat color);
}