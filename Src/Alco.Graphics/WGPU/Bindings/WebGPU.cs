
using System.Runtime.InteropServices;

namespace WebGPU;

internal delegate void WGPULogCallback(WGPULogLevel level, string message, nint userdata = 0);

internal delegate void WGPUErrorCallback(WGPUErrorType type, string message);

internal static unsafe partial class WebGPU
{
    private const string LibraryName = "wgpu_native";

    private static WGPULogCallback? s_logCallback;

    public static void wgpuSetLogCallback(WGPULogCallback callback, nint userdata = 0)
    {
        s_logCallback = callback;
        wgpuSetLogCallback(callback != null ? &NativeLogCallback : null, userdata.ToPointer());
    }

    public static ReadOnlySpan<WGPUFeatureName> wgpuAdapterEnumerateFeatures(WGPUAdapter adapter)
    {
        WGPUSupportedFeatures supportedFeatures = new();
        wgpuAdapterGetFeatures(adapter, &supportedFeatures);

        WGPUFeatureName[] features = new WGPUFeatureName[(int)supportedFeatures.featureCount];
        for (nuint i = 0; i < supportedFeatures.featureCount; i++)
        {
            features[i] = supportedFeatures.features[i];
        }

        wgpuSupportedFeaturesFreeMembers(supportedFeatures);

        return features;
    }

    public static void wgpuQueueSubmit(WGPUQueue queue, WGPUCommandBuffer commandBuffer)
    {
        wgpuQueueSubmit(queue, 1u, &commandBuffer);
    }

    public static void wgpuQueueSubmit(WGPUQueue queue, ReadOnlySpan<WGPUCommandBuffer> commandBuffers)
    {
        fixed (WGPUCommandBuffer* pCommandBuffers = commandBuffers)
        {
            wgpuQueueSubmit(queue, (nuint)commandBuffers.Length, pCommandBuffers);
        }
    }

    public static void wgpuQueueSubmit(WGPUQueue queue, WGPUCommandBuffer[] commandBuffers)
    {
        fixed (WGPUCommandBuffer* pCommandBuffers = commandBuffers)
        {
            wgpuQueueSubmit(queue, (nuint)commandBuffers.LongLength, pCommandBuffers);
        }
    }

    public static void wgpuQueueWriteBuffer<T>(WGPUQueue queue, WGPUBuffer buffer, ref T data, ulong bufferOffset, nuint size)
        where T : unmanaged
    {
        fixed (void* dataPointer = &data)
        {
            wgpuQueueWriteBuffer(queue, buffer, bufferOffset, dataPointer, size);
        }
    }

    public static void wgpuQueueWriteBuffer<T>(WGPUQueue queue, WGPUBuffer buffer, ReadOnlySpan<T> data, ulong bufferOffset = 0)
        where T : unmanaged
    {
        fixed (void* dataPointer = data)
        {
            wgpuQueueWriteBuffer(queue, buffer, bufferOffset, dataPointer, (nuint)(data.Length * sizeof(T)));
        }
    }

    public static void wgpuQueueWriteBuffer<T>(WGPUQueue queue, WGPUBuffer buffer, T[] data, ulong bufferOffset = 0)
        where T : unmanaged
    {
        fixed (void* dataPointer = data)
        {
            wgpuQueueWriteBuffer(queue, buffer, bufferOffset, dataPointer, (nuint)(data.Length * sizeof(T)));
        }
    }

    public static void wgpuQueueWriteTexture<T>(WGPUQueue queue, WGPUTexelCopyTextureInfo* destination, ref T data, nuint dataSize, WGPUTexelCopyBufferLayout* dataLayout, WGPUExtent3D* writeSize)
        where T : unmanaged
    {
        fixed (void* dataPointer = &data)
        {
            wgpuQueueWriteTexture(queue, destination, dataPointer, dataSize, dataLayout, writeSize);
        }
    }

    public static void wgpuQueueWriteTexture<T>(WGPUQueue queue, WGPUTexelCopyTextureInfo* destination, ReadOnlySpan<T> data, nuint dataSize, WGPUTexelCopyBufferLayout* dataLayout, WGPUExtent3D* writeSize)
        where T : unmanaged
    {
        fixed (void* dataPointer = data)
        {
            wgpuQueueWriteTexture(queue, destination, dataPointer, dataSize, dataLayout, writeSize);
        }
    }

