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
    public delegate int2 GetSizeDelegate();

    private readonly GPUDevice _device;
    private readonly GetSizeDelegate _getSize;

    //preffered
    private readonly GPURenderPass _prefferedSDRPass;
    private readonly GPURenderPass _prefferedHDRPass;
    private readonly PixelFormat _prefferedSDRFormat;
    private readonly PixelFormat _prefferedHDRFormat;

    //state
    private readonly GPUFrameBuffer _defaultBackBuffer;
    private GPURenderPass? _mainRenderPass;
    private GPUFrameBuffer? _mainFrameBuffer;
    private ColorSpaceConverter? _mainPassToSwapChain;

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

    public RenderingSystem(GPUDevice device, GetSizeDelegate getSize)
    {
        _device = device;
        _getSize = getSize;

        _prefferedSDRFormat = device.PrefferedSDRFormat;
        _prefferedHDRFormat = device.PrefferedHDRFormat;

        _prefferedSDRPass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(_prefferedSDRFormat)],
            new(PixelFormat.Depth24PlusStencil8),
            "sdr_pass"
        ));

        _prefferedHDRPass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(_prefferedHDRFormat)],
            new(PixelFormat.Depth24PlusStencil8),
            "hdr_pass"
        ));

        int2 size = getSize();
        FrameBufferDescriptor descriptor = new FrameBufferDescriptor
        {
            RenderPass = _prefferedSDRPass,
            Width = (uint)size.x,
            Height = (uint)size.y,
            Name = "default_back_buffer"
        };

        _defaultBackBuffer = _device.CreateFrameBuffer(descriptor);
        _selectedFrameBuffer = _defaultBackBuffer;
    }

    /// <summary>
    /// Use a custom render pass to create a frame buffer that used to replace the swap chain frame buffer. Usually used for post-processing and HDR rendering.
    /// </summary>
    /// <param name="renderPass">The custom render pass</param>
    /// <param name="colorSpaceConverter">The tone mapping to convert color of custom render pass to the swap chain frame buffer</param>
    public void SetMainRenderPass(GPURenderPass renderPass, ColorSpaceConverter colorSpaceConverter)
    {
        _mainRenderPass = renderPass;
        _mainPassToSwapChain = colorSpaceConverter;

        UpdateMainFrameBuffer();
    }

    public void ResetMainRenderPass()
    {
        _mainRenderPass = null;
        _mainPassToSwapChain = null;
        _mainFrameBuffer?.Dispose();
        _mainFrameBuffer = null;
        _selectedFrameBuffer = _defaultBackBuffer;
    }


    public void BlitMainFrameBuffer(GPUFrameBuffer frameBuffer)
    {
        _mainPassToSwapChain?.Blit(frameBuffer);
    }

    private void UpdateMainFrameBuffer()
    {
        if (_mainRenderPass == null)
        {
            return;
        }

        if (_mainPassToSwapChain == null)
        {
            return;
        }

        int2 size = _getSize();
        _mainFrameBuffer?.Dispose();

        uint width = (uint)size.x;
        uint height = (uint)size.y;

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

    internal void OnResize(int2 size)
    {
        UpdateMainFrameBuffer();
    }
}