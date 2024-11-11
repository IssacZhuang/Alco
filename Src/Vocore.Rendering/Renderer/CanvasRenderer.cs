using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The renderer for UI rendering.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary>
public partial class CanvasRenderer : AutoDisposable, IRenderer
{
    private enum RenderingState
    {
        None,
        Text,
        Sprite
    }

    public const string ShaderId_camera = "_camera";
    public const string ShaderId_texture = "_texture";
    public const string ShaderId_textBuffer = "_textBuffer";
    public const string ShaderId_font = "_font";

    public const float Depth = 100;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly Texture2D _textWhite;
    private GPUFrameBuffer? _renderTarget;
    private GraphicsBuffer _camera;
    private RenderingState _state;
    private bool _isDrawing;

    //sprite properties
    private readonly Shader _shaderSprite;
    private GraphicsPipelineInfo _pipelineInfoSprite;
    private readonly Mesh _meshSprite;

    private uint _spriteShaderId_camera;
    private uint _spriteShaderId_texture;

    //text properties
    private readonly Shader _shaderText;
    private GraphicsPipelineInfo _pipelineInfoText;
    private readonly Mesh _meshText;

    private uint _textShaderId_camera;
    private uint _textShaderId_textBuffer;
    private uint _textShaderId_font;

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

        _pipelineInfoText = _shaderText.GetGraphicsPipeline(
            system.PrefferedSDRPass,
            DepthStencilState.Default,
            BlendState.AlphaBlend
        );

        _textShaderId_camera = _pipelineInfoText.GetResourceId(ShaderId_camera);
        _textShaderId_textBuffer = _pipelineInfoText.GetResourceId(ShaderId_textBuffer);
        _textShaderId_font = _pipelineInfoText.GetResourceId(ShaderId_font);

        //init sprite rendering
        _meshSprite = system.MeshSprite;
        _shaderSprite = shaderSpirte;

        _pipelineInfoSprite = _shaderSprite.GetGraphicsPipeline(
            system.PrefferedSDRPass,
            DepthStencilState.Default,
            BlendState.AlphaBlend
        );

        _spriteShaderId_camera = _pipelineInfoSprite.GetResourceId(ShaderId_camera);
        _spriteShaderId_texture = _pipelineInfoSprite.GetResourceId(ShaderId_texture);
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

        if(_shaderSprite.TryUpdatePipelineInfo(ref _pipelineInfoSprite, target.RenderPass))
        {
            _spriteShaderId_camera = _pipelineInfoSprite.GetResourceId(ShaderId_camera);
            _spriteShaderId_texture = _pipelineInfoSprite.GetResourceId(ShaderId_texture);
        }

        if(_shaderText.TryUpdatePipelineInfo(ref _pipelineInfoText, target.RenderPass))
        {
            _textShaderId_camera = _pipelineInfoText.GetResourceId(ShaderId_camera);
            _textShaderId_textBuffer = _pipelineInfoText.GetResourceId(ShaderId_textBuffer);
            _textShaderId_font = _pipelineInfoText.GetResourceId(ShaderId_font);
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
        _state = RenderingState.None;
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
        //dipose native resources
        _textBufferCPU.Dispose();
        //dispose private resources
        _textBufferGPU.Dispose();
        _command.Dispose();
    }
}