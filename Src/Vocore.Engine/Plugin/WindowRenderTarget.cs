
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.IO;
using System.Runtime.CompilerServices;

namespace Vocore.Engine;

public class WindowRenderTarget : BaseEngineSystem, IRenderTarget
{
    private readonly Window _window;
    private readonly RenderingSystem _rendering;
    private readonly GPUSwapchain? _windowSwapchain;
    private readonly GPUCommandBuffer _command;
    private ColorSpaceConverter _converter;
    private GPURenderPass _renderPass;
    private RenderTexture _renderTexture;

    private bool _shouldResize = false;
    private uint _width;
    private uint _height;

    public RenderTexture RenderTexture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderTexture;
    }

    public GPUFrameBuffer FrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderTexture.FrameBuffer;
    }

    internal WindowRenderTarget(GameEngine engine, Window window, GPURenderPass renderPass, Shader blitShader)
    {
        _window = window;
        _window.OnResize += OnWindowResize;

        _rendering = engine.Rendering;

        _width = math.max(1, window.Size.x);
        _height = math.max(1, window.Size.y);

        _renderPass = renderPass;
        _converter = _rendering.CreateColorSpaceConverter(blitShader);
        _renderTexture = _rendering.CreateRenderTexture(renderPass, _width, _height);

        _windowSwapchain = window.Swapchain;
        

        if (_windowSwapchain != null)
        {
            _converter.SetInput(_renderTexture.FrameBuffer);
        }

        _command = _rendering.GraphicsDevice.CreateCommandBuffer();
    }

    public void SetRenderPass(GPURenderPass renderPass, ColorSpaceConverter colorSpaceConverter)
    {
        _converter.Dispose();
        _converter = colorSpaceConverter;
        _renderPass = renderPass;
        _renderTexture.Dispose();
        _renderTexture = _rendering.CreateRenderTexture(renderPass, _width, _height);
        _converter.SetInput(_renderTexture.FrameBuffer);
    }

    public override void OnBeginFrame()
    {
        _command.Begin();
        _command.SetFrameBuffer(_renderTexture.FrameBuffer);
        _command.ClearColor(new ColorFloat(0, 0, 0, 1));
        _command.ClearDepthStencil(1, 0);
        _command.End();
        _rendering.GraphicsDevice.Submit(_command);
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
            
            _renderTexture.Dispose();
            _renderTexture = _rendering.CreateRenderTexture(_renderPass!, _width, _height);
            _converter.SetInput(_renderTexture.FrameBuffer);
            _shouldResize = false;
        }
    }

    public override void Dispose()
    {
        _window.OnResize -= OnWindowResize;
    }

    private void OnWindowResize(uint2 size)
    {
        _shouldResize = true;
        _width = size.x;
        _height = size.y;
        
        _windowSwapchain?.Resize(_width, _height);
    }

}