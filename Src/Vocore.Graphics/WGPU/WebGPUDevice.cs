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
    // private readonly WGPUTextureFormat[] _supportedSurfaceFormats;
    // private readonly WGPUPresentMode[] _supportedPresentModes;

    // private readonly WGPUTextureFormat _swapChainFormat;
    //private readonly PixelFormat _preferredSurfaceFormat;
    private readonly PixelFormat _preferredSDRFormat;
    private readonly PixelFormat _preferredHDRFormat;
    private readonly PixelFormat? _preferredDepthStencilFormat;
     
    // private uint _width;
    // private uint _height;
    // private bool _vsync;

    // private readonly WebGPUSurfaceFrameBuffer _surfaceFrameBuffer;
    // private readonly WebGPURenderPass _surfaceRenderPass;

    private bool _hasCommandSubmitted;

    #endregion

    #region Abstract Implementation


    // public override GPURenderPass SwapChainRenderPass
    // {
    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     get => _surfaceRenderPass;
    // }

    // public override GPUFrameBuffer SwapChainFrameBuffer
    // {
    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     get => _surfaceFrameBuffer;
    // }

    // public override PixelFormat? PrefferedDepthStencilFormat
    // {
    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     get
    //     {
    //         return _preferredDepthStencilFormat;
    //     }
    // }

    // public override PixelFormat PrefferedSurfaceFomat
    // {
    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     get => _preferredSurfaceFormat;
    // }

    // public override PixelFormat PrefferedHDRFormat
    // {
    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     get => _preferredHDRFormat;
    // }

    // public override bool VSync
    // {
    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     get => _vsync;
    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     set => _vsync = value;
    // }

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
        _hasCommandSubmitted = true;
    }

    protected override void SubmitCore(GPUResuableRenderBuffer renderBuffer)
    {
        WebGPUResuableRenderBuffer buffer = (WebGPUResuableRenderBuffer)renderBuffer;
        buffer.ExecuteBundle(Queue);
        _hasCommandSubmitted = true;
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

    protected override unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint pixelSzie, uint mipLevel)
    {
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
            bytesPerRow = pixelSzie * texture.Width,
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

    protected unsafe override void ResizeSurfaceCore(uint width, uint height)
    {
        // _width = width;
        // _height = height;
        // _surfaceFrameBuffer.UpdateSurfaceConfig(GetSurfaceConfig());
    }

    protected unsafe override void SwapBuffersCore()
    {
        if (!_hasCommandSubmitted)
        {
            return;
        }
        //_surfaceFrameBuffer.SwapBuffers();
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
        wgpuSetLogCallback(LogCallback);

        // create instance
        WGPUInstanceExtras extras = new WGPUInstanceExtras()
        {
            chain = new WGPUChainedStruct
            {
                sType = (WGPUSType)WGPUNativeSType.InstanceExtras,
                next = null,
            },
            flags = descriptor.Debug ? WGPUInstanceFlags.Validation : WGPUInstanceFlags.None,
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
        wgpuInstanceRequestAdapter(Instance, &requestAdapterOptions, &OnAdapterRequestEnded, new nint(&adapter));
        Adapter = adapter;

        // WGPUAdapterProperties properties = default;
        // wgpuAdapterGetProperties(Adapter, &properties);
        // GraphicsLogger.Info($"Graphics backend: {properties.backendType}");

        // WGPUSurfaceCapabilities surfaceCapabilities = default;
        // wgpuSurfaceGetCapabilities(Surface, Adapter, &surfaceCapabilities);
        // // get supported formats
        // _supportedPresentModes = new WGPUPresentMode[surfaceCapabilities.presentModeCount];
        // for (uint i = 0; i < surfaceCapabilities.presentModeCount; i++)
        // {
        //     _supportedPresentModes[i] = surfaceCapabilities.presentModes[i];
        // }
        // //get supported formats
        // _supportedSurfaceFormats = new WGPUTextureFormat[surfaceCapabilities.formatCount];
        // for (uint i = 0; i < surfaceCapabilities.formatCount; i++)
        // {
        //     _supportedSurfaceFormats[i] = surfaceCapabilities.formats[i];
        // }

        //wgpuSurfaceCapabilitiesFreeMembers(surfaceCapabilities);

        // GraphicsLogger.Info($"Supported present modes: {string.Join(", ", _supportedPresentModes)}");

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

        // why RG11B10Ufloat is slower than RGBA16Float ???
        // if (IsFeatureSupported(WGPUFeatureName.RG11B10UfloatRenderable, supportedFeatures, supportedFeaturesCount))
        // {
        //     featuresList.Add(WGPUFeatureName.RG11B10UfloatRenderable);
        //     _preferredHDRFormat = PixelFormat.RG11B10Ufloat;
        // }
        // else
        // {
        //     _preferredHDRFormat = PixelFormat.RGBA16Float;
        // }
        //_preferredHDRFormat = PixelFormat.RGBA16Float;

        WGPUFeatureName* features = stackalloc WGPUFeatureName[featuresList.Count];
        for (int i = 0; i < featuresList.Count; i++)
        {
            features[i] = featuresList[i];
        }

        // create device
        fixed (sbyte* name = descriptor.Name.GetUtf8Span())
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


            wgpuAdapterRequestDevice(Adapter, &deviceDescriptor, &OnDeviceRequestEnded, new nint(&device));
            Device = device;
        }

        wgpuDeviceSetUncapturedErrorCallback(Device, &OnUnhandleError);

        //get queue
        Queue = wgpuDeviceGetQueue(Device);

        // load config
        // _swapChainFormat = UtilsWebGPU.PixelFormatToWebGPU(descriptor.SurfaceFormat);
        // bool isFormatSupported = false;
        // for (int i = 0; i < _supportedSurfaceFormats.Length; i++)
        // {
        //     if (_supportedSurfaceFormats[i] == _swapChainFormat)
        //     {
        //         isFormatSupported = true;
        //         break;
        //     }
        // }
        // if(!isFormatSupported)
        // {
        //     WGPUTextureFormat oldFormat = _swapChainFormat;
        //     _swapChainFormat = wgpuSurfaceGetPreferredFormat(Surface, Adapter);
        //     GraphicsLogger.Info($"Surface format {oldFormat} is not supported, using {_swapChainFormat} instead");
        // }

        //_preferredSurfaceFormat = UtilsWebGPU.PixelFormatToAbstract(_swapChainFormat);

        // _width = descriptor.InitialSurfaceSizeWidth;
        // _height = descriptor.InitialSurfaceSizeHeight;
        // _vsync = descriptor.VSync;



        // create surface render pass
        // DepthAttachment? depth = null;
        // if (descriptor.DepthFormat.HasValue)
        // {
        //     _preferredDepthStencilFormat = descriptor.DepthFormat;
        //     depth = new DepthAttachment()
        //     {
        //         Format = descriptor.DepthFormat.Value,
        //         ClearDepth = 1.0f,
        //         ClearStencil = 0,
        //     };
        // }

        // RenderPassDescriptor renderPassDescriptor = new RenderPassDescriptor(
        //     new ColorAttachment[]
        //     {
        //         new ColorAttachment()
        //         {
        //             Format = UtilsWebGPU.PixelFormatToAbstract(_swapChainFormat),
        //             ClearColor = descriptor.SurfaceClearColor,
        //         },
        //     },
        //     depth,
        //     "surface_render_pass"
        // );



        //_surfaceRenderPass = new WebGPURenderPass(this, renderPassDescriptor);

        // create surface frame buffer
        //_surfaceFrameBuffer = new WebGPUSurfaceFrameBuffer(this, _surfaceRenderPass, Surface, GetSurfaceConfig());

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


    // private WGPUSurfaceConfiguration GetSurfaceConfig()
    // {
    //     return new WGPUSurfaceConfiguration
    //     {
    //         nextInChain = null,
    //         device = Device,
    //         format = _swapChainFormat,
    //         usage = WGPUTextureUsage.RenderAttachment,
    //         // viewFormatCount = 0,
    //         // viewFormats = null,
    //         alphaMode = WGPUCompositeAlphaMode.Auto,
    //         width = _width,
    //         height = _height,
    //         presentMode = GetPresentMode(_descriptor.VSync),
    //     };
    // }

    // public WGPUPresentMode GetPresentMode(bool vsync)
    // {
    //     if (!vsync)
    //     {
    //         if (IsPresentModeSupported(WGPUPresentMode.Immediate))
    //         {
    //             return WGPUPresentMode.Immediate;
    //         }
    //         else if (IsPresentModeSupported(WGPUPresentMode.Mailbox))
    //         {
    //             return WGPUPresentMode.Mailbox;
    //         }
    //         else
    //         {
    //             GraphicsLogger.Warning("VSync is off but no supported present mode found, using FIFO");
    //         }
    //     }
    //     return WGPUPresentMode.Fifo;
    // }

    // private bool IsPresentModeSupported(WGPUPresentMode mode)
    // {
    //     for (int i = 0; i < _supportedPresentModes.Length; i++)
    //     {
    //         if (_supportedPresentModes[i] == mode)
    //         {
    //             return true;
    //         }
    //     }
    //     return false;
    // }

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
                GraphicsLogger.Warning(message +"\n" + Environment.StackTrace);
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