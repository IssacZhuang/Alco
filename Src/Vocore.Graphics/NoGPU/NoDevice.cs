namespace Vocore.Graphics.NoGPU;

internal class NoDevice : GPUDevice
{
    private static readonly NoRenderPass noRenderPass = new NoRenderPass();
    private static readonly NoFrameBuffer noFrameBuffer = new NoFrameBuffer();
    private static readonly NoTexture noTexture = new NoTexture();
    private static readonly NoTextureView noTextureView = new NoTextureView();
    private static readonly NoSampler noSampler = new NoSampler();
    private static readonly NoBindGroup noBindGroup = new NoBindGroup();
    private static readonly NoBuffer noBuffer = new NoBuffer();
    private static readonly NoCommandBuffer noCommandBuffer = new NoCommandBuffer();
    private static readonly NoResuableRenderBuffer noResuableRenderBuffer = new NoResuableRenderBuffer();
    private static readonly NoPipeline noPipeline = new NoPipeline();
    private static readonly NoResourceGroup noResourceGroup = new NoResourceGroup();

    public override GPURenderPass SwapChainRenderPass => noRenderPass;

    public override GPUFrameBuffer SwapChainFrameBuffer => noFrameBuffer;

    public override PixelFormat PrefferedSurfaceFomat => PixelFormat.RGBA8Unorm;

    public override PixelFormat? PrefferedDepthStencilFormat => null;

    public override bool VSync { get; set; }

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

    public override GPUBindGroup BindGroupStorageTexture2D => noBindGroup;

    public override string Name => "no_gpu_device";

    protected override GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor)
    {
        return noBindGroup;
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        return noBuffer;
    }

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
    {
        return noCommandBuffer;
    }

    protected override GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor)
    {
        return noPipeline;
    }

    protected override GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor)
    {
        return noPipeline;
    }

    protected override GPURenderPass CreateRenderPassCore(in RenderPassDescriptor descriptor)
    {
        return noRenderPass;
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

    protected override void DestroyBindGroupCore(GPUBindGroup bindGroup)
    {
        
    }

    protected override void DestroyBufferCore(GPUBuffer buffer)
    {
        
    }

    protected override void DestroyCommandBufferCore(GPUCommandBuffer commandBuffer)
    {
        
    }

    protected override void DestroyComputePipelineCore(GPUPipeline pipeline)
    {
        
    }

    protected override void DestroyGraphicsPipelineCore(GPUPipeline pipeline)
    {
        
    }

    protected override void DestroyRenderPassCore(GPURenderPass renderPass)
    {
        
    }

    protected override void DestroyResourceGroupCore(GPUResourceGroup resourceGroup)
    {
        
    }

    protected override void DestroyResuableRenderBufferCore(GPUResuableRenderBuffer renderBuffer)
    {
        
    }

    protected override void DestroySamplerCore(GPUSampler sampler)
    {
        
    }

    protected override void DestroyTextureCore(GPUTexture texture)
    {
        
    }

    protected override void DestroyTextureViewCore(GPUTextureView textureView)
    {
        
    }

    protected override void Dispose(bool disposing)
    {
        
    }

    protected override void ResizeSurfaceCore(uint width, uint height)
    {
        
    }

    protected override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        
    }

    protected override void SubmitCore(GPUResuableRenderBuffer renderBuffer)
    {
        
    }

    protected override void SwapBuffersCore()
    {
        
    }

    protected override unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        
    }

    protected override unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint pixelSize, uint mipLevel)
    {
        
    }
}