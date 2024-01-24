using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

public partial class WebGPUDevice : GPUDevice
{

    #region Properties
    public readonly WGPUInstance Instance;
    public readonly WGPUAdapter Adapter;
    public readonly WGPUSurface Surface;
    public readonly WGPUDevice Device;
    public readonly WGPUQueue Queue;

    private readonly DeviceDescriptor _descriptor;


    private readonly WGPUTextureFormat _swapChainFormat;
    private readonly PixelFormat _preferredSurfaceFormat;
    private uint _width;
    private uint _height;
    private bool _vsync;

    private readonly WebGPUSurfaceFrameBuffer _surfaceFrameBuffer;
    private readonly WebGPURenderPass _surfaceRenderPass;

    private bool _hasCommandSubmitted;

    #endregion

    #region Abstract Implementation

    public override string Name { get; }

    public override GPURenderPass SwapChainRenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _surfaceRenderPass;
    }

    public override GPUFrameBuffer SwapChainFrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _surfaceFrameBuffer;
    }

    public override PixelFormat PrefferedDepthFomat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => PixelFormat.Depth24PlusStencil8;
    }

    public override PixelFormat PrefferedSurfaceFomat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredSurfaceFormat;
    }
    public override bool VSync
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _vsync;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _vsync = value;
    }

    protected unsafe override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        WGPUCommandBuffer buffer = ((WebGPUCommandBuffer)commandBuffer).Native;
        wgpuQueueSubmit(Queue, 1, &buffer);
        _hasCommandSubmitted = true;
    }

    protected override void Dispose(bool disposing)
    {
        _surfaceFrameBuffer.Dispose();
        _surfaceRenderPass.Dispose();

        wgpuInstanceRelease(Instance);
        wgpuDeviceDestroy(Device);
        wgpuDeviceRelease(Device);
        wgpuSurfaceRelease(Surface);
        wgpuAdapterRelease(Adapter);
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        return new WebGPUBuffer(Native, descriptor);
    }

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
    {
        return new WebGPUCommandBuffer(Native, descriptor);
    }

    protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor)
    {
        return new WebGPUTexture(Native, descriptor);
    }

    protected override GPURenderPass CreateRenderPassCore(in RenderPassDescriptor descriptor)
    {
        return new WebGPURenderPass(Native, descriptor);
    }


    protected override void DestroyBufferCore(GPUBuffer buffer)
    {
        buffer.Dispose();
    }

    protected override void DestroyCommandBufferCore(GPUCommandBuffer commandBuffer)
    {
        commandBuffer.Dispose();
    }

    protected override void DestroyTextureCore(GPUTexture texture)
    {
        texture.Dispose();
    }

    protected override void DestroyRenderPassCore(GPURenderPass renderPass)
    {
        renderPass.Dispose();
    }

    protected override unsafe void UpdateBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        WGPUBuffer nativeBuffer = ((WebGPUBuffer)buffer).Native;
        wgpuQueueWriteBuffer(Queue, nativeBuffer, bufferOffset, data, size);
    }

    protected unsafe override void ResizeSurfaceCore(uint width, uint height)
    {
        _width = width;
        _height = height;
        _surfaceFrameBuffer.UpdateSurfaceConfig(GetSurfaceConfig());
    }

    protected unsafe override void SwapBuffersCore()
    {
        // WGPUSurfaceTexture surfaceTexture = default;
        // wgpuSurfaceGetCurrentTexture(Surface, &surfaceTexture);

        // if (surfaceTexture.status == WGPUSurfaceGetCurrentTextureStatus.Timeout)
        // {
        //     Console.WriteLine("Cannot acquire next swap chain texture");
        //     return;
        // }

        // if (surfaceTexture.status == WGPUSurfaceGetCurrentTextureStatus.Outdated)
        // {
        //     Console.WriteLine("Surface texture is outdated, reconfigure the surface!");
        //     return;
        // }

        // WGPUTextureView view = wgpuTextureCreateView(surfaceTexture.texture, null);

        // WGPUCommandEncoder encoder = wgpuDeviceCreateCommandEncoder(Device, null);

        // WGPURenderPassColorAttachment attachment = new WGPURenderPassColorAttachment
        // {
        //     view = view,
        //     resolveTarget = WGPUTextureView.Null,
        //     loadOp = WGPULoadOp.Clear,
        //     storeOp = WGPUStoreOp.Store,
        //     clearValue = new WGPUColor
        //     {
        //         r = 0.1,
        //         g = 0.2,
        //         b = 0.3,
        //         a = 1,
        //     },
        // };
        // WGPURenderPassDescriptor renderPassDescriptor = new()
        // {
        //     colorAttachmentCount = 1,
        //     colorAttachments = &attachment,
        //     depthStencilAttachment = null,
        // };

        // WGPURenderPassEncoder passEncoder = wgpuCommandEncoderBeginRenderPass(encoder, &renderPassDescriptor);
        // wgpuRenderPassEncoderEnd(passEncoder);
        // WGPUCommandBuffer buffer = wgpuCommandEncoderFinish(encoder, null);
        // wgpuQueueSubmit(Queue, 1, &buffer);
        // wgpuSurfacePresent(Surface);
        // wgpuCommandBufferRelease(buffer);
        // wgpuCommandEncoderRelease(encoder);
        // wgpuRenderPassEncoderRelease(passEncoder);
        // wgpuTextureRelease(surfaceTexture.texture);
        // wgpuTextureViewRelease(view);
        if (!_hasCommandSubmitted)
        {
            return;
        }
        _surfaceFrameBuffer.SwapBuffers();
        _hasCommandSubmitted = false;
    }

    #endregion

    #region WebGPU Implementation

    public WGPUDevice Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Device;
    }

    public unsafe WebGPUDevice(in DeviceDescriptor descriptor)
    {
        _descriptor = descriptor;
        Name = descriptor.Name;
        wgpuSetLogCallback(LogCallback);

        // create instance
        WGPUInstanceExtras extras = new WGPUInstanceExtras()
        {
            flags = descriptor.Debug ? WGPUInstanceFlags.Validation : WGPUInstanceFlags.None,
            backends = UtilsWebGPU.BackendToWebGPU(descriptor.Backend),
        };

        WGPUInstanceDescriptor instanceDescriptor = new WGPUInstanceDescriptor()
        {
            nextInChain = (WGPUChainedStruct*)&extras,
        };

        Instance = wgpuCreateInstance(&instanceDescriptor);

        // create surface
        Surface = Instance.CreateSurface(descriptor.SurfaceSource);

        // create adapter
        WGPURequestAdapterOptions requestAdapterOptions = new WGPURequestAdapterOptions()
        {
            nextInChain = null,
            compatibleSurface = Surface,
            backendType = UtilsWebGPU.BackendTypeToWebGPU(descriptor.Backend),
        };

        WGPUAdapter adapter = WGPUAdapter.Null;
        wgpuInstanceRequestAdapter(Instance, &requestAdapterOptions, &OnAdapterRequestEnded, new nint(&adapter));
        Adapter = adapter;

        // create device
        fixed (sbyte* name = descriptor.Name.GetUtf8Span())
        {
            WGPUDeviceDescriptor deviceDescriptor = new()
            {
                nextInChain = null,
                label = name,
                requiredLimits = null,
                requiredFeatureCount = 0,
            };

            WGPUDevice device = WGPUDevice.Null;
            wgpuAdapterRequestDevice(Adapter, &deviceDescriptor, &OnDeviceRequestEnded, new nint(&device));
            Device = device;
        }
        wgpuDeviceSetUncapturedErrorCallback(Device, &OnUnhandleError);

        //get queue
        Queue = wgpuDeviceGetQueue(Device);

        // load config
        _swapChainFormat = wgpuSurfaceGetPreferredFormat(Surface, Adapter);
        _preferredSurfaceFormat = UtilsWebGPU.PixelFormatToAbstract(_swapChainFormat);

        _width = descriptor.InitialSurfaceSizeWidth;
        _height = descriptor.InitialSurfaceSizeHeight;
        _vsync = descriptor.VSync;

        //TODO: create render pass and frame buffer
        // create surface render pass
        RenderPassDescriptor renderPassDescriptor = new RenderPassDescriptor(
            new ColorAttachment[]
            {
                new ColorAttachment()
                {
                    Format = UtilsWebGPU.PixelFormatToAbstract(_swapChainFormat),
                    ClearColor = descriptor.SurfaceClearColor,
                },
            },
            new DepthAttachment()
            {
                Format = PixelFormat.Depth24PlusStencil8,
                ClearDepth = 1.0f,
                ClearStencil = 0,
            },
            "Surface Render Pass"
        );

        _surfaceRenderPass = new WebGPURenderPass(Device, renderPassDescriptor);

        // create surface frame buffer
        _surfaceFrameBuffer = new WebGPUSurfaceFrameBuffer(_surfaceRenderPass, Surface, GetSurfaceConfig());
    }

    private WGPUSurfaceConfiguration GetSurfaceConfig()
    {
        return new WGPUSurfaceConfiguration
        {
            nextInChain = null,
            device = Device,
            format = _swapChainFormat,
            usage = WGPUTextureUsage.RenderAttachment,
            // viewFormatCount = 0,
            // viewFormats = null,
            alphaMode = WGPUCompositeAlphaMode.Auto,
            width = _width,
            height = _height,
            presentMode = GetPresentMode(),
        };
    }

    private WGPUPresentMode GetPresentMode()
    {
        if (_descriptor.Backend == GraphicsBackend.OpenGL || _descriptor.Backend == GraphicsBackend.OpenGLES)
        {
            return _vsync ? WGPUPresentMode.Fifo : WGPUPresentMode.Mailbox;
        }
        return _vsync ? WGPUPresentMode.Fifo : WGPUPresentMode.Immediate;
    }

    #endregion

    #region Callbacks

    [UnmanagedCallersOnly]
    private unsafe static void OnAdapterRequestEnded(WGPURequestAdapterStatus status, WGPUAdapter candidateAdapter, sbyte* message, nint pUserData)
    {
        if (status == WGPURequestAdapterStatus.Success)
        {
            *(WGPUAdapter*)pUserData = candidateAdapter;
            GraphicsLogger.Info("Adapter created");
        }
        else
        {
            throw new GraphicsException("Could not get WebGPU adapter: " + Interop.GetString(message));
        }
    }

    [UnmanagedCallersOnly]
    private unsafe static void OnDeviceRequestEnded(WGPURequestDeviceStatus status, WGPUDevice device, sbyte* message, nint pUserData)
    {
        if (status == WGPURequestDeviceStatus.Success)
        {
            *(WGPUDevice*)pUserData = device;
            GraphicsLogger.Info("Device created");
        }
        else
        {
            throw new GraphicsException("Could not get WebGPU device: " + Interop.GetString(message));
        }
    }

    [UnmanagedCallersOnly]
    private unsafe static void OnUnhandleError(WGPUErrorType type, sbyte* message, nint pUserData)
    {
        throw new GraphicsException("Unhandle WebGPU error: " + Interop.GetString(message));
    }

    private static void LogCallback(WGPULogLevel level, string message, nint userdata = 0)
    {
        switch (level)
        {
            case WGPULogLevel.Error:
                throw new GraphicsException(message);
            case WGPULogLevel.Warn:
                GraphicsLogger.Warning(message);
                break;
            case WGPULogLevel.Info:
            case WGPULogLevel.Debug:
            case WGPULogLevel.Trace:
                GraphicsLogger.Info(message);
                break;
        }
    }

    #endregion
}