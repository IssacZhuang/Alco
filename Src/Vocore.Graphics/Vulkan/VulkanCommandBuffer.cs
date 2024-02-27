using System.Runtime.InteropServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Vocore.Graphics.Vulkan;

internal unsafe class VulkanCommandBuffer : GPUCommandBuffer
{
    #region Members
    private const int FrameCount = 2;
    private readonly VkCommandBuffer[] _buffers;
    private readonly VkCommandPool[] _pools;
    private readonly VulkanDevice _device;
    private bool _isRenderPassBegin;
    private int _frameIndex;
    private VkCommandBuffer _currentBuffer;

    #endregion

    #region Abstract Implementation

    public override bool HasBuffer => throw new NotImplementedException();

    public override string Name { get; }

    protected override void BeginCore()
    {
        VkCommandBufferBeginInfo beginInfo = new VkCommandBufferBeginInfo
        {
            flags = VkCommandBufferUsageFlags.None,
            pInheritanceInfo = null
        };

        vkBeginCommandBuffer(_currentBuffer, &beginInfo).CheckResult("Failed to begin command buffer");
    }

    protected override void EndCore()
    {
        TryEndRenderPass();
        vkEndCommandBuffer(_currentBuffer).CheckResult("Failed to end command buffer");
        SwapBuffer();
    }

    protected override void SetFrameBufferCore(GPUFrameBuffer frameBuffer)
    {
        VulkanFramebufferBase vulkanFrameBuffer = (VulkanFramebufferBase)frameBuffer;
        BeginRenderPass(vulkanFrameBuffer.PassBegineInfo);
    }


    protected override void ClearColorCore(ColorFloat color, uint index)
    {
        //Todo
    }

    protected override void ClearDepthStencilCore(float depth, uint stencil)
    {
        //Todo
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



    protected override void SetComputePipelineCore(GPUPipeline pipeline)
    {
        throw new NotImplementedException();
    }

    protected override void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup)
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

    public VulkanCommandBuffer(VulkanDevice device, CommandBufferDescriptor? descriptor)
    {
        Name = descriptor?.Name ?? "unamed_command_buffer";
        _buffers = new VkCommandBuffer[FrameCount];
        _pools = new VkCommandPool[FrameCount];
        _device = device;

        for (var i = 0; i < FrameCount; i++)
        {
            VkCommandPoolCreateInfo poolCreateInfo = new VkCommandPoolCreateInfo
            {
                queueFamilyIndex = device.GraphicsQueueIndex,
                flags = VkCommandPoolCreateFlags.ResetCommandBuffer
            };

            vkCreateCommandPool(device.Native, &poolCreateInfo, null, out _pools[i]).CheckResult();

            VkCommandBufferAllocateInfo allocateInfo = new VkCommandBufferAllocateInfo
            {
                commandPool = _pools[i],
                level = VkCommandBufferLevel.Primary,
                commandBufferCount = 1
            };

            VkCommandBuffer tmp;
            vkAllocateCommandBuffers(device.Native, &allocateInfo, &tmp).CheckResult();
            _buffers[i] = tmp;
        }

        _frameIndex = 0;
        _currentBuffer = _buffers[_frameIndex];
    }

    private void SwapBuffer()
    {
        _frameIndex = (_frameIndex + 1) % FrameCount;
        _currentBuffer = _buffers[_frameIndex];
    }

    private void BeginRenderPass(VkRenderPassBeginInfo passBeginInfo)
    {
        vkCmdBeginRenderPass(_currentBuffer, &passBeginInfo, VkSubpassContents.Inline);
        _isRenderPassBegin = true;
    }

    private void TryEndRenderPass()
    {
        if (_isRenderPassBegin)
        {
            vkCmdEndRenderPass(_currentBuffer);
            _isRenderPassBegin = false;
        }
    }

    #endregion
}