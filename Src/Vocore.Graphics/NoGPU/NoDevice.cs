

namespace Vocore.Graphics.NoGPU;

internal class NoDevice : GPUDevice
{
    private class DummyLoopProvider : IGPULoopProvider
    {
        event Action IGPULoopProvider.OnEndFrame
        {
            add
            {
                
            }

            remove
            {
                
            }
        }
    }
    public static readonly NoDevice noDevice = new NoDevice();

    public override GPUSampler SamplerNearestRepeat {get;   }

    public override GPUSampler SamplerLinearRepeat {get;}

    public override GPUSampler SamplerNearestClamp {get;}

    public override GPUSampler SamplerLinearClamp {get;}

    public override GPUSampler SamplerNearestMirrorRepeat {get;}

    public override GPUSampler SamplerLinearMirrorRepeat {get;}

    public override GPUBindGroup BindGroupUniformBuffer {get;}

    public override GPUBindGroup BindGroupStorageBuffer {get;}

    public override GPUBindGroup BindGroupTexture2DSampled {get;}

    public override GPUBindGroup BindGroupTexture2DRead {get;}

    public override GPUBindGroup BindGroupTexture2DStorage {get;}

    public override PixelFormat PrefferedSurfaceFomat {get;}

    public NoDevice(): base(new DeviceDescriptor{
        LoopProvider = new DummyLoopProvider(),
        Backend = GraphicsBackend.None,
        Debug = false,
        PreferredSurfaceFormat = PixelFormat.RGBA8Unorm,
        Name = "NoGPU Device"
    })
    {
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

    protected override GPUResuableRenderBuffer CreateResuableRenderBufferCore(in ResuableRenderBufferDescriptor? descriptor)
    {
        return new NoResuableRenderBuffer(descriptor);
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

    protected override void SubmitCore(GPUResuableRenderBuffer renderBuffer)
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