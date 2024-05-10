using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;


public abstract class BaseDebugGUIRenderer: IDebugGUIRenderer, IDisposable
{
    private static readonly float TextOffsetYMultiplier = 0.125f;
    private static readonly Matrix4x4 Rotation = math.matrix4rotation(Quaternion.Identity);
    private readonly RenderingSystem _renderingSystem;
    private readonly CanvasRenderer _renderer;
    private readonly Camera2D _camera;
    private readonly Texture2D _textureWhite;


    public abstract Vector2 MousePosition { get; }
    public abstract bool IsMouseClicked { get; }
    public abstract bool IsMousePressing { get; }


    protected BaseDebugGUIRenderer(float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite)
    {
        _renderingSystem = renderingSystem;
        //external resources

        _textureWhite = renderingSystem.TextureWhite;

        //internal resources
        _camera = renderingSystem.CreateCamera2D(width, height, 100);
        _camera.Position = new Vector2(width / 2, -height / 2);

        _renderer = _renderingSystem.CreateCanvasRenderer(_camera, shaderText, shaderSprite);
    }

    public void SetResolution(float width, float height)
    {
        _camera.Size = new Vector2(width, height);
        _camera.Position = new Vector2(width / 2, -height / 2);
    }

    public void Begin()
    {
        _renderer.Begin(_renderingSystem.DefaultFrameBuffer);
    }

    public void End()
    {
        _renderer.End();
    }

    public void DrawQuad(Vector2 position, float depth, Vector2 size, ColorFloat color)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, size);
        _renderer.Draw(_textureWhite, matrix, color);
    }

    public unsafe float DrawText(Vector2 position, float depth, Font font, char* str, int strLength, float fontSize, ColorFloat color, Pivot pivot)
    {
        position.Y += fontSize * TextOffsetYMultiplier;
        Matrix4x4 matrix = GetTransformMatrix(position, depth, Vector2.One* fontSize);
        return _renderer.DrawChars(font, str, strLength, matrix, pivot, color);
    }

    public void DrawTexture(Vector2 position, float depth, Vector2 size, Texture2D texture, ColorFloat color)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, size);
        _renderer.Draw(texture, matrix, color);
    }

    public virtual void Dispose()
    {
        _renderer.Dispose();
        _camera.Dispose();
    }

    private Matrix4x4 GetTransformMatrix(Vector2 position, float depth, Vector2 size)
    {
        Matrix4x4 translation = math.matrix4translation(new Vector3(position, depth));
        Matrix4x4 scale = math.matrix4scale(new Vector3(size, 1));
        return scale * Rotation * translation;
    }
}