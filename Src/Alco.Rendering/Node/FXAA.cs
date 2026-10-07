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
    /// Medium quality - 8 search steps, balanced performance
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
/// FXAA edge-detection modes. The axis is a generic value specialization of
/// the shader's MainPS entry point (like the quality axis): switching modes
/// selects another precompiled specialization, never a recompile.
/// </summary>
public enum FXAAMode
{
    /// <summary>
    /// Classic FXAA 3.11 luma-only edge detection. No depth sampling.
    /// </summary>
    Luma = 0,

    /// <summary>
    /// Depth-assisted: depth discontinuities rescue edges the luma gate
    /// rejects (dark-on-dark silhouettes) and steer the edge walk on
    /// geometric edges; luma detection remains the fallback for shader and
    /// alpha-tested edges. Requires a bound depth source.
    /// </summary>
    DepthAssisted = 1,

    /// <summary>
    /// Depth-gated: only depth-detected geometric edges are anti-aliased;
    /// shader/specular aliasing passes through untouched. Requires a bound
    /// depth source.
    /// </summary>
    DepthOnly = 2
}

/// <summary>
/// Fast Approximate Anti-Aliasing (FXAA) post-processing effect.
/// Provides screen-space anti-aliasing with minimal performance cost.
/// FXAA 3.11's luma-based edge detection assumes tone-mapped input: register
/// the node after the tonemap node — on linear HDR the bright regions dominate
/// the luma range and edges in the darks get missed or over-blurred.
/// <br/>The mode axis selects the edge detector (see <see cref="FXAAMode"/>);
/// the depth-aware modes sample <see cref="DepthSource"/>'s depth attachment.
/// </summary>
public class FXAA : TextureProcessor
{
    // Shader resource identifiers
    public const string ShaderId_texture = "texture";
    public const string ShaderId_depthTexture = "depthTexture";
    public const string ShaderId_fxaaData = "fxaaData";

    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;

    // The fxaa material: each (quality, mode) pair is a generic value
    // specialization of the shader's MainPS<let Quality, let Mode> entry,
    // compiled lazily and cached inside the shader — switching either axis is
    // a cache-hit pipeline build, never a recompile of a used variant.
    private readonly GraphicsMaterial _fxaaMaterial;
    private FXAAQuality _quality;
    private FXAAMode _mode;

    // Blit material for the final copy.
    private readonly GraphicsMaterial _blitMaterial;

    // Threshold mirrored on the CPU: the uniform buffer is write-only by name.
    private float _threshold = 0.125f;
    // Subpixel AA amount, likewise mirrored (see Threshold).
    private float _subpix = 0.75f;
    // Depth edge detection thresholds, likewise mirrored (see Threshold).
    private float _depthThresholdMin = 1.0f / 4096.0f;
    private float _depthThresholdRel = 1.0f / 64.0f;
    // Reflection-driven uniform buffer over the shader's fxaaData block — no
    // hand-written CPU twin (the alignment padding lives in the reflected layout).
    private readonly UniformGraphicsBuffer _fxaaShaderData;

    private RenderTexture? _intermediateTexture;
    private GPUAttachmentLayout? _intermediateLayout;

    // Fallback depth binding when no DepthSource is set: the shader's
    // ParameterBlock keeps the depth slot in every specialization's layout
    // (fields are not pruned per specialization), so the slot must always be
    // bound even in luma mode, which never samples it. 1x1, never rendered
    // into — a bind-only placeholder.
    private RenderTexture? _placeholderDepthTexture;
    private GPUAttachmentLayout? _placeholderDepthLayout;

    /// <summary>
    /// The depth-aware modes' scene depth source; its depth attachment is
    /// bound as the shader's depth texture. Null falls back to a 1x1
    /// placeholder (valid but meaningless — use luma mode then).
    /// </summary>
    public RenderTexture? DepthSource { get; set; }

    /// <summary>
    /// Gets or sets the FXAA quality preset.
    /// Changes switch to the preset's specialized material and rebuild the pipeline.
    /// </summary>
    public FXAAQuality Quality
    {
        get => _quality;
        set
        {
            if (_quality != value)
            {
                _quality = value;
                ApplySpecialization();
            }
        }
    }

