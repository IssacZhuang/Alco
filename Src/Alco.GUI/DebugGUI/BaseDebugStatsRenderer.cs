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
    private RenderTexture _backBuffer;

    //blit
    private readonly RenderContext _rendererBlit;
    private readonly RenderContext _rendererContent;
    private readonly TextRenderer _textRenderer;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly Material _material;
    private readonly Mesh _mesh;


    public abstract Vector2 MousePosition { get; }
    public abstract bool IsMouseClicked { get; }
    public abstract bool IsMousePressing { get; }


    protected BaseDebugStatsRenderer(float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite, Shader shaderBlit)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;
        //external resources
        _textureWhite = renderingSystem.TextureWhite;

        //internal resources
        _camera = renderingSystem.CreateCamera2D(width, height, 100, "debug_gui_camera_2d");
        _camera.Position = new Vector2(width / 2, -height / 2);
        Vector2 halfSize = _camera.ViewSize * 0.5f;

        Material textMaterial = _renderingSystem.CreateMaterial(shaderText);
        textMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        textMaterial.BlendState = BlendState.AlphaBlend;

        _rendererBlit = _renderingSystem.CreateRenderContext("debug_stats_blit");
        _rendererContent = _renderingSystem.CreateRenderContext("debug_stats_content");
        _textRenderer = _renderingSystem.CreateTextRenderer(_rendererContent, textMaterial);

        Material spriteMaterial = _renderingSystem.CreateMaterial(shaderSprite);
        spriteMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        spriteMaterial.BlendState = BlendState.AlphaBlend;
        _spriteRenderer = _renderingSystem.CreateSpriteRenderer(_rendererContent, spriteMaterial);

        _mesh = _renderingSystem.MeshFullScreen;

        _backBuffer = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedSDRPass, (uint)width, (uint)height, "debug_gui_backbuffer");

        _material = _renderingSystem.CreateMaterial(shaderBlit);
        _material.SetRenderTexture(ShaderResourceId.Texture, _backBuffer);
        _material.DepthStencilState = DepthStencilState.Default;
        _material.BlendState = BlendState.AlphaBlend;
    }

    public void SetResolution(float width, float height)
    {
        _camera.ViewSize = new Vector2(width, height);
        _camera.Position = new Vector2(width / 2, -height / 2);
        Vector2 halfSize = _camera.ViewSize * 0.5f;
        _backBuffer.Dispose();
        _backBuffer = _renderingSystem.CreateRenderTexture(_renderingSystem.PrefferedSDRPass, (uint)width, (uint)height, "debug_gui_backbuffer");
        _material.SetRenderTexture(ShaderResourceId.Texture, _backBuffer);
    }

    public void Begin()
    {
        _rendererContent.Begin(_backBuffer.FrameBuffer, Vector4.Zero);//tranparent background
    }

    public void End()
    {
        _rendererContent.End();
    }

    public void Blit(GPUFrameBuffer frameBuffer)
    {
        _rendererBlit.Begin(frameBuffer);
        _rendererBlit.Draw(_mesh, _material);
        _rendererBlit.End();
    }

    public void DrawQuad(Vector2 position, float depth, Vector2 size, ColorFloat color)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, size);
        _spriteRenderer.Draw(_textureWhite, matrix, color);
    }

    public unsafe float DrawText(Vector2 position, float depth, Font font, char* str, int strLength, float fontSize, ColorFloat color, Pivot pivot)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, Vector2.One* fontSize);
        return _textRenderer.DrawChars(font, str, strLength, matrix, pivot, color, 1.0f);
    }

    public void DrawTexture(Vector2 position, float depth, Vector2 size, Texture2D texture, ColorFloat color)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, size);
        _spriteRenderer.Draw(texture, matrix, color);
    }

    public virtual void Dispose()
    {
        _camera.Dispose();
    }

    private static Matrix4x4 GetTransformMatrix(Vector2 position, float depth, Vector2 size)
    {
        Matrix4x4 translation = math.matrix4translation(new Vector3(position, depth));
        Matrix4x4 scale = math.matrix4scale(new Vector3(size, 1));
        return scale * translation;
    }
}