using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Sum-of-gaussians bloom: the input is thresholded into a
/// half-resolution setup texture, downsampled to 1/64 through a five-level
/// chain, then six independently-sized separable gaussians (Bloom1..6) are
/// blurred at the chain level where each radius is cheapest and accumulated
/// coarse-to-fine, each weighted by tint * intensity / 6. The accumulation,
/// at the last executed stage's resolution, is finally composited additively
/// onto the scene image.
/// </summary>
public class Bloom : TextureProcessor
{
    private struct SetupConstants
    {
        public float Threshold;
    }

    private struct DownsampleConstants
    {
        public Vector2 InvTextureSize;
    }

    private struct GaussianConstants
    {
        public Vector2 Direction;
        public float SigmaRadius;
        public int IntegerRadius;
        public float TintR;
        public float TintG;
        public float TintB;
        public float AdditiveBlend;
        public Vector2 InvSourceSize;
    }

    public const string ShaderId_texture = "texture";
    public const string ShaderId_source = "source";
    public const string ShaderId_additive = "additive";

    private const int StageCount = 6;
    private const float MaxRadius = 31f;

    private readonly GPUAttachmentLayout _backBufferPass;
    private readonly RenderingSystem _renderingSystem;

    // One material per pass shader: pipeline states (additive blend on the final
    // composite, opaque on the pyramid passes) live on the materials and the
    // passes draw through RenderContext like every other renderer.
    private readonly GraphicsMaterial _blitMaterial;
    private readonly GraphicsMaterial _setupMaterial;
    private readonly GraphicsMaterial _downsampleMaterial;
    private readonly GraphicsMaterial _gaussianMaterial;

    // The input size the current pyramid was built for; a size mismatch on the next
    // blit rebuilds the pyramid lazily.
    private uint _builtWidth;
    private uint _builtHeight;

    // Default Bloom1..6 sizes, as percents of the screen width.
    private readonly float[] _sizes = [0.3f, 1.0f, 2.0f, 10.0f, 30.0f, 64.0f];
    private readonly Vector3[] _tints = [Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One];

    private float _threshold = 0f;
    private float _intensity = 1f;
    private float _sizeScale = 1f;

    // The thresholded half-resolution image doubles as gaussian chain level 0;
    // levels 1..5 hold the 1/4 .. 1/64 downsamples.
    private RenderTexture? _setupTexture;
    private RenderTexture[]? _chainTextures;

    // Per gaussian stage: the horizontal-pass output and the accumulated
    // vertical-pass output, both at the stage source's resolution.
    private RenderTexture[]? _horizontalTextures;
    private RenderTexture[]? _stageTextures;

    /// <summary>
    /// Gets or sets the brightness cutoff of the bloom setup pass. The threshold
    /// is a linear ramp reaching full strength two units above the cutoff;
    /// negative values disable it so the whole image blooms. Defaults to 0: the
    /// darkest pixels contribute less while everything at or above two units of
    /// luminance blooms at full strength.
    /// </summary>
    public float Threshold
    {
        get => _threshold;
        set => _threshold = value;
    }

    /// <summary>Gets or sets the overall bloom strength. Defaults to 1.</summary>
    public float Intensity
    {
        get => _intensity;
        set => _intensity = Math.Max(value, 0.0f);
    }

    /// <summary>
    /// Gets or sets a multiplier on every gaussian radius.
    /// </summary>
    public float SizeScale
    {
        get => _sizeScale;
        set => _sizeScale = Math.Clamp(value, 0.0001f, 10.0f);
    }

    /// <summary>
    /// Gets the per-stage gaussian sizes in Bloom1..6 order, as percents of the
    /// screen width. The array is owned by the effect and mutated in place by
    /// the creating node.
    /// </summary>
    public float[] Sizes => _sizes;

    /// <summary>
    /// Gets the per-stage gaussian tints in Bloom1..6 order. The array is owned
    /// by the effect and mutated in place by the creating node.
    /// </summary>
    public Vector3[] Tints => _tints;

    internal Bloom(RenderingSystem system, Shader blitShader, Shader setupShader, Shader downsampleShader, Shader gaussianShader) : base(system)
    {
        _renderingSystem = system;

        _backBufferPass = system.PreferredLightMapPass;

        // The bloom composite is additive: the target already holds the scene image.
        _blitMaterial = system.CreateGraphicsMaterial(blitShader, "bloom_blit_material");
        _blitMaterial.BlendState = BlendState.Additive;

        _setupMaterial = system.CreateGraphicsMaterial(setupShader, "bloom_setup_material");
        _downsampleMaterial = system.CreateGraphicsMaterial(downsampleShader, "bloom_downsample_material");
        _gaussianMaterial = system.CreateGraphicsMaterial(gaussianShader, "bloom_gaussian_material");
    }

