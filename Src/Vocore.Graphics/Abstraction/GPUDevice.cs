namespace Vocore.Graphics;


public abstract class GPUDevice : BaseGPUObject
{
    /// <summary>
    /// Creates a GPU buffer with the specified descriptor.
    /// </summary>
    /// <param name="createInfo">The descriptor for the GPU buffer.</param>
    /// <returns>The created GPU buffer.</returns>
    public GPUBuffer CreateBuffer(in BufferDescriptor createInfo)
    {
        return InternalCreateBuffer(createInfo);
    }

    /// <summary>
    /// Destroys the specified GPU buffer.
    /// </summary>
    /// <param name="buffer">The GPU buffer to destroy.</param>
    public void DestroyBuffer(GPUBuffer buffer)
    {
        InternalDestroyBuffer(buffer);
    }

    /// <summary>
    /// Creates a GPU texture with the specified descriptor.
    /// </summary>
    /// <param name="createInfo">The descriptor for the GPU texture.</param>
    /// <returns>The created GPU texture.</returns>
    public GPUTexture CreateTexture(in TextureDescriptor createInfo)
    {
        return InternalCreateTexture(createInfo);
    }

    /// <summary>
    /// Destroys the specified GPU texture.
    /// </summary>
    /// <param name="texture">The GPU texture to destroy.</param>
    public void DestroyTexture(GPUTexture texture)
    {
        InternalDestroyTexture(texture);
    }

    /// <summary>
    /// Creates a GPU command buffer.
    /// </summary>
    /// <returns>The created GPU command buffer.</returns>
    public GPUCommandBuffer CreateCommandBuffer()
    {
        return InternalCreateCommandBuffer();
    }

    /// <summary>
    /// Destroys the specified GPU command buffer.
    /// </summary>
    /// <param name="commandBuffer">The GPU command buffer to destroy.</param>
    public void DestroyCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        InternalDestroyCommandBuffer(commandBuffer);
    }

    /// <summary>
    /// Creates a GPU render pass with the specified descriptor.
    /// </summary>
    public GPURenderPass CreateRenderPass(in RenderPassDescriptor descriptor)
    {
        return InternalCreateRenderPass(descriptor);
    }

    /// <summary>
    /// Destroys the specified GPU render pass.
    /// </summary>
    public void DestroyRenderPass(GPURenderPass renderPass)
    {
        InternalDestroyRenderPass(renderPass);
    }

    public abstract PixelFormat GetPrefferedSurfaceFomat();
    public abstract PixelFormat GetPrefferedDepthFomat();

    protected abstract GPUBuffer InternalCreateBuffer(in BufferDescriptor descriptor);

    protected abstract void InternalDestroyBuffer(GPUBuffer buffer);

    protected abstract GPUTexture InternalCreateTexture(in TextureDescriptor descriptor);

    protected abstract void InternalDestroyTexture(GPUTexture texture);

    protected abstract GPUCommandBuffer InternalCreateCommandBuffer();

    protected abstract void InternalDestroyCommandBuffer(GPUCommandBuffer commandBuffer);

    protected abstract GPURenderPass InternalCreateRenderPass(in RenderPassDescriptor descriptor);

    protected abstract void InternalDestroyRenderPass(GPURenderPass renderPass);
}