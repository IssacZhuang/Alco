namespace Vocore.Graphics.NoGPU;

internal class NoDevice : GPUDevice
{
    public static readonly NoTexture noTexture = new NoTexture();
    public static readonly NoTextureView noTextureView = new NoTextureView();
    public static readonly NoSampler noSampler = new NoSampler();
    public static readonly NoBindGroup noBindGroup = new NoBindGroup();
    public static readonly NoCommandBuffer noCommandBuffer = new NoCommandBuffer();
    public static readonly NoResuableRenderBuffer noResuableRenderBuffer = new NoResuableRenderBuffer();
    public static readonly NoPipeline noPipeline = new NoPipeline();
    public static readonly NoResourceGroup noResourceGroup = new NoResourceGroup();
    public static readonly NoDevice noDevice = new NoDevice();

    public override GPUSampler SamplerNearestRepeat => noSampler;

    public override GPUSampler SamplerLinearRepeat => noSampler;

    public override GPUSampler SamplerNearestClamp => noSampler;

    public override GPUSampler SamplerLinearClamp => noSampler;

    public override GPUSampler SamplerNearestMirrorRepeat => noSampler;

    public override GPUSampler SamplerLinearMirrorRepeat => noSampler;

    public override GPUBindGroup BindGroupUniformBuffer => noBindGroup;

    public override GPUBindGroup BindGroupStorageBuffer => noBindGroup;

    public override GPUBindGroup BindGroupTexture2DSampled => noBindGroup;

    public override GPUBindGroup BindGroupTexture2DRead => noBindGroup;

    public override GPUBindGroup BindGroupTexture2DStorage => noBindGroup;

    public override PixelFormat PrefferedSurfaceFomat => PixelFormat.RGBA8UnormSrgb;

    protected override GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor)
    {
        return noBindGroup;
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        return new NoBuffer(descriptor);
    }

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
    {
        return noCommandBuffer;
    }

    protected override GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor)
    {
        return noPipeline;
    }

    protected override GPUFrameBuffer CreateFrameBufferCore(in FrameBufferDescriptor descriptor)
    {
        return new NoFrameBuffer(descriptor);
    }

    protected override GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor)
    {
        return noPipeline;
    }

    protected override GPURenderPass CreateRenderPassCore(in RenderPassDescriptor descriptor)
    {
        return new NoRenderPass(descriptor);
    }

    protected override GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor)
    {
        return noResourceGroup;
    }

    protected override GPUResuableRenderBuffer CreateResuableRenderBufferCore(in ResuableRenderBufferDescriptor? descriptor)
    {
        return noResuableRenderBuffer;
    }

    protected override GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor)
    {
        return noSampler;
    }

    protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor)
    {
        return noTexture;
    }

    protected override GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor)
    {
        return noTextureView;
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