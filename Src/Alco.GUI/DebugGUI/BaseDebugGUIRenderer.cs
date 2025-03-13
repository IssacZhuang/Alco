using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;


public abstract class BaseDebugGUIRenderer: IDebugGUIRenderer, IDisposable
{
    

    //for rendering
    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly CanvasRenderer _canvasRenderer;
    
    private readonly Camera2D _camera;
    private readonly Texture2D _textureWhite;
    private RenderTexture _backBuffer;
    private BoundingBox2D _cameraMask;

    //blit
    private readonly RenderContext _renderer;
    private readonly Material _material;
    private readonly Mesh _mesh;    


    public abstract Vector2 MousePosition { get; }
    public abstract bool IsMouseClicked { get; }
    public abstract bool IsMousePressing { get; }


    protected BaseDebugGUIRenderer(float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite, Shader shaderBlit)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;
        //external resources
        _textureWhite = renderingSystem.TextureWhite;

        //internal resources
        _camera = renderingSystem.CreateCamera2D(width, height, 100, "debug_gui_camera_2d");
        _camera.Position = new Vector2(width / 2, -height / 2);
        Vector2 halfSize = _camera.ViewSize * 0.5f;
        _cameraMask = new BoundingBox2D(_camera.Position - halfSize, _camera.Position + halfSize);

        _canvasRenderer = _renderingSystem.CreateCanvasRenderer(_camera, shaderSprite, shaderText);



        _renderer = _renderingSystem.CreateRenderContext();
        _mesh = _renderingSystem.MeshFullScreen;

        _backBuffer = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedSDRPass, (uint)width, (uint)height, "debug_gui_backbuffer");

        _material = _renderingSystem.CreateGraphicsMaterial(shaderBlit);
        _material.SetRenderTexture(ShaderResourceId.Texture, _backBuffer);
        _material.DepthStencilState = DepthStencilState.Default;
        _material.BlendState = BlendState.AlphaBlend;
    }

    public void SetResolution(float width, float height)
    {
        _camera.ViewSize = new Vector2(width, height);
        _camera.Position = new Vector2(width / 2, -height / 2);
        Vector2 halfSize = _camera.ViewSize * 0.5f;
        _cameraMask = new BoundingBox2D(_camera.Position - halfSize, _camera.Position + halfSize);
        _backBuffer.Dispose();
        _backBuffer = _renderingSystem.CreateRenderTexture(_renderingSystem.PrefferedSDRPass, (uint)width, (uint)height, "debug_gui_backbuffer");
        _material.SetRenderTexture(ShaderResourceId.Texture, _backBuffer);
    }

    public void Begin()
    {
         _canvasRenderer.Begin(_backBuffer.FrameBuffer);
         _canvasRenderer.ClearColor(new ColorFloat(0, 0, 0, 0));
        //_canvasRenderer.Begin(_renderingSystem.DefaultFrameBuffer);
    }

    public void End()
    {
        _canvasRenderer.End();
    }

    public void Blit(GPUFrameBuffer frameBuffer)
    {
        _renderer.Begin(frameBuffer);
        _renderer.Draw(_mesh, _material);
        _renderer.End();
    }

    public void DrawQuad(Vector2 position, float depth, Vector2 size, ColorFloat color)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, size);
        _canvasRenderer.DrawSprite(_textureWhite, matrix, color, _cameraMask);
    }

    public unsafe float DrawText(Vector2 position, float depth, Font font, char* str, int strLength, float fontSize, ColorFloat color, Pivot pivot)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, Vector2.One* fontSize);
        return _canvasRenderer.DrawChars(font, str, strLength, matrix, pivot, color, 1f, _cameraMask);
    }

    public void DrawTexture(Vector2 position, float depth, Vector2 size, Texture2D texture, ColorFloat color)
    {
        Matrix4x4 matrix = GetTransformMatrix(position, depth, size);
        _canvasRenderer.DrawSprite(texture, matrix, color, _cameraMask);
    }

    public virtual void Dispose()
    {
        _canvasRenderer.Dispose();
        _camera.Dispose();
    }

    private Matrix4x4 GetTransformMatrix(Vector2 position, float depth, Vector2 size)
    {
        Matrix4x4 translation = math.matrix4translation(new Vector3(position, depth));
        Matrix4x4 scale = math.matrix4scale(new Vector3(size, 1));
        return scale * translation;
    }
}