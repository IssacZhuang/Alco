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
public class FXAA : TextureProcessor
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
    private readonly RenderingSystem _renderingSystem;

    // FXAA shader and pipeline: the quality-preset shaders arrive injected (one
    // specialized shader per preset of the fxaa module's MainPS<let Quality : int>
    // generic), keyed by preset.
    private readonly IReadOnlyDictionary<FXAAQuality, Shader> _fxaaShaders;
    private Shader _fxaaShader = null!;
    private GraphicsPipelineContext _fxaaPipelineInfo;
    private uint _fxaaShaderId_texture;
    private uint _fxaaShaderId_fxaaData;

    private FXAAQuality _quality = FXAAQuality.Medium;

    // Blit shader and pipeline for final copy
    private readonly Shader _blitShader;
    private GraphicsPipelineContext _blitPipelineInfo;
    private uint _blitShaderId_texture;

    private readonly GraphicsValueBuffer<FXAAShaderData> _fxaaShaderData;

    private RenderTexture? _intermediateTexture;
    private GPUAttachmentLayout? _intermediateLayout;

    /// <summary>
    /// Gets or sets the FXAA quality preset.
    /// Changes switch to the preset's specialized shader and rebuild the pipeline.
    /// </summary>
    public FXAAQuality Quality
    {
        get => _quality;
        set
        {
            if (_quality != value)
            {
                _quality = value;
                ApplyQuality();
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
    /// <param name="blitShader">The blit shader for final copy</param>
    /// <param name="qualityShaders">One specialized shader per quality preset
    /// (the fxaa module's MainPS&lt;let Quality : int&gt; generic); every
    /// <see cref="FXAAQuality"/> value must be present.</param>
    /// <exception cref="ArgumentException">Thrown when a quality preset has no shader.</exception>
    internal FXAA(RenderingSystem renderingSystem, Shader blitShader,
        IReadOnlyDictionary<FXAAQuality, Shader> qualityShaders) : base(renderingSystem)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;
        _blitShader = blitShader;
        _fxaaShaders = qualityShaders;

        foreach (FXAAQuality quality in Enum.GetValues<FXAAQuality>())
        {
            if (!qualityShaders.ContainsKey(quality))
            {
                throw new ArgumentException($"Missing the {quality} quality-preset shader.", nameof(qualityShaders));
            }
        }

        // Initialize the FXAA pipeline context with the default quality preset
        ApplyQuality();

        // Initialize blit pipeline context (placeholder layout only: Blit re-creates
        // the pipeline against the real target layout on first use).
        _blitPipelineInfo = GraphicsPipelineContext.Default;
        _blitShader.TryUpdatePipelineContext(ref _blitPipelineInfo, renderingSystem.PreferredLightMapPass);
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
    }

    // Keeps the intermediate texture matching the input's size and pixel format,
    // recreating or resizing it lazily when either changed since the last blit.
    private void EnsureIntermediate(RenderTexture input)
    {
        // The intermediate texture must keep the input's pixel format: rendering through
        // an 8-bit SDR target here would quantize the linear HDR image before tone
        // mapping and produce severe banding in dark areas.
        PixelFormat inputFormat = input.AttachmentLayout.Colors[0].Format;
        if (_intermediateTexture != null && _intermediateLayout!.Colors[0].Format == inputFormat)
        {
            if (_intermediateTexture.Width == input.Width && _intermediateTexture.Height == input.Height)
            {
                return;
            }

            // Same format, new size: resize in place (the wrapper identity is stable).
            _intermediateTexture.Resize(input.Width, input.Height);
        }
        else
        {
            _intermediateTexture?.Dispose();

            if (_intermediateLayout == null || _intermediateLayout.Colors[0].Format != inputFormat)
            {
                _intermediateLayout?.Dispose();
                _intermediateLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
                    [new ColorAttachment(inputFormat)],
                    null,
                    "fxaa_intermediate"
                ));
            }

            // Create intermediate texture with same size and format as input
            _intermediateTexture = _renderingSystem.CreateRenderTexture(
                _intermediateLayout,
                input.Width,
                input.Height,
                "fxaa_intermediate"
            );
        }

        // Update frame size for shader
        var data = _fxaaShaderData.Value;
        data.InvFrameSize = new Vector2(1.0f / input.Width, 1.0f / input.Height);
        _fxaaShaderData.Value = data;
        _fxaaShaderData.UpdateBuffer();
    }

    /// <summary>
    /// GPU timing span for the current blit, set by the wrapping graph node on
    /// sample frames (null = no timing). The first pass writes the begin
    /// timestamp, the final pass the end timestamp.
    /// </summary>
    internal GpuTimestampSampler? TimestampSampler { get; set; }

    /// <summary>The first query slot of the timing span in <see cref="TimestampSampler"/>.</summary>
    internal int TimestampBaseSlot { get; set; }

    /// <summary>
    /// Anti-aliases the input and records two fullscreen passes onto
    /// <paramref name="command"/>, rendering the result into <paramref name="target"/>.
    /// The command buffer is neither ended nor submitted here.
    /// </summary>
    /// <param name="command">The caller-owned open command buffer to record into.</param>
    /// <param name="input">The input render texture to anti-alias.</param>
    /// <param name="target">The target framebuffer to render to</param>
    public override void Blit(GPUCommandBuffer command, RenderTexture input, GPUFrameBuffer target)
    {
        EnsureIntermediate(input);

        Mesh fullScreenMesh = FullScreenMesh;

        // EnsureIntermediate guarantees the intermediate texture exists.
        if (_fxaaShader.TryUpdatePipelineContext(ref _fxaaPipelineInfo, _intermediateTexture!.FrameBuffer.AttachmentLayout))
        {
            _fxaaShaderId_texture = _fxaaPipelineInfo.GetResourceId(ShaderId_texture);
            _fxaaShaderId_fxaaData = _fxaaPipelineInfo.GetResourceId(ShaderId_fxaaData);
        }

        if (_blitShader.TryUpdatePipelineContext(ref _blitPipelineInfo, target.AttachmentLayout))
        {
            _blitShaderId_texture = _blitPipelineInfo.GetResourceId(ShaderId_texture);
        }

        GpuTimestampSampler? timestamps = TimestampSampler;

        using (var renderPass = timestamps != null
            ? command.BeginRender(_intermediateTexture.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty,
                timestamps.QuerySet, (uint)TimestampBaseSlot, null)
            : command.BeginRender(_intermediateTexture.FrameBuffer))
        {
            renderPass.SetPipeline(_fxaaPipelineInfo.Pipeline!);
            uint indexCount = renderPass.SetMesh(fullScreenMesh);
            renderPass.SetResources(_fxaaShaderId_texture, input.ColorTextures[0].EntrySample);
            renderPass.SetResources(_fxaaShaderId_fxaaData, _fxaaShaderData.EntryReadonly);
            renderPass.DrawIndexed(indexCount, 1, 0, 0, 0);
        }

        using (var renderPass = timestamps != null
            ? command.BeginRender(target, ReadOnlySpan<ClearColorData>.Empty,
                timestamps.QuerySet, null, (uint)(TimestampBaseSlot + 1))
            : command.BeginRender(target))
        {
            renderPass.SetPipeline(_blitPipelineInfo.Pipeline!);
            uint indexCount = renderPass.SetMesh(fullScreenMesh);
            renderPass.SetResources(_blitShaderId_texture, _intermediateTexture.ColorTextures[0].EntrySample);
            renderPass.DrawIndexed(indexCount, 1, 0, 0, 0);
        }

        if (timestamps != null)
        {
            timestamps.ResolveAll(command);
        }
    }

    /// <summary>
    /// Switches to the current quality preset's specialized shader and rebuilds
    /// the FXAA pipeline against a placeholder layout (the real intermediate
    /// layout replaces it on the next Blit). The quality axis is a generic value
    /// specialization (MainPS&lt;let Quality : int&gt;), so each preset is its own
    /// specialized Shader, not a defines permutation; the preset shaders arrive
    /// injected at construction.
    /// </summary>
    private void ApplyQuality()
    {
        _fxaaShader = _fxaaShaders[_quality];

        // Fresh context: the new shader's pipeline differs from the previous one's.
        _fxaaPipelineInfo = _fxaaShader.GetGraphicsPipeline(_renderingSystem.PreferredLightMapPass);
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
            _fxaaShaderData.Dispose();
            _intermediateTexture?.Dispose();
            _intermediateLayout?.Dispose();
        }
        base.Dispose(disposing);
    }
}