    // Rebuilds the downsample chain and gaussian stage textures when the input
    // size changed since the last blit. The textures are bound through the
    // materials, so recreating them needs no other notification.
    private void EnsurePyramid(RenderTexture input)
    {
        if (_setupTexture != null && _builtWidth == input.Width && _builtHeight == input.Height)
        {
            return;
        }

        _builtWidth = input.Width;
        _builtHeight = input.Height;

        TryDisposeFrames();

        // Level 0 is the thresholded half-resolution setup image; levels 1..5
        // progressively halve down to 1/64 (never below one pixel).
        _setupTexture = _renderingSystem.CreateRenderTexture(_backBufferPass, Math.Max(input.Width >> 1, 1), Math.Max(input.Height >> 1, 1));

        _chainTextures = new RenderTexture[StageCount - 1];
        uint width = _setupTexture.Width;
        uint height = _setupTexture.Height;
        for (int i = 0; i < _chainTextures.Length; i++)
        {
            width = Math.Max(width >> 1, 1);
            height = Math.Max(height >> 1, 1);
            _chainTextures[i] = _renderingSystem.CreateRenderTexture(_backBufferPass, width, height);
        }

        // One horizontal output and one accumulated stage output per gaussian
        // stage, each at its chain level's resolution (stage 0 at 1/64 through
        // stage 5 at 1/2).
        _horizontalTextures = new RenderTexture[StageCount];
        _stageTextures = new RenderTexture[StageCount];
        for (int i = 0; i < StageCount; i++)
        {
            RenderTexture source = GetStageSource(i);
            _horizontalTextures[i] = _renderingSystem.CreateRenderTexture(_backBufferPass, source.Width, source.Height);
            _stageTextures[i] = _renderingSystem.CreateRenderTexture(_backBufferPass, source.Width, source.Height);
        }
    }

    // The gaussian stages run coarse-to-fine: stage 0 blurs the 1/64 level with
    // Bloom6's size, stage 5 the half-resolution setup with Bloom1's.
    private RenderTexture GetStageSource(int stage)
    {
        if (stage < StageCount - 1)
        {
            return _chainTextures![StageCount - 2 - stage];
        }
        return _setupTexture!;
    }

    /// <summary>
    /// GPU timing span for the current pyramid, set by the wrapping graph node on
    /// sample frames (null = no timing). The setup pass writes the begin
    /// timestamp, the final composite pass the end timestamp, so one pair covers
    /// the whole chain.
    /// </summary>
    internal GpuTimestampSampler? TimestampSampler { get; set; }

    /// <summary>The first query slot of the timing span in <see cref="TimestampSampler"/>.</summary>
    internal int TimestampBaseSlot { get; set; }

