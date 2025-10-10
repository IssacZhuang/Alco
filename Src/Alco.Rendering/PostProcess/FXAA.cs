using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// FXAA quality preset levels.
/// </summary>
public enum FXAAQuality
{
    /// <summary>
    /// Low quality - 4 search steps, fastest performance
    /// </summary>
    Low,

    /// <summary>
    /// Medium quality - 8 search steps, balanced
    /// </summary>
    Medium,

    /// <summary>
    /// High quality - 12 search steps, recommended for most games
    /// </summary>
    High,

    /// <summary>
    /// Ultra quality - 29 search steps, maximum quality at the cost of performance
    /// </summary>
    Ultra
}

/// <summary>
/// Fast Approximate Anti-Aliasing (FXAA) post-processing effect.
/// Provides screen-space anti-aliasing with minimal performance cost.
/// </summary>
public class FXAA : PostProcess
{
    /// <summary>
    /// Shader data structure for FXAA parameters
    /// </summary>
    private struct FXAAShaderData
    {
        public Vector2 InvFrameSize;    // 1.0 / frame size
        public float Threshold;         // Edge detection threshold (0.063-0.333, default: 0.125)
        public float Padding;           // Padding for alignment
    }

    // Shader resource identifiers
    public const string ShaderId_texture = "_texture";
    public const string ShaderId_fxaaData = "_fxaaData";

    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandFXAA;
    private readonly RenderingSystem _renderingSystem;

    // FXAA shader and pipeline
    private readonly Shader _fxaaShader;
    private GraphicsPipelineContext _fxaaPipelineInfo;
    private uint _fxaaShaderId_texture;
    private uint _fxaaShaderId_fxaaData;

    private FXAAQuality _quality = FXAAQuality.Medium;
    private string[] _currentDefines = Array.Empty<string>();

    // Blit shader and pipeline for final copy
    private readonly Shader _blitShader;
    private GraphicsPipelineContext _blitPipelineInfo;
    private uint _blitShaderId_texture;

    private readonly GraphicsValueBuffer<FXAAShaderData> _fxaaShaderData;

    protected RenderTexture? _input;
    private RenderTexture? _intermediateTexture;

    /// <summary>
    /// Gets or sets the FXAA quality preset.
    /// Changes will require recompiling the shader with appropriate defines.
    /// </summary>
    public FXAAQuality Quality
    {
        get => _quality;
        set
        {
            if (_quality != value)
            {
                _quality = value;
                UpdateShaderDefines();
            }
        }
    }

    /// <summary>
    /// Gets or sets the edge detection threshold.
    /// Lower values detect more edges but may introduce artifacts.
    /// Valid range: 0.063 - 0.333, Default: 0.125
    /// </summary>
    public float Threshold
    {
        get => _fxaaShaderData.Value.Threshold;
        set
        {
            var data = _fxaaShaderData.Value;
            data.Threshold = Math.Clamp(value, 0.063f, 0.333f);
            _fxaaShaderData.Value = data;
            _fxaaShaderData.UpdateBuffer();
        }
    }

    /// <summary>
    /// Initializes a new instance of the FXAA post-processing effect.
    /// </summary>
    /// <param name="renderingSystem">The rendering system instance</param>
    /// <param name="fxaaShader">The FXAA shader</param>
    /// <param name="blitShader">The blit shader for final copy</param>
    internal FXAA(RenderingSystem renderingSystem, Shader fxaaShader, Shader blitShader) : base(renderingSystem, fxaaShader)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;
        _fxaaShader = fxaaShader;
        _blitShader = blitShader;

        // Initialize FXAA pipeline context with default HIGH quality
        UpdateShaderDefines();

        // Initialize blit pipeline context
        _blitPipelineInfo = GraphicsPipelineContext.Default;
        _blitShader.TryUpdatePipelineContext(ref _blitPipelineInfo, renderingSystem.PrefferedSDRPass);
        _blitShaderId_texture = _blitPipelineInfo.GetResourceId(ShaderId_texture);

        // Create shader data buffer with default values
        _fxaaShaderData = renderingSystem.CreateGraphicsValueBuffer<FXAAShaderData>("fxaa_data");
        _fxaaShaderData.Value = new FXAAShaderData
        {
            InvFrameSize = Vector2.One,
            Threshold = 0.125f,
            Padding = 0.0f
        };
        _fxaaShaderData.UpdateBuffer();

