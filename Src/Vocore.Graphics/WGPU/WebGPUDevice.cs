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
    }

    protected unsafe override void InternalSubmit(GPUCommandBuffer commandBuffer)
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

    protected override GPUBuffer InternalCreateBuffer(in BufferDescriptor descriptor)
    {
        throw new NotImplementedException();
    }

    protected override GPUCommandBuffer InternalCreateCommandBuffer(in CommandBufferDescriptor? descriptor = null)
    {
        return new WebGPUCommandBuffer(Native, descriptor);
    }

    protected override GPUTexture InternalCreateTexture(in TextureDescriptor descriptor)
    {
        return new WebGPUTexture(Native, descriptor);
    }

    protected override GPURenderPass InternalCreateRenderPass(in RenderPassDescriptor descriptor)
    {
       return new WebGPURenderPass(Native, descriptor);
    }


    protected override void InternalDestroyBuffer(GPUBuffer buffer)
    {
        throw new NotImplementedException();
    }

    protected override void InternalDestroyCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        throw new NotImplementedException();
    }

    
    protected override void InternalDestroyTexture(GPUTexture texture)
    {
        throw new NotImplementedException();
    }

    protected override void InternalDestroyRenderPass(GPURenderPass renderPass)
    {
        throw new NotImplementedException();
    }

    protected override unsafe void InternalUpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        WGPUBuffer nativeBuffer = ((WebGPUBuffer)buffer).Native;
        wgpuQueueWriteBuffer(Queue, nativeBuffer, bufferOffset, data, size);
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