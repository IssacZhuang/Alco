using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;


public abstract class ImGuiRenderer
{
    private readonly RenderingSystem _renderingSystem;
    private readonly TextRenderer _textRenderer;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly Camera2D _camera;
    private readonly Texture2D _textureWhite;
    private readonly Font _font;

    public abstract Vector2 MousePosition { get; }
    public abstract bool IsMouseClicked { get; }

    protected ImGuiRenderer(float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite, Font font)
    {
        _renderingSystem = renderingSystem;
        _font = font;
        _camera = renderingSystem.CreateCamera2D(width, height, 100);
        _textRenderer = renderingSystem.CreateTextRenderer(_camera, shaderText);
        _spriteRenderer = renderingSystem.CreateSpriteRenderer(_camera, shaderSprite);
        _textureWhite = renderingSystem.TextureWhite;
    }

    public void SetResolution(float width, float height)
    {
        _camera.Size = new Vector2(width, height);
    }

    public void Begin()
    {
        _textRenderer.Begin(_renderingSystem.DefaultFrameBuffer);
        _spriteRenderer.Begin(_renderingSystem.DefaultFrameBuffer);
    }

    public void End()
    {
        _textRenderer.End();
        _spriteRenderer.End();
    }

    public void DrawQuad(Vector2 position, Vector2 size, Vector4 color)
    {
        _spriteRenderer.Draw(_textureWhite, position, Rotation2D.Identity, size, color);
    }

    public void DrawText(Vector2 position, string text, float fontSize)
    {
        DrawText(position, text, fontSize, new Vector4(1, 1, 1, 1));
    }

    public void DrawText(Vector2 position, string text, float fontSize, Vector4 color)
    {
        DrawText(position, text, fontSize, color, Pivot.LeftTop);
    }

    public void DrawText(Vector2 position, string text, float fontSize, Vector4 color, Pivot pivot)
    {
        _textRenderer.DrawString(_font, text, fontSize, position, Rotation2D.Identity, pivot, color);
    }


    public void DrawTexture(Vector2 position, Vector2 size, Texture2D texture)
    {
        _spriteRenderer.Draw(texture, position, Rotation2D.Identity, size, new Vector4(1, 1, 1, 1));
    }
}