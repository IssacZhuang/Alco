namespace Vocore.Graphics;

/// <summary>
/// The context and resource factory for the GPU.
/// </summary> 
public abstract class GPUDevice : BaseGPUObject
{
    // Abstract properties
    public abstract GPURenderPass SwapChainRenderPass { get; }
    public abstract GPUFrameBuffer SwapChainFrameBuffer { get; }
    public abstract PixelFormat PrefferedSurfaceFomat { get; }
    public abstract PixelFormat? PrefferedDepthStencilFormat { get; }
    public abstract bool VSync { get; set; }


    // Default samplers, those are the most common samplers used in the graphics pipeline.
    // user can also create their own samplers by using the CreateSampler method.
    public abstract GPUSampler SamplerNearestRepeat { get; }
    public abstract GPUSampler SamplerLinearRepeat { get; }
    public abstract GPUSampler SamplerNearestClamp { get; }
    public abstract GPUSampler SamplerLinearClamp { get; }
    public abstract GPUSampler SamplerNearestMirrorRepeat { get; }
    public abstract GPUSampler SamplerLinearMirrorRepeat { get; }

    // Default bind groups, those are the most common bind groups used in the graphics pipeline.
    public abstract GPUBindGroup BindGroupBuffer { get; }
    public abstract GPUBindGroup BindGroupTexture2D { get; }
    public abstract GPUBindGroup BindGroupStorageTexture2D { get; }


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
        WriteBuffer(buffer, data);
        return buffer;
    }

    public GPUBuffer CreateBuffer<T>(in BufferDescriptor createInfo, T data) where T : unmanaged
    {
        GPUBuffer buffer = CreateBufferCore(createInfo);
        WriteBuffer(buffer, data);
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

    public GPUBindGroup CreateBindGroup(in BindGroupDescriptor descriptor)
    {
        return CreateBindGroupCore(descriptor);
    }

    public void DestroyBindGroup(GPUBindGroup bindGroup)
    {
        DestroyBindGroupCore(bindGroup);
    }

    public GPUResourceGroup CreateResourceGroup(in ResourceGroupDescriptor descriptor)
    {
        return CreateResourceGroupCore(descriptor);
    }

    public void DestroyResourceGroup(GPUResourceGroup resourceGroup)
    {
        DestroyResourceGroupCore(resourceGroup);
    }

    public GPUTextureView CreateTextureView(in TextureViewDescriptor descriptor)
    {
        return CreateTextureViewCore(descriptor);
    }

    public void DestroyTextureView(GPUTextureView textureView)
    {
        DestroyTextureViewCore(textureView);
    }

    public GPUSampler CreateSampler(in SamplerDescriptor descriptor)
    {
        return CreateSamplerCore(descriptor);
    }

    public void DestroySampler(GPUSampler sampler)
    {
        DestroySamplerCore(sampler);
    }

    public void ResizeSurface(uint width, uint height)
    {
        if (width <= 0 || height <= 0)
        {
            //it might be the window is minimized
            return;
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

    public unsafe void WriteBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        WriteBufferCore(buffer, bufferOffset, data, size);
    }

    public unsafe void WriteTexture(GPUTexture texture, byte* data, uint dataSize, uint pixelSize, uint mipLevel = 0)
    {
        WriteTextureCore(texture, data, dataSize, pixelSize, mipLevel);
    }

    // polymorphism

    public unsafe void WriteBuffer(GPUBuffer buffer, byte* data, uint size)
    {
        WriteBuffer(buffer, 0, data, size);
    }

    public unsafe void WriteBuffer<T>(GPUBuffer buffer, uint bufferOffset, T data) where T : unmanaged
    {
        WriteBuffer(buffer, bufferOffset, (byte*)&data, (uint)sizeof(T));
    }

    public unsafe void WriteBuffer<T>(GPUBuffer buffer, T data) where T : unmanaged
    {
        WriteBuffer(buffer, 0, (byte*)&data, (uint)sizeof(T));
    }

    public unsafe void WriteBuffer<T>(GPUBuffer buffer, uint bufferOffset, T[] data) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            WriteBuffer(buffer, bufferOffset, (byte*)ptr, (uint)(sizeof(T) * data.Length));
        }
    }

    public unsafe void WriteBuffer<T>(GPUBuffer buffer, T[] data) where T : unmanaged
    {
        WriteBuffer(buffer, 0, data);
    }

    public unsafe void WriteTexture<TColor>(GPUTexture texture, TColor[] data, uint mipLevel = 0) where TColor : unmanaged
    {
        fixed (TColor* ptr = data)
        {
            WriteTexture(texture, (byte*)ptr, (uint)(sizeof(TColor) * data.Length), (uint)sizeof(TColor), mipLevel);
        }
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

    protected abstract GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor);
    protected abstract void DestroyBindGroupCore(GPUBindGroup bindGroup);

    protected abstract GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor);
    protected abstract void DestroyResourceGroupCore(GPUResourceGroup resourceGroup);

    protected abstract GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor);
    protected abstract void DestroyTextureViewCore(GPUTextureView textureView);

    protected abstract GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor);
    protected abstract void DestroySamplerCore(GPUSampler sampler);

    protected abstract void ResizeSurfaceCore(uint width, uint height);

    protected abstract void SubmitCore(GPUCommandBuffer commandBuffer);

    protected abstract void SwapBuffersCore();
    /// <summary>
    /// Do not store the fucking pointer when implementing, it is unsafe;<br/> Try only read data from it.
    /// </summary>
    protected abstract unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size);

    protected abstract unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint pixelSize, uint mipLevel);
}