using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>Renders the debug stats overlay with immediate-mode draw calls.</summary>
public class DebugStatsRenderer : IDisposable
{
    private readonly Input _input;
    private readonly View _window;

    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;

    private readonly Camera2DBuffer _camera;
    private readonly Texture2D _textureWhite;

    private readonly RenderContext _rendererContent;
    private readonly TextRenderer _textRenderer;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly Mesh _mesh;

    // The open pass scope between Begin and End (the pass spans both calls),
    // plus the frame scope that submits when End runs.
    private RenderPassScope? _passScope;
    private RenderFrameScope? _frameScope;
    private bool _isBegun;

    /// <summary>Gets the cursor position in window coordinates.</summary>
    public Vector2 MousePosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _window.MousePosition;
    }

    /// <summary>Gets whether the left mouse button was pressed this frame.</summary>
    public bool IsMouseClicked
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMouseDown(Mouse.Left);
    }

    /// <summary>Gets whether the left mouse button is currently pressed.</summary>
    public bool IsMousePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMousePressing(Mouse.Left);
    }

    /// <summary>Creates a debug stats renderer and its overlay resources.</summary>
    /// <param name="input">The input system used for mouse state.</param>
    /// <param name="window">The window used for cursor coordinates.</param>
    /// <param name="width">The initial render width in pixels.</param>
    /// <param name="height">The initial render height in pixels.</param>
    /// <param name="renderingSystem">The rendering system that owns the created resources.</param>
    /// <param name="shaderText">The shader used for text.</param>
    /// <param name="shaderSprite">The shader used for sprites.</param>
    public DebugStatsRenderer(Input input, View window, float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite)
    {
        _input = input;
        _window = window;
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;
        _textureWhite = renderingSystem.TextureWhite;

        //internal resources
        _camera = renderingSystem.CreateCamera2D(width, height, 100, "debug_gui_camera_2d");
        _camera.Position = new Vector2(width / 2, -height / 2);
        Vector2 halfSize = _camera.ViewSize * 0.5f;

        GraphicsMaterial textMaterial = _renderingSystem.CreateGraphicsMaterial(shaderText);
        textMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        textMaterial.BlendState = BlendState.PremultipliedAlpha;

        _rendererContent = _renderingSystem.CreateRenderContext("debug_stats_content");
        _textRenderer = _renderingSystem.CreateTextRenderer(_rendererContent, textMaterial);

        // The sprite module is generic (MainPS<let Repeated : bool>): the debug
        // overlay pins the non-repeating default specialization.
        GraphicsMaterial spriteMaterial = _renderingSystem.CreateGraphicsMaterial(shaderSprite, "debug_stats_sprite", false);
        spriteMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        spriteMaterial.BlendState = BlendState.AlphaBlend;
        _spriteRenderer = _renderingSystem.CreateSpriteRenderer(_rendererContent, spriteMaterial);

        _mesh = _renderingSystem.MeshFullScreen;
    }

    /// <summary>Sets the render resolution in pixels.</summary>
    /// <param name="width">The render width in pixels.</param>
    /// <param name="height">The render height in pixels.</param>
    public void SetResolution(float width, float height)
    {
        _camera.ViewSize = new Vector2(width, height);
        _camera.Position = new Vector2(width / 2, -height / 2);
        _camera.UpdateMatrixToGPU();
    }

    /// <summary>Begins an overlay frame targeting the given framebuffer.</summary>
    /// <param name="target">The framebuffer to draw into.</param>
    public void Begin(GPUFrameBuffer target)
    {
        _isBegun = true;
        _frameScope = _rendererContent.BeginFrame();
        _passScope = _rendererContent.BeginPass(target);
    }

    /// <summary>Ends the current overlay frame.</summary>
    public void End()
    {
        if (_isBegun)
        {
            _passScope!.Dispose();
            _passScope = null;
            _frameScope!.Dispose();
            _frameScope = null;
            _isBegun = false;
        }
    }

    /// <summary>Draws a solid quad.</summary>
    /// <param name="position">The quad position.</param>
    /// <param name="size">The quad size.</param>
    /// <param name="color">The quad color.</param>
    public void DrawQuad(Vector2 position, Vector2 size, ColorFloat color)
    {
        if (!_isBegun)
        {
            return;
        }
        Matrix4x4 matrix = GetTransformMatrix(position, size);
        _spriteRenderer.Draw(_textureWhite, matrix, color);
    }

    /// <summary>Draws text and returns its advance width.</summary>
    /// <param name="str">The text to draw.</param>
    /// <param name="position">The text position.</param>
    /// <param name="font">The font to use.</param>
    /// <param name="fontSize">The font size in pixels.</param>
    /// <param name="color">The text color.</param>
    /// <param name="pivot">The text pivot.</param>
    /// <returns>The advance width of the drawn text.</returns>
    public unsafe float DrawText(ReadOnlySpan<char> str, Vector2 position, Font font, float fontSize, ColorFloat color, Pivot pivot)
    {
        if (!_isBegun)
        {
            return 0f;
        }
        Matrix4x4 matrix = GetTransformMatrix(position, Vector2.One * fontSize);
        return _textRenderer.DrawText(font, str, matrix, pivot, color, 1.0f);
    }

    /// <summary>Draws a texture.</summary>
    /// <param name="position">The texture position.</param>
    /// <param name="size">The texture size.</param>
    /// <param name="texture">The texture to draw.</param>
    /// <param name="color">The texture tint.</param>
    public void DrawTexture(Vector2 position, Vector2 size, Texture2D texture, ColorFloat color)
    {
        if (!_isBegun)
        {
            return;
        }
        Matrix4x4 matrix = GetTransformMatrix(position, size);
        _spriteRenderer.Draw(texture, matrix, color);
    }

    /// <summary>Releases the overlay resources.</summary>
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
