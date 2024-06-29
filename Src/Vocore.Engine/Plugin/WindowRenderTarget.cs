
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.IO;

namespace Vocore.Engine;

public class WindowRenderTarget : BaseEngineSystem
{
    private readonly RenderingSystem _rendering;
    private readonly ColorSpaceConverter _converter;
    private readonly GPUSwapchain? _windowSwapchain;
    public WindowRenderTarget(GameEngine engine, Window window, Shader? blitShader)
    {
        _rendering = engine.Rendering;
        BuiltInAssets builtInAssets = engine.BuiltInAssets;

        if (blitShader == null)
        {
            blitShader = builtInAssets.Shader_Blit;
        }

        _converter = _rendering.CreateColorSpaceConverter(blitShader);

        _windowSwapchain = window.Swapchain;
        if (_windowSwapchain != null)
        {
            _converter.SetInput(_windowSwapchain.FrameBuffer);
        }
    }

    public override void OnEndFrame()
    {
        if (_windowSwapchain == null)
        {
            return;
        }
    }

}