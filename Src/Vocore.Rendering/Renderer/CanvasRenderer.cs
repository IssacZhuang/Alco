using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The renderer for UI rendering.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary>
public partial class CanvasRenderer : AutoDisposable
{
    private enum RenderingState
    {
        None,
        Text,
        Sprite
    }

    public const float Depth = 100;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly Texture2D _textWhite;
    private GPUFrameBuffer? _renderTarget;
    private GPURenderPass? _currentPass;
    private GraphicsBuffer _camera;
    private RenderingState _state;
    private bool _isDrawing;



    public CanvasRenderer(RenderingSystem system, GraphicsBuffer camera, Shader shaderSpirte, Shader shaderText)
    {
        _device = system.GraphicsDevice;
        _camera = camera;
        _command = _device.CreateCommandBuffer();

        _textWhite = system.TextureWhite;

        //init test rendering
        _meshText = system.MeshTrueType;
        _textBufferGPU = system.CreateGraphicsArrayBuffer<TextData>(MaxTextInstancingCount);
        _textBufferCPU = new NativeBuffer<TextData>(MaxTextInstancingCount);
        _shaderText = shaderText;
        _textShaderId_camera = shaderText.GetResourceId("_camera");
        _textShaderId_textBuffer = shaderText.GetResourceId("_textBuffer");
        _textShaderId_font = shaderText.GetResourceId("_font");

        //init sprite rendering
        _meshSprite = system.MeshSprite;
        _shaderSprite = shaderSpirte;
        _spriteShaderId_camera = shaderSpirte.GetResourceId("_camera");
        _spriteShaderId_texture = shaderSpirte.GetResourceId("_texture");
    }


    public GraphicsBuffer Camera
    {
        get => _camera;
    }

    /// <summary>
    ///  Begin drawing text on the target frame buffer.
    /// </summary>
    /// <param name="target">The target frame buffer to draw text on.</param>
    /// <exception cref="InvalidOperationException">TextRenderer.Begin() called twice without calling End()</exception>
    /// <exception cref="ArgumentNullException">The render target is null</exception>
    public void Begin(GPUFrameBuffer target)
    {
        if (_isDrawing)
        {
            throw new InvalidOperationException("TextRenderer.Begin() called twice without calling End()");
        }

        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (target.RenderPass != _currentPass)
        {
            //compile shader variant
            _currentPass = target.RenderPass;
            _pipelineText = _shaderText.GetPipelineVariant(target.RenderPass);
            _pipelineSprite = _shaderSprite.GetPipelineVariant(target.RenderPass);
        }

        _renderTarget = target;
        _isDrawing = true;
        BeginDraw();
        _textInstanceIndex = 0;
    }

    /// <summary>
    /// End drawing text and submit the command to GPU
    /// </summary>
    public void End()
    {
        if (!_isDrawing)
        {
            throw new InvalidOperationException("TextRenderer.End() called without calling Begin()");
        }

        FlushBuffer();
        _renderTarget = null;
        _isDrawing = false;
        _state = RenderingState.None;
    }

    /// <summary>
    /// Clear thre render target with the color.
    /// </summary>
    /// <param name="color">The color to clear the render target.</param>
    public void ClearColor(ColorFloat color)
    {
        _command.ClearColor(color);
    }

    // called when command end or text buffer is full
    private void FlushBuffer()
    {
        _textBufferGPU.UpdateBufferRanged(0, (uint)_textInstanceIndex);
        _command.End();
        _device.Submit(_command);
        _textInstanceIndex = 0;
    }

    private void BeginDraw()
    {
        _command.Begin();
        _command.SetFrameBuffer(_renderTarget!);
    }


    private void SetState(RenderingState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        switch (state)
        {
            case RenderingState.Text:
                SetTextPipeline();
                break;
            case RenderingState.Sprite:
                SetSpritePipeline();
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        _textBufferCPU.Dispose();
        _textBufferGPU.Dispose();
        _command.Dispose();

        _currentPass = null;
        _pipelineSprite = null;
        _pipelineText = null;
    }
}