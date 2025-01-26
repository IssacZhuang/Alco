
using Alco.Graphics;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

public class BloomSystem : BaseEngineSystem
{

    private readonly WindowRenderTarget _renderTarget;
    private readonly GameEngine _engine;
    private readonly RenderingSystem _rendering;
    private readonly Shader _blitShader;
    private readonly Shader _clampShader;
    private readonly Shader _downSampleShader;
    private readonly Shader _upSampleShader;
    private readonly Bloom _bloom;

    public override int Order => 900;

    public BloomSystem(GameEngine engine, WindowRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;

        RenderingSystem rendering = engine.Rendering;
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
        _bloom.Blit(_renderTarget.RenderTexture);
    }

    private void OnRenderTargetResize(uint2 size)
    {
        _bloom.SetInput(_renderTarget.RenderTexture);
    }

    public override void Dispose()
    {
        _renderTarget.OnResize -= OnRenderTargetResize;
        _bloom.Dispose();
        _clampShader.Dispose();
        _downSampleShader.Dispose();
        _blitShader.Dispose();
        GC.SuppressFinalize(this);
    }
}