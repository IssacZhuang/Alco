namespace Vocore.Rendering;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

/// <summary>
/// The facility to manage rendering resource and perform rendering.
/// <br/>It is a high-level encapsulation of <see cref="GPUDevice"/>.
/// </summary>
public partial class RenderingSystem
{
    private readonly GPUDevice _device;
    private readonly Dictionary<string, GPURenderPass> _renderPasses;
    //preffered
    private readonly GPURenderPass _prefferedSDRPass;
    private readonly GPURenderPass _prefferedHDRPass;

    //state
    private GPURenderPass? _mainRenderPass;
    private GPUFrameBuffer? _mainFrameBuffer;
    private ToneMap? _mainPassToSwapChain;
    private GPUFrameBuffer _selectedFrameBuffer;

    

    public GPUDevice GraphicsDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    public GPURenderPass DefaultRenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _selectedFrameBuffer.RenderPass;
        }
    }

    public GPUFrameBuffer DefaultFrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _selectedFrameBuffer;
        }
    }

    public GPURenderPass PrefferedSDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _prefferedSDRPass;
        }
    }

    public GPURenderPass PrefferedHDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _prefferedHDRPass;
        }
    }

    public RenderingSystem(GPUDevice device)
    {
        _device = device;
        _renderPasses = new Dictionary<string, GPURenderPass>();
        _selectedFrameBuffer = device.SwapChainFrameBuffer;

        _prefferedSDRPass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(PixelFormat.RGBA8Unorm)],
            new(PixelFormat.Depth24PlusStencil8),
            "sdr_pass"
        ));

        _prefferedHDRPass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(device.PrefferedHDRFormat)],
            new(PixelFormat.Depth24PlusStencil8),
            "hdr_pass"
        ));

        RegisterRenderPass("Surface", device.SwapChainFrameBuffer.RenderPass);
    }

    /// <summary>
    /// Use a custom render pass to create a frame buffer that used to replace the swap chain frame buffer. Usually used for post-processing and HDR rendering.
    /// </summary>
    /// <param name="renderPass">The custom render pass</param>
    /// <param name="toneMap">The tone mapping to convert color of custom render pass to the swap chain frame buffer</param>
    public void SetMainRenderPass(GPURenderPass renderPass, ToneMap toneMap)
    {
        _mainRenderPass = renderPass;
        _mainPassToSwapChain = toneMap;

        UpdateMainFrameBuffer();
    }

    public void ResetMainRenderPass()
    {
        _mainRenderPass = null;
        _mainPassToSwapChain = null;
        _mainFrameBuffer?.Dispose();
        _mainFrameBuffer = null;
        _selectedFrameBuffer = _device.SwapChainFrameBuffer;
    }

    public void RegisterRenderPass(string name, GPURenderPass renderPass)
    {
        if (_renderPasses.ContainsKey(name))
        {
            throw new ArgumentException($"The render pass with name '{name}' has already been registered.");
        }

        _renderPasses.Add(name, renderPass);
    }


    public bool HasRenderPass(string name)
    {
        return _renderPasses.ContainsKey(name);
    }

    public GPURenderPass GetRenderPass(string name)
    {
        if (!_renderPasses.TryGetValue(name, out GPURenderPass? renderPass))
        {
            throw new ArgumentException($"The render pass with name '{name}' has not been registered.");
        }

        return renderPass;
    }

    public bool TryGetRenderPass(string name, [NotNullWhen(true)] out GPURenderPass? renderPass)
    {
        return _renderPasses.TryGetValue(name, out renderPass);
    }

    public void UnregisterRenderPass(string name)
    {
        if (!_renderPasses.ContainsKey(name))
        {
            throw new ArgumentException($"The render pass with name '{name}' has not been registered.");
        }

        _renderPasses.Remove(name);
    }

    private void UpdateMainFrameBuffer()
    {
        if(_mainRenderPass == null)
        {
            return;
        }

        if(_mainPassToSwapChain == null)
        {
            return;
        }

        _mainFrameBuffer?.Dispose();

        uint width = _device.SwapChainFrameBuffer.Width;
        uint height = _device.SwapChainFrameBuffer.Height;

        FrameBufferDescriptor descriptor = new FrameBufferDescriptor
        {
            RenderPass = _mainRenderPass,
            Width = width,
            Height = height,
            Name = "main_frame_buffer"
        };

        _mainFrameBuffer = _device.CreateFrameBuffer(descriptor);
        _mainPassToSwapChain.SetInput(_mainFrameBuffer);
        _selectedFrameBuffer = _mainFrameBuffer;
    }

    internal void RenderToSwapChain()
    {
        _mainPassToSwapChain?.Blit(_device.SwapChainFrameBuffer);
    }

    internal void OnResize(int2 size)
    {
        UpdateMainFrameBuffer();
    }
}