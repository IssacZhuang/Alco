using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

public partial class WebGPUDevice : GPUDevice
{
    public readonly WGPUInstance Instance;
    public readonly WGPUAdapter Adapter;
    public readonly WGPUSurface Surface;
    public readonly WGPUDevice Device;
    public readonly WGPUQueue Queue;

    private readonly WGPUTextureFormat _swapChainFormat;
    private uint _width;
    private uint _height;
    private bool _vsync;

    public WGPUDevice Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Device;
    }

    public unsafe WebGPUDevice(in DeviceDescriptor descriptor)
    {
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
        Surface = Instance.CreateSurface(descriptor.SurfaceSource);

        WGPURequestAdapterOptions requestAdapterOptions = new WGPURequestAdapterOptions()
        {
            nextInChain = null,
            compatibleSurface = Surface,
            powerPreference = WGPUPowerPreference.HighPerformance,
            backendType = WGPUBackendType.Undefined,
            forceFallbackAdapter = false
        };

        fixed (WGPUAdapter* adapterPtr = &Adapter)
        {
            // TODO: add native error handler
            wgpuInstanceRequestAdapter(Instance, &requestAdapterOptions, &OnAdapterRequestEnded, (IntPtr)adapterPtr);
        }

        fixed (sbyte* name = descriptor.Name.GetUtf8Span())
        fixed (WGPUDevice* devicePtr = &Device)
        {
            WGPUDeviceDescriptor deviceDescriptor = new()
            {
                nextInChain = null,
                label = name,
                requiredLimits = null,
                requiredFeatureCount = 0,
            };

            // TODO: add native error handler
            wgpuAdapterRequestDevice(Adapter, &deviceDescriptor, &OnDeviceRequestEnded, (IntPtr)devicePtr);
        }

        Queue = wgpuDeviceGetQueue(Device);

        _swapChainFormat = wgpuSurfaceGetPreferredFormat(Surface, Adapter);
        _width = descriptor.InitialSurfaceSizeWidth;
        _height = descriptor.InitialSurfaceSizeHeight;
        _vsync = descriptor.VSync;

        ResizeSurfaceCore(_width, _height);
    }

    protected unsafe override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
;
        WGPUCommandBuffer buffer = ((WebGPUCommandBuffer)commandBuffer).Native;
        wgpuQueueSubmit(Queue, 1, &buffer);
    }

    protected override void Dispose(bool disposing)
    {
        wgpuInstanceRelease(Instance);
        wgpuDeviceDestroy(Device);
        wgpuDeviceRelease(Device);
    }

    public override PixelFormat GetPrefferedDepthFomat()
    {
        throw new NotImplementedException();
    }

    public override PixelFormat GetPrefferedSurfaceFomat()
    {
        throw new NotImplementedException();
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }

    protected override void DestroyCommandBufferCore(GPUCommandBuffer commandBuffer)
    {
        throw new NotImplementedException();
    }


    protected override void DestroyTextureCore(GPUTexture texture)
    {
        throw new NotImplementedException();
    }

    protected override void DestroyRenderPassCore(GPURenderPass renderPass)
    {
        throw new NotImplementedException();
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

        WGPUTextureFormat viewFormat = _swapChainFormat;
        WGPUSurfaceConfiguration surfaceConfiguration = new()
        {
            nextInChain = null,
            device = Device,
            format = _swapChainFormat,
            usage = WGPUTextureUsage.RenderAttachment,
            viewFormatCount = 1,
            viewFormats = &viewFormat,
            alphaMode = WGPUCompositeAlphaMode.Auto,
            width = width,
            height = height,
            presentMode = _vsync ? WGPUPresentMode.Fifo : WGPUPresentMode.Immediate,
        };
        wgpuSurfaceConfigure(Surface, &surfaceConfiguration);
    }

    protected unsafe override void SwapBuffersCore()
    {
        WGPUSurfaceTexture surfaceTexture = default;
        wgpuSurfaceGetCurrentTexture(Surface, &surfaceTexture);
        if (surfaceTexture.status == WGPUSurfaceGetCurrentTextureStatus.Timeout)
        {
            Console.WriteLine("Cannot acquire next swap chain texture");
            return;
        }

        if (surfaceTexture.status == WGPUSurfaceGetCurrentTextureStatus.Outdated)
        {
            Console.WriteLine("Surface texture is outdated, reconfigure the surface!");
            return;
        }
        wgpuSurfacePresent(Surface);
    }

    [UnmanagedCallersOnly]
    private unsafe static void OnAdapterRequestEnded(WGPURequestAdapterStatus status, WGPUAdapter candidateAdapter, sbyte* message, nint pUserData)
    {
        if (status == WGPURequestAdapterStatus.Success)
        {
            *(WGPUAdapter*)pUserData = candidateAdapter;
        }
        else
        {
            ErrorCallback?.Invoke("Could not get WebGPU adapter: " + Interop.GetString(message));
        }
    }

    [UnmanagedCallersOnly]
    private unsafe static void OnDeviceRequestEnded(WGPURequestDeviceStatus status, WGPUDevice device, sbyte* message, nint pUserData)
    {
        if (status == WGPURequestDeviceStatus.Success)
        {
            *(WGPUDevice*)pUserData = device;
        }
        else
        {
            ErrorCallback?.Invoke("Could not get WebGPU device: " + Interop.GetString(message));
        }
    }


}