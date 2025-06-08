

namespace Alco.Graphics.NoGPU;

internal class NoDevice : GPUDevice
{
    private class DummyLoopProvider : IGPUDeviceHost
    {
        event Action IGPUDeviceHost.OnEndFrame
        {
            add{}
            remove{}
        }

        event Action IGPUDeviceHost.OnDispose
        {
            add { }
            remove { }
        }

        public void LogError(ReadOnlySpan<char> message)
        {
            
        }

        public void LogInfo(ReadOnlySpan<char> message)
        {
            
        }

        public void LogSuccess(ReadOnlySpan<char> message)
        {
            
        }

        public void LogWarning(ReadOnlySpan<char> message)
        {
            
        }
    }
    public static readonly NoDevice noDevice = new NoDevice();

    public override GPUBindGroup BindGroupUniformBuffer {get;}

    public override GPUBindGroup BindGroupStorageBuffer {get;}

    public override GPUBindGroup BindGroupStorageBufferWithCounter {get;}

    public override GPUBindGroup BindGroupTexture2DSampled {get;}

    public override GPUBindGroup BindGroupTextureDepthSampled {get;}

    public override GPUBindGroup BindGroupTexture2DRead {get;}

    public override GPUBindGroup BindGroupTexture2DStorage {get;}

    public override PixelFormat PrefferedSurfaceFomat {get;}

    public override bool TextureCompressBC3Supported => false;

    public NoDevice(): base(new DeviceDescriptor{
        Host = new DummyLoopProvider(),
        Backend = GraphicsBackend.None,
        Debug = false,
        PreferredSurfaceFormat = PixelFormat.RGBA8Unorm,
        Name = "NoGPU Device"
    })
    {

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

        BindGroupTextureDepthSampled = CreateBindGroup(new BindGroupDescriptor

        {
            Name = "default_bind_group_texture_depth_sampled",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D, TextureSampleType.Depth)),
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

    protected override GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor)
    {
        return new NoBindGroup(descriptor);
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        return new NoBuffer(descriptor);
    }

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
    {
        return new NoCommandBuffer(descriptor);
    }

    protected override GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor)
    {
        return new NoPipeline(descriptor);
    }

    protected override GPUFrameBuffer CreateFrameBufferCore(in FrameBufferDescriptor descriptor)
    {
        return new NoFrameBuffer(descriptor);
    }

    protected override GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor)
    {
        return new NoPipeline(descriptor);
    }

    protected override GPURenderPass CreateRenderPassCore(in RenderPassDescriptor descriptor)
    {
        return new NoRenderPass(descriptor);
    }

    protected override GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor)
    {
        return new NoResourceGroup(descriptor);
    }

    protected override GPURenderBundle CreateRenderBundleCore(in RenderBundleDescriptor? descriptor)
    {
        return new NoRenderBundle(descriptor);
    }

    protected override GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor)
    {
        return new NoSampler(descriptor);
    }

    protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor)
    {
        return new NoTexture(descriptor);
    }

    protected override GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor)
    {
        return new NoTextureView(descriptor);
    }

    public override GPUSwapchain CreateSwapchainCore(in SwapchainDescriptor descriptor)
    {
        return new NoSwapchain(descriptor);
    }


    public override void Destroy(BaseGPUObject obj)
    {
        //do nothing
        // base.Destroy(obj);
    }

    public override void DestroyImmediate(BaseGPUObject obj)
    {
        //do nothing
        // base.DestroyImmediate(obj);
    }

    protected override void DisposeCore()
    {
        
    }

    protected override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        
    }

    protected override unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        
    }

    protected override unsafe void ReadBufferCore(GPUBuffer buffer, byte* dest, uint bufferOffset, uint size)
    {
        
    }

    protected override unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint mipLevel)
    {
        
    }

    protected override unsafe void ReadTextureCore(GPUTexture texture, byte* dest, uint dataSize, uint mipLevel = 0)
    {
        
    }

    protected override void OnEndFrameCore()
    {
        
    }
}