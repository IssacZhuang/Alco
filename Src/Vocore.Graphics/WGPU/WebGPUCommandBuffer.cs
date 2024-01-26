using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUCommandBuffer : GPUCommandBuffer
{
    #region Properties
    private readonly WGPUDevice _nativeDevice;

    // used every frame
    private WGPUCommandEncoder _encoder;

    // cached state create by internal, release on end()
    private WGPURenderPassEncoder _renderPass;
    private WGPUComputePassEncoder _computePass;

    // cached state from outside
    private WGPURenderPipeline _graphicsPipeline;
    private WGPUComputePipeline _computePipeline;

    // create on end(), can be reused
    private WGPUCommandBuffer _buffer;

    #endregion

    #region Abstract Implementation

    public override string Name { get; }

    public override bool HasBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer != WGPUCommandBuffer.Null;
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseCommandBuffer();
        ReleaseCommandEncoder();

    }

    // begin the encoder
    protected unsafe override void BeginCore()
    {
        _encoder = wgpuDeviceCreateCommandEncoder(_nativeDevice, Name);

        // clear buffer
        if (_buffer != WGPUCommandBuffer.Null)
        {
            wgpuCommandBufferRelease(_buffer);
            _buffer = WGPUCommandBuffer.Null;
        }
    }

    // end the encoder
    protected unsafe override void EndCore()
    {
        TryFinishCurrentRenderPass();
        TryFinishCurrentComputePass();
        _buffer = wgpuCommandEncoderFinish(_encoder, Name);

        // release encoder
        wgpuCommandEncoderRelease(_encoder);
        _encoder = WGPUCommandEncoder.Null;
    }

    protected override unsafe void SetFrameBufferCore(GPUFrameBuffer frameBuffer)
    {
        WebGPUFrameBufferBase nativeFrameBuffer = (WebGPUFrameBufferBase)frameBuffer;

        TryFinishCurrentRenderPass();
        TryFinishCurrentComputePass();
        WGPURenderPassDescriptor descriptor = nativeFrameBuffer.Native;
        _renderPass = wgpuCommandEncoderBeginRenderPass(_encoder, &descriptor);
    }

    protected override void SetPipelineCore(GPUPipeline pipeline)
    {
        WGPURenderPipeline nativePipeline = ((WebGPUGraphicsPipeline)pipeline).Native;
        wgpuRenderPassEncoderSetPipeline(_renderPass, nativePipeline);
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        wgpuRenderPassEncoderDrawIndexed(_renderPass, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        throw new NotImplementedException();
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {

    }

    protected override unsafe void UpdateBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {

    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        throw new NotImplementedException();
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region WebGPU Implementation

    public WGPUCommandBuffer Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    public bool HasRenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderPass != WGPURenderPassEncoder.Null;
    }

    public bool HasComputePass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _computePass != WGPUComputePassEncoder.Null;
    }

    public unsafe WebGPUCommandBuffer(WGPUDevice nativeDevice, CommandBufferDescriptor? descriptor = null)
    {

        _nativeDevice = nativeDevice;
        if (descriptor.HasValue)
        {
            Name = descriptor.Value.Name;
        }
        else
        {
            Name = "Unnamed Command Buffer";
        }

        _encoder = wgpuDeviceCreateCommandEncoder(_nativeDevice, Name);


        _buffer = WGPUCommandBuffer.Null;
        _encoder = WGPUCommandEncoder.Null;

        _renderPass = WGPURenderPassEncoder.Null;
        _computePass = WGPUComputePassEncoder.Null;
    }

    private void ReleaseCommandEncoder()
    {
        if (_encoder != WGPUCommandEncoder.Null)
        {
            wgpuCommandEncoderRelease(_encoder);
            _encoder = WGPUCommandEncoder.Null;
        }
    }

    private void ReleaseCommandBuffer()
    {
        if (_buffer != WGPUCommandBuffer.Null)
        {
            wgpuCommandBufferRelease(_buffer);
            _buffer = WGPUCommandBuffer.Null;
        }
    }

    private unsafe void StartRenderPass(WGPURenderPassDescriptor descriptor)
    {
        _renderPass = wgpuCommandEncoderBeginRenderPass(_encoder, &descriptor);
    }

    private void TryFinishCurrentRenderPass()
    {
        if (_renderPass != WGPURenderPassEncoder.Null)
        {
            wgpuRenderPassEncoderEnd(_renderPass);
            wgpuRenderPassEncoderRelease(_renderPass);
            _renderPass = WGPURenderPassEncoder.Null;
        }
    }

    private void TryFinishCurrentComputePass()
    {
        if (_computePass != WGPUComputePassEncoder.Null)
        {
            wgpuComputePassEncoderEnd(_computePass);
            wgpuComputePassEncoderRelease(_computePass);
            _computePass = WGPUComputePassEncoder.Null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearGraphicsPipeline()
    {
        _graphicsPipeline = WGPURenderPipeline.Null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearComputePipeline()
    {
        _computePipeline = WGPUComputePipeline.Null;
    }

    #endregion
}