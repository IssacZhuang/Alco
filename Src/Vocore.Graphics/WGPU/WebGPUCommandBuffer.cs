using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUCommandBuffer : GPUCommandBuffer
{
    private readonly WGPUDevice _nativeDevice;
    private readonly string _name;



    // used every frame
    private WGPUCommandEncoder _encoder;
    private WGPURenderPassEncoder _renderPass;
    // create on end(), can be reused
    private WGPUCommandBuffer _buffer;

    public WGPUCommandBuffer Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    public override bool HasBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer != WGPUCommandBuffer.Null;
    }

    public override string Name => throw new NotImplementedException();

    public unsafe WebGPUCommandBuffer(WGPUDevice nativeDevice, CommandBufferDescriptor? descriptor = null)
    {
        _nativeDevice = nativeDevice;
        if (descriptor.HasValue)
        {
            _name = descriptor.Value.Name;
        }
        else
        {
            _name = "Unnamed Command Buffer";
        }

        _encoder = wgpuDeviceCreateCommandEncoder(_nativeDevice, _name);


        _buffer = WGPUCommandBuffer.Null;
        _renderPass = WGPURenderPassEncoder.Null;
        _encoder = WGPUCommandEncoder.Null;
    }

    protected override void Dispose(bool disposing)
    {
        if (_buffer != WGPUCommandBuffer.Null)
        {
            wgpuCommandBufferRelease(_buffer);
            _buffer = WGPUCommandBuffer.Null;
        }

        if (_renderPass != WGPURenderPassEncoder.Null)
        {
            wgpuRenderPassEncoderRelease(_renderPass);
            _renderPass = WGPURenderPassEncoder.Null;
        }

        if (_encoder != WGPUCommandEncoder.Null)
        {
            wgpuCommandEncoderRelease(_encoder);
            _encoder = WGPUCommandEncoder.Null;
        }
    }

    protected unsafe override void InternalBegin(GPURenderPass renderPass)
    {
        _encoder = wgpuDeviceCreateCommandEncoder(_nativeDevice, _name);

        // begin render pass
        WebGPURenderPass webGPURenderPass = (WebGPURenderPass)renderPass;
        WGPURenderPassDescriptor descriptor = webGPURenderPass.RenderPassDescriptor;
        _renderPass = wgpuCommandEncoderBeginRenderPass(_encoder, &descriptor);

        // clear buffer
        wgpuCommandBufferRelease(_buffer);
        _buffer = WGPUCommandBuffer.Null;
    }

    protected unsafe override void InternalEnd()
    {
        _buffer = wgpuCommandEncoderFinish(_encoder, _name);

        // release encoder
        wgpuCommandEncoderRelease(_encoder);
        _encoder = WGPUCommandEncoder.Null;

        // release render pass
        wgpuRenderPassEncoderRelease(_renderPass);
        _renderPass = WGPURenderPassEncoder.Null;
    }

    protected override void InternalSetPipeline(GPUGraphicsPipeline pipeline)
    {
        WGPURenderPipeline nativePipeline = ((WebGPUGraphicsPipeline)pipeline).Native;
        wgpuRenderPassEncoderSetPipeline(_renderPass, nativePipeline);
    }

    protected override void InternalDrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        wgpuRenderPassEncoderDrawIndexed(_renderPass, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    protected override void InternalDrawIndexedIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        throw new NotImplementedException();
    }

    protected override void InternalDrawIndirect(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {


    }

    protected override unsafe void InternalUpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {

    }

    protected override void InternalSetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        throw new NotImplementedException();
    }

    protected override void InternalSetIndexBuffer(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        throw new NotImplementedException();
    }
}