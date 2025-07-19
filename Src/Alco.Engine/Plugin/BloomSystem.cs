
using Alco.Graphics;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

public class BloomSystem : BaseEngineSystem
{

    private readonly ViewRenderTarget _renderTarget;
    private readonly GameEngine _engine;
    private readonly RenderingSystem _rendering;
    private readonly Shader _blitShader;
    private readonly Shader _clampShader;
    private readonly Shader _downSampleShader;
    private readonly Shader _upSampleShader;
    private readonly Bloom _bloom;
    private bool _isEnabled = true;

    public override int Order => 1000;

    /// <summary>
    /// Gets or sets whether the bloom effect is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// Gets or sets the bloom threshold value. Only pixels above this brightness will contribute to the bloom effect.
    /// </summary>
    public float Threshold
    {
        get => _bloom.Threshold;
        set => _bloom.Threshold = value;
    }

    /// <summary>
    /// Gets or sets the bloom spread intensity. Controls how much the bloom effect spreads across the entire pipeline.
    /// </summary>
    public float Spread
    {
        get => _bloom.Spread;
        set => _bloom.Spread = value;
    }

    /// <summary>
    /// Gets or sets the bloom intensity. Controls the final output strength of the bloom effect.
    /// </summary>
    public float Intensity
    {
        get => _bloom.Intensity;
        set => _bloom.Intensity = value;
    }

    /// <summary>
    /// Gets or sets the gamma correction value for bloom blending. Default is 2.2.
    /// </summary>
    public float Gamma
    {
        get => _bloom.Gamma;
        set => _bloom.Gamma = value;
    }

    public BloomSystem(GameEngine engine, ViewRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;

        RenderingSystem rendering = engine.RenderingSystem;
        BuiltInAssets builtInAssets = engine.BuiltInAssets;

        _engine = engine;
        _rendering = rendering;

        _clampShader = builtInAssets.Shader_BloomClamp;
        _downSampleShader = builtInAssets.Shader_BloomDownSample;
        _upSampleShader = builtInAssets.Shader_BloomUpSample;
        _blitShader = builtInAssets.Shader_BloomBlit;
        _bloom = rendering.CreateBloom(_blitShader, _clampShader, _downSampleShader, _upSampleShader, 11);
        _bloom.SetInput(renderTarget.RenderTexture);

        renderTarget.OnResize += OnRenderTargetResize;
    }

    public override void OnPostUpdate(float delta)
    {
        if (_isEnabled)
        {
            _bloom.Blit(_renderTarget.RenderTexture);
        }
    }

    private void OnRenderTargetResize(uint2 size)
    {
        _bloom.SetInput(_renderTarget.RenderTexture);
    }

    public override void Dispose()
    {
        _renderTarget.OnResize -= OnRenderTargetResize;
        _bloom.Dispose();
        GC.SuppressFinalize(this);
    }
}