    public static void wgpuQueueWriteTexture<T>(WGPUQueue queue, WGPUTexelCopyTextureInfo* destination, T[] data, nuint dataSize, WGPUTexelCopyBufferLayout* dataLayout, WGPUExtent3D* writeSize)
        where T : unmanaged
    {
        fixed (void* dataPointer = data)
        {
            wgpuQueueWriteTexture(queue, destination, dataPointer, dataSize, dataLayout, writeSize);
        }
    }

    public static WGPUCommandEncoder wgpuDeviceCreateCommandEncoder(WGPUDevice device, string? label = default, WGPUChainedStruct* nextInChain = default)
    {
        ReadOnlySpan<byte> labelSpan = label.GetUtf8Span();
        fixed (byte* pLabel = labelSpan)
        {
            WGPUCommandEncoderDescriptor descriptor = new()
            {
                nextInChain = nextInChain,
                label = new WGPUStringView(pLabel, labelSpan.Length)
            };

            return wgpuDeviceCreateCommandEncoder(device, &descriptor);
        }
    }

    public static WGPUCommandBuffer wgpuCommandEncoderFinish(WGPUCommandEncoder commandEncoder, string? label = default, WGPUChainedStruct* nextInChain = default)
    {
        ReadOnlySpan<byte> labelSpan = label.GetUtf8Span();
        fixed (byte* pLabel = labelSpan)
        {
            WGPUCommandBufferDescriptor descriptor = new()
            {
                nextInChain = nextInChain,
                label = new WGPUStringView(pLabel, labelSpan.Length)
            };

            return wgpuCommandEncoderFinish(commandEncoder, &descriptor);
        }
    }

    public static WGPUShaderModule wgpuDeviceCreateShaderModule(WGPUDevice device, ReadOnlySpan<byte> wgslShaderSource)
    {
        fixed (byte* pShaderSource = wgslShaderSource)
        {
            WGPUStringView wgpuStringView = new(pShaderSource, wgslShaderSource.Length);
            // Use the extension mechanism to load a WGSL shader source code
            WGPUShaderSourceWGSL shaderCodeDesc = new();
            shaderCodeDesc.chain.next = null;
            shaderCodeDesc.chain.sType = WGPUSType.ShaderSourceWGSL;
            shaderCodeDesc.code = wgpuStringView;

            WGPUShaderModuleDescriptor shaderDesc = new()
            {
                nextInChain = &shaderCodeDesc.chain,
            };

            return wgpuDeviceCreateShaderModule(device, &shaderDesc);
        }
    }

    public static WGPUShaderModule wgpuDeviceCreateShaderModule(WGPUDevice device, string wgslShaderSource)
    {
        return wgpuDeviceCreateShaderModule(device, wgslShaderSource.GetUtf8Span());
    }

    public static WGPUBuffer wgpuDeviceCreateBuffer(WGPUDevice device, WGPUBufferUsage usage, ulong size, bool mappedAtCreation = false)
    {
        WGPUBufferDescriptor descriptor = new()
        {
            nextInChain = null,
            usage = usage,
            size = size,
            mappedAtCreation = mappedAtCreation
        };
        return wgpuDeviceCreateBuffer(device, &descriptor);
    }

    public static WGPUBuffer wgpuDeviceCreateBuffer(WGPUDevice device, WGPUBufferUsage usage, int size, bool mappedAtCreation = false)
    {
        WGPUBufferDescriptor descriptor = new()
        {
            nextInChain = null,
            usage = usage,
            size = (ulong)size,
            mappedAtCreation = mappedAtCreation
        };
        return wgpuDeviceCreateBuffer(device, &descriptor);
    }

    public static WGPUBuffer wgpuDeviceCreateBuffer<T>(WGPUDevice device, WGPUQueue queue, Span<T> data, WGPUBufferUsage usage, bool mappedAtCreation = false)
        where T : unmanaged
    {
        WGPUBufferDescriptor descriptor = new()
        {
            nextInChain = null,
            usage = usage | WGPUBufferUsage.CopyDst,
            size = (ulong)(sizeof(T) * data.Length),
            mappedAtCreation = mappedAtCreation
        };

        WGPUBuffer buffer = wgpuDeviceCreateBuffer(device, &descriptor);

        fixed (void* dataPointer = data)
        {
            wgpuQueueWriteBuffer(queue, buffer, 0, dataPointer, (nuint)descriptor.size);
        }

        return buffer;
    }

    #region Native Callbacks
    [UnmanagedCallersOnly]
    private static void NativeLogCallback(WGPULogLevel level, WGPUStringView message, void* userData)
    {
        if (s_logCallback != null)
        {
            string strMessage = Interop.GetString(message.data, (int)message.length)!;
            s_logCallback(level, strMessage, (nint)userData);
        }
    }
    #endregion
}
