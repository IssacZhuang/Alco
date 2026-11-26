namespace Alco.Graphics.LowLevel;

/// <summary>Hardware-level escape hatches over high-level <see cref="Alco.Graphics"/> resources and scopes.</summary>
public static class LowLevelGraphicsExtensions
{
    /// <summary>Gets the underlying backing <see cref="GPURange"/> of <paramref name="buffer"/>.</summary>
    public static GPURange GetRange(this Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.LowLevelRange;
    }

    /// <summary>Gets the underlying backing <see cref="GPURange"/> of <paramref name="slice"/>.</summary>
    public static GPURange GetRange(this BufferSlice slice) => slice.LowLevelRange;

    /// <summary>Gets the underlying placed <see cref="GPUTexture"/> of <paramref name="texture"/>.</summary>
    public static GPUTexture GetNativeTexture(this Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return texture.LowLevelTexture;
    }

    /// <summary>Gets the mip-zero, slice-zero <see cref="GPURenderView"/> of <paramref name="texture"/>.</summary>
    public static GPURenderView GetRenderView(this Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return texture.LowLevelRenderView;
    }

    /// <summary>Gets the underlying <see cref="GPURenderView"/> of <paramref name="target"/>, or null if invalid.</summary>
    public static GPURenderView? GetRenderView(this RenderTarget target) => target.View;

    /// <summary>Wraps a raw <see cref="GPURenderView"/> as a <see cref="RenderTarget"/>.</summary>
    public static RenderTarget FromRenderView(GPURenderView view, Extent2D extent = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new RenderTarget(view, null, extent);
    }

    /// <summary>Wraps an acquired <see cref="SwapchainFrame"/> as a <see cref="RenderGraphTarget"/>.</summary>
    public static RenderGraphTarget ToRenderGraphTarget(this SwapchainFrame frame, Format format) =>
        RenderGraphTarget.FromSwapchain(frame, format);

    /// <summary>Gets the underlying <see cref="GPURenderView"/> of <paramref name="target"/>, or null if invalid.</summary>
    public static GPURenderView? GetRenderView(this RenderGraphTarget target) => target.RenderView;

    /// <summary>Gets the underlying <see cref="GPUTexture"/> of <paramref name="target"/>, or null if invalid.</summary>
    public static GPUTexture? GetNativeTexture(this RenderGraphTarget target) => target.NativeTexture;

    /// <summary>Resolves a render-graph texture handle (including output) to its mip-zero <see cref="GPURenderView"/>.</summary>
    public static GPURenderView GetRenderView(this RenderGraphContext context, RGTexture handle) =>
        context.GetRenderViewInternal(handle);

    /// <summary>Resolves a render-graph texture handle (including output) to its native <see cref="GPUTexture"/>.</summary>
    public static GPUTexture GetNativeTexture(this RenderGraphContext context, RGTexture handle) =>
        context.GetNativeTextureInternal(handle);

    /// <summary>Converts a compiled stage into a <see cref="ShaderStage"/> descriptor.</summary>
    public static ShaderStage ToGpuStage(this CompiledShaderStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return stage.ToGpuStage();
    }

    /// <summary>Converts a unique stage kind into a <see cref="ShaderStage"/> descriptor.</summary>
    public static ShaderStage GetGpuStage(this CompiledShader shader, ShaderStageKind stage)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return shader.GetGpuStage(stage);
    }

    /// <summary>Converts a named stage into a <see cref="ShaderStage"/> descriptor.</summary>
    public static ShaderStage GetGpuStage(this CompiledShader shader, string sourceEntryName)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return shader.GetGpuStage(sourceEntryName);
    }

    /// <summary>Attempts to convert a unique optional stage into a <see cref="ShaderStage"/> descriptor.</summary>
    public static bool TryGetGpuStage(this CompiledShader shader, ShaderStageKind stage, out ShaderStage gpuStage)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return shader.TryGetGpuStage(stage, out gpuStage);
    }

    /// <summary>Gets the owning <see cref="GPUDevice"/> of <paramref name="system"/>.</summary>
    public static GPUDevice GetDevice(this RenderingSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.Device;
    }

    /// <summary>Gets the bindless texture descriptor heap of <paramref name="system"/>.</summary>
    public static GPUTextureDescriptorHeap GetTextureDescriptorHeap(this RenderingSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.TextureDescriptorHeap;
    }

    /// <summary>Gets the bindless sampler descriptor heap of <paramref name="system"/>.</summary>
    public static GPUSamplerDescriptorHeap GetSamplerDescriptorHeap(this RenderingSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.SamplerDescriptorHeap;
    }

    /// <summary>Flushes uploads and returns the queue-zero upload completion timeline point.</summary>
    public static TimelinePoint FlushUploadsWithCompletion(this RenderingSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.FlushUploadsInternal();
    }

    /// <summary>Flushes pending texture initializations and returns the queue-zero upload completion timeline point.</summary>
    public static TimelinePoint FlushInitializationsWithCompletion(this RenderingSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.FlushUploadsInternal();
    }

    /// <summary>Submits active context frames with explicit timeline waits and returns the completion point.</summary>
    public static TimelinePoint Submit(this RenderingSystem system, scoped ReadOnlySpan<RenderContext> contexts,
        scoped ReadOnlySpan<TimelinePoint> waits)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.SubmitInternal(contexts, waits);
    }

    /// <summary>Submits active context frames with optional waits and presents the specified swapchain.</summary>
    public static TimelinePoint SubmitAndPresent(this RenderingSystem system, scoped ReadOnlySpan<RenderContext> contexts,
        GPUSwapchain swapchain, scoped ReadOnlySpan<TimelinePoint> waits = default)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.SubmitAndPresentInternal(contexts, swapchain, waits);
    }

    /// <summary>Gets the underlying low-level <see cref="BumpAllocator"/> for the current frame.</summary>
    public static BumpAllocator GetFrameData(this RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.FrameData;
    }

    /// <summary>Allocates and writes a root struct into frame storage and returns its <see cref="GPURange"/>.</summary>
    public static GPURange AllocateRootRange<TRoot>(this RenderContext context, in TRoot value) where TRoot : unmanaged
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.AllocateRootRange(in value);
    }

    /// <summary>Acquires a swapchain image on queue zero for presentation.</summary>
    public static SwapchainFrame Acquire(this RenderContext context, GPUSwapchain swapchain)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.AcquireInternal(swapchain);
    }

    /// <summary>Ends frame recording for caller-managed submission.</summary>
    public static GPUCommandBuffer EndFrame(this RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.EndFrameInternal();
    }

    /// <summary>Registers a caller-submitted completion point covering the active frame.</summary>
    public static void MarkSubmitted(this RenderContext context, TimelinePoint completion)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.MarkSubmittedInternal(completion);
    }

    /// <summary>Submits the active frame with explicit timeline waits and returns the completion point.</summary>
    public static TimelinePoint Submit(this RenderContext context, scoped ReadOnlySpan<TimelinePoint> waits)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.SubmitInternal(waits);
    }

    /// <summary>Submits the active frame and returns the completion point.</summary>
    public static TimelinePoint SubmitWithCompletion(this RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.SubmitInternal();
    }

    /// <summary>Submits the active frame and presents the specified swapchain image.</summary>
    public static TimelinePoint SubmitAndPresent(this RenderContext context, GPUSwapchain swapchain,
        scoped ReadOnlySpan<TimelinePoint> waits = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.SubmitAndPresentInternal(swapchain, waits);
    }

    /// <summary>Begins a render pass using low-level attachment descriptors.</summary>
    public static RenderContext.RenderScope BeginRenderPass(this RenderContext context, scoped GPURenderingDesc desc)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.BeginRenderPassInternal(desc);
    }

    /// <summary>Begins a render pass using low-level attachment descriptors and an explicit signature.</summary>
    public static RenderContext.RenderScope BeginRenderPass(this RenderContext context, scoped GPURenderingDesc desc,
        RenderPassSignature signature)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.BeginRenderPassInternal(desc, signature);
    }

    /// <summary>Begins a render pass using low-level attachment descriptors and explicit formats.</summary>
    public static RenderContext.RenderScope BeginRenderPass(this RenderContext context, scoped GPURenderingDesc desc,
        scoped ReadOnlySpan<Format> colorFormats, Format depthFormat = Format.Undefined, Format stencilFormat = Format.Undefined)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.BeginRenderPassInternal(desc, colorFormats, depthFormat, stencilFormat);
    }

    /// <summary>Begins a prepared render pass for caller-owned pipeline and descriptor heaps.</summary>
    public static RenderContext.RenderScope BeginPreparedRenderPass(this RenderContext context, scoped GPURenderingDesc desc,
        GPUPipeline pipeline, GPUTextureDescriptorHeap textures, GPUSamplerDescriptorHeap samplers)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.BeginPreparedRenderPassInternal(desc, pipeline, textures, samplers);
    }

    /// <summary>Records a low-level buffer memory copy between scopes.</summary>
    public static void CopyMemory(this RenderContext context, GPURange source, GPURange destination)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CopyMemoryInternal(source, destination);
    }

    /// <summary>Records a low-level memory-to-texture copy between scopes.</summary>
    public static void CopyMemoryToTexture(this RenderContext context, GPURange source, GPUTexture destination,
        TextureCopyDesc copy = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CopyMemoryToTextureInternal(source, destination, copy);
    }

    /// <summary>Records a low-level texture-to-memory copy between scopes.</summary>
    public static void CopyTextureToMemory(this RenderContext context, GPUTexture source, GPURange destination,
        TextureCopyDesc copy = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CopyTextureToMemoryInternal(source, destination, copy);
    }

    /// <summary>Records a low-level texture-to-texture copy between scopes.</summary>
    public static void CopyTexture(this RenderContext context, GPUTexture source, GPUTexture destination,
        TextureRegionCopyDesc copy = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CopyTextureInternal(source, destination, copy);
    }

    /// <summary>Records a draw against a pre-bound pipeline in a prepared render pass.</summary>
    public static void Draw(this RenderContext.RenderScope scope, GPURange root, uint vertexCount) =>
        scope.DrawLowLevel(root, vertexCount);

    /// <summary>Records a draw with an explicit prepacked <see cref="GPURange"/> root.</summary>
    public static void Draw(this RenderContext.RenderScope scope, Material material, GPURange root,
        DepthStencilState depthStencil, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) =>
        scope.DrawLowLevel(material, root, depthStencil, vertexCount, instanceCount, firstVertex, firstInstance);

    /// <summary>Records an indexed draw with an explicit prepacked <see cref="GPURange"/> root.</summary>
    public static void DrawIndexed(this RenderContext.RenderScope scope, Material material, GPURange root,
        DepthStencilState depthStencil, Buffer indices, IndexType type, uint indexCount, uint instanceCount = 1,
        uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0, ulong byteOffset = 0) =>
        scope.DrawIndexedLowLevel(material, root, depthStencil, indices, type, indexCount, instanceCount,
            firstIndex, vertexOffset, firstInstance, byteOffset);

    /// <summary>Records an indirect draw with an explicit prepacked <see cref="GPURange"/> root.</summary>
    public static void DrawIndirect(this RenderContext.RenderScope scope, Material material, GPURange root,
        DepthStencilState depthStencil, Buffer arguments, uint drawCount = 1, uint stride = 0, ulong argumentByteOffset = 0) =>
        scope.DrawIndirectLowLevel(material, root, depthStencil, arguments, drawCount, stride, argumentByteOffset);

    /// <summary>Records an indexed indirect draw with an explicit prepacked <see cref="GPURange"/> root.</summary>
    public static void DrawIndexedIndirect(this RenderContext.RenderScope scope, Material material, GPURange root,
        DepthStencilState depthStencil, Buffer indices, IndexType type, Buffer arguments,
        uint drawCount = 1, uint stride = 0, ulong argumentByteOffset = 0, ulong indexByteOffset = 0) =>
        scope.DrawIndexedIndirectLowLevel(material, root, depthStencil, indices, type, arguments,
            drawCount, stride, argumentByteOffset, indexByteOffset);

    /// <summary>Records an indexed indirect draw with explicit low-level <see cref="GPURange"/> root, index, and argument spans.</summary>
    public static void DrawIndexedIndirect(this RenderContext.RenderScope scope, Material material, GPURange root,
        DepthStencilState depthStencil, GPURange indices, IndexType type, GPURange arguments,
        uint drawCount = 1, uint stride = 0) =>
        scope.DrawIndexedIndirectLowLevel(material, root, depthStencil, indices, type, arguments, drawCount, stride);

    /// <summary>Records a compute dispatch with an explicit prepacked <see cref="GPURange"/> root.</summary>
    public static void Dispatch(this RenderContext.ComputeScope scope, Material material, GPURange root, Extent3D groupCount) =>
        scope.DispatchLowLevel(material, root, groupCount);

    /// <summary>Records a compute dispatch directly for a shader with an explicit prepacked <see cref="GPURange"/> root.</summary>
    public static void Dispatch(this RenderContext.ComputeScope scope, Shader shader, GPURange root, Extent3D groupCount) =>
        scope.DispatchLowLevel(shader, root, groupCount);

    /// <summary>Records an indirect compute dispatch with an explicit prepacked <see cref="GPURange"/> root.</summary>
    public static void DispatchIndirect(this RenderContext.ComputeScope scope, Material material, GPURange root,
        Buffer arguments, ulong byteOffset = 0) =>
        scope.DispatchIndirectLowLevel(material, root, arguments, byteOffset);

    /// <summary>Records an indirect compute dispatch directly for a shader with an explicit prepacked <see cref="GPURange"/> root.</summary>
    public static void DispatchIndirect(this RenderContext.ComputeScope scope, Shader shader, GPURange root,
        Buffer arguments, ulong byteOffset = 0) =>
        scope.DispatchIndirectLowLevel(shader, root, arguments, byteOffset);

    /// <summary>Gets or creates the shared pipeline for a material and render-pass signature.</summary>
    public static GPUPipeline GetPipeline(this Material material, RenderPassSignature pass)
    {
        ArgumentNullException.ThrowIfNull(material);
        return material.GetPipeline(pass);
    }

    /// <summary>Gets or creates the shared pipeline for a material and explicit attachment formats.</summary>
    public static GPUPipeline GetPipeline(this Material material, ReadOnlySpan<Format> colorFormats, Format depthFormat,
        Format stencilFormat = Format.Undefined, uint sampleCount = 1)
    {
        ArgumentNullException.ThrowIfNull(material);
        return material.GetPipeline(colorFormats, depthFormat, stencilFormat, sampleCount);
    }

    /// <summary>Copies resolved material root parameters into a low-level <see cref="BumpAllocator"/>.</summary>
    public static GPUAllocation<byte> AllocateRoot(this Material material, BumpAllocator frameData)
    {
        ArgumentNullException.ThrowIfNull(material);
        return material.AllocateRoot(frameData);
    }

    /// <summary>Gets or compiles the compute pipeline for a shader and optional specialization arguments.</summary>
    public static GPUPipeline GetComputePipeline(this Shader shader, params ReadOnlySpan<object> specializations)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return shader.GetComputePipeline(specializations);
    }

    /// <summary>Gets or compiles a shared raster pipeline for a shader and render-pass signature.</summary>
    public static GPUPipeline GetPipeline(this Shader shader, in PipelineState state, RenderPassSignature pass)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return shader.GetPipeline(in state, pass);
    }

    /// <summary>Gets or compiles a shared raster pipeline for a shader specialization and render-pass signature.</summary>
    public static GPUPipeline GetPipeline(this Shader shader, in PipelineState state, RenderPassSignature pass,
        params ReadOnlySpan<object> specializations)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return shader.GetPipeline(in state, pass, specializations);
    }

    /// <summary>Gets or compiles a shared raster pipeline for a shader and explicit attachment formats.</summary>
    public static GPUPipeline GetPipeline(this Shader shader, in PipelineState state, ReadOnlySpan<Format> colorFormats,
        Format depthFormat, Format stencilFormat)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return shader.GetPipeline(in state, colorFormats, depthFormat, stencilFormat);
    }

    /// <summary>Gets or compiles a shared raster pipeline for a shader specialization and explicit formats.</summary>
    public static GPUPipeline GetPipeline(this Shader shader, in PipelineState state, ReadOnlySpan<Format> colorFormats,
        Format depthFormat, Format stencilFormat, params ReadOnlySpan<object> specializations)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return shader.GetPipeline(in state, colorFormats, depthFormat, stencilFormat, specializations);
    }
}