    /// <summary>
    /// Builds the bloom pyramid from <paramref name="input"/> and records the
    /// setup, downsample chain, gaussian stages plus the final additive composite
    /// onto <paramref name="context"/>, rendering into <paramref name="target"/>.
    /// The context is neither opened nor submitted here.
    /// </summary>
    /// <param name="context">The render context recording the frame.</param>
    /// <param name="input">The input render texture.</param>
    /// <param name="target">The target framebuffer; must already hold the scene image.</param>
    public override void Blit(RenderContext context, RenderTexture input, GPUFrameBuffer target)
    {
        EnsurePyramid(input);

        Mesh mesh = FullScreenMesh;
        GpuTimestampSampler? timestamps = TimestampSampler;
        RenderTexture setupFrame = _setupTexture!;

        // Setup: threshold into the half-resolution level-0 image.
        _setupMaterial.SetRenderTexture(ShaderId_texture, input);
        var setupConstants = new SetupConstants
        {
            Threshold = Threshold,
        };
        using (RenderPassScope renderPass = timestamps != null
            ? context.BeginPass(setupFrame.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty,
                timestamps.QuerySet, (uint)TimestampBaseSlot, null)
            : context.BeginPass(setupFrame.FrameBuffer))
        {
            renderPass.DrawWithConstant(mesh, _setupMaterial, setupConstants);
        }

        // Downsample chain: 1/4 .. 1/64. The taps offset by one texel of the
        // INPUT texture, pairing into a 4x4 box.
        RenderTexture previous = setupFrame;
        for (int i = 0; i < _chainTextures!.Length; i++)
        {
            RenderTexture chainFrame = _chainTextures[i];
            _downsampleMaterial.SetRenderTexture(ShaderId_texture, previous);
            var downsampleConstants = new DownsampleConstants
            {
                InvTextureSize = new Vector2(1f) / new Vector2(previous.Width, previous.Height),
            };
            using (RenderPassScope renderPass = context.BeginPass(chainFrame.FrameBuffer))
            {
                renderPass.DrawWithConstant(mesh, _downsampleMaterial, downsampleConstants);
            }

            previous = chainFrame;
        }

        // Gaussian stages, coarse to fine. The horizontal pass is untinted; the
        // vertical pass applies the stage tint and accumulates the previous
        // stage's output bilinearly. Stages whose
        // size is zero are skipped and the accumulation passes through.
        float tintScale = Intensity / StageCount;
        RenderTexture? accumulated = null;
        for (int stage = 0; stage < StageCount; stage++)
        {
            float size = _sizes[StageCount - 1 - stage] * SizeScale;
            if (size <= float.Epsilon)
            {
                continue;
            }

            RenderTexture source = GetStageSource(stage);
            RenderTexture horizontal = _horizontalTextures![stage];
            RenderTexture output = _stageTextures![stage];

            // Radius: source width * size% * 0.5% ... i.e. the size percent
            // is a diameter of the screen, halved into a radius, in source texels.
            float radius = source.Width * size * 0.005f;
            float sigmaRadius = Math.Clamp(radius, 0.0001f, MaxRadius);
            int integerRadius = Math.Min(Math.Max((int)MathF.Ceiling(sigmaRadius), 1), (int)MaxRadius);
            Vector2 invSourceSize = new Vector2(1f) / new Vector2(source.Width, source.Height);

            _gaussianMaterial.SetRenderTexture(ShaderId_source, source);
            _gaussianMaterial.SetRenderTexture(ShaderId_additive, source);
            var horizontalConstants = new GaussianConstants
            {
                Direction = new Vector2(1f, 0f),
                SigmaRadius = sigmaRadius,
                IntegerRadius = integerRadius,
                TintR = 1f,
                TintG = 1f,
                TintB = 1f,
                AdditiveBlend = 0f,
                InvSourceSize = invSourceSize,
            };
            using (RenderPassScope renderPass = context.BeginPass(horizontal.FrameBuffer))
            {
                renderPass.DrawWithConstant(mesh, _gaussianMaterial, horizontalConstants);
            }

            Vector3 tint = _tints[StageCount - 1 - stage] * tintScale;
            _gaussianMaterial.SetRenderTexture(ShaderId_source, horizontal);
            _gaussianMaterial.SetRenderTexture(ShaderId_additive, accumulated ?? horizontal);
            var verticalConstants = new GaussianConstants
            {
                Direction = new Vector2(0f, 1f),
                SigmaRadius = sigmaRadius,
                IntegerRadius = integerRadius,
                TintR = tint.X,
                TintG = tint.Y,
                TintB = tint.Z,
                AdditiveBlend = accumulated != null ? 1f : 0f,
                InvSourceSize = invSourceSize,
            };
            using (RenderPassScope renderPass = context.BeginPass(output.FrameBuffer))
            {
                renderPass.DrawWithConstant(mesh, _gaussianMaterial, verticalConstants);
            }

            accumulated = output;
        }

        if (accumulated == null)
        {
            return;
        }

        // Additive composite of the accumulation, at the last executed stage's
        // resolution (1/2 with the default all-on sizes).
        _blitMaterial.SetRenderTexture(ShaderId_texture, accumulated);
        using (RenderPassScope renderPass = timestamps != null
            ? context.BeginPass(target, ReadOnlySpan<ClearColorData>.Empty,
                timestamps.QuerySet, null, (uint)(TimestampBaseSlot + 1))
            : context.BeginPass(target))
        {
            renderPass.Draw(mesh, _blitMaterial);
        }

        if (timestamps != null)
        {
            timestamps.ResolveAll(context.CommandBuffer);
        }
    }

    private void TryDisposeFrames()
    {
        _setupTexture?.Dispose();
        _setupTexture = null;

        if (_chainTextures != null)
        {
            for (int i = 0; i < _chainTextures.Length; i++)
            {
                _chainTextures[i].Dispose();
            }
            _chainTextures = null;
        }

        if (_horizontalTextures != null)
        {
            for (int i = 0; i < _horizontalTextures.Length; i++)
            {
                _horizontalTextures[i].Dispose();
            }
            _horizontalTextures = null;
        }

        if (_stageTextures != null)
        {
            for (int i = 0; i < _stageTextures.Length; i++)
            {
                _stageTextures[i].Dispose();
            }
            _stageTextures = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TryDisposeFrames();
            _blitMaterial.Dispose();
            _setupMaterial.Dispose();
            _downsampleMaterial.Dispose();
            _gaussianMaterial.Dispose();
        }
    }
}
