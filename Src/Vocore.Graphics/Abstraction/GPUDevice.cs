using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

/// <summary>
/// The low-level interface to do the operations on the GPU. It is the entry point to create the GPU resources and submit the commands to the GPU.
/// <br/> !Attention: The GPUDevice is not thread-safe, it should only be used in the main thread or use the synchronization mechanism to protect the access.
/// </summary> 
public abstract class GPUDevice : BaseGPUObject
{
    // Abstract properties
    /// <summary>
    /// The <see cref="GPURenderPass"/> of the surface swap chain.
    /// </summary>
    public abstract GPURenderPass SwapChainRenderPass { get; }
    /// <summary>
    /// The <see cref="GPUFrameBuffer"/> of the surface swap chain.
    /// </summary>
    public abstract GPUFrameBuffer SwapChainFrameBuffer { get; }
    /// <summary>
    /// The <see cref="PixelFormat"/> of the surface swap chain.
    /// </summary>
    public abstract PixelFormat PrefferedSurfaceFomat { get; }
    /// <summary>
    /// The <see cref="PixelFormat"/> of the depth and stencil buffer. It might be null if the depth test not required.
    /// </summary>
    public abstract PixelFormat? PrefferedDepthStencilFormat { get; }
    /// <summary>
    /// Enable or disable the vertical synchronization. The frame rate will be limited to the refresh rate of the monitor if enabled.
    /// </summary>
    public abstract PixelFormat PrefferedHDRFormat { get; }
    public abstract bool VSync { get; set; }


    // Default samplers, those are the most common samplers used in the graphics pipeline.
    // user can also create their own samplers by using the CreateSampler method.
    /// <summary>
    /// The <see cref="GPUSampler"/> of the nearest filtering with repeat mode.
    /// </summary> 
    public abstract GPUSampler SamplerNearestRepeat { get; }
    /// <summary>
    /// The <see cref="GPUSampler"/> of the linear filtering with repeat mode.
    /// </summary>
    public abstract GPUSampler SamplerLinearRepeat { get; }
    /// <summary>
    /// The <see cref="GPUSampler"/> of the nearest filtering with clamp mode.
    /// </summary>
    public abstract GPUSampler SamplerNearestClamp { get; }
    /// <summary>
    /// The <see cref="GPUSampler"/> of the linear filtering with clamp mode.
    /// </summary>
    public abstract GPUSampler SamplerLinearClamp { get; }
    /// <summary>
    /// The <see cref="GPUSampler"/> of the nearest filtering with mirror repeat mode.
    /// </summary>
    public abstract GPUSampler SamplerNearestMirrorRepeat { get; }
    /// <summary>
    /// The <see cref="GPUSampler"/> of the linear filtering with mirror repeat mode.
    /// </summary>
    public abstract GPUSampler SamplerLinearMirrorRepeat { get; }

    // Default bind groups, those are the most common bind groups used in the graphics pipeline.
    /// <summary>
    /// The <see cref="GPUBindGroup"/> for the uniform buffer, which only contains a entry of the uniform buffer.
    /// </summary> 
    public abstract GPUBindGroup BindGroupUniformBuffer { get; }
    /// <summary>
    /// The <see cref="GPUBindGroup"/> for the storage buffer, which only contains a entry of the storage buffer.
    /// </summary>
    public abstract GPUBindGroup BindGroupStorageBuffer { get; }
    /// <summary>
    /// The <see cref="GPUBindGroup"/> for the sampled 2D texture, which contains a texture view and a sampler.
    /// </summary> 
    public abstract GPUBindGroup BindGroupTexture2DSampled { get; }
    /// <summary>
    /// The <see cref="GPUBindGroup"/> for the read-only 2D texture, which contains a texture view. Can only be used in the compute shader.
    /// </summary>
    public abstract GPUBindGroup BindGroupTexture2DRead { get; }
    /// <summary>
    /// The <see cref="GPUBindGroup"/> for the write-only 2D texture, which contains a texture view. Can only be used in the compute shader.
    /// </summary>
    public abstract GPUBindGroup BindGroupTexture2DStorage { get; }

    /// <summary>
    /// Whether the depth and stencil test enabled.
    /// </summary> 
    public bool HasDepth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => PrefferedDepthStencilFormat.HasValue;
    }


    /// <summary>
    /// Creates a GPU buffer with the descriptor.
    /// </summary>
    /// <param name="createInfo">The descriptor for the GPU buffer.</param>
    /// <returns>The created GPU buffer.</returns>
    public GPUBuffer CreateBuffer(in BufferDescriptor createInfo)
    {
        return CreateBufferCore(createInfo);
    }

    /// <summary>
    /// Creates a GPU buffer with the descriptor and initializes the buffer with the data.
    /// </summary>
    /// <param name="createInfo">The descriptor for the GPU buffer.</param>
    /// <param name="data">The initial data to write to the buffer.</param>
    /// <typeparam name="T">The type of the data to write to the buffer.</typeparam>
    /// <returns>The created GPU buffer.</returns>
    public GPUBuffer CreateBuffer<T>(in BufferDescriptor createInfo, T[] data) where T : unmanaged
    {
        GPUBuffer buffer = CreateBufferCore(createInfo);
        WriteBuffer(buffer, data);
        return buffer;
    }

    /// <summary>
    /// Creates a GPU buffer with the descriptor and initializes the buffer with the data.
    /// </summary>
    /// <param name="createInfo">The descriptor for the GPU buffer.</param>
    /// <param name="data">The initial data to write to the buffer.</param>
    /// <typeparam name="T">The type of the data to write to the buffer.</typeparam>
    /// <returns>The created GPU buffer.</returns>
    public GPUBuffer CreateBuffer<T>(in BufferDescriptor createInfo, T data) where T : unmanaged
    {
        GPUBuffer buffer = CreateBufferCore(createInfo);
        WriteBuffer(buffer, data);
        return buffer;
    }

    /// <summary>
    /// Destroys the GPU buffer.
    /// </summary>
    /// <param name="buffer">The GPU buffer to destroy.</param>
    public void DestroyBuffer(GPUBuffer buffer)
    {
        DestroyBufferCore(buffer);
    }

    /// <summary>
    /// Creates a GPU texture with the descriptor.
    /// </summary>
    /// <param name="createInfo">The descriptor for the GPU texture.</param>
    /// <returns>The created GPU texture.</returns>
    public GPUTexture CreateTexture(in TextureDescriptor createInfo)
    {
        return CreateTextureCore(createInfo);
    }

    /// <summary>
    /// Destroys the GPU texture.
    /// </summary>
    /// <param name="texture">The GPU texture to destroy.</param>
    public void DestroyTexture(GPUTexture texture)
    {
        DestroyTextureCore(texture);
    }

    /// <summary>
    /// Creates a GPU command buffer with the descriptor. The descriptor can be null.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU command buffer.</param>
    /// <returns>The created GPU command buffer.</returns>
    public GPUCommandBuffer CreateCommandBuffer(in CommandBufferDescriptor? descriptor = null)
    {
        return CreateCommandBufferCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU command buffer.
    /// </summary>
    /// <param name="commandBuffer">The GPU command buffer to destroy.</param>
    public void DestroyCommandBuffer(GPUCommandBuffer commandBuffer)
    {
        DestroyCommandBufferCore(commandBuffer);
    }

    /// <summary>
    /// Creates a GPU resuable render buffer with the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU resuable render buffer.</param>
    /// <returns>The created GPU resuable render buffer.</returns>
    public GPUResuableRenderBuffer CreateResuableRenderBuffer(in ResuableRenderBufferDescriptor? descriptor = null)
    {
        return CreateResuableRenderBufferCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU resuable render buffer.
    /// </summary>
    /// <param name="renderBuffer">The GPU resuable render buffer to destroy.</param>
    public void DestroyResuableRenderBuffer(GPUResuableRenderBuffer renderBuffer)
    {
        DestroyResuableRenderBufferCore(renderBuffer);
    }

    /// <summary>
    /// Creates a GPU render pass with the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU render pass.</param>
    /// <returns>The created GPU render pass.</returns> 
    public GPURenderPass CreateRenderPass(in RenderPassDescriptor descriptor)
    {
        return CreateRenderPassCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU render pass.
    /// </summary>
    /// <param name="renderPass">The GPU render pass to destroy.</param>
    public void DestroyRenderPass(GPURenderPass renderPass)
    {
        DestroyRenderPassCore(renderPass);
    }

    /// <summary>
    /// Creates a GPU frame buffer with the render pass, width, and height.
    /// </summary>
    /// <param name="renderPass"> The render pass of the frame buffer.</param>
    /// <param name="width"> The width of the frame buffer.</param>
    /// <param name="height"> The height of the frame buffer.</param>
    /// <param name="name"> The name of the frame buffer.</param>
    /// <returns></returns>
    public GPUFrameBuffer CreateFrameBuffer(in FrameBufferDescriptor descriptor)
    {
        return CreateFrameBufferCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU frame buffer.
    /// </summary>
    /// <param name="frameBuffer">The GPU frame buffer to destroy.</param>
    public void DestroyFrameBuffer(GPUFrameBuffer frameBuffer)
    {
        DestroyFrameBufferCore(frameBuffer);
    }

    /// <summary>
    /// Creates a GPU graphics pipeline with the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU graphics pipeline.</param>
    /// <returns>The created GPU graphics pipeline.</returns> 
    public GPUPipeline CreateGraphicsPipeline(in GraphicsPipelineDescriptor descriptor)
    {
        return CreateGraphicsPipelineCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU graphics pipeline.
    /// </summary>
    /// <param name="pipeline">The GPU graphics pipeline to destroy.</param>
    public void DestroyGraphicsPipeline(GPUPipeline pipeline)
    {
        DestroyGraphicsPipelineCore(pipeline);
    }

    /// <summary>
    /// Creates a GPU compute pipeline with the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU compute pipeline.</param>
    /// <returns>The created GPU compute pipeline.</returns>
    public GPUPipeline CreateComputePipeline(in ComputePipelineDescriptor descriptor)
    {
        return CreateComputePipelineCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU compute pipeline.
    /// </summary>
    /// <param name="pipeline">The GPU compute pipeline to destroy.</param>
    public void DestroyComputePipeline(GPUPipeline pipeline)
    {
        DestroyComputePipelineCore(pipeline);
    }

    /// <summary>
    /// Creates a GPU bind group with the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU bind group.</param>
    /// <returns>The created GPU bind group.</returns>
    public GPUBindGroup CreateBindGroup(in BindGroupDescriptor descriptor)
    {
        return CreateBindGroupCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU bind group.
    /// </summary>
    /// <param name="bindGroup">The GPU bind group to destroy.</param>
    public void DestroyBindGroup(GPUBindGroup bindGroup)
    {
        DestroyBindGroupCore(bindGroup);
    }

    /// <summary>
    /// Creates a GPU resource group with the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU resource group.</param>
    /// <returns>The created GPU resource group.</returns>
    public GPUResourceGroup CreateResourceGroup(in ResourceGroupDescriptor descriptor)
    {
        return CreateResourceGroupCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU resource group.
    /// </summary>
    /// <param name="resourceGroup">The GPU resource group to destroy.</param>
    public void DestroyResourceGroup(GPUResourceGroup resourceGroup)
    {
        DestroyResourceGroupCore(resourceGroup);
    }

    /// <summary>
    /// Creates a GPU texture view with the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU texture view.</param>
    /// <returns>The created GPU texture view.</returns>
    public GPUTextureView CreateTextureView(in TextureViewDescriptor descriptor)
    {
        return CreateTextureViewCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU texture view.
    /// </summary>
    /// <param name="textureView">The GPU texture view to destroy.</param>
    public void DestroyTextureView(GPUTextureView textureView)
    {
        DestroyTextureViewCore(textureView);
    }

    /// <summary>
    /// Creates a GPU sampler with the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor for the GPU sampler.</param>
    /// <returns>The created GPU sampler.</returns>
    public GPUSampler CreateSampler(in SamplerDescriptor descriptor)
    {
        return CreateSamplerCore(descriptor);
    }

    /// <summary>
    /// Destroys the GPU sampler.
    /// </summary>
    /// <param name="sampler">The GPU sampler to destroy.</param>
    public void DestroySampler(GPUSampler sampler)
    {
        DestroySamplerCore(sampler);
    }

    /// <summary>
    /// Resizes the surface swap chain to the width and height. The depth and stencil buffer will also be resized if enabled.
    /// </summary>
    public void ResizeSurface(uint width, uint height)
    {
        if (width <= 0 || height <= 0)
        {
            //it might be the window is minimized
            return;
        }
        ResizeSurfaceCore(width, height);
    }

    /// <summary>
    /// Submits the GPU command buffer to the GPU for execution.
    /// </summary>
    /// <param name="commandBuffer">The GPU command buffer to submit.</param>
    public void Submit(GPUCommandBuffer commandBuffer)
    {
        if (!commandBuffer.HasBuffer)
        {
            throw new GraphicsException($"Command buffer:{commandBuffer.Name} is empty, try use GPUCommandBuffer.Begin() and GPUCommandBuffer.End() to record commands.");
        }

        SubmitCore(commandBuffer);
    }

    /// <summary>
    /// Submits the GPU resuable render buffer to the GPU for execution.
    /// </summary>
    /// <param name="commandBuffer">The GPU resuable render buffer to submit.</param>
    public void Submit(GPUResuableRenderBuffer commandBuffer)
    {
        if (!commandBuffer.HasBuffer)
        {
            throw new GraphicsException($"Reuseable render buffer:{commandBuffer.Name} is empty, try use GPUResuableRenderBuffer.Begin() and GPUResuableRenderBuffer.End() to record commands.");
        }

        SubmitCore(commandBuffer);
    }

    /// <summary>
    /// Swaps the front and back buffers of the surface swap chain.
    /// </summary>
    public void SwapBuffers()
    {
        SwapBuffersCore();
    }

    /// <summary>
    /// Writes the data to the GPU buffer at the offset.
    /// </summary>
    /// <param name="buffer">The target GPU buffer.</param>
    /// <param name="bufferOffset">The offset in the GPU buffer. (unit: byte)</param>
    /// <param name="data">The pointer to the data.</param>
    /// <param name="size">The size of the data. (unit: byte)</param>
    public unsafe void WriteBuffer(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        WriteBufferCore(buffer, bufferOffset, data, size);
    }

    /// <summary>
    /// Writes the data to the GPU texture at the mip level.
    /// </summary>
    /// <param name="texture">The target GPU texture.</param>
    /// <param name="data">The pointer to the data.</param>
    /// <param name="dataSize">The size of the data. (unit: byte)</param>
    /// <param name="pixelSize">The size of the pixel in the texture. (unit: byte).<br/>For example, if the texture is R8G8B8A8_UNORM, the pixel size is 4.</param>
    /// <param name="mipLevel">The mip level of the texture.</param>
    public unsafe void WriteTexture(GPUTexture texture, byte* data, uint dataSize, uint pixelSize, uint mipLevel = 0)
    {
        WriteTextureCore(texture, data, dataSize, pixelSize, mipLevel);
    }

    // polymorphism

    /// <summary>
    /// Writes the data to the GPU buffer at the offset 0.
    /// </summary>
    /// <param name="buffer">The target GPU buffer.</param>
    /// <param name="data">The pointer to the data.</param>
    /// <param name="size">The size of the data. (unit: byte)</param>
    public unsafe void WriteBuffer(GPUBuffer buffer, byte* data, uint size)
    {
        WriteBuffer(buffer, 0, data, size);
    }

    /// <summary>
    /// Writes the data to the GPU buffer at the offset.
    /// </summary>
    /// <param name="buffer">The target GPU buffer.</param>
    /// <param name="bufferOffset">The offset in the GPU buffer. (unit: byte)</param>
    /// <param name="data">The data to write to the buffer.</param>
    /// <typeparam name="T">The type of the data to write to the buffer.</typeparam>
    public unsafe void WriteBuffer<T>(GPUBuffer buffer, uint bufferOffset, T data) where T : unmanaged
    {
        WriteBuffer(buffer, bufferOffset, (byte*)&data, (uint)sizeof(T));
    }

    /// <summary>
    /// = Writes the data to the GPU buffer at the offset 0.
    /// </summary>
    /// <param name="buffer">The target GPU buffer.</param>
    /// <param name="data">The data to write to the buffer.</param>
    /// <typeparam name="T">The type of the data.</typeparam>
    public unsafe void WriteBuffer<T>(GPUBuffer buffer, T data) where T : unmanaged
    {
        WriteBuffer(buffer, 0, (byte*)&data, (uint)sizeof(T));
    }

    /// <summary>
    /// Writes the data to the GPU buffer at the offset.
    /// </summary>
    /// <param name="buffer">The target GPU buffer.</param>
    /// <param name="bufferOffset">The offset in the GPU buffer. (unit: byte)</param>
    /// <param name="data">The array data to write to the buffer.</param>
    /// <typeparam name="T">The type of the data.</typeparam>
    public unsafe void WriteBuffer<T>(GPUBuffer buffer, uint bufferOffset, T[] data) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            WriteBuffer(buffer, bufferOffset, (byte*)ptr, (uint)(sizeof(T) * data.Length));
        }
    }

    /// <summary>
    /// Writes the data to the GPU buffer at the offset 0.
    /// </summary>
    /// <param name="buffer">The target GPU buffer.</param>
    /// <param name="data">The array data to write to the buffer.</param>
    /// <typeparam name="T">The type of the data.</typeparam>
    public unsafe void WriteBuffer<T>(GPUBuffer buffer, T[] data) where T : unmanaged
    {
        WriteBuffer(buffer, 0, data);
    }

    /// <summary>
    /// Writes the data to the GPU texture at the mip level.
    /// </summary>
    /// <param name="texture">The target GPU texture.</param>
    /// <param name="data">The data to write to the texture.</param>
    /// <param name="mipLevel">The target mip level of the texture.</param>
    /// <typeparam name="TColor">The type of the color data. For example: The <see cref="Color32"/> represents the R8G8B8A8_UNORM format</typeparam>
    public unsafe void WriteTexture<TColor>(GPUTexture texture, TColor[] data, uint mipLevel = 0) where TColor : unmanaged
    {
        fixed (TColor* ptr = data)
        {
            WriteTexture(texture, (byte*)ptr, (uint)(sizeof(TColor) * data.Length), (uint)sizeof(TColor), mipLevel);
        }
    }

    /// <exclude />
    protected abstract GPUBuffer CreateBufferCore(in BufferDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroyBufferCore(GPUBuffer buffer);

    /// <exclude />
    protected abstract GPUTexture CreateTextureCore(in TextureDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroyTextureCore(GPUTexture texture);

    /// <exclude />
    protected abstract GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null);
    /// <exclude />
    protected abstract void DestroyCommandBufferCore(GPUCommandBuffer commandBuffer);

    /// <exclude />
    protected abstract GPUResuableRenderBuffer CreateResuableRenderBufferCore(in ResuableRenderBufferDescriptor? descriptor);
    /// <exclude />
    protected abstract void DestroyResuableRenderBufferCore(GPUResuableRenderBuffer renderBuffer);

    /// <exclude />
    protected abstract GPURenderPass CreateRenderPassCore(in RenderPassDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroyRenderPassCore(GPURenderPass renderPass);

    /// <exclude />
    protected abstract GPUFrameBuffer CreateFrameBufferCore(in FrameBufferDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroyFrameBufferCore(GPUFrameBuffer frameBuffer);

    /// <exclude />
    protected abstract GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroyGraphicsPipelineCore(GPUPipeline pipeline);

    /// <exclude />
    protected abstract GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroyComputePipelineCore(GPUPipeline pipeline);

    /// <exclude />
    protected abstract GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroyBindGroupCore(GPUBindGroup bindGroup);

    /// <exclude />
    protected abstract GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroyResourceGroupCore(GPUResourceGroup resourceGroup);

    /// <exclude />
    protected abstract GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroyTextureViewCore(GPUTextureView textureView);

    /// <exclude />
    protected abstract GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor);
    /// <exclude />
    protected abstract void DestroySamplerCore(GPUSampler sampler);

    /// <exclude />
    protected abstract void ResizeSurfaceCore(uint width, uint height);

    /// <exclude />
    protected abstract void SubmitCore(GPUCommandBuffer commandBuffer);
    
    /// <exclude />
    protected abstract void SubmitCore(GPUResuableRenderBuffer renderBuffer);

    /// <exclude />
    protected abstract void SwapBuffersCore();

    // Do not store the fucking pointer when implementing, it is unsafe;<br/> Try only read data from it.
    /// <exclude />
    protected abstract unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size);

    /// <exclude />
    protected abstract unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint pixelSize, uint mipLevel);
}