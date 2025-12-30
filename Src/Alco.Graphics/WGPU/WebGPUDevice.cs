using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed partial class WebGPUDevice : GPUDevice
{

    #region Properties

    public readonly WGPUInstance Instance;
    public readonly WGPUAdapter Adapter;
    // public readonly WGPUSurface Surface;
    public readonly WGPUDevice Device;
    public readonly WGPUQueue Queue;

    private readonly DeviceDescriptor _descriptor;

    // supported details
    private readonly PixelFormat _preferredSurfaceFormat;
    private GCHandle _thisHandle;

    public bool IsDebug { get; }

    #endregion

    #region Abstract Implementation


    public override PixelFormat PrefferedSurfaceFomat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredSurfaceFormat;
    }


    //default bind groups
    public override GPUBindGroup BindGroupUniformBuffer { get; }
    public override GPUBindGroup BindGroupStorageBuffer { get; }
    public override GPUBindGroup BindGroupStorageBufferWithCounter { get; }
    public override GPUBindGroup BindGroupTexture2DSampled { get; }
    public override GPUBindGroup BindGroupTextureDepthRead { get; }
    public override GPUBindGroup BindGroupTexture2DRead { get; }
    public override GPUBindGroup BindGroupTexture2DStorage { get; }

    public override bool TextureCompressBC3Supported { get; }

    protected unsafe override void SubmitCore(GPUCommandBuffer commandBuffer)

    {
        WGPUCommandBuffer buffer = ((WebGPUCommandBuffer)commandBuffer).TakeBuffer();
        wgpuQueueSubmit(Queue, 1, &buffer);//add reference count
        wgpuCommandBufferRelease(buffer);//just decrement the reference count
    }

    protected override void DisposeCore()
    {
        //dispose default resources
        SamplerNearestRepeat.Destroy();
        SamplerLinearRepeat.Destroy();
        SamplerNearestClamp.Destroy();
        SamplerLinearClamp.Destroy();
        SamplerNearestMirrorRepeat.Destroy();
        SamplerLinearMirrorRepeat.Destroy();

        BindGroupUniformBuffer.Destroy();
        BindGroupStorageBuffer.Destroy();
        BindGroupStorageBufferWithCounter.Destroy();
        BindGroupTexture2DSampled.Destroy();
        BindGroupTextureDepthRead.Destroy();
        BindGroupTexture2DRead.Destroy();
        BindGroupTexture2DStorage.Destroy();

        DebugPrintReport();

        wgpuInstanceRelease(Instance);
        wgpuDeviceDestroy(Device);
        wgpuDeviceRelease(Device);
        wgpuAdapterRelease(Adapter);

        _thisHandle.Free();
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        return new WebGPUBuffer(this, descriptor);
    }

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
    {
        return new WebGPUCommandBuffer(this, descriptor);
    }

    protected override GPURenderBundle CreateRenderBundleCore(in RenderBundleDescriptor? descriptor)
    {
        return new WebGPURenderBundle(this, descriptor);
    }

    protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor)
    {
        return new WebGPUTexture(this, descriptor);
    }

    protected override GPUAttachmentLayout CreateAttachmentLayoutCore(in AttachmentLayoutDescriptor descriptor)
    {
        return new WebGPUAttachmentLayout(this, descriptor);
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
        return new WebGPUSampler(this, descriptor);
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
        WGPUBuffer nativeBuffer = ((WebGPUBuffer)buffer).Native;
        WGPUBufferDescriptor tmpBufferDescriptor = new WGPUBufferDescriptor
        {
            size = size,
            usage = WGPUBufferUsage.MapRead | WGPUBufferUsage.CopyDst,
            mappedAtCreation = false,
        };
        WGPUBuffer tmpBuffer = wgpuDeviceCreateBuffer(Device, &tmpBufferDescriptor);

        WGPUCommandEncoder encoder = wgpuDeviceCreateCommandEncoder(Device, null);
        wgpuCommandEncoderCopyBufferToBuffer(encoder, nativeBuffer, bufferOffset, tmpBuffer, 0, size);

        WGPUCommandBuffer commandBuffer = wgpuCommandEncoderFinish(encoder, null);
        
        wgpuQueueSubmit(Queue, 1, &commandBuffer);

        wgpuBufferMapAsync(tmpBuffer, WGPUMapMode.Read, 0, size,
            new WGPUBufferMapCallbackInfo()
            {
                mode = WGPUCallbackMode.None,
                callback = &BufferMapCallback,
                userdata1 = null,
                userdata2 = null,
            });
        wgpuDevicePoll(Device, WGPUBool.True, null);

        void* pointer = wgpuBufferGetConstMappedRange(tmpBuffer, 0, size);

        Unsafe.CopyBlock(dest, pointer, size);

        wgpuBufferUnmap(tmpBuffer);

        wgpuCommandEncoderRelease(encoder);
        wgpuCommandBufferRelease(commandBuffer);
        wgpuBufferDestroy(tmpBuffer);
        wgpuBufferRelease(tmpBuffer);
    }

    protected override unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint mipLevel)
    {
        //todo: get pixel size by format
        WGPUTexture nativeTexture = ((WebGPUTexture)texture).Native;

        WGPUTexelCopyTextureInfo copyTextureInfo = new WGPUTexelCopyTextureInfo
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

        WGPUTexelCopyBufferLayout textureDataLayout = WebGPUUtility.GetTextureDataLayout(texture.PixelFormat, texture.Width, texture.Height);

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

        WGPUTexelCopyTextureInfo source = new WGPUTexelCopyTextureInfo
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

        WGPUTexelCopyBufferInfo destBuffer = new WGPUTexelCopyBufferInfo
        {
            buffer = tmpBuffer,
            layout = WebGPUUtility.GetTextureDataLayout(texture.PixelFormat, texture.Width, texture.Height),
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

        wgpuBufferMapAsync(tmpBuffer, WGPUMapMode.Read, 0, dataSize,
            new WGPUBufferMapCallbackInfo()
            {
                mode = WGPUCallbackMode.None,
                callback = &BufferMapCallback,
                userdata1 = null,
                userdata2 = null,
            });
        wgpuDevicePoll(Device, WGPUBool.True, null);

        void* pointer = wgpuBufferGetConstMappedRange(tmpBuffer, 0, dataSize);
        Unsafe.CopyBlock(dest, pointer, dataSize);
        wgpuBufferUnmap(tmpBuffer);

        wgpuCommandEncoderRelease(encoder);
        wgpuCommandBufferRelease(commandBuffer);
        wgpuBufferDestroy(tmpBuffer);
        wgpuBufferRelease(tmpBuffer);
    }

    protected unsafe override void OnEndFrameCore()
    {

    }

    #endregion

    #region WebGPU Implementation

    public WGPUDevice Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Device;
    }

    public unsafe WebGPUDevice(in DeviceDescriptor descriptor) : base(descriptor)
    {
        _thisHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        IsDebug = descriptor.Debug;

        _descriptor = descriptor;
        wgpuSetLogCallback(LogCallback, GCHandle.ToIntPtr(_thisHandle));

        _preferredSurfaceFormat = descriptor.PreferredSurfaceFormat;

        // create instance
        WGPUInstanceExtras extras = new WGPUInstanceExtras()
        {
            chain = new WGPUChainedStruct
            {
                sType = (WGPUSType)WGPUNativeSType.InstanceExtras,
                next = null,
            },
            flags = descriptor.Debug ? WGPUInstanceFlag.Validation : WGPUInstanceFlag.Default,
            backends = WebGPUUtility.BackendToWebGPU(descriptor.Backend),
        };

        WGPUInstanceDescriptor instanceDescriptor = new WGPUInstanceDescriptor()
        {
            nextInChain = (WGPUChainedStruct*)&extras,
        };

        Instance = wgpuCreateInstance(&instanceDescriptor);

        // create adapter
        WGPURequestAdapterOptions requestAdapterOptions = new WGPURequestAdapterOptions()
        {
            nextInChain = null,
            //compatibleSurface = Surface,
            powerPreference = WGPUPowerPreference.HighPerformance,
            backendType = WebGPUUtility.BackendTypeToWebGPU(descriptor.Backend),
        };

        WGPUAdapter adapter = WGPUAdapter.Null;
        wgpuInstanceRequestAdapter(
            Instance,
            &requestAdapterOptions,
            new WGPURequestAdapterCallbackInfo()
            {
                callback = &OnAdapterRequestEnded,
                userdata1 = &adapter,
                userdata2 = null,
            });
        Adapter = adapter;

        WGPUAdapterInfo info = default;
        wgpuAdapterGetInfo(adapter, &info);
        _host.LogSuccess($"Adapter name: {info.device}");
        _host.LogSuccess($"Graphics backend: {info.backendType}");

        wgpuAdapterInfoFreeMembers(info);

        ReadOnlySpan<WGPUFeatureName> supportedFeatures = wgpuAdapterEnumerateFeatures(Adapter);

        List<WGPUFeatureName> featuresList = new List<WGPUFeatureName>(){
            (WGPUFeatureName)WGPUNativeFeature.VertexWritableStorage
        };

        if (!IsFeatureSupported((WGPUFeatureName)WGPUNativeFeature.PushConstants, supportedFeatures))
        {
            throw new GraphicsException("Push constants are not supported which is required");
        }

        if(IsFeatureSupported(WGPUFeatureName.TextureCompressionBC, supportedFeatures))
        {
            TextureCompressBC3Supported = true;
            featuresList.Add(WGPUFeatureName.TextureCompressionBC);
            _host.LogSuccess("Texture compression BC is supported");
        }


        featuresList.Add((WGPUFeatureName)WGPUNativeFeature.PushConstants);
        featuresList.Add((WGPUFeatureName)WGPUNativeFeature.TextureAdapterSpecificFormatFeatures);

        WGPUFeatureName* features = stackalloc WGPUFeatureName[featuresList.Count];
        for (int i = 0; i < featuresList.Count; i++)
        {
            features[i] = featuresList[i];
        }

        // create device
        ReadOnlySpan<byte> nameSpan = descriptor.Name.GetUtf8Span();
        fixed (byte* name = nameSpan)
        {


            WGPULimits limits = default;
            
            WGPUStatus status = wgpuAdapterGetLimits(Adapter, &limits);
            if(status != WGPUStatus.Success)
            {
                throw new GraphicsException("Could not get WebGPU adapter limits");
            }
        
            WGPUNativeLimits nativeLimits = new WGPUNativeLimits()
            {
                chain = new WGPUChainedStructOut()
                {
                    sType = (WGPUSType)WGPUNativeSType.NativeLimits,
                },
                maxPushConstantSize = descriptor.PushConstantsSize
            };

            limits.nextInChain = (WGPUChainedStructOut*)&nativeLimits;

            WGPUDeviceDescriptor deviceDescriptor = new WGPUDeviceDescriptor()
            {
                nextInChain = null,
                label = new WGPUStringView(name, nameSpan.Length),
                requiredLimits = &limits,
                requiredFeatureCount = (uint)featuresList.Count,
                requiredFeatures = features,
                uncapturedErrorCallbackInfo = new WGPUUncapturedErrorCallbackInfo
                {
                    nextInChain = null,
                    callback = &ErrorCallback,
                    userdata1 = null,
                    userdata2 = null,
                },
            };

            deviceDescriptor.defaultQueue.nextInChain = null;

            WGPUDevice device = WGPUDevice.Null;
            wgpuAdapterRequestDevice(
                Adapter,
                &deviceDescriptor,
                new WGPURequestDeviceCallbackInfo()
                {
                    callback = &OnDeviceRequestEnded,
                    userdata1 = &device,
                    userdata2 = null,
                });
            Device = device;
        }

        //wgpuDeviceSetUncapturedErrorCallback(Device, &OnUnhandleError);

        //get queue
        Queue = wgpuDeviceGetQueue(Device);
        
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

        BindGroupStorageBufferWithCounter = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_buffer_with_counter",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageBuffer),
                new BindGroupEntry(1, ShaderStage.Standard, BindingType.StorageBuffer),
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

        BindGroupTextureDepthRead = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_texture_depth_sampled",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D, TextureSampleType.UnfilterableFloat)),
            },
        });

        BindGroupTexture2DRead = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_texture_read",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D)),
            },
        });

        BindGroupTexture2DStorage = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_texture",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageTexture, null, new StorageTextureBindingInfo(AccessMode.ReadWrite, TextureViewDimension.Texture2D,PixelFormat.RGBA8Unorm)),
            },
        });
    }


    #endregion

    #region Callbacks

    [UnmanagedCallersOnly]
    private unsafe static void OnAdapterRequestEnded(WGPURequestAdapterStatus status, WGPUAdapter candidateAdapter, WGPUStringView message, void* pUserData1, void* pUserData2)
    {
        if (status == WGPURequestAdapterStatus.Success)
        {
            *(WGPUAdapter*)pUserData1 = candidateAdapter;
        }
        else
        {
            throw new GraphicsException("Could not get WebGPU adapter: " + message.ToString());
        }
    }

    [UnmanagedCallersOnly]
    private unsafe static void OnDeviceRequestEnded(WGPURequestDeviceStatus status, WGPUDevice device, WGPUStringView message, void* pUserData1, void* pUserData2)
    {
        if (status == WGPURequestDeviceStatus.Success)
        {
            *(WGPUDevice*)pUserData1 = device;
        }
        else
        {
            throw new GraphicsException("Could not get WebGPU device: " + message.ToString());
        }
    }

    [UnmanagedCallersOnly]
    private unsafe static void OnUnhandleError(WGPUErrorType type, byte* message, void* pUserData)
    {
        throw new GraphicsException("Unhandle WebGPU error: " + Interop.GetString(message));
    }

    [UnmanagedCallersOnly]
    private unsafe static void BufferMapCallback(WGPUMapAsyncStatus status, WGPUStringView message, void* userdata1, void* userdata2)
    {
        if (status != WGPUMapAsyncStatus.Success)
        {
            throw new GraphicsException("WebGPU buffer map failed: " + message.ToString());
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe void ErrorCallback(WGPUDevice* device, WGPUErrorType type, WGPUStringView message, void* userdata1, void* userdata2)
    {
        throw new GraphicsException("WebGPU error: " + message.ToString());
    }

    private static void LogCallback(WGPULogLevel level, string message, nint userdata = 0)
    {
        WebGPUDevice device = (WebGPUDevice)GCHandle.FromIntPtr(userdata).Target!;
        switch (level)
        {
            case WGPULogLevel.Error:
                throw new GraphicsException(message);
            case WGPULogLevel.Warn:
                if (device.IsDebug)
                {
                    device._host.LogWarning(message);
                }
                break;
            case WGPULogLevel.Info:
            case WGPULogLevel.Debug:
            case WGPULogLevel.Trace:
                device._host.LogInfo(message);
                break;
        }
    }

    private unsafe static bool IsFeatureSupported(WGPUFeatureName feature, ReadOnlySpan<WGPUFeatureName> supportedFeatures)
    {
        for (int i = 0; i < supportedFeatures.Length; i++)
        {
            if (supportedFeatures[i] == feature)
            {
                return true;
            }
        }
        return false;
    }


    //for wgpu object usage only
    internal void LogInfo(ReadOnlySpan<char> message){
        _host.LogInfo(message);
    }

    internal void LogWarning(ReadOnlySpan<char> message){
        _host.LogWarning(message);
    }
    
    [Conditional("DEBUG")]
    private unsafe void DebugPrintReport()
    {
        // WGPUGlobalReport report;
        // wgpuGenerateReport(Instance, &report);

        // switch(report.backendType)
        // {
        //     case WGPUBackendType.Vulkan:
        //         PrintHubReport(report.vulkan);
        //         break;
        //     case WGPUBackendType.Metal:
        //         PrintHubReport(report.metal);
        //         break;
        //     case WGPUBackendType.D3D12:
        //         PrintHubReport(report.dx12);
        //         break;
        //     case WGPUBackendType.OpenGLES:
        //     case WGPUBackendType.OpenGL:
        //         PrintHubReport(report.gl);
        //         break;
        //     case WGPUBackendType.WebGPU:
        //     default:
        //         break;
        // }
    }

    private void PrintHubReport(WGPUHubReport report)
    {
        _host.LogInfo("Hub report:");
        PrintRegistryReport(report.adapters, "adapters");
        PrintRegistryReport(report.devices, "devices");
        PrintRegistryReport(report.queues, "queues");
        PrintRegistryReport(report.pipelineLayouts, "pipelineLayouts");
        PrintRegistryReport(report.shaderModules, "shaderModules");
        PrintRegistryReport(report.bindGroupLayouts, "bindGroupLayouts");
        PrintRegistryReport(report.bindGroups, "bindGroups");
        PrintRegistryReport(report.commandBuffers, "commandBuffers");
        PrintRegistryReport(report.renderBundles, "renderBundles");
        PrintRegistryReport(report.renderPipelines, "renderPipelines");
        PrintRegistryReport(report.computePipelines, "computePipelines");
        PrintRegistryReport(report.querySets, "querySets");
        PrintRegistryReport(report.buffers, "buffers");
        PrintRegistryReport(report.textures, "textures");
        PrintRegistryReport(report.textureViews, "textureViews");
        PrintRegistryReport(report.samplers, "samplers");
    }

    private void PrintRegistryReport(WGPURegistryReport report, string name)
    {
        _host.LogInfo($"Registry report for {name}:");
        _host.LogInfo($"  Element size: {report.elementSize}");
        _host.LogInfo($"  Allocated: {report.numAllocated}");
        _host.LogInfo($"  Kept from user: {report.numKeptFromUser}");
        _host.LogInfo($"  Released from user: {report.numReleasedFromUser}");
    }

    #endregion
}