    /// <summary>
    /// Gets or sets the edge-detection mode. Changes switch to the mode's
    /// specialized material and rebuild the pipeline.
    /// </summary>
    public FXAAMode Mode
    {
        get => _mode;
        set
        {
            if (_mode != value)
            {
                _mode = value;
                ApplySpecialization();
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
        get => _threshold;
        set
        {
            _threshold = Math.Clamp(value, 0.063f, 0.333f);
            _fxaaShaderData.SetValue("threshold", _threshold);
            _fxaaShaderData.Flush();
        }
    }

    /// <summary>
    /// Gets or sets the subpixel aliasing removal amount.
    /// Higher values remove more subpixel aliasing but blur more detail.
    /// Valid range: 0 - 1, Default: 0.75
    /// </summary>
    public float Subpix
    {
        get => _subpix;
        set
        {
            _subpix = Math.Clamp(value, 0.0f, 1.0f);
            _fxaaShaderData.SetValue("subpix", _subpix);
            _fxaaShaderData.Flush();
        }
    }

    /// <summary>
    /// Gets or sets the depth edge detection absolute floor (depth modes):
    /// neighbor depth differences below it never count as geometric edges.
    /// Valid range: 0 - 0.01, Default: 1/4096
    /// </summary>
    public float DepthThresholdMin
    {
        get => _depthThresholdMin;
        set
        {
            _depthThresholdMin = Math.Clamp(value, 0.0f, 0.01f);
            _fxaaShaderData.SetValue("depthThresholdMin", _depthThresholdMin);
            _fxaaShaderData.Flush();
        }
    }

    /// <summary>
    /// Gets or sets the depth edge detection relative threshold (depth modes):
    /// a neighbor counts as across an edge when its depth differs from the
    /// center by this fraction of the center depth. Valid range: 0 - 0.25,
    /// Default: 1/64
    /// </summary>
    public float DepthThresholdRel
    {
        get => _depthThresholdRel;
        set
        {
            _depthThresholdRel = Math.Clamp(value, 0.0f, 0.25f);
            _fxaaShaderData.SetValue("depthThresholdRel", _depthThresholdRel);
            _fxaaShaderData.Flush();
        }
    }

    /// <summary>
    /// Initializes a new instance of the FXAA post-processing effect.
    /// </summary>
    /// <param name="renderingSystem">The rendering system instance</param>
    /// <param name="blitShader">The blit shader for final copy</param>
    /// <param name="fxaaShader">The fxaa shader (MainPS&lt;let Quality : int,
    /// let Mode : int&gt;); each (preset, mode) pair is a specialization
    /// requested on demand.</param>
    internal FXAA(RenderingSystem renderingSystem, Shader blitShader, Shader fxaaShader) : base(renderingSystem)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;

        _quality = FXAAQuality.Medium;
        _mode = FXAAMode.Luma;
        _fxaaMaterial = renderingSystem.CreateGraphicsMaterial(fxaaShader, "fxaa_material", (int)_quality, (int)_mode);

        _blitMaterial = renderingSystem.CreateGraphicsMaterial(blitShader, "fxaa_blit_material");

        // Create the reflection-driven data buffer over the shader's fxaaData
        // block; members land by name at their reflected offsets. The entry
        // points are (Quality, Mode)-generic, so the reflected module is the
        // current pair's specialization (the block layout is specialization-
        // independent — see FxaaModeSpecializationTest).
        _fxaaShaderData = renderingSystem.CreateUniformGraphicsBuffer(
            fxaaShader.GetShaderModules((int)_quality, (int)_mode).ReflectionInfo.UniformBlocks.First(block => block.Name == ShaderId_fxaaData),
            "fxaa_data");
        _fxaaShaderData.SetValue("invFrameSize", Vector2.One);
        _fxaaShaderData.SetValue("threshold", _threshold);
        _fxaaShaderData.SetValue("subpix", _subpix);
        _fxaaShaderData.SetValue("depthThresholdMin", _depthThresholdMin);
        _fxaaShaderData.SetValue("depthThresholdRel", _depthThresholdRel);
        _fxaaShaderData.Flush();
        _fxaaMaterial.SetBuffer(ShaderId_fxaaData, _fxaaShaderData);
    }

    // Keeps the intermediate texture matching the input's size and pixel format,
    // recreating or resizing it lazily when either changed since the last blit.
    private void EnsureIntermediate(RenderTexture input)
    {
        // The intermediate texture must keep the input's pixel format: rendering
        // through a lower-precision target here would quantize the input's range
        // (e.g. severe banding in dark areas when the node sits on a linear HDR
        // chain before tone mapping).
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
        _fxaaShaderData.SetValue("invFrameSize", new Vector2(1.0f / input.Width, 1.0f / input.Height));
        _fxaaShaderData.Flush();
    }

    // The 1x1 depth placeholder for the always-present depth slot (see the
    // field remarks) — created lazily on the first blit without a DepthSource.
    private RenderTexture EnsurePlaceholderDepth()
    {
        if (_placeholderDepthTexture != null)
        {
            return _placeholderDepthTexture;
        }

        _placeholderDepthLayout ??= _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [],
            new DepthAttachment(PixelFormat.Depth32Float),
            "fxaa_placeholder_depth"
        ));
        _placeholderDepthTexture = _renderingSystem.CreateRenderTexture(
            _placeholderDepthLayout, 1, 1, "fxaa_placeholder_depth");
        return _placeholderDepthTexture;
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
    /// <paramref name="context"/>, rendering the result into <paramref name="target"/>.
    /// The context is neither opened nor submitted here.
    /// </summary>
    /// <param name="context">The render context recording the frame.</param>
    /// <param name="input">The input render texture to anti-alias.</param>
    /// <param name="target">The target framebuffer to render to</param>
    public override void Blit(RenderContext context, RenderTexture input, GPUFrameBuffer target)
    {
        EnsureIntermediate(input);

        Mesh fullScreenMesh = FullScreenMesh;

        // EnsureIntermediate guarantees the intermediate texture exists.
        _fxaaMaterial.SetRenderTexture(ShaderId_texture, input);
        // The depth slot exists in every specialization's layout; bind the
        // source's depth attachment, or the placeholder when none is set.
        _fxaaMaterial.SetRenderTextureDepth(ShaderId_depthTexture, DepthSource ?? EnsurePlaceholderDepth());
        _blitMaterial.SetRenderTexture(ShaderId_texture, _intermediateTexture!);

        GpuTimestampSampler? timestamps = TimestampSampler;

        using (RenderPassScope renderPass = timestamps != null
            ? context.BeginPass(_intermediateTexture!.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty,
                timestamps.QuerySet, (uint)TimestampBaseSlot, null)
            : context.BeginPass(_intermediateTexture!.FrameBuffer))
        {
            renderPass.Draw(fullScreenMesh, _fxaaMaterial);
        }

        using (RenderPassScope renderPass = timestamps != null
            ? context.BeginPass(target, ReadOnlySpan<ClearColorData>.Empty,
                timestamps.QuerySet, null, (uint)(TimestampBaseSlot + 1))
            : context.BeginPass(target))
        {
            renderPass.Draw(fullScreenMesh, _blitMaterial);
        }

        if (timestamps != null)
        {
            timestamps.ResolveAll(context.CommandBuffer);
        }
    }

    /// <summary>
    /// Switches to the current (quality, mode) pair's specialized material.
    /// Both axes are generic value specializations (MainPS&lt;let Quality : int,
    /// let Mode : int&gt;): the shader compiles each pair once and caches it,
    /// so switching back to a used pair is a cache hit.
    /// </summary>
    private void ApplySpecialization()
    {
        // SetSpecializations rebuilds the variant's parameter set with bindings
        // carried over by name, so the texture/depth/data buffer bindings survive.
        _fxaaMaterial.SetSpecializations((int)_quality, (int)_mode);
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
            _placeholderDepthTexture?.Dispose();
            _placeholderDepthLayout?.Dispose();
            _fxaaMaterial.Dispose();
            _blitMaterial.Dispose();
        }
        base.Dispose(disposing);
    }
}
