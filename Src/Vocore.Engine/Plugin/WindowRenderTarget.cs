
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.IO;
using System.Runtime.CompilerServices;

namespace Vocore.Engine;

public class WindowRenderTarget : BaseEngineSystem
{
    private readonly Window _window;
    private readonly RenderingSystem _rendering;
    private readonly ColorSpaceConverter _converter;
    private readonly GPUSwapchain? _windowSwapchain;
    private readonly GPURenderPass? _renderPass;
    private RenderTexture _renderTarget;

    private bool _shouldResize = false;
    private uint _width;
    private uint _height;

    public RenderTexture RenderTarget
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderTarget;
    }

    internal WindowRenderTarget(GameEngine engine, Window window, GPURenderPass renderPass, Shader blitShader)
    {
        _window = window;
        _window.OnResize += OnWindowResize;

        _rendering = engine.Rendering;

        _renderPass = renderPass;
        _converter = _rendering.CreateColorSpaceConverter(blitShader);
        _renderTarget = _rendering.CreateRenderTexture(renderPass, (uint)window.Size.x, (uint)window.Size.y);

        _windowSwapchain = window.Swapchain;
        

        if (_windowSwapchain != null)
        {
            _converter.SetInput(_renderTarget.FrameBuffer);
        }
    }

    public override void OnEndFrame()
    {
        if (_windowSwapchain == null)
        {
            return;
        }

        _converter.Blit(_windowSwapchain.FrameBuffer);
        _windowSwapchain.Present();

        if (_shouldResize)
        {
            
            _renderTarget.Dispose();
            _renderTarget = _rendering.CreateRenderTexture(_renderPass!, _width, _height);
            _converter.SetInput(_renderTarget.FrameBuffer);
            _shouldResize = false;
        }
    }

    public override void Dispose()
    {
        _window.OnResize -= OnWindowResize;
    }

    private void OnWindowResize(int2 size)
    {
        _shouldResize = true;
        _width = (uint)size.x;
        _height = (uint)size.y;
        
        _windowSwapchain?.Resize(_width, _height);
    }

}