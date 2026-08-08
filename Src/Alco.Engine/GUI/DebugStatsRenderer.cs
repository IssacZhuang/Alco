using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

public class DebugStatsRenderer : IDisposable
{
    private readonly Input _input;
    private readonly View _window;

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

    private bool _isBegun;

    /// <summary>
    /// The frame buffer the stats overlay draws into (typically the swapchain).
    /// Set every frame before drawing; when null all drawing is skipped.
    /// </summary>
    public GPUFrameBuffer? Target { get; set; }

    public Vector2 MousePosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _window.MousePosition;
    }

    public bool IsMouseClicked
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMouseDown(Mouse.Left);
    }

    public bool IsMousePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMousePressing(Mouse.Left);
    }

    public DebugStatsRenderer(Input input, View window, float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite)
    {
        _input = input;
        _window = window;
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
        textMaterial.BlendState = BlendState.PremultipliedAlpha;

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
        //transparent background; skipped entirely while no target is set
        _isBegun = Target != null;
        if (_isBegun)
        {
            _rendererContent.Begin(Target!);
        }
    }

    public void End()
    {
        if (_isBegun)
        {
            _rendererContent.End();
            _isBegun = false;
        }
    }

    public void DrawQuad(Vector2 position, Vector2 size, ColorFloat color)
    {
        if (!_isBegun)
        {
            return;
        }
        Matrix4x4 matrix = GetTransformMatrix(position, size);
        _spriteRenderer.Draw(_textureWhite, matrix, color);
    }

    public unsafe float DrawText(ReadOnlySpan<char> str, Vector2 position, Font font, float fontSize, ColorFloat color, Pivot pivot)
    {
        if (!_isBegun)
        {
            return 0f;
        }
        Matrix4x4 matrix = GetTransformMatrix(position, Vector2.One * fontSize);
        return _textRenderer.DrawText(font, str, matrix, pivot, color, 1.0f);
    }

    public void DrawTexture(Vector2 position, Vector2 size, Texture2D texture, ColorFloat color)
    {
        if (!_isBegun)
        {
            return;
        }
        Matrix4x4 matrix = GetTransformMatrix(position, size);
        _spriteRenderer.Draw(texture, matrix, color);
    }

    public void Dispose()
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
