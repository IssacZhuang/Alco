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
/// Fast Approximate Anti-Aliasing (FXAA) post-processing effect.
/// Provides screen-space anti-aliasing with minimal performance cost.
/// </summary>
public class FXAA : TextureProcessor
{
    // Shader resource identifiers
    public const string ShaderId_texture = "_texture";
    public const string ShaderId_fxaaData = "_fxaaData";

    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;

    // The fxaa material: each quality preset is a generic value specialization of
    // the shader's MainPS<let Quality : int> entry, compiled lazily and cached
    // inside the shader — switching presets is a cache-hit pipeline build, never
    // a recompile of a previously used preset.
    private readonly GraphicsMaterial _fxaaMaterial;
    private FXAAQuality _quality;

    // Blit material for the final copy.
    private readonly GraphicsMaterial _blitMaterial;

    // Threshold mirrored on the CPU: the uniform buffer is write-only by name.
    private float _threshold = 0.125f;
    // Reflection-driven uniform buffer over the shader's _fxaaData block — no
    // hand-written CPU twin (the alignment padding lives in the reflected layout).
    private readonly UniformGraphicsBuffer _fxaaShaderData;

    private RenderTexture? _intermediateTexture;
    private GPUAttachmentLayout? _intermediateLayout;

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
        get => _threshold;
        set
        {
            _threshold = Math.Clamp(value, 0.063f, 0.333f);
            _fxaaShaderData.SetValue("Threshold", _threshold);
            _fxaaShaderData.Flush();
        }
    }

    /// <summary>
    /// Initializes a new instance of the FXAA post-processing effect.
    /// </summary>
    /// <param name="renderingSystem">The rendering system instance</param>
    /// <param name="blitShader">The blit shader for final copy</param>
    /// <param name="fxaaShader">The fxaa shader (MainPS&lt;let Quality : int&gt;);
    /// each preset is a specialization requested on demand.</param>
    internal FXAA(RenderingSystem renderingSystem, Shader blitShader, Shader fxaaShader) : base(renderingSystem)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;

        _quality = FXAAQuality.Medium;
        _fxaaMaterial = renderingSystem.CreateGraphicsMaterial(fxaaShader, "fxaa_material", (int)_quality);

        _blitMaterial = renderingSystem.CreateGraphicsMaterial(blitShader, "fxaa_blit_material");

        // Create the reflection-driven data buffer over the shader's _fxaaData
        // block; members land by name at their reflected offsets. The entry
        // points are Quality-generic, so the reflected module is the current
        // preset's specialization (the block layout is quality-independent).
        _fxaaShaderData = renderingSystem.CreateUniformGraphicsBuffer(
            fxaaShader.GetShaderModules((int)_quality).ReflectionInfo.UniformBlocks.First(block => block.Name == ShaderId_fxaaData),
            "fxaa_data");
        _fxaaShaderData.SetValue("InvFrameSize", Vector2.One);
        _fxaaShaderData.SetValue("Threshold", _threshold);
        _fxaaShaderData.Flush();
        _fxaaMaterial.SetBuffer(ShaderId_fxaaData, _fxaaShaderData);
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
        _fxaaShaderData.SetValue("InvFrameSize", new Vector2(1.0f / input.Width, 1.0f / input.Height));
        _fxaaShaderData.Flush();
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
    /// Switches to the current quality preset's specialized material. The quality
    /// axis is a generic value specialization (MainPS&lt;let Quality : int&gt;):
    /// the shader compiles each preset once and caches it, so switching back to
    /// a used preset is a cache hit.
    /// </summary>
    private void ApplyQuality()
    {
        // SetSpecializations rebuilds the variant's parameter set with bindings
        // carried over by name, so the texture/data buffer bindings survive.
        _fxaaMaterial.SetSpecializations((int)_quality);
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
            _fxaaMaterial.Dispose();
            _blitMaterial.Dispose();
        }
        base.Dispose(disposing);
    }
}
