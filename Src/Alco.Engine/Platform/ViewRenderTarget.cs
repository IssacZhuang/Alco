
using Alco.Graphics;
using Alco.Rendering;
using Alco.IO;
using System.Runtime.CompilerServices;

namespace Alco.Engine;

public class ViewRenderTarget : BaseEngineSystem, IRenderTarget
{
    public const int SystemOrder = 10000;
    private readonly View _view;
    private readonly RenderingSystem _rendering;
    private readonly GPUSwapchain? _viewSwapchain;
    private readonly GPUCommandBuffer _command;
    private GPUAttachmentLayout _attachmentLayout;
    private RenderTexture _renderTexture;

    private RenderContext _renderer;
    private Material _blitMaterial;
    private MaterialInstance? _customBlitMaterial;
    private Mesh _mesh;

    private Camera2DBuffer _screenCamera;

    private bool _shouldResize = false;
    private bool _isMinimized = false;
    private uint _width;
    private uint _height;

    /// <summary>
    /// Handle the view resize event on the end of the frame, safe to delete the GPU resources in the event.
    /// </summary>
    public event Action<uint2>? OnResize;

    /// <summary>
    /// The view that the render target is attached to
    /// </summary>
    /// <value></value>
    public View View
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _view;
    }

    /// <summary>
    /// The render texture of the render target
    /// <br/>[Attention] The render texture will be recreated when the view is resized
    /// </summary>
    /// <value></value>
    public RenderTexture RenderTexture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderTexture;
    }

    /// <summary>
    /// The frame buffer of the render target
    /// <br/>[Attention] The frame buffer will be recreated when the window is resized
    /// </summary>
    /// <value></value>
    public GPUFrameBuffer FrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderTexture.FrameBuffer;
    }

    /// <summary>
    /// The screen camera for 2D rendering that matches the render target size
    /// <br/>[Attention] The camera will be updated when the view is resized
    /// </summary>
    /// <value></value>
    public Camera2DBuffer ScreenCamera
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _screenCamera;
    }

    public override int Order => SystemOrder;

    internal ViewRenderTarget(GameEngine engine, View view, GPUAttachmentLayout attachmentLayout, Shader blitShader)
    {
        _view = view;
        _view.OnResize += OnWindowResize;
        _view.OnMinimize += OnWindowMinimize;
        _view.OnRestore += OnWindowRestore;

        _rendering = engine.RenderingSystem;

        _width = math.max(1, view.Size.X);
        _height = math.max(1, view.Size.Y);

        _attachmentLayout = attachmentLayout;
        _renderer = _rendering.CreateRenderContext();

        _mesh = _rendering.MeshFullScreen;

        _renderTexture = _rendering.CreateRenderTexture(attachmentLayout, _width, _height);

        _blitMaterial = _rendering.CreateMaterial(blitShader);
        _blitMaterial.SetRenderTexture(ShaderResourceId.Texture, _renderTexture);

        _viewSwapchain = view.Swapchain;

        _command = _rendering.GraphicsDevice.CreateCommandBuffer();

        // Create the screen camera with current dimensions
        _screenCamera = _rendering.CreateCamera2D(_width, _height, 1000, "screen_camera");
    }

    public void SetAttachmentLayout(GPUAttachmentLayout attachmentLayout, Material? blitMaterial = null)
    {
        ArgumentNullException.ThrowIfNull(attachmentLayout);
        if (!attachmentLayout.Equals(_attachmentLayout))
        {
            _attachmentLayout = attachmentLayout;
            _renderTexture.Dispose();
            _renderTexture = _rendering.CreateRenderTexture(attachmentLayout, _width, _height);
        }

        if (blitMaterial != null)
        {
            _customBlitMaterial = blitMaterial.CreateInstance();
            _customBlitMaterial.SetRenderTexture(ShaderResourceId.Texture, _renderTexture);
        }
    }

    public override void OnBeginFrame(float deltaTime)
    {
        _command.Begin();
        using (var renderPass = _command.BeginRender(_renderTexture.FrameBuffer, ColorFloat.Black, 1f, 0))
        {
        }
        _command.End();
        _rendering.GraphicsDevice.Submit(_command);
    }

    public override void OnEndFrame(float deltaTime)
    {
        if (_viewSwapchain == null)
        {
            return;
        }

        if (_isMinimized)
        {
            return;
        }

        if (!_viewSwapchain.RequestSurfaceTexture())
        {
            return;
        }

        //_converter.Blit(_windowSwapchain.FrameBuffer);
        _renderer.Begin(_viewSwapchain.FrameBuffer);
        if (_customBlitMaterial != null)
        {
            _renderer.Draw(_mesh, _customBlitMaterial);
        }
        else
        {
            _renderer.Draw(_mesh, _blitMaterial);
        }
        _renderer.End();
        _viewSwapchain.Present();

        if (_shouldResize)
        {
            RecreateRenderTexture();
            _shouldResize = false;
            OnResize?.Invoke(new uint2(_width, _height));
        }
    }


    public override void Dispose()
    {
        _view.OnResize -= OnWindowResize;
        _view.OnMinimize -= OnWindowMinimize;
        _view.OnRestore -= OnWindowRestore;

        _screenCamera.Dispose();
    }

    private void RecreateRenderTexture()
    {
        _renderTexture.Dispose();
        _renderTexture = _rendering.CreateRenderTexture(_attachmentLayout!, _width, _height);

        _blitMaterial.SetRenderTexture(ShaderResourceId.Texture, _renderTexture);
        if (_customBlitMaterial != null)
        {
            _customBlitMaterial.SetRenderTexture(ShaderResourceId.Texture, _renderTexture);
        }

        // Update screen camera dimensions to match new render texture size
        _screenCamera.Width = _width;
        _screenCamera.Height = _height;
        _screenCamera.UpdateMatrixToGPU();
    }

    private void OnWindowResize(uint2 size)
    {
        _shouldResize = true;
        _width = size.X;
        _height = size.Y;
        
        _viewSwapchain?.Resize(_width, _height);
    }

    private void OnWindowMinimize()
    {
        _isMinimized = true;
    }

    private void OnWindowRestore()
    {
        _isMinimized = false;
        //RecreateRenderTexture();
    }

}