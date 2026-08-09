
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Content processor node that adds a bloom glow: the input is first copied into the
/// target, then the bloom pyramid is composited additively on top.
/// </summary>
public sealed class RenderNode_Bloom : AutoDisposable, IContentProcessorNode
{
    private readonly Bloom _bloom;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _blitMaterial;

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Only pixels above this brightness contribute to the bloom effect.
    /// </summary>
    public float Threshold
    {
        get => _bloom.Threshold;
        set => _bloom.Threshold = value;
    }

    /// <summary>
    /// How far the bloom spreads across the pyramid.
    /// </summary>
    public float Spread
    {
        get => _bloom.Spread;
        set => _bloom.Spread = value;
    }

    /// <summary>
    /// The final output strength of the bloom effect.
    /// </summary>
    public float Intensity
    {
        get => _bloom.Intensity;
        set => _bloom.Intensity = value;
    }

    /// <summary>
    /// The gamma correction value for bloom blending. Default is 2.2.
    /// </summary>
    public float Gamma
    {
        get => _bloom.Gamma;
        set => _bloom.Gamma = value;
    }

    /// <summary>
    /// Creates the node wrapping the bloom effect. The node takes ownership of
    /// <paramref name="bloom"/>; <paramref name="blitShader"/> stays owned by the caller.
    /// </summary>
    public RenderNode_Bloom(RenderingSystem rendering, Bloom bloom, Shader blitShader)
    {
        _bloom = bloom;
        _renderContext = rendering.CreateRenderContext();
        _fullScreenMesh = rendering.MeshFullScreen;
        _blitMaterial = rendering.CreateMaterial(blitShader);
    }

    /// <inheritdoc />
    public void OnRenderForward(RenderTexture input, RenderTexture target)
    {
        // Setting the same texture is a no-op; an in-place resize of the input is
        // picked up by the material system's version check.
        _blitMaterial.SetRenderTexture(ShaderResourceId.Texture, input);

        // The bloom blit is additive: the target must already hold the scene image.
        _renderContext.Begin(target.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _blitMaterial);
        _renderContext.End();

        _bloom.Blit(input, target.FrameBuffer);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bloom.Dispose();
            _blitMaterial.Dispose();
            _renderContext.Dispose();
        }
    }
}
