using System.Runtime.InteropServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal class VulkanCommandBuffer: GPUCommandBuffer
{
    #region Members
    private readonly VkCommandBuffer _native;
    private readonly VkDevice _nativeDevice;

    #endregion

    #region Abstract Implementation

    public override bool HasBuffer => throw new NotImplementedException();

    public override string Name => throw new NotImplementedException();

    protected override void BeginCore()
    {
        throw new NotImplementedException();
    }

    protected override void ClearFrameCore(ColorFloat color, float depth, uint stencil)
    {
        throw new NotImplementedException();
    }

    protected override void DispatchComputeCore(uint x, uint y, uint z)
    {
        throw new NotImplementedException();
    }

    protected override void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        throw new NotImplementedException();
    }

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        throw new NotImplementedException();
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        throw new NotImplementedException();
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        throw new NotImplementedException();
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        throw new NotImplementedException();
    }

    protected override void EndCore()
    {
        throw new NotImplementedException();
    }

    protected override void SetComputePipelineCore(GPUPipeline pipeline)
    {
        throw new NotImplementedException();
    }

    protected override void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        throw new NotImplementedException();
    }

    protected override void SetFrameBufferCore(GPUFrameBuffer frameBuffer)
    {
        throw new NotImplementedException();
    }

    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        throw new NotImplementedException();
    }

    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        throw new NotImplementedException();
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        throw new NotImplementedException();
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        throw new NotImplementedException();
    }

    protected override unsafe void UpdateBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Vulkan Specific

    public VulkanCommandBuffer(VkDevice device, CommandBufferDescriptor descriptor)
    {
        
    }
    #endregion
}