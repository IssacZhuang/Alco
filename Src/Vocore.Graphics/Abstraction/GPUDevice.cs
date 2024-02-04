namespace Vocore.Graphics;

/// <summary>
/// The context and resource factory for the GPU.
/// </summary> 
public abstract class GPUDevice : BaseGPUObject
{
    public abstract GPURenderPass SwapChainRenderPass { get; }
    public abstract GPUFrameBuffer SwapChainFrameBuffer { get; }
    public abstract PixelFormat PrefferedSurfaceFomat { get; }
    public abstract PixelFormat? PrefferedDepthStencilFormat { get; }
    public abstract bool VSync { get; set; }


    /// <summary>
    /// Creates a GPU buffer with the specified descriptor.
    /// </summary>
    /// <param name="createInfo">The descriptor for the GPU buffer.</param>
    /// <returns>The created GPU buffer.</returns>
    public GPUBuffer CreateBuffer(in BufferDescriptor createInfo)
    {
        return CreateBufferCore(createInfo);
    }

    public GPUBuffer CreateBuffer<T>(in BufferDescriptor createInfo, T[] data) where T : unmanaged
    {
        GPUBuffer buffer = CreateBufferCore(createInfo);
        UpdateBuffer(buffer, data);
        return buffer;
    }

    public GPUBuffer CreateBuffer<T>(in BufferDescriptor createInfo, T data) where T : unmanaged
    {
        GPUBuffer buffer = CreateBufferCore(createInfo);
        UpdateBuffer(buffer, data);
        return buffer;
    }

    /// <summary>
    /// Destroys the specified GPU buffer.
    /// </summary>
    /// <param name="buffer">The GPU buffer to destroy.</param>
    public void DestroyBuffer(GPUBuffer buffer)
    {
        DestroyBufferCore(buffer);
    }

    /// <summary>
    /// Creates a GPU texture with the specified descriptor.
    /// </summary>
    /// <param name="createInfo">The descriptor for the GPU texture.</param>
    /// <returns>The created GPU texture.</returns>
    public GPUTexture CreateTexture(in TextureDescriptor createInfo)
    {
        return CreateTextureCore(createInfo);
    }

    /// <summary>
    /// Destroys the specified GPU texture.
    /// </summary>
    /// <param name="texture">The GPU texture to destroy.</param>
    public void DestroyTexture(GPUTexture texture)
    {
        DestroyTextureCore(texture);
    }

    /// <summary>
    /// Creates a GPU command buffer.
    /// </summary>
    /// <returns>The created GPU command buffer.</returns>
    public GPUCommandBuffer CreateCommandBuffer()
    {
        return CreateCommandBufferCore();
    }

    /// <summary>
    /// Destroys the specified GPU command buffer.
    /// </summary>
    /// <param name="commandBuffer">The GPU command buffer to destroy.</param>
    public void DestroyCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        DestroyCommandBufferCore(commandBuffer);
    }

    /// <summary>
    /// Creates a GPU render pass with the specified descriptor.
    /// </summary>
    public GPURenderPass CreateRenderPass(in RenderPassDescriptor descriptor)
    {
        return CreateRenderPassCore(descriptor);
    }

    /// <summary>
    /// Destroys the specified GPU render pass.
    /// </summary>
    public void DestroyRenderPass(GPURenderPass renderPass)
    {
        DestroyRenderPassCore(renderPass);
    }

    public GPUPipeline CreateGraphicsPipeline(in GraphicsPipelineDescriptor descriptor)
    {
        return CreateGraphicsPipelineCore(descriptor);
    }

    public void DestroyGraphicsPipeline(GPUPipeline pipeline)
    {
        DestroyGraphicsPipelineCore(pipeline);
    }

    public void ResizeSurface(uint width, uint height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new GraphicsException("Surface width and height must be greater than 0.");
        }
        ResizeSurfaceCore(width, height);
    }

    public void Submit(GPUCommandBuffer commandBuffer)
    {
        if (!commandBuffer.HasBuffer)
        {
            throw new GraphicsException($"Command buffer:{commandBuffer.Name} is empty, try use GPUCommandBuffer.Begin() and GPUCommandBuffer.End() to record commands.");
        }

        SubmitCore(commandBuffer);
    }

    public void SwapBuffers()
    {
        SwapBuffersCore();
    }

    public unsafe void UpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        UpdateBufferCore(buffer, bufferOffset, data, size);
    }

    // polymorphism

    public unsafe void UpdateBuffer(GPUBuffer buffer, byte* data, uint size)
    {
        UpdateBuffer(buffer, 0, data, size);
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, uint bufferOffset, T data) where T : unmanaged
    {
        UpdateBuffer(buffer, bufferOffset, (byte*)&data, (uint)sizeof(T));
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, T data) where T : unmanaged
    {
        UpdateBuffer(buffer, 0, (byte*)&data, (uint)sizeof(T));
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, uint bufferOffset, T[] data) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            UpdateBuffer(buffer, bufferOffset, (byte*)ptr, (uint)(sizeof(T) * data.Length));
        }
    }

    public unsafe void UpdateBuffer<T>(GPUBuffer buffer, T[] data) where T : unmanaged
    {
        UpdateBuffer(buffer, 0, data);
    }



    protected abstract GPUBuffer CreateBufferCore(in BufferDescriptor descriptor);
    protected abstract void DestroyBufferCore(GPUBuffer buffer);

    protected abstract GPUTexture CreateTextureCore(in TextureDescriptor descriptor);
    protected abstract void DestroyTextureCore(GPUTexture texture);

    protected abstract GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null);
    protected abstract void DestroyCommandBufferCore(GPUCommandBuffer commandBuffer);

    protected abstract GPURenderPass CreateRenderPassCore(in RenderPassDescriptor descriptor);
    protected abstract void DestroyRenderPassCore(GPURenderPass renderPass);

    protected abstract GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor);
    protected abstract void DestroyGraphicsPipelineCore(GPUPipeline pipeline);

    protected abstract void ResizeSurfaceCore(uint width, uint height);

    protected abstract void SubmitCore(GPUCommandBuffer commandBuffer);

    protected abstract void SwapBuffersCore();
    /// <summary>
    /// Do not store the fucking pointer when implementing, it is unsafe;<br/> Try only read data from it.
    /// </summary>
    protected abstract unsafe void UpdateBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size);


}