using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;


public abstract class BaseDebugGUIRenderer: IDebugGUIRenderer, IDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private readonly CanvasRenderer _renderer;
    private readonly Camera2D _camera;
    private readonly Texture2D _textureWhite;
    private BoundingBox2D _cameraMask;


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
        Vector2 halfSize = _camera.Size * 0.5f;
        _cameraMask = new BoundingBox2D(_camera.Position - halfSize, _camera.Position + halfSize);

        _renderer = _renderingSystem.CreateCanvasRenderer(_camera, shaderSprite, shaderText);
    }

    public void SetResolution(float width, float height)
    {
        _camera.Size = new Vector2(width, height);
        _camera.Position = new Vector2(width / 2, -height / 2);
        Vector2 halfSize = _camera.Size * 0.5f;
        _cameraMask = new BoundingBox2D(_camera.Position - halfSize, _camera.Position + halfSize);
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
        // Matrix4x4 viewProj = _camera.Data.ViewProjectionMatrix;
        // Vector4 maskMin = new Vector4(_cameraMask.min, 0, 1f);
        // Vector4 maskMax = new Vector4(_cameraMask.max, 0, 1f);
        // Log.Info(Vector4.Transform(maskMin, viewProj), Vector4.Transform(maskMax, viewProj));
        Matrix4x4 matrix = GetTransformMatrix(position, depth, size);
        _renderer.DrawSprite(_textureWhite, matrix, color, _cameraMask);
    }

    public unsafe float DrawText(Vector2 position, float depth, Font font, char* str, int strLength, float fontSize, ColorFloat color, Pivot pivot)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, Vector2.One* fontSize);
        return _renderer.DrawChars(font, str, strLength, matrix, pivot, color, 1f, _cameraMask);
    }

    public void DrawTexture(Vector2 position, float depth, Vector2 size, Texture2D texture, ColorFloat color)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, size);
        _renderer.DrawSprite(texture, matrix, color, _cameraMask);
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
        return scale * translation;
    }
}