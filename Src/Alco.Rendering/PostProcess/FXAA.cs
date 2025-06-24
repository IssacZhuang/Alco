using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

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
        public float Quality;           // Quality setting (0.5-2.0, default: 1.0)
        public float Threshold;         // Edge detection threshold (0.063-0.333, default: 0.125)
    }

    // Shader resource identifiers
    public const string ShaderId_texture = "_texture";
    public const string ShaderId_fxaaData = "_fxaaData";

    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandFXAA;
    private readonly GPUCommandBuffer _commandBlit;
    private readonly RenderingSystem _renderingSystem;

    // FXAA shader and pipeline
    private readonly Shader _fxaaShader;
    private GraphicsPipelineContext _fxaaPipelineInfo;
    private readonly uint _fxaaShaderId_texture;
    private readonly uint _fxaaShaderId_fxaaData;

    // Blit shader and pipeline for final copy
    private readonly Shader _blitShader;
    private GraphicsPipelineContext _blitPipelineInfo;
    private uint _blitShaderId_texture;

    private readonly GraphicsValueBuffer<FXAAShaderData> _fxaaShaderData;

    protected RenderTexture? _input;
    private RenderTexture? _intermediateTexture; 

    /// <summary>
    /// Gets or sets the quality setting for FXAA.
    /// Higher values provide better quality at the cost of performance.
    /// Valid range: 0.5 - 2.0, Default: 1.0
    /// </summary>
    public float Quality
    {
        get => _fxaaShaderData.Value.Quality;
        set
        {
            var data = _fxaaShaderData.Value;
            data.Quality = Math.Clamp(value, 0.5f, 2.0f);
            _fxaaShaderData.Value = data;
            _fxaaShaderData.UpdateBuffer();
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

        // Initialize FXAA pipeline context
        _fxaaPipelineInfo = GraphicsPipelineContext.Default;
        _fxaaShader.TryUpdatePipelineContext(ref _fxaaPipelineInfo, renderingSystem.PrefferedSDRPass);

        // Get FXAA shader resource IDs
        _fxaaShaderId_texture = _fxaaPipelineInfo.GetResourceId(ShaderId_texture);
        _fxaaShaderId_fxaaData = _fxaaPipelineInfo.GetResourceId(ShaderId_fxaaData);

        // Initialize blit pipeline context
        _blitPipelineInfo = GraphicsPipelineContext.Default;
        _blitShader.TryUpdatePipelineContext(ref _blitPipelineInfo, renderingSystem.PrefferedSDRPass);
        _blitShaderId_texture = _blitPipelineInfo.GetResourceId(ShaderId_texture);

        // Create shader data buffer with default values
        _fxaaShaderData = renderingSystem.CreateGraphicsValueBuffer<FXAAShaderData>("fxaa_data");
        _fxaaShaderData.Value = new FXAAShaderData
        {
            InvFrameSize = Vector2.One,
            Quality = 1.0f,
            Threshold = 0.125f
        };
        _fxaaShaderData.UpdateBuffer();

        // Create command buffers
        _commandFXAA = _device.CreateCommandBuffer();
        _commandBlit = _device.CreateCommandBuffer();
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

        // Step 1: Apply FXAA to intermediate texture
        _commandFXAA.Begin();
        _commandFXAA.SetFrameBuffer(_intermediateTexture);
        _commandFXAA.SetGraphicsPipeline(_fxaaPipelineInfo);
        uint indexCount = _commandFXAA.SetMesh(fullScreenMesh);
        _commandFXAA.SetGraphicsResources(_fxaaShaderId_texture, _input.ColorTextures[0].EntrySample);
        _commandFXAA.SetGraphicsResources(_fxaaShaderId_fxaaData, _fxaaShaderData.EntryReadonly);
        _commandFXAA.DrawIndexed(indexCount, 1, 0, 0, 0);
        _commandFXAA.End();
        _renderingSystem.ScheduleCommandBuffer(_commandFXAA);

        // Step 2: Blit intermediate texture to final target
        if (_blitShader.TryUpdatePipelineContext(ref _blitPipelineInfo, target.RenderPass))
        {
            _blitShaderId_texture = _blitPipelineInfo.GetResourceId(ShaderId_texture);
        }

        _commandBlit.Begin();
        _commandBlit.SetFrameBuffer(target);
        _commandBlit.SetGraphicsPipeline(_blitPipelineInfo);
        indexCount = _commandBlit.SetMesh(fullScreenMesh);
        _commandBlit.SetGraphicsResources(_blitShaderId_texture, _intermediateTexture.ColorTextures[0].EntrySample);
        _commandBlit.DrawIndexed(indexCount, 1, 0, 0, 0);
        _commandBlit.End();
        _renderingSystem.ScheduleCommandBuffer(_commandBlit);
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
            _commandBlit.Dispose();
            _fxaaShaderData.Dispose();
            _intermediateTexture?.Dispose();
        }
        base.Dispose(disposing);
    }
}