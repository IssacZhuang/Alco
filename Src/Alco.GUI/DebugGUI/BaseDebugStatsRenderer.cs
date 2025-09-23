using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;


public abstract class BaseDebugStatsRenderer : IDebugStatsRenderer, IDisposable
{
    

    //for rendering
    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;

    private readonly Camera2DBuffer _camera;
    private readonly Texture2D _textureWhite;

    //blit
    private readonly RenderContext _rendererContent;
    private readonly TextRenderer _textRenderer;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly Mesh _mesh;

    private IRenderTarget _renderTarget;

    public abstract Vector2 MousePosition { get; }
    public abstract bool IsMouseClicked { get; }
    public abstract bool IsMousePressing { get; }


    protected BaseDebugStatsRenderer(float width, float height, IRenderTarget renderTarget,RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;
        //external resources
        _textureWhite = renderingSystem.TextureWhite;

        _renderTarget = renderTarget;

        //internal resources
        _camera = renderingSystem.CreateCamera2D(width, height, 100, "debug_gui_camera_2d");
        _camera.Position = new Vector2(width / 2, -height / 2);
        Vector2 halfSize = _camera.ViewSize * 0.5f;

        Material textMaterial = _renderingSystem.CreateMaterial(shaderText);
        textMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        textMaterial.BlendState = BlendState.AlphaBlend;

        _rendererContent = _renderingSystem.CreateRenderContext("debug_stats_content");
        _textRenderer = _renderingSystem.CreateTextRenderer(_rendererContent, textMaterial);

        Material spriteMaterial = _renderingSystem.CreateMaterial(shaderSprite);
        spriteMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        spriteMaterial.BlendState = BlendState.AlphaBlend;
        _spriteRenderer = _renderingSystem.CreateSpriteRenderer(_rendererContent, spriteMaterial);

        _mesh = _renderingSystem.MeshFullScreen;


    }

    public void SetResolution(float width, float height)
    {
        _camera.ViewSize = new Vector2(width, height);
        _camera.Position = new Vector2(width / 2, -height / 2);
        _camera.UpdateMatrixToGPU();
    }

    public void Begin()
    {
        _rendererContent.Begin(_renderTarget.RenderTexture.FrameBuffer);//tranparent background
    }

    public void End()
    {
        _rendererContent.End();
    }


    public void DrawQuad(Vector2 position, Vector2 size, ColorFloat color)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, size);
        _spriteRenderer.Draw(_textureWhite, matrix, color);
    }

    public unsafe float DrawText(ReadOnlySpan<char> str, Vector2 position, Font font, float fontSize, ColorFloat color, Pivot pivot)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, Vector2.One* fontSize);
        return _textRenderer.DrawChars(font, str, matrix, pivot, color, 1.0f);
    }

    public void DrawTexture(Vector2 position, Vector2 size, Texture2D texture, ColorFloat color)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, size);
        _spriteRenderer.Draw(texture, matrix, color);
    }

    public virtual void Dispose()
    {
        _rendererContent.Dispose();
        _textRenderer.Dispose();
        _spriteRenderer.Dispose();
        _camera.Dispose();
    }

    private static Matrix4x4 GetTransformMatrix(Vector2 position, Vector2 size)
    {
        Matrix4x4 translation = math.matrix4translation(new Vector3(position, 0));
        Matrix4x4 scale = math.matrix4scale(new Vector3(size, 1));
        return scale * translation;
    }
}