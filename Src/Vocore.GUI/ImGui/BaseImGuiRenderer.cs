using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;


public abstract class BaseImGuiRenderer: IImGuiRenderer, IDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private readonly TextRenderer _textRenderer;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly Camera2D _camera;
    private readonly Texture2D _textureWhite;


    public abstract Vector2 MousePosition { get; }
    public abstract bool IsMouseClicked { get; }

    protected BaseImGuiRenderer(float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite)
    {
        _renderingSystem = renderingSystem;
        //external resources

        _textureWhite = renderingSystem.TextureWhite;

        //internal resources
        _camera = renderingSystem.CreateCamera2D(width, height, 100);
        _textRenderer = renderingSystem.CreateTextRenderer(_camera, shaderText);
        _spriteRenderer = renderingSystem.CreateSpriteRenderer(_camera, shaderSprite);
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

    public void DrawQuad(Vector2 position, Vector2 size, ColorFloat color)
    {
        _spriteRenderer.Draw(_textureWhite, position, Rotation2D.Identity, size, color);
    }

    public unsafe float DrawText(Vector2 position, Font font, char* str, int strLength, float fontSize, ColorFloat color, Pivot pivot)
    {
       return _textRenderer.DrawChars(font, str, strLength, fontSize, position, Rotation2D.Identity, pivot, color);
    }

    public void DrawTexture(Vector2 position, Vector2 size, Texture2D texture, ColorFloat color)
    {
        _spriteRenderer.Draw(texture, position, Rotation2D.Identity, size, color);
    }

    public virtual void Dispose()
    {
        _spriteRenderer.Dispose();
        _textRenderer.Dispose();
        _camera.Dispose();
    }
}