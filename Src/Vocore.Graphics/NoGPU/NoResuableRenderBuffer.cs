namespace Vocore.Graphics.NoGPU;

internal class NoResuableRenderBuffer : GPUResuableRenderBuffer
{
    public override bool HasBuffer => true;

    public override string Name => "no_gpu_reusable_render_buffer";

    protected override void BeginCore()
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