        // Create command buffers
        _commandFXAA = _device.CreateCommandBuffer("fxaa_command_buffer");
    }

    /// <summary>
    /// Sets the input render texture for FXAA processing.
    /// </summary>
    /// <param name="input">The input render texture</param>
    public override void SetInput(RenderTexture input)
    {
        base.SetInput(input);
        _input = input;

        // Dispose old intermediate texture if it exists
        _intermediateTexture?.Dispose();

        // Create intermediate texture with same size as input
        _intermediateTexture = _renderingSystem.CreateRenderTexture(
            _renderingSystem.PrefferedSDRPass,
            input.Width,
            input.Height,
            "fxaa_intermediate"
        );

        // Update frame size for shader
        var data = _fxaaShaderData.Value;
        data.InvFrameSize = new Vector2(1.0f / input.Width, 1.0f / input.Height);
        _fxaaShaderData.Value = data;
        _fxaaShaderData.UpdateBuffer();
    }

    /// <summary>
    /// Applies FXAA anti-aliasing to the input and renders to the target framebuffer.
    /// </summary>
    /// <param name="target">The target framebuffer to render to</param>
    public override void Blit(GPUFrameBuffer target)
    {
        if (_input == null || _intermediateTexture == null)
        {
            throw new InvalidOperationException("Input render texture is not set. Call SetInput() first.");
        }

        Mesh fullScreenMesh = FullScreenMesh;

        if (_blitShader.TryUpdatePipelineContext(ref _blitPipelineInfo, target.AttachmentLayout))
        {
            _blitShaderId_texture = _blitPipelineInfo.GetResourceId(ShaderId_texture);
        }

        _commandFXAA.Begin();

        using (var renderPass = _commandFXAA.BeginRender(_intermediateTexture.FrameBuffer))
        {
            renderPass.SetPipeline(_fxaaPipelineInfo.Pipeline!);
            uint indexCount = renderPass.SetMesh(fullScreenMesh);
            renderPass.SetResources(_fxaaShaderId_texture, _input.ColorTextures[0].EntrySample);
            renderPass.SetResources(_fxaaShaderId_fxaaData, _fxaaShaderData.EntryReadonly);
            renderPass.DrawIndexed(indexCount, 1, 0, 0, 0);
        }

        using (var renderPass = _commandFXAA.BeginRender(target))
        {
            renderPass.SetPipeline(_blitPipelineInfo.Pipeline!);
            uint indexCount = renderPass.SetMesh(fullScreenMesh);
            renderPass.SetResources(_blitShaderId_texture, _intermediateTexture.ColorTextures[0].EntrySample);
            renderPass.DrawIndexed(indexCount, 1, 0, 0, 0);
        }

        _commandFXAA.End();
        _renderingSystem.ScheduleCommandBuffer(_commandFXAA);
    }

    /// <summary>
    /// Updates the shader defines based on the current quality setting.
    /// </summary>
    private void UpdateShaderDefines()
    {
        string qualityDefine = _quality switch
        {
            FXAAQuality.Low => "FXAA_QUALITY_LOW",
            FXAAQuality.Medium => "FXAA_QUALITY_MEDIUM",
            FXAAQuality.High => "FXAA_QUALITY_HIGH",
            FXAAQuality.Ultra => "FXAA_QUALITY_ULTRA",
            _ => "FXAA_QUALITY_HIGH"
        };

        _currentDefines = new[] { qualityDefine };

        // Get a new pipeline with the specified defines
        _fxaaPipelineInfo = _fxaaShader.GetGraphicsPipeline(_renderingSystem.PrefferedSDRPass, _currentDefines);

        // Update resource IDs after pipeline recreation
        _fxaaShaderId_texture = _fxaaPipelineInfo.GetResourceId(ShaderId_texture);
        _fxaaShaderId_fxaaData = _fxaaPipelineInfo.GetResourceId(ShaderId_fxaaData);
    }

    /// <summary>
    /// Disposes of resources used by the FXAA effect.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _commandFXAA.Dispose();
            _fxaaShaderData.Dispose();
            _intermediateTexture?.Dispose();
        }
        base.Dispose(disposing);
    }
}