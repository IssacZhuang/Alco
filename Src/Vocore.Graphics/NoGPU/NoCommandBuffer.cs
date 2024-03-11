namespace Vocore.Graphics.NoGPU;

internal class NoCommandBuffer : GPUCommandBuffer
{
    public override bool HasBuffer => false;

    public override string Name => "no_gpu_command_buffer";

    protected override void BeginCore()
    {
        
    }

    protected override void ClearColorCore(ColorFloat color, uint index)
    {
        
    }

    protected override void ClearDepthStencilCore(float depth, uint stencil)
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

    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        
    }
}