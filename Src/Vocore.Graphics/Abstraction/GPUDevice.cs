namespace Vocore.Graphics;


public abstract class GPUDevice : BaseGPUObject
{
    public static Action<string>? ErrorCallback { get; set; }
    public static Action<string>? WarningCallback { get; set; }
    public static Action<string>? InfoCallback { get; set; }
    
    /// <summary>
    /// Creates a GPU buffer with the specified descriptor.
    /// </summary>
    /// <param name="createInfo">The descriptor for the GPU buffer.</param>
    /// <returns>The created GPU buffer.</returns>
    public GPUBuffer CreateBuffer(in BufferDescriptor createInfo)
    {
        return InternalCreateBuffer(createInfo);
    }

    public GPUBuffer CreateBuffer<T>(in BufferDescriptor createInfo, T[] data) where T : unmanaged
    {
        GPUBuffer buffer = InternalCreateBuffer(createInfo);
        UpdateBuffer(buffer, data);
        return buffer;
    }

    public GPUBuffer CreateBuffer<T>(in BufferDescriptor createInfo, T data) where T : unmanaged
    {
        GPUBuffer buffer = InternalCreateBuffer(createInfo);
        UpdateBuffer(buffer, data);
        return buffer;
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

    public void Submit(GPUCommandBuffer commandBuffer)
    {
        if (!commandBuffer.HasBuffer)
        {
            throw new GraphicsException($"Command buffer:{commandBuffer.Name} is empty, try use GPUCommandBuffer.Begin() and GPUCommandBuffer.End() to record commands.");
        }

        InternalSubmit(commandBuffer);
    }

    public unsafe void UpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        InternalUpdateBuffer(buffer, bufferOffset, data, size);
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

    public abstract PixelFormat GetPrefferedSurfaceFomat();
    public abstract PixelFormat GetPrefferedDepthFomat();

    protected abstract GPUBuffer InternalCreateBuffer(in BufferDescriptor descriptor);

    protected abstract void InternalDestroyBuffer(GPUBuffer buffer);

    protected abstract GPUTexture InternalCreateTexture(in TextureDescriptor descriptor);

    protected abstract void InternalDestroyTexture(GPUTexture texture);

    protected abstract GPUCommandBuffer InternalCreateCommandBuffer(in CommandBufferDescriptor? descriptor = null);

    protected abstract void InternalDestroyCommandBuffer(GPUCommandBuffer commandBuffer);

    protected abstract GPURenderPass InternalCreateRenderPass(in RenderPassDescriptor descriptor);

    protected abstract void InternalDestroyRenderPass(GPURenderPass renderPass);

    protected abstract void InternalSubmit(GPUCommandBuffer commandBuffer);
    /// <summary>
    /// Do not store the fucking pointer when implementing, it is unsafe;<br/> Try only read data from it.
    /// </summary>
    protected abstract unsafe void InternalUpdateBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size);


}