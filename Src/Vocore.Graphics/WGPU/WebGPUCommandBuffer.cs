using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

public class WebGPUCommandBuffer : GPUCommandBuffer
{
    private readonly WGPUCommandEncoder _encoder;
    private readonly string _name;

    public WebGPUCommandBuffer(WGPUDevice nativeDevice, CommandBufferDescriptor? descriptor)
    {
        if (descriptor.HasValue)
        {
            _name = descriptor.Value.Name;
        }
        else
        {
            _name = "Unnamed Command Buffer";
        }
    }

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }

    protected override void InternalBegin(GPURenderPass renderPass)
    {
        throw new NotImplementedException();
    }

    protected override void InternalDrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        throw new NotImplementedException();
    }

    protected override void InternalDrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        throw new NotImplementedException();
    }

    protected override void InternalDrawIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        throw new NotImplementedException();
    }

    protected override void InternalEnd()
    {
        throw new NotImplementedException();
    }

    protected override void InternalSetPipeline(GPUGraphicsPipeline pipeline)
    {
        throw new NotImplementedException();
    }

    protected override unsafe void InternalUpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        throw new NotImplementedException();
    }
}