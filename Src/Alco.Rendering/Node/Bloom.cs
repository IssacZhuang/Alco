using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

public class Bloom : TextureProcessor
{
    private struct ClampConstant
    {
        public Vector2 InvFrameSize;
        public float Threshold;
        public float Spread;
        public float Intensity;
    }

    private struct DownSampleConstants
    {
        public Vector2 InvTextureSize;
        public float Spread;
    }

    private struct UpSampleConstants
    {
        public Vector2 InvTextureSize;
        public float Spread;
    }

    private struct BlitConstants
    {
        public float Gamma;
    }

    public const string ShaderId_texture = "texture";
    public const string ShaderId_previousTexture = "previousTexture";
    public const string ShaderId_currentTexture = "currentTexture";

    private readonly GPUAttachmentLayout _backBufferPass;
    private readonly RenderingSystem _renderingSystem;

    // One material per pass shader: pipeline states (additive blend on the final
    // composite, opaque on the pyramid passes) live on the materials and the
    // passes draw through RenderContext like every other renderer.
    private readonly GraphicsMaterial _blitMaterial;
    private readonly GraphicsMaterial _clampMaterial;
    private readonly GraphicsMaterial _downSampleMaterial;
    private readonly GraphicsMaterial _upSampleMaterial;

    // The input size the current pyramid was built for; a size mismatch on the next
    // blit rebuilds the pyramid lazily.
    private uint _builtWidth;
    private uint _builtHeight;

    private Vector2 _clampInvFrameSize;

    private float _threshold = 1.0f;
    private float _spread = 1.0f;
    private float _intensity = 1.0f;
    private float _gamma = 2.2f;

    public float Threshold
    {
        get => _threshold;
        set => _threshold = Math.Max(value, 0.0f);
    }

    public float Spread
    {
        get => _spread;
        set => _spread = Math.Max(value, 0.0001f);
    }

    public float Intensity
    {
        get => _intensity;
        set => _intensity = Math.Max(value, 0.0f);
    }

    /// <summary>
    /// Gets or sets the gamma correction value for bloom blending. Default is 2.2.
    /// </summary>
    public float Gamma
    {
        get => _gamma;
        set => _gamma = Math.Max(value, 0.0001f);
    }

    private readonly uint _targetDownSampleHeight;

    private RenderTexture[]? _downSampleTextures;
    private RenderTexture[]? _upSampleTextures;

    internal Bloom(RenderingSystem system, Shader blitShader, Shader clampShader, Shader downSampleShader, Shader upSampleShader, uint targetDownSampleHeight) : base(system)
    {
        _renderingSystem = system;
        _targetDownSampleHeight = targetDownSampleHeight;

        _backBufferPass = system.PreferredLightMapPass;

        // The bloom composite is additive: the target already holds the scene image.
        _blitMaterial = system.CreateGraphicsMaterial(blitShader, "bloom_blit_material");
        _blitMaterial.BlendState = BlendState.Additive;

        _clampMaterial = system.CreateGraphicsMaterial(clampShader, "bloom_clamp_material");
        _downSampleMaterial = system.CreateGraphicsMaterial(downSampleShader, "bloom_downsample_material");
        _upSampleMaterial = system.CreateGraphicsMaterial(upSampleShader, "bloom_upsample_material");
    }

    // Rebuilds the down/up sample pyramid when the input size changed since the last
    // blit. The pyramid textures are bound through the materials, so recreating them
    // needs no other notification.
    private void EnsurePyramid(RenderTexture input)
    {
        if (_downSampleTextures != null && _builtWidth == input.Width && _builtHeight == input.Height)
        {
            return;
        }

        _builtWidth = input.Width;
        _builtHeight = input.Height;

        TryDisposeFrames();

        int downSampleCount = GetDownSampleCount(input.Height);
        _downSampleTextures = new RenderTexture[downSampleCount];

        for (int i = 0; i < downSampleCount; i++)
        {
            uint width = input.Width >> (i + 1);
            uint height = input.Height >> (i + 1);

            if (i >= downSampleCount - 1)
            {
                float aspectRatio = (float)input.Width / input.Height;
                width = (uint)(_targetDownSampleHeight * aspectRatio);
                height = _targetDownSampleHeight;
            }

            _downSampleTextures[i] = _renderingSystem.CreateRenderTexture(_backBufferPass, width, height);
        }

        // Calculate and store InvFrameSize for clamp shader
        _clampInvFrameSize = new Vector2(1f) / new Vector2(_downSampleTextures[0].Width, _downSampleTextures[0].Height);

        int upSampleCount = downSampleCount - 1;
        _upSampleTextures = new RenderTexture[upSampleCount];

        for (int i = 0; i < upSampleCount; i++)
        {
            int offset = upSampleCount - i;
            uint width = input.Width >> (offset);
            uint height = input.Height >> (offset);

            _upSampleTextures[i] = _renderingSystem.CreateRenderTexture(_backBufferPass, width, height);
        }
    }

    /// <summary>
    /// GPU timing span for the current pyramid, set by the wrapping graph node on
    /// sample frames (null = no timing). The clamp pass writes the begin
    /// timestamp, the final composite pass the end timestamp, so one pair covers
    /// the whole down/up-sample chain.
    /// </summary>
    internal GpuTimestampSampler? TimestampSampler { get; set; }

    /// <summary>The first query slot of the timing span in <see cref="TimestampSampler"/>.</summary>
    internal int TimestampBaseSlot { get; set; }

    /// <summary>
    /// Builds the bloom pyramid from <paramref name="input"/> and records the whole
    /// down/up-sample chain plus the final additive composite onto
    /// <paramref name="context"/>, rendering into <paramref name="target"/>. The
    /// context is neither opened nor submitted here.
    /// </summary>
    /// <param name="context">The render context recording the frame.</param>
    /// <param name="input">The input render texture.</param>
    /// <param name="target">The target framebuffer; must already hold the scene image.</param>
    public override void Blit(RenderContext context, RenderTexture input, GPUFrameBuffer target)
    {
        EnsurePyramid(input);

        Mesh mesh = FullScreenMesh;
        GpuTimestampSampler? timestamps = TimestampSampler;

        RenderTexture clampFrame = _downSampleTextures![0];

        //clamp
        _clampMaterial.SetRenderTexture(ShaderId_texture, input);
        var clampShaderData = new ClampConstant
        {
            InvFrameSize = _clampInvFrameSize,
            Threshold = Threshold,
            Spread = Spread,
            Intensity = Intensity
        };
        using (RenderPassScope renderPass = timestamps != null
            ? context.BeginPass(clampFrame.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty,
                timestamps.QuerySet, (uint)TimestampBaseSlot, null)
            : context.BeginPass(clampFrame.FrameBuffer))
        {
            renderPass.DrawWithConstant(mesh, _clampMaterial, clampShaderData);
        }

        for (int i = 1; i < _downSampleTextures!.Length; i++)
        {
            RenderTexture downSampleFrame = _downSampleTextures[i];
            Vector2 invFrameSize = new Vector2(1f) / new Vector2(downSampleFrame.Width, downSampleFrame.Height);
            _downSampleMaterial.SetRenderTexture(ShaderId_texture, _downSampleTextures[i - 1]);
            var downSampleConstants = new DownSampleConstants
            {
                InvTextureSize = invFrameSize,
                Spread = Spread
            };
            using (RenderPassScope renderPass = context.BeginPass(downSampleFrame.FrameBuffer))
            {
                renderPass.DrawWithConstant(mesh, _downSampleMaterial, downSampleConstants);
            }
        }

        //up sample

        // First pass of the chain: previous is the pyramid's bottom, current the
        // step above it; later passes walk previous through the up-sample chain.
        _upSampleMaterial.SetRenderTexture(ShaderId_previousTexture, _downSampleTextures![_downSampleTextures.Length - 1]);
        _upSampleMaterial.SetRenderTexture(ShaderId_currentTexture, _downSampleTextures[_downSampleTextures.Length - 2]);

        for (int i = 0; i < _upSampleTextures!.Length; i++)
        {
            if (i > 0)
            {
                _upSampleMaterial.SetRenderTexture(ShaderId_previousTexture, _upSampleTextures[i - 1]);
                _upSampleMaterial.SetRenderTexture(ShaderId_currentTexture, _downSampleTextures[_downSampleTextures.Length - i - 2]);
            }

            var upSampleConstants = new UpSampleConstants
            {
                InvTextureSize = new Vector2(1f) / new Vector2(_upSampleTextures[i].Width, _upSampleTextures[i].Height),
                Spread = Spread
            };
            using (RenderPassScope renderPass = context.BeginPass(_upSampleTextures[i].FrameBuffer))
            {
                renderPass.DrawWithConstant(mesh, _upSampleMaterial, upSampleConstants);
            }
        }

        //blit
        _blitMaterial.SetRenderTexture(ShaderId_texture, _upSampleTextures![_upSampleTextures.Length - 1]);
        var blitConstants = new BlitConstants
        {
            Gamma = Gamma
        };
        using (RenderPassScope renderPass = timestamps != null
            ? context.BeginPass(target, ReadOnlySpan<ClearColorData>.Empty,
                timestamps.QuerySet, null, (uint)(TimestampBaseSlot + 1))
            : context.BeginPass(target))
        {
            renderPass.DrawWithConstant(mesh, _blitMaterial, blitConstants);
        }

        if (timestamps != null)
        {
            timestamps.ResolveAll(context.CommandBuffer);
        }
    }

    private int GetDownSampleCount(uint height)
    {
        int count = 0;
        while (height > _targetDownSampleHeight)
        {
            height >>= 1;
            count++;
        }
        return count;
    }

    private void TryDisposeFrames()
    {
        if (_downSampleTextures != null)
        {
            for (int i = 0; i < _downSampleTextures.Length; i++)
            {
                _downSampleTextures[i].Dispose();
            }
        }

        if (_upSampleTextures != null)
        {
            for (int i = 0; i < _upSampleTextures.Length; i++)
            {
                _upSampleTextures[i].Dispose();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TryDisposeFrames();
            _blitMaterial.Dispose();
            _clampMaterial.Dispose();
            _downSampleMaterial.Dispose();
            _upSampleMaterial.Dispose();
        }
    }
}
