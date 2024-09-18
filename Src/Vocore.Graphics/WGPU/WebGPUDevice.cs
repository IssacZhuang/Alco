using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal partial class WebGPUDevice : GPUDevice
{

    #region Properties


    public static readonly WGPULimits DefaultLimits = new WGPULimits
    {
        maxTextureDimension1D = 8192,
        maxTextureDimension2D = 8192,
        maxTextureDimension3D = 2048,
        maxTextureArrayLayers = 256,
        maxBindGroups = 4,
        maxBindingsPerBindGroup = 1000,
        maxDynamicUniformBuffersPerPipelineLayout = 8,
        maxDynamicStorageBuffersPerPipelineLayout = 4,
        maxSampledTexturesPerShaderStage = 16,
        maxSamplersPerShaderStage = 16,
        maxStorageBuffersPerShaderStage = 8,
        maxStorageTexturesPerShaderStage = 4,
        maxUniformBuffersPerShaderStage = 12,
        maxUniformBufferBindingSize = 64 * 1024,
        maxStorageBufferBindingSize = 128 * 1024 * 1024,
        maxVertexBuffers = 8,
        maxBufferSize = 256 * 1024 * 1024,
        maxVertexAttributes = 16,
        maxVertexBufferArrayStride = 2048,
        minUniformBufferOffsetAlignment = 256,
        minStorageBufferOffsetAlignment = 256,
        maxInterStageShaderComponents = 60,
        maxInterStageShaderVariables = 60,
        maxComputeWorkgroupStorageSize = 16352,
        maxComputeInvocationsPerWorkgroup = 256,
        maxComputeWorkgroupSizeX = 256,
        maxComputeWorkgroupSizeY = 256,
        maxComputeWorkgroupSizeZ = 64,
        maxComputeWorkgroupsPerDimension = 65535,
    };

    public readonly WGPUInstance Instance;
    public readonly WGPUAdapter Adapter;
    // public readonly WGPUSurface Surface;
    public readonly WGPUDevice Device;
    public readonly WGPUQueue Queue;

    private readonly DeviceDescriptor _descriptor;

    // supported details
    private readonly PixelFormat _preferredSurfaceFormat;
    private readonly PixelFormat _preferredSDRFormat;
    private readonly PixelFormat _preferredHDRFormat;
    private readonly PixelFormat? _preferredDepthStencilFormat;
     


    #endregion

    #region Abstract Implementation


    public override PixelFormat PrefferedSurfaceFomat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredSurfaceFormat;
    }

    public override PixelFormat PrefferedSDRFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredSDRFormat;
    }

    public override PixelFormat PrefferedHDRFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredHDRFormat;
    }


    public override PixelFormat? PrefferedDepthStencilFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _preferredDepthStencilFormat;
        }
    }

    //default samplers

    public override GPUSampler SamplerNearestRepeat { get; }
    public override GPUSampler SamplerLinearRepeat { get; }
    public override GPUSampler SamplerNearestClamp { get; }
    public override GPUSampler SamplerLinearClamp { get; }
    public override GPUSampler SamplerNearestMirrorRepeat { get; }
    public override GPUSampler SamplerLinearMirrorRepeat { get; }

    //default bind groups
    public override GPUBindGroup BindGroupUniformBuffer { get; }
    public override GPUBindGroup BindGroupStorageBuffer { get; }
    public override GPUBindGroup BindGroupTexture2DSampled { get; }
    public override GPUBindGroup BindGroupTexture2DRead { get; }
    public override GPUBindGroup BindGroupTexture2DStorage { get; }

    protected unsafe override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        WGPUCommandBuffer buffer = ((WebGPUCommandBuffer)commandBuffer).Native;
        wgpuQueueSubmit(Queue, 1, &buffer);
    }

    protected override void SubmitCore(GPUResuableRenderBuffer renderBuffer)
    {
        WebGPUResuableRenderBuffer buffer = (WebGPUResuableRenderBuffer)renderBuffer;
        buffer.ExecuteBundle(Queue);
    }

    public override void Dispose()
    {
        // _surfaceFrameBuffer.Destroy();
        // _surfaceRenderPass.Destroy();

        //dispose default resources
        SamplerNearestRepeat.Destroy();
        SamplerLinearRepeat.Destroy();
        SamplerNearestClamp.Destroy();
        SamplerLinearClamp.Destroy();
        SamplerNearestMirrorRepeat.Destroy();
        SamplerLinearMirrorRepeat.Destroy();

        BindGroupUniformBuffer.Destroy();
        BindGroupStorageBuffer.Destroy();
        BindGroupTexture2DSampled.Destroy();
        BindGroupTexture2DRead.Destroy();
        BindGroupTexture2DStorage.Destroy();

        wgpuInstanceRelease(Instance);
        wgpuDeviceDestroy(Device);
        wgpuDeviceRelease(Device);
        //wgpuSurfaceRelease(Surface);
        wgpuAdapterRelease(Adapter);
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        return new WebGPUBuffer(this, descriptor);
    }

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
    {
        return new WebGPUCommandBuffer(this, descriptor);
    }

    protected override GPUResuableRenderBuffer CreateResuableRenderBufferCore(in ResuableRenderBufferDescriptor? descriptor)
    {
        return new WebGPUResuableRenderBuffer(this, descriptor);
    }

    protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor)
    {
        return new WebGPUTexture(this, descriptor);
    }

    protected override GPURenderPass CreateRenderPassCore(in RenderPassDescriptor descriptor)
    {
        return new WebGPURenderPass(this, descriptor);
    }

    protected override GPUFrameBuffer CreateFrameBufferCore(in FrameBufferDescriptor descriptor)
    {
        return new WebGPUFrameBuffer(this, descriptor);
    }

    protected override GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor)
    {
        return new WebGPUGraphicsPipeline(this, descriptor);
    }

    protected override GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor)
    {
        return new WebGPUComputePipeline(this, descriptor);
    }

    protected override GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor)
    {
        return new WebGPUBindGroup(this, descriptor);
    }

    protected override GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor)
    {
        return new WebGPUResourceGroup(this, descriptor);
    }

    protected override GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor)
    {
        return new WebGPUTextureView(this, descriptor);
    }

    
    protected unsafe override GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor)
    {
        return new WebGPUSampler(this, descriptor, false);
    }

    public override GPUSwapchain CreateSwapchainCore(in SwapchainDescriptor descriptor)
    {
        return new WebGPUSwapchain(this, descriptor);
    }

    protected override unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        WGPUBuffer nativeBuffer = ((WebGPUBuffer)buffer).Native;
        wgpuQueueWriteBuffer(Queue, nativeBuffer, bufferOffset, data, size);
    }

    protected override unsafe void ReadBufferCore(GPUBuffer buffer, byte* dest, uint bufferOffset, uint size)
    {
        // Stopwatch stopwatch = new Stopwatch();
        // stopwatch.Start();

        WGPUBuffer nativeBuffer = ((WebGPUBuffer)buffer).Native;
        WGPUBufferDescriptor tmpBufferDescriptor = new WGPUBufferDescriptor
        {
            size = size,
            usage = WGPUBufferUsage.MapRead | WGPUBufferUsage.CopyDst,
            mappedAtCreation = false,
        };
        WGPUBuffer tmpBuffer = wgpuDeviceCreateBuffer(Device, &tmpBufferDescriptor);

        // stopwatch.Stop();
        // GraphicsLogger.Info("create buffer: " + stopwatch.ElapsedTicks);

        // stopwatch.Restart();

        WGPUCommandEncoder encoder = wgpuDeviceCreateCommandEncoder(Device, null);
        wgpuCommandEncoderCopyBufferToBuffer(encoder, nativeBuffer, bufferOffset, tmpBuffer, 0, size);

        WGPUCommandBuffer commandBuffer = wgpuCommandEncoderFinish(encoder, null);
        
        wgpuQueueSubmit(Queue, 1, &commandBuffer);

        // stopwatch.Stop();
        // GraphicsLogger.Info("gpu copy: " + stopwatch.ElapsedTicks);

        // stopwatch.Restart();
        
        wgpuBufferMapAsync(tmpBuffer, WGPUMapMode.Read, 0, size, &BufferMapCallback, null);
        wgpuDevicePoll(Device, WGPUBool.True, null);

        // stopwatch.Stop();
        // GraphicsLogger.Info("map: " + stopwatch.ElapsedTicks);

        // stopwatch.Restart();

        void* pointer = wgpuBufferGetConstMappedRange(tmpBuffer, 0, size);

        Unsafe.CopyBlock(dest, pointer, size);

        // stopwatch.Stop();
        // GraphicsLogger.Info("cpoy: " + stopwatch.ElapsedTicks);

        // stopwatch.Restart();

        wgpuBufferUnmap(tmpBuffer);

        // stopwatch.Stop();
        // GraphicsLogger.Info("unmap: " + stopwatch.ElapsedTicks);

        // stopwatch.Restart();

        wgpuCommandEncoderRelease(encoder);
        wgpuCommandBufferRelease(commandBuffer);
        wgpuBufferDestroy(tmpBuffer);
        wgpuBufferRelease(tmpBuffer);

        // stopwatch.Stop();
        // GraphicsLogger.Info("release: " + stopwatch.ElapsedTicks);
    }

    protected override unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint mipLevel)
    {
        //todo: get pixel size by format
        WGPUTexture nativeTexture = ((WebGPUTexture)texture).Native;

        WGPUImageCopyTexture copyTextureInfo = new WGPUImageCopyTexture
        {
            texture = nativeTexture,
            mipLevel = mipLevel,
            origin = new WGPUOrigin3D
            {
                x = 0,
                y = 0,
                z = 0,
            },
            aspect = WGPUTextureAspect.All,
        };

        WGPUTextureDataLayout textureDataLayout = new WGPUTextureDataLayout
        {
            offset = 0,
            bytesPerRow = UtilsWebGPU.GetTextureBytesPerRow(texture.PixelFormat, texture.Width, texture.Height),
            rowsPerImage = texture.Height,
        };

        WGPUExtent3D writeSize = new WGPUExtent3D
        {
            width = texture.Width,
            height = texture.Height,
            depthOrArrayLayers = texture.Depth,
        };

        wgpuQueueWriteTexture(Queue, &copyTextureInfo, data, dataSize, &textureDataLayout, &writeSize);
    }

    protected override unsafe void ReadTextureCore(GPUTexture texture, byte* dest, uint dataSize, uint mipLevel = 0)
    {
        WGPUTexture nativeTexture = ((WebGPUTexture)texture).Native;

        WGPUBufferDescriptor tmpBufferDescriptor = new WGPUBufferDescriptor
        {
            size = dataSize,
            usage = WGPUBufferUsage.MapRead | WGPUBufferUsage.CopyDst,
            mappedAtCreation = false,
        };

        WGPUBuffer tmpBuffer = wgpuDeviceCreateBuffer(Device, &tmpBufferDescriptor);

        WGPUImageCopyTexture source = new WGPUImageCopyTexture
        {
            texture = nativeTexture,
            mipLevel = mipLevel,
            origin = new WGPUOrigin3D
            {
                x = 0,
                y = 0,
                z = 0,
            },
            aspect = WGPUTextureAspect.All,
        };

        WGPUImageCopyBuffer destBuffer = new WGPUImageCopyBuffer
        {
            buffer = tmpBuffer,
            layout = new WGPUTextureDataLayout
            {
                offset = 0,
                bytesPerRow = UtilsWebGPU.GetTextureBytesPerRow(texture.PixelFormat, texture.Width, texture.Height),
                rowsPerImage = texture.Height,
            },
        };

        WGPUExtent3D copySize = new WGPUExtent3D
        {
            width = texture.Width,
            height = texture.Height,
            depthOrArrayLayers = texture.Depth,
        };

        WGPUCommandEncoder encoder = wgpuDeviceCreateCommandEncoder(Device, null);
        wgpuCommandEncoderCopyTextureToBuffer(encoder, &source, &destBuffer, &copySize);

        WGPUCommandBuffer commandBuffer = wgpuCommandEncoderFinish(encoder, null);
        wgpuQueueSubmit(Queue, 1, &commandBuffer);

        wgpuBufferMapAsync(tmpBuffer, WGPUMapMode.Read, 0, dataSize, &BufferMapCallback, null);
        wgpuDevicePoll(Device, WGPUBool.True, null);

        void* pointer = wgpuBufferGetConstMappedRange(tmpBuffer, 0, dataSize);
        Unsafe.CopyBlock(dest, pointer, dataSize);
        wgpuBufferUnmap(tmpBuffer);

        wgpuCommandEncoderRelease(encoder);
        wgpuCommandBufferRelease(commandBuffer);
        wgpuBufferDestroy(tmpBuffer);
        wgpuBufferRelease(tmpBuffer);
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
        wgpuSetLogCallback(LogCallback);

        _preferredSurfaceFormat = descriptor.PreferredSurfaceFormat;
        _preferredSDRFormat = descriptor.PreferredSDRFormat;
        _preferredHDRFormat = descriptor.PreferredHDRFormat;
        _preferredDepthStencilFormat = descriptor.DepthFormat;

        // create instance
        WGPUInstanceExtras extras = new WGPUInstanceExtras()
        {
            chain = new WGPUChainedStruct
            {
                sType = (WGPUSType)WGPUNativeSType.InstanceExtras,
                next = null,
            },
            flags = descriptor.Debug ? WGPUInstanceFlags.Debug : WGPUInstanceFlags.None,
            backends = UtilsWebGPU.BackendToWebGPU(descriptor.Backend),
        };

        WGPUInstanceDescriptor instanceDescriptor = new WGPUInstanceDescriptor()
        {
            nextInChain = (WGPUChainedStruct*)&extras,
        };

        Instance = wgpuCreateInstance(&instanceDescriptor);

        // create surface
        //Surface = Instance.CreateSurface(descriptor.SurfaceSource);

        // create adapter
        WGPURequestAdapterOptions requestAdapterOptions = new WGPURequestAdapterOptions()
        {
            nextInChain = null,
            //compatibleSurface = Surface,
            powerPreference = WGPUPowerPreference.HighPerformance,
            //backendType = UtilsWebGPU.BackendTypeToWebGPU(descriptor.Backend),
            //forceFallbackAdapter = WGPUBool.True,
        };



        WGPUAdapter adapter = WGPUAdapter.Null;
        wgpuInstanceRequestAdapter(Instance, &requestAdapterOptions, &OnAdapterRequestEnded, &adapter);
        Adapter = adapter;

        WGPUAdapterInfo info = default;
        wgpuAdapterGetInfo(adapter, &info);
        GraphicsLogger.Info($"Graphics backend: {info.backendType}");

        wgpuAdapterInfoFreeMembers(info);

        nuint supportedFeaturesCount = wgpuAdapterEnumerateFeatures(Adapter, null);
        WGPUFeatureName* supportedFeatures = stackalloc WGPUFeatureName[(int)supportedFeaturesCount];
        wgpuAdapterEnumerateFeatures(Adapter, supportedFeatures);

        List<WGPUFeatureName> featuresList = new List<WGPUFeatureName>(){
            (WGPUFeatureName)WGPUNativeFeature.VertexWritableStorage
        };

        if (!IsFeatureSupported((WGPUFeatureName)WGPUNativeFeature.PushConstants, supportedFeatures, supportedFeaturesCount))
        {
            throw new GraphicsException("Push constants are not supported which is required");
        }

        featuresList.Add((WGPUFeatureName)WGPUNativeFeature.PushConstants);

        WGPUFeatureName* features = stackalloc WGPUFeatureName[featuresList.Count];
        for (int i = 0; i < featuresList.Count; i++)
        {
            features[i] = featuresList[i];
        }

        // create device
        fixed (byte* name = descriptor.Name.GetUtf8Span())
        {
            WGPUDeviceDescriptor deviceDescriptor = new()
            {
                nextInChain = null,
                label = name,
                requiredLimits = null,
                requiredFeatureCount = (uint)featuresList.Count,
                requiredFeatures = features,
            };

            WGPUDevice device = WGPUDevice.Null;


            WGPUNativeLimits limits = new WGPUNativeLimits
            {
                maxPushConstantSize = 128,
            };

            WGPURequiredLimitsExtras requiredLimitsExtras = new WGPURequiredLimitsExtras
            {
                chain = new WGPUChainedStruct
                {
                    sType = (WGPUSType)WGPUNativeSType.RequiredLimitsExtras,
                    next = null,
                },
                limits = limits,
            };

            WGPURequiredLimits requiredLimits = new WGPURequiredLimits
            {
                nextInChain = &requiredLimitsExtras.chain,
                limits = DefaultLimits
            };

            deviceDescriptor.requiredLimits = &requiredLimits;


            wgpuAdapterRequestDevice(Adapter, &deviceDescriptor, &OnDeviceRequestEnded, &device);
            Device = device;
        }

        //wgpuDeviceSetUncapturedErrorCallback(Device, &OnUnhandleError);

        //get queue
        Queue = wgpuDeviceGetQueue(Device);
        
        // create default samplers

        SamplerNearestRepeat = CreateSampler(SamplerDescriptor.Default with
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.Repeat,
            MinFilter = FilterMode.Nearest,
            MagFilter = FilterMode.Nearest,
            MipFilter = FilterMode.Nearest,
            Name = "nearest_repeat_sampler",
        });

        SamplerLinearRepeat = CreateSampler(SamplerDescriptor.Default with
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.Repeat,
            MinFilter = FilterMode.Linear,
            MagFilter = FilterMode.Linear,
            MipFilter = FilterMode.Linear,
            Name = "linear_repeat_sampler",
        });

        SamplerNearestClamp = CreateSampler(SamplerDescriptor.Default with
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MinFilter = FilterMode.Nearest,
            MagFilter = FilterMode.Nearest,
            MipFilter = FilterMode.Nearest,
            Name = "nearest_clamp_sampler",
        });

        SamplerLinearClamp = CreateSampler(SamplerDescriptor.Default with
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MinFilter = FilterMode.Linear,
            MagFilter = FilterMode.Linear,
            MipFilter = FilterMode.Linear,
            Name = "linear_clamp_sampler",
        });

        SamplerNearestMirrorRepeat = CreateSampler(SamplerDescriptor.Default with
        {
            AddressModeU = AddressMode.MirrorRepeat,
            AddressModeV = AddressMode.MirrorRepeat,
            AddressModeW = AddressMode.MirrorRepeat,
            MinFilter = FilterMode.Nearest,
            MagFilter = FilterMode.Nearest,
            MipFilter = FilterMode.Nearest,
            Name = "nearest_mirror_repeat_sampler",
        });

        SamplerLinearMirrorRepeat = CreateSampler(SamplerDescriptor.Default with
        {
            AddressModeU = AddressMode.MirrorRepeat,
            AddressModeV = AddressMode.MirrorRepeat,
            AddressModeW = AddressMode.MirrorRepeat,
            MinFilter = FilterMode.Linear,
            MagFilter = FilterMode.Linear,
            MipFilter = FilterMode.Linear,
            Name = "linear_mirror_repeat_sampler",
        });

        //create default bind groups
        BindGroupUniformBuffer = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_buffer",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.UniformBuffer),
            },
        });

        BindGroupStorageBuffer = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_buffer",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageBuffer),
            },
        });

        BindGroupTexture2DSampled = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_texture",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D)),
                new BindGroupEntry(1, ShaderStage.Standard, BindingType.Sampler),
            },
        });

        BindGroupTexture2DRead = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_texture_read",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Compute, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D)),
            },
        });

        BindGroupTexture2DStorage = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_texture",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Compute, BindingType.StorageTexture, null, new StorageTextureBindingInfo(AccessMode.Write, TextureViewDimension.Texture2D,PixelFormat.RGBA8Unorm)),
            },
        });

    }


    #endregion

    #region Callbacks

    [UnmanagedCallersOnly]
    private unsafe static void OnAdapterRequestEnded(WGPURequestAdapterStatus status, WGPUAdapter candidateAdapter, byte* message, void* pUserData)
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
    private unsafe static void OnDeviceRequestEnded(WGPURequestDeviceStatus status, WGPUDevice device, byte* message, void* pUserData)
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

    // [UnmanagedCallersOnly]
    // private unsafe static void OnUnhandleError(WGPUErrorType type, sbyte* message, nint pUserData)
    // {
    //     throw new GraphicsException("Unhandle WebGPU error: " + Interop.GetString(message));
    // }

    [UnmanagedCallersOnly]
    private unsafe static void BufferMapCallback(WGPUBufferMapAsyncStatus status, void* userdata)
    {
        if (status != WGPUBufferMapAsyncStatus.Success)
        {
            GraphicsLogger.Error("Buffer map failed, status: " + status);
        }

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

    private unsafe static bool IsFeatureSupported(WGPUFeatureName feature, WGPUFeatureName* supportedFeatures, nuint supportedFeaturesCount)
    {
        for (nuint i = 0; i < supportedFeaturesCount; i++)
        {
            if (supportedFeatures[i] == feature)
            {
                return true;
            }
        }
        return false;
    }

    #endregion
}