namespace Alco.Graphics.NoGPU;

internal class NoCommandBuffer : GPUCommandBuffer
{
    private bool _hasBuffer = false;
    public override bool HasBuffer => _hasBuffer;
    protected override GPUDevice Device => NoDevice.noDevice;

    public NoCommandBuffer(in CommandBufferDescriptor? descriptor): base(descriptor)
    {
    }

    protected override void BeginCore()
    {
        _hasBuffer = false;
    }

    protected override void ClearColorCore(ColorFloat color, uint index)
    {
        
    }

    protected override void ClearDepthCore(float depth)
    {
        
    }

    protected override void ClearStencilCore(uint stencil)
    {
        
    }

    protected override void DispatchComputeCore(uint x, uint y, uint z)
    {
        
    }

    protected override void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        
    }

    protected override void Dispose(bool disposing)
    {
        
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        
    }

    protected override void EndCore()
    {
        _hasBuffer = true;
    }

    protected override unsafe void PushConstantsCore(ShaderStage stage, uint bufferOffset, byte* data, uint size)
    {
        
    }

    protected override void SetComputePipelineCore(GPUPipeline pipeline)
    {
        
    }

    protected override void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        
    }

    protected override void SetFrameBufferCore(GPUFrameBuffer frameBuffer)
    {
        
    }

    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        
    }

    protected override void SetStencilReferenceCore(uint value)
    {
        
    }

    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        
    }

    protected override void CopyBufferCore(GPUBuffer src, GPUBuffer dst, ulong srcOffset, ulong dstOffset, ulong size)
    {
        
    }

    protected override void CopyBufferToTextureCore(GPUBuffer src, GPUTexture dst, uint offset, TextureAspect aspect)
    {
        
    }
}