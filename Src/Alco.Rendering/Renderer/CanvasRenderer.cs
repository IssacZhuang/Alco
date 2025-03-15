using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The renderer for UI rendering.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary>
public sealed partial class CanvasRenderer : AutoDisposable
{
    private enum RenderingState
    {
        None,
        Text,
        Sprite
    }

    public const float Depth = 100;
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly Texture2D _textWhite;
    private GPUFrameBuffer? _renderTarget;
    private GraphicsBuffer _camera;
    private RenderingState _state;
    private bool _isDrawing;

    //sprite properties
    private readonly Shader _shaderSprite;
    private GraphicsPipelineContext _pipelineInfoSprite;
    private readonly Mesh _meshSprite;

    private uint _spriteShaderId_camera;
    private uint _spriteShaderId_texture;

    //text properties
    private readonly Shader _shaderText;
    private GraphicsPipelineContext _pipelineInfoText;
    private readonly Mesh _meshText;

    private uint _textShaderId_camera;
    private uint _textShaderId_textBuffer;
    private uint _textShaderId_font;

    public CanvasRenderer(RenderingSystem renderingSystem, GraphicsBuffer camera, Shader shaderSpirte, Shader shaderText)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _camera = camera;
        _command = _device.CreateCommandBuffer();

        _textWhite = renderingSystem.TextureWhite;

        //init test rendering
        _meshText = renderingSystem.MeshTrueType;
        _textBufferGPU = renderingSystem.CreateGraphicsArrayBuffer<TextData>(MaxTextInstancingCount);
        _textBufferCPU = new NativeBuffer<TextData>(MaxTextInstancingCount);
        _shaderText = shaderText;

        _pipelineInfoText = _shaderText.GetGraphicsPipeline(
            renderingSystem.PrefferedSDRPass,
            DepthStencilState.Default,
            BlendState.AlphaBlend
        );

        _textShaderId_camera = _pipelineInfoText.GetResourceId(ShaderResourceId.Camera);
        _textShaderId_textBuffer = _pipelineInfoText.GetResourceId(ShaderResourceId.TextBuffer);
        _textShaderId_font = _pipelineInfoText.GetResourceId(ShaderResourceId.Font);

        //init sprite rendering
        _meshSprite = renderingSystem.MeshCenteredSprite;
        _shaderSprite = shaderSpirte;

        _pipelineInfoSprite = _shaderSprite.GetGraphicsPipeline(
            renderingSystem.PrefferedSDRPass,
            DepthStencilState.Default,
            BlendState.AlphaBlend
        );

        _spriteShaderId_camera = _pipelineInfoSprite.GetResourceId(ShaderResourceId.Camera);
        _spriteShaderId_texture = _pipelineInfoSprite.GetResourceId(ShaderResourceId.Texture);
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

        if (_shaderSprite.TryUpdatePipelineContext(ref _pipelineInfoSprite, target.RenderPass))
        {
            _spriteShaderId_camera = _pipelineInfoSprite.GetResourceId(ShaderResourceId.Camera);
            _spriteShaderId_texture = _pipelineInfoSprite.GetResourceId(ShaderResourceId.Texture);
        }

        if (_shaderText.TryUpdatePipelineContext(ref _pipelineInfoText, target.RenderPass))
        {
            _textShaderId_camera = _pipelineInfoText.GetResourceId(ShaderResourceId.Camera);
            _textShaderId_textBuffer = _pipelineInfoText.GetResourceId(ShaderResourceId.TextBuffer);
            _textShaderId_font = _pipelineInfoText.GetResourceId(ShaderResourceId.Font);
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
        _renderingSystem.ScheduleCommandBuffer(_command);
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