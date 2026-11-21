// Public resource contracts and shared state; device creation selects the platform implementation.

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

/// <summary>A GPU device that creates resources and submits work.</summary>
/// <remarks>
/// Independent resources may be created and destroyed concurrently on the same device.
/// Externally synchronize each queue and each command pool, including all command buffers allocated from that pool.
/// </remarks>
public abstract partial class GPUDevice
{
    internal DeviceCaps Capabilities = new();
    internal uint QueueCount;
    internal uint TimestampQueryCount;

    private protected GPUDevice(GraphicsBackend activeBackend) => ActiveBackend = activeBackend;

    /// <summary>The backend that created this device; never <see cref="GraphicsBackend.Auto"/>.</summary>
    public GraphicsBackend ActiveBackend { get; }

    /// <summary>Releases the device after its resources have been destroyed and all submitted work has completed.</summary>
    /// <remarks>Requires exclusive access; no other calls on this device or its resources may overlap destruction.</remarks>
    public abstract void Destroy();

    /// <summary>Allocates a linear GPU heap on this device.</summary>
    /// <param name="size">The heap size in bytes.</param>
    /// <param name="memory">The heap's memory type.</param>
    /// <returns>The allocated heap.</returns>
    /// <exception cref="GPUException">The heap could not be allocated or mapped.</exception>
    public abstract GPUHeap AllocateGPUHeap(ulong size, MemoryType memory);

    /// <summary>Immutable device capabilities.</summary>
    public DeviceCaps Caps => Capabilities;

    /// <summary>Returns whether textures of the given format and usage can be created.</summary>
    /// <param name="format">The texture format.</param>
    /// <param name="usage">The intended texture usage.</param>
    /// <returns>Whether the format supports the requested usage.</returns>
    public abstract bool SupportsTextureFormat(Format format, TextureUsage usage);

    /// <summary>Creates an independent swapchain for a native window. The device must not be headless.</summary>
    /// <param name="desc">The native surface and presentation configuration.</param>
    /// <returns>The swapchain.</returns>
    /// <exception cref="GPUException">The surface or presentation resources could not be created.</exception>
    public abstract GPUSwapchain CreateSwapchain(in SwapchainDesc desc);

    /// <summary>Blocks until every submitted command has completed.</summary>
    /// <remarks>Externally synchronize host access to every queue for the duration of this call.</remarks>
    public abstract void WaitIdle();

    /// <summary>Creates a timeline semaphore.</summary>
    /// <param name="initialValue">The initial completed value.</param>
    /// <returns>The semaphore.</returns>
    /// <exception cref="GPUException">The semaphore could not be created.</exception>
    public abstract GPUTimelineSemaphore CreateTimelineSemaphore(ulong initialValue = 0);

    /// <summary>Allocates a linear GPU heap.</summary>
    /// <param name="byteCount">The heap size in bytes.</param>
    /// <param name="memory">The heap memory type.</param>
    /// <returns>The heap.</returns>
    /// <exception cref="GPUException">The heap could not be allocated or mapped.</exception>
    public GPUHeap CreateGPUHeap(ulong byteCount, MemoryType memory = MemoryType.CpuVisible) => AllocateGPUHeap(byteCount, memory);

    /// <summary>Allocates a dedicated texture-placement heap.</summary>
    /// <param name="byteCount">The heap size in bytes.</param>
    /// <returns>The heap.</returns>
    /// <exception cref="GPUException">The heap could not be allocated.</exception>
    public abstract GPUTextureHeap CreateTextureHeap(ulong byteCount);

    /// <summary>Size and placement alignment a texture occupies in a texture heap.</summary>
    /// <param name="desc">The texture description.</param>
    /// <returns>The required size and placement alignment, or zero for an unsupported texture.</returns>
    public abstract SizeAlign GetTextureSizeAlign(TextureDesc desc);

    /// <summary>Places a texture in a heap.</summary>
    /// <param name="desc">The texture description.</param>
    /// <param name="heap">The backing texture heap.</param>
    /// <param name="offset">The aligned placement offset in bytes.</param>
    /// <returns>The texture.</returns>
    /// <exception cref="GPUException">The texture could not be created or bound to the heap.</exception>
    public abstract GPUTexture CreateTexture(TextureDesc desc, GPUTextureHeap heap, ulong offset);

    /// <summary>Creates a bindless texture descriptor heap.</summary>
    /// <param name="capacity">The number of descriptor slots.</param>
    /// <returns>The descriptor heap.</returns>
    /// <exception cref="GPUException">The descriptor heap could not be created.</exception>
    public abstract GPUTextureDescriptorHeap CreateTextureDescriptorHeap(uint capacity);

    /// <summary>Creates a bindless texture descriptor heap with the device's standard bindless capacity.</summary>
    /// <returns>The descriptor heap; <see cref="GPUTextureDescriptorHeap.Capacity"/> reports the slot count.</returns>
    /// <exception cref="GPUException">The descriptor heap could not be created.</exception>
    public GPUTextureDescriptorHeap CreateTextureDescriptorHeap() => CreateTextureDescriptorHeap(Capabilities.TextureDescriptorHeapCapacity);

    /// <summary>Creates a sampler descriptor heap.</summary>
    /// <param name="capacity">The number of descriptor slots.</param>
    /// <returns>The descriptor heap.</returns>
    /// <exception cref="GPUException">The descriptor heap could not be created.</exception>
    public abstract GPUSamplerDescriptorHeap CreateSamplerDescriptorHeap(uint capacity);

    /// <summary>Creates a sampler descriptor heap with the device's standard bindless capacity.</summary>
    /// <returns>The descriptor heap; <see cref="GPUSamplerDescriptorHeap.Capacity"/> reports the slot count.</returns>
    /// <exception cref="GPUException">The descriptor heap could not be created.</exception>
    public GPUSamplerDescriptorHeap CreateSamplerDescriptorHeap() => CreateSamplerDescriptorHeap(Capabilities.SamplerDescriptorHeapCapacity);

    /// <summary>Builds a vertex/pixel pipeline.</summary>
    /// <param name="desc">The graphics pipeline description.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="GPUException">The pipeline could not be created.</exception>
    public abstract GPUPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc);

    /// <summary>Builds a task/mesh/pixel pipeline.</summary>
    /// <param name="desc">The mesh pipeline description.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="GPUException">The pipeline could not be created.</exception>
    public abstract GPUPipeline CreateMeshPipeline(MeshPipelineDesc desc);

    /// <summary>Builds a compute pipeline.</summary>
    /// <param name="compute">The compute shader and its threadgroup dimensions.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="GPUException">The pipeline could not be created.</exception>
    public abstract GPUPipeline CreateComputePipeline(ShaderStage compute);

    /// <summary>Creates a command pool for the given queue.</summary>
    /// <param name="queueIndex">The queue the pool records commands for.</param>
    /// <returns>The command pool.</returns>
    /// <exception cref="GPUException">The command pool could not be created.</exception>
    public abstract GPUCommandPool CreateCommandPool(uint queueIndex);

    /// <summary>Submits recorded command buffers without presenting.</summary>
    /// <param name="desc">The command buffers, waits, and completion point.</param>
    /// <param name="queueIndex">The submission queue index.</param>
    public abstract void Submit(SubmitDesc desc, uint queueIndex = 0);

    /// <summary>Submits work on queue zero and presents the specified swapchain's acquired image.</summary>
    /// <param name="desc">The command buffers, waits, and required completion point; commands may be empty.</param>
    /// <param name="swapchain">The swapchain to present after all prior uses of its acquired image.</param>
    public abstract void SubmitAndPresent(SubmitDesc desc, GPUSwapchain swapchain);
}

/// <summary>Creates GPU devices and exposes backend-independent format information and diagnostics.</summary>
public static unsafe partial class Gpu
{
    /// <summary>Creates a device using the requested backend or the <see cref="GraphicsBackend.Auto"/> policy.</summary>
    /// <param name="desc">The device configuration; presentation is available unless Headless is set.</param>
    /// <returns>The initialized device with its resolved backend.</returns>
    /// <exception cref="GPUException">The backend selection or operating system is unsupported, or initialization failed.</exception>
    public static GPUDevice CreateDevice(DeviceDesc desc)
    {
        GraphicsBackend backend = desc.Backend;
        if (backend == GraphicsBackend.Auto)
        {
            string? setting = Environment.GetEnvironmentVariable("ALCO_GRAPHICS_BACKEND");
            backend = setting switch
            {
                null when OperatingSystem.IsWindows() => GraphicsBackend.Dx12,
                null when OperatingSystem.IsMacOS() => GraphicsBackend.Metal,
                null => GraphicsBackend.Vulkan,
                "vulkan" => GraphicsBackend.Vulkan,
                "vulkan-compact" => GraphicsBackend.VulkanCompact,
                "dx12" => GraphicsBackend.Dx12,
                "metal" => GraphicsBackend.Metal,
                "nogpu" => GraphicsBackend.NoGPU,
                _ => throw new GPUException(Error.Unsupported,
                    $"Invalid ALCO_GRAPHICS_BACKEND '{setting}'; expected vulkan, vulkan-compact, metal, dx12, or nogpu."),
            };
        }
        return backend switch
        {
            GraphicsBackend.Vulkan when OperatingSystem.IsWindows() || OperatingSystem.IsLinux() => VulkanDevice.Create(desc),
            GraphicsBackend.VulkanCompact when OperatingSystem.IsWindows() || OperatingSystem.IsLinux() => VulkanCompactDevice.Create(desc),
            GraphicsBackend.Dx12 when OperatingSystem.IsWindows() => D3D12Device.Create(desc),
            GraphicsBackend.Metal when OperatingSystem.IsMacOS() => MetalDevice.Create(desc),
            GraphicsBackend.NoGPU => NoGPUDevice.Create(desc),
            GraphicsBackend.Vulkan or GraphicsBackend.VulkanCompact or GraphicsBackend.Dx12 or GraphicsBackend.Metal =>
                throw new GPUException(Error.Unsupported, $"The '{backend}' backend is unsupported on this operating system."),
            _ => throw new GPUException(Error.Unsupported, $"Invalid graphics backend '{backend}'."),
        };
    }
}

// Lifetime: destroy each object exactly once, after all CPU calls and recorded or executing GPU uses of it have ended.
/// <summary>A window's presentation images. Reconfigure or destroy after recorded and executing uses end, with queue zero externally synchronized.</summary>
/// <remarks>Serialize access to this swapchain, including acquisition, presentation, reconfiguration, and destruction.</remarks>
public abstract class GPUSwapchain
{
    private protected GPUSwapchain() { }

    /// <summary>The current drawable extent in framebuffer pixels; zero while suspended.</summary>
    public abstract Extent2D DrawableExtent { get; }

    /// <summary>The actual presentation format.</summary>
    public abstract Format Format { get; }

    /// <summary>The actual presentation color space.</summary>
    public abstract ColorSpace ColorSpace { get; }

    /// <summary>Whether presentation is synchronized to the display refresh.</summary>
    public abstract bool VSync { get; }

    /// <summary>Cancels any acquired frame and changes the drawable extent.</summary>
    /// <param name="extent">The framebuffer size in pixels; a zero dimension suspends presentation.</param>
    /// <param name="error">None on success, or the reason the swapchain could not be reconfigured.</param>
    /// <returns>true if the swapchain was reconfigured; otherwise false.</returns>
    public abstract bool TryResize(Extent2D extent, out Error error);

    /// <summary>Cancels any acquired frame and changes the presentation format and color space.</summary>
    /// <param name="format">The presentation format; Undefined selects a platform default.</param>
    /// <param name="colorSpace">The presentation color space.</param>
    /// <param name="error">None on success, or the reason the configuration is unsupported.</param>
    /// <returns>true if the configuration was applied; otherwise false.</returns>
    public abstract bool TrySetFormat(Format format, ColorSpace colorSpace, out Error error);

    /// <summary>Cancels any acquired frame and changes display synchronization without silently selecting another mode.</summary>
    /// <param name="enabled">Whether to synchronize presentation to the display refresh.</param>
    /// <param name="error">None on success, or the reason the requested mode is unsupported.</param>
    /// <returns>true if the mode was applied; otherwise false.</returns>
    public abstract bool TrySetVSync(bool enabled, out Error error);

    /// <summary>Releases this swapchain and its pending presentation resources; the native window must remain alive until this returns.</summary>
    public abstract void Destroy();
}

/// <summary>A GPU timeline used to signal and wait for completion values.</summary>
/// <remarks>Completion queries and waits may run concurrently.</remarks>
public abstract partial class GPUTimelineSemaphore
{
    private protected GPUTimelineSemaphore() { }

    internal abstract void Wait(ulong value);

    /// <summary>The latest completed value signaled by the GPU.</summary>
    public abstract ulong CompletedValue { get; }

    /// <summary>Releases the semaphore.</summary>
    public abstract void Destroy();
}

/// <summary>Owns reusable command storage for one queue family.</summary>
/// <remarks>Externally synchronize this pool and all its command buffers; independent pools may be used concurrently.</remarks>
public abstract partial class GPUCommandPool
{
    private protected GPUCommandPool() { }

    /// <summary>Begins recording a primary command buffer; end it with <see cref="GPUCommandBuffer.EndCommands"/>.</summary>
    /// <returns>The recording command buffer.</returns>
    /// <exception cref="GPUException">Command storage could not be created or recording could not begin.</exception>
    public abstract GPUCommandBuffer BeginCommands();

    /// <summary>Recycles every command buffer allocated from this pool.</summary>
    public abstract void Reset();

    /// <summary>Collects completed timestamp results for the pool's command buffers.</summary>
    public abstract void ReadTimestamps();

    /// <summary>Releases the pool and its command buffers.</summary>
    public abstract void Destroy();
}

/// <summary>A heap-placed texture or a borrowed swapchain image supporting attachment and transfer access.</summary>
public abstract partial class GPUTexture
{
    private protected GPUTexture() { }

    /// <summary>Creates an owned render-target view; destroy views of swapchain images before swapchain reconfiguration or destruction.</summary>
    /// <param name="desc">The attachment mip level and slice.</param>
    /// <returns>The caller-owned render view.</returns>
    /// <exception cref="GPUException">The render view could not be created.</exception>
    public abstract GPURenderView CreateRenderView(RenderViewDesc desc);

    /// <summary>Releases an owned texture after every view and GPU use have finished; borrowed swapchain textures must not be destroyed.</summary>
    public abstract void Destroy();
}

/// <summary>A texture view used as a render attachment.</summary>
public abstract partial class GPURenderView
{
    private protected GPURenderView() { }

    /// <summary>The pixel format of the underlying attachment texture.</summary>
    public abstract Format Format { get; }

    /// <summary>The sample count of the underlying attachment texture.</summary>
    public abstract uint SampleCount { get; }

    /// <summary>Releases the view after no recorded or executing render pass uses it.</summary>
    public abstract void Destroy();
}

/// <summary>A compiled graphics, mesh, or compute pipeline.</summary>
public abstract partial class GPUPipeline
{
    private protected GPUPipeline() { }

    /// <summary>Releases the pipeline after no recorded or executing work uses it.</summary>
    public abstract void Destroy();
}

/// <summary>An application-indexed heap of texture descriptors.</summary>
/// <remarks>Synchronize overlapping CPU accesses that include a write; order descriptor updates against GPU reads of the affected slots.</remarks>
public abstract partial class GPUTextureDescriptorHeap
{
    /// <summary>The number of descriptor slots in the heap.</summary>
    public uint Capacity { get; internal set; }

    private protected GPUTextureDescriptorHeap() { }

    /// <summary>Binds a texture view into one heap slot.</summary>
    /// <param name="index">The destination slot.</param>
    /// <param name="texture">The referenced texture.</param>
    /// <param name="type">The descriptor access type.</param>
    /// <param name="desc">The texture view description.</param>
    public abstract void WriteTextureDescriptor(uint index, GPUTexture texture, TextureDescriptorType type, TextureDescriptorDesc desc);

    /// <summary>Copies heap slots into another heap.</summary>
    /// <param name="sourceIndex">The first source slot.</param>
    /// <param name="destination">The destination heap.</param>
    /// <param name="destinationIndex">The first destination slot.</param>
    /// <param name="count">The number of slots to copy.</param>
    public abstract void CopyTextureDescriptors(uint sourceIndex, GPUTextureDescriptorHeap destination, uint destinationIndex, uint count);

    /// <summary>Releases the heap.</summary>
    public abstract void Destroy();
}

/// <summary>An application-indexed heap of sampler descriptors.</summary>
/// <remarks>Synchronize overlapping CPU accesses that include a write; order descriptor updates against GPU reads of the affected slots.</remarks>
public abstract partial class GPUSamplerDescriptorHeap
{
    /// <summary>The number of descriptor slots in the heap.</summary>
    public uint Capacity { get; internal set; }

    private protected GPUSamplerDescriptorHeap() { }

    /// <summary>Binds a sampler into one heap slot.</summary>
    /// <param name="index">The destination slot.</param>
    /// <param name="desc">The sampler description.</param>
    public abstract void WriteSamplerDescriptor(uint index, SamplerDesc desc);

    /// <summary>Copies heap slots into another heap.</summary>
    /// <param name="sourceIndex">The first source slot.</param>
    /// <param name="destination">The destination heap.</param>
    /// <param name="destinationIndex">The first destination slot.</param>
    /// <param name="count">The number of slots to copy.</param>
    public abstract void CopySamplerDescriptors(uint sourceIndex, GPUSamplerDescriptorHeap destination, uint destinationIndex, uint count);

    /// <summary>Releases the heap.</summary>
    public abstract void Destroy();
}

/// <summary>Records GPU commands. Draw and dispatch commands reference buffer-backed root storage without copying it.</summary>
/// <remarks>Parallel recording requires independent command pools; synchronize access with the owning pool and its other command buffers.</remarks>
public abstract partial class GPUCommandBuffer
{
    internal uint TimestampCount;

    private protected GPUCommandBuffer() { }

    /// <summary>Acquires an image and records its initial transition on queue zero; submit this buffer before any subsequent image use.</summary>
    /// <param name="swapchain">The swapchain to acquire; other swapchains may already have acquired images.</param>
    /// <returns>A borrowed frame valid until presentation or reconfiguration, or an empty frame while unavailable.</returns>
    public abstract SwapchainFrame Acquire(GPUSwapchain swapchain);

    /// <summary>Records a timestamp that is written to the first span element once the stage completes.</summary>
    /// <param name="destination">Stationary CPU storage retained until the pool reads its timestamps.</param>
    /// <param name="stage">The pipeline stage to timestamp.</param>
    public abstract void WriteTimestamp(Span<ulong> destination, Stage stage = Stage.AllCommands);

    /// <summary>Binds a pipeline for subsequent draws and dispatches.</summary>
    /// <param name="pipeline">The pipeline to bind.</param>
    public abstract void BindPipeline(GPUPipeline pipeline);

    /// <summary>Binds the texture descriptor heap subsequent draws read bindless slots from.</summary>
    /// <param name="heap">The texture descriptor heap.</param>
    public abstract void SetTextureDescriptorHeap(GPUTextureDescriptorHeap heap);

    /// <summary>Binds the sampler descriptor heap subsequent draws read bindless slots from.</summary>
    /// <param name="heap">The sampler descriptor heap.</param>
    public abstract void SetSamplerDescriptorHeap(GPUSamplerDescriptorHeap heap);

    /// <summary>Inserts an execution and memory dependency between two command scopes.</summary>
    /// <param name="before">The source stages.</param>
    /// <param name="beforeAccess">The source accesses.</param>
    /// <param name="after">The destination stages.</param>
    /// <param name="afterAccess">The destination accesses.</param>
    public abstract void Barrier(Stage before, Access beforeAccess, Stage after, Access afterAccess);

    /// <summary>Records a linear GPU-memory copy.</summary>
    /// <param name="source">The source GPU range.</param>
    /// <param name="destination">The destination GPU range.</param>
    public abstract void CopyMemory(GPURange source, GPURange destination);

    /// <summary>Records a single-sample texture upload; depth/stencil transfers cover complete mip subresources.</summary>
    /// <param name="source">The source GPU range.</param>
    /// <param name="destination">The destination texture; combined depth/stencil formats are not supported.</param>
    /// <param name="copy">The texture region and source pitches; depth/stencil texture origins must be zero.</param>
    public abstract void CopyMemoryToTexture(GPURange source, GPUTexture destination, TextureCopyDesc copy = default);

    /// <summary>Records single-sample texture readback; depth/stencil transfers cover complete mip subresources.</summary>
    /// <param name="source">The source texture; combined depth/stencil formats are not supported.</param>
    /// <param name="destination">The destination GPU range.</param>
    /// <param name="copy">The texture region and destination pitches; depth/stencil texture origins must be zero.</param>
    public abstract void CopyTextureToMemory(GPUTexture source, GPURange destination, TextureCopyDesc copy = default);

    /// <summary>Copies between single-sample textures outside render passes. Formats, types, and selected mip extents must match; no overlap.</summary>
    /// <param name="source">The initialized transfer-source texture; combined depth/stencil formats are not supported.</param>
    /// <param name="destination">The initialized transfer-destination texture.</param>
    /// <param name="copy">The regions; depth/stencil copies require zero origins and complete mip subresources. Synchronization is explicit.</param>
    public abstract void CopyTexture(GPUTexture source, GPUTexture destination, TextureRegionCopyDesc copy = default);

    /// <summary>Begins a render pass with the given attachments.</summary>
    /// <param name="desc">The render attachments and their operations.</param>
    /// <param name="flags">The render pass continuation flags.</param>
    public abstract void BeginRenderPass(RenderingDesc desc, RenderingFlags flags = RenderingFlags.None);

    /// <summary>Ends the current render pass.</summary>
    public abstract void EndRenderPass();

    /// <summary>Sets the top-left framebuffer viewport for upward-positive clip Y.</summary>
    /// <param name="viewport">The viewport rectangle with positive dimensions and a zero-to-one depth range.</param>
    public abstract void SetViewport(Viewport viewport);

    /// <summary>Sets the scissor rectangle.</summary>
    /// <param name="scissor">The scissor rectangle.</param>
    public abstract void SetScissor(Scissor scissor);

    /// <summary>Sets depth and stencil tests for the current render pass.</summary>
    /// <param name="state">The depth and stencil test state.</param>
    public abstract void SetDepthStencil(DepthStencilState state);

    /// <summary>Records a non-indexed draw.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="vertexCount">Number of vertices per instance.</param>
    /// <param name="instanceCount">Number of instances.</param>
    /// <param name="firstVertex">Index of the first vertex.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    public abstract void Draw(GPURange root, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);

    /// <summary>Records an indexed draw.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="indices">GPU index-buffer range.</param>
    /// <param name="type">Index element type.</param>
    /// <param name="indexCount">Number of indices per instance.</param>
    /// <param name="instanceCount">Number of instances.</param>
    /// <param name="firstIndex">Index of the first index-buffer element.</param>
    /// <param name="vertexOffset">Offset added to each fetched index.</param>
    /// <param name="firstInstance">Index of the first instance.</param>
    public abstract void DrawIndexed(GPURange root, GPURange indices, IndexType type, uint indexCount, uint instanceCount = 1,
        uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0);

    /// <summary>Records draws with arguments read from GPU memory.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="arguments">GPU range containing indirect draw arguments.</param>
    /// <param name="drawCount">Number of draw argument records.</param>
    /// <param name="stride">Argument stride in bytes; zero selects tightly packed records.</param>
    public abstract void DrawIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0);

    /// <summary>Records indexed draws with arguments read from GPU memory.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="indices">GPU index-buffer range.</param>
    /// <param name="type">Index element type.</param>
    /// <param name="arguments">GPU range containing indirect indexed-draw arguments.</param>
    /// <param name="drawCount">Number of draw argument records.</param>
    /// <param name="stride">Argument stride in bytes; zero selects tightly packed records.</param>
    public abstract void DrawIndexedIndirect(GPURange root, GPURange indices, IndexType type, GPURange arguments, uint drawCount = 1, uint stride = 0);

    /// <summary>Records a compute dispatch.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="groupCount">Number of workgroups in each dimension.</param>
    public abstract void Dispatch(GPURange root, Extent3D groupCount);

    /// <summary>Records a compute dispatch with arguments read from GPU memory.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="arguments">GPU range containing three 32-bit workgroup counts.</param>
    public abstract void DispatchIndirect(GPURange root, GPURange arguments);

    /// <summary>Records a task/mesh-shading draw.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="groupCount">Number of task workgroups, or mesh workgroups when no task stage is bound.</param>
    public abstract void DrawMeshlets(GPURange root, Extent3D groupCount);

    /// <summary>Records indirect mesh-shading draws; requires <see cref="DeviceCaps.IndirectMeshDraw"/>.</summary>
    /// <param name="root">Root range with a 16-byte-aligned offset, retained until GPU completion; default preserves the binding.</param>
    /// <param name="arguments">GPU range containing indirect task/mesh draw arguments.</param>
    /// <param name="drawCount">Number of draw argument records.</param>
    /// <param name="stride">Argument stride in bytes; zero selects tightly packed records.</param>
    public abstract void DrawMeshletsIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0);

    /// <summary>Initializes a newly placed texture for rendering use.</summary>
    /// <param name="texture">The newly placed or aliased texture; acquired swapchain images are already initialized.</param>
    public abstract void InitializeTexture(GPUTexture texture);

    /// <summary>Opens a named debug group until the matching <see cref="EndDebugGroup"/>. Balance groups within the command buffer.</summary>
    /// <param name="name">The group name shown by GPU capture tools.</param>
    public abstract void BeginDebugGroup(ReadOnlySpan<char> name);

    /// <summary>Closes the innermost open debug group.</summary>
    public abstract void EndDebugGroup();

    /// <summary>Inserts a standalone debug marker at the current position.</summary>
    /// <param name="name">The marker name shown by GPU capture tools.</param>
    public abstract void InsertDebugMarker(ReadOnlySpan<char> name);

    /// <summary>Finishes recording for submission through the owning device.</summary>
    public abstract void EndCommands();
}

/// <summary>Identifies and owns the backing buffer of a linear GPU heap.</summary>
public abstract class GPUHeapOwner
{
    internal uint ViewIndex;

    private protected GPUHeapOwner() { }

    internal abstract void Destroy();
}

/// <summary>Owns the backing allocation of a texture-placement heap.</summary>
public abstract class GPUTextureHeapOwner
{
    private protected GPUTextureHeapOwner() { }

    internal abstract void Destroy();
}
