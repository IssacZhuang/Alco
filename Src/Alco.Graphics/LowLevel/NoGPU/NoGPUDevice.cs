using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe class NoGPUDevice : GPUDevice
{
    private const ulong TextureAlignment = 64 * 1024;

    private readonly ulong* _bufferViewAddresses;
    private readonly BufferViewSlots _bufferViewSlots;
    private bool _destroyed;

    private NoGPUDevice(in DeviceDesc desc) : base(GraphicsBackend.NoGPU)
    {
        uint generalQueues = Math.Max(1u, desc.DesiredQueueCount);
        uint computeQueues = desc.DesiredComputeQueueCount;
        uint copyQueues = desc.DesiredCopyQueueCount;

        Capabilities = new DeviceCaps
        {
            DeviceName = "NoGPU",
            GeneralQueueCount = generalQueues,
            ComputeQueueCount = computeQueues,
            CopyQueueCount = copyQueues,
            QueueCount = generalQueues + computeQueues + copyQueues,
            CopyTextureGranularity = new Extent3D(1, 1, 1),
            MaxPushDataSize = 16,
            TextureDescriptorHeapCapacity = Backend.BindlessTextureDescriptorCapacity,
            SamplerDescriptorHeapCapacity = Backend.BindlessSamplerDescriptorCapacity,
            TextureHeapAlignment = TextureAlignment,
            TimestampPeriodNs = desc.TimestampQueryCount > 0 ? 1.0f : 0.0f,
            SubTexelPrecisionBits = 8,
            TextureCompressionBc = true,
            TextureCompressionAstc = true,
            StorageInputOutput16 = false,
            IndirectMeshDraw = false,
        };
        QueueCount = Capabilities.QueueCount;
        TimestampQueryCount = desc.TimestampQueryCount;

        _bufferViewAddresses = (ulong*)NativeMemory.AllocZeroed(BufferViewSlots.Capacity, (nuint)sizeof(ulong));
        _bufferViewSlots = new BufferViewSlots(_bufferViewAddresses);
    }

    internal static NoGPUDevice Create(in DeviceDesc desc)
    {
        if (desc.DesiredQueueCount == 0)
            throw new GPUException(Error.Unsupported, "At least one general queue is required.");
        return new NoGPUDevice(in desc);
    }

    public override void Destroy()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        NativeMemory.Free(_bufferViewAddresses);
    }

    public override GPUHeap AllocateGPUHeap(ulong size, MemoryType memory)
    {
        if (size == 0)
            throw new GPUException(Error.Unsupported, "GPU heap size must be nonzero.");
        ulong alignedSize = Backend.AlignUp(size, Backend.GpuAllocationAlignment);
        byte* storage = (byte*)NativeMemory.AlignedAlloc((nuint)alignedSize, (nuint)Backend.GpuAllocationAlignment);
        if (storage == null)
            throw new GPUException(Error.OutOfMemory, "Failed to allocate NoGPU heap.");
        if (memory != MemoryType.GpuOnly)
            NativeMemory.Clear(storage, (nuint)alignedSize);

        uint viewIndex = _bufferViewSlots.Allocate((ulong)storage);
        if (viewIndex == 0)
        {
            NativeMemory.AlignedFree(storage);
            throw new GPUException(Error.OutOfMemory, "NoGPU buffer view table is full.");
        }

        NoGPUHeapOwner owner = new(this, storage, viewIndex);
        return new GPUHeap
        {
            Owner = owner,
            Range = new GPUAllocation<byte>
            {
                Cpu = memory == MemoryType.GpuOnly ? null : storage,
                Buffer = owner,
                Offset = 0,
                SizeBytes = size,
            },
        };
    }

    internal void FreeHeapOwner(byte* storage, uint viewIndex)
    {
        if (viewIndex != 0 && !_destroyed)
            _bufferViewSlots.Free(viewIndex);
        if (storage != null)
            NativeMemory.AlignedFree(storage);
    }

    public override bool SupportsTextureFormat(Format format, TextureUsage usage)
    {
        return format != Format.Undefined && usage != TextureUsage.None;
    }

    public override GPUSwapchain CreateSwapchain(in SwapchainDesc desc) => new NoGPUSwapchain(in desc);

    public override void WaitIdle() { }

    public override GPUTimelineSemaphore CreateTimelineSemaphore(ulong initialValue = 0) => new NoGPUTimelineSemaphore(initialValue);

    public override GPUTextureHeap CreateTextureHeap(ulong byteCount)
    {
        if (byteCount == 0)
            throw new GPUException(Error.Unsupported, "Texture heap size must be nonzero.");
        return new GPUTextureHeap { Size = byteCount, Owner = new NoGPUTextureHeapOwner() };
    }

    public override SizeAlign GetTextureSizeAlign(TextureDesc desc)
    {
        if (desc.Format == Format.Undefined || desc.Extent.X == 0 || desc.Extent.Y == 0 || desc.Extent.Z == 0)
            return default;
        TextureFormatInfo info = Gpu.GetTextureFormatInfo(desc.Format);
        if (info.BytesPerBlock == 0 || info.BlockExtent.X == 0 || info.BlockExtent.Y == 0)
            return default;

        ulong totalBytes = 0;
        uint mipLevels = Math.Max(1u, desc.MipLevels);
        uint layers = Math.Max(1u, desc.LayerCount);
        for (uint mip = 0; mip < mipLevels; mip++)
        {
            uint w = Math.Max(1u, desc.Extent.X >> (int)mip);
            uint h = Math.Max(1u, desc.Extent.Y >> (int)mip);
            uint d = Math.Max(1u, desc.Extent.Z >> (int)mip);
            ulong bx = Backend.DivideUp(w, info.BlockExtent.X);
            ulong by = Backend.DivideUp(h, info.BlockExtent.Y);
            totalBytes += bx * by * d * info.BytesPerBlock * layers;
        }

        ulong sampleMultiplier = Math.Max(1u, desc.SampleCount);
        totalBytes = Backend.AlignUp(totalBytes * sampleMultiplier, TextureAlignment);
        return new SizeAlign { Size = totalBytes, Align = TextureAlignment };
    }

    public override GPUTexture CreateTexture(TextureDesc desc, GPUTextureHeap heap, ulong offset) => new NoGPUTexture(in desc);

    public override GPUTextureDescriptorHeap CreateTextureDescriptorHeap(uint capacity) => new NoGPUTextureDescriptorHeap(capacity);

    public override GPUSamplerDescriptorHeap CreateSamplerDescriptorHeap(uint capacity) => new NoGPUSamplerDescriptorHeap(capacity);

    public override GPUPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => new NoGPUPipeline();

    public override GPUPipeline CreateMeshPipeline(MeshPipelineDesc desc) => new NoGPUPipeline();

    public override GPUPipeline CreateComputePipeline(ShaderStage compute) => new NoGPUPipeline();

    public override GPUCommandPool CreateCommandPool(uint queueIndex) => new NoGPUCommandPool();

    public override void Submit(SubmitDesc desc, uint queueIndex = 0)
    {
        SignalCompletion(desc.Completion);
    }

    public override void SubmitAndPresent(SubmitDesc desc, GPUSwapchain swapchain)
    {
        SignalCompletion(desc.Completion);
    }

    private static void SignalCompletion(TimelinePoint completion)
    {
        if (completion.Semaphore is NoGPUTimelineSemaphore noGpuSemaphore)
            noGpuSemaphore.Signal(completion.Value);
    }
}

internal sealed unsafe class NoGPUHeapOwner : GPUHeapOwner
{
    private readonly NoGPUDevice _device;
    private byte* _storage;

    internal NoGPUHeapOwner(NoGPUDevice device, byte* storage, uint viewIndex)
    {
        _device = device;
        _storage = storage;
        ViewIndex = viewIndex;
    }

    internal override void Destroy()
    {
        byte* storage = _storage;
        uint viewIndex = ViewIndex;
        _storage = null;
        ViewIndex = 0;
        _device.FreeHeapOwner(storage, viewIndex);
    }
}

internal sealed class NoGPUTextureHeapOwner : GPUTextureHeapOwner
{
    internal override void Destroy() { }
}

internal sealed class NoGPUTimelineSemaphore : GPUTimelineSemaphore
{
    private ulong _completedValue;

    internal NoGPUTimelineSemaphore(ulong initialValue)
    {
        _completedValue = initialValue;
    }

    internal override void Wait(ulong value)
    {
        Signal(value);
    }

    internal void Signal(ulong value)
    {
        while (true)
        {
            ulong current = Volatile.Read(ref _completedValue);
            if (current >= value)
                return;
            if (Interlocked.CompareExchange(ref _completedValue, value, current) == current)
                return;
        }
    }

    public override ulong CompletedValue => Volatile.Read(ref _completedValue);

    public override void Destroy() { }
}

internal sealed class NoGPUTexture : GPUTexture
{
    private readonly TextureDesc _desc;

    internal NoGPUTexture(in TextureDesc desc)
    {
        _desc = desc;
    }

    public override GPURenderView CreateRenderView(RenderViewDesc desc) => new NoGPURenderView(_desc.Format, _desc.SampleCount);

    public override void Destroy() { }
}

internal sealed class NoGPURenderView : GPURenderView
{
    internal Format ViewFormat;
    internal uint ViewSampleCount;

    internal NoGPURenderView(Format format, uint sampleCount = 1)
    {
        ViewFormat = format;
        ViewSampleCount = sampleCount == 0 ? 1u : sampleCount;
    }

    public override Format Format => ViewFormat;

    public override uint SampleCount => ViewSampleCount;

    public override void Destroy() { }
}

internal sealed class NoGPUPipeline : GPUPipeline
{
    public override void Destroy() { }
}

internal sealed class NoGPUTextureDescriptorHeap : GPUTextureDescriptorHeap
{
    internal NoGPUTextureDescriptorHeap(uint capacity)
    {
        Capacity = capacity;
    }

    public override void WriteTextureDescriptor(uint index, GPUTexture texture, TextureDescriptorType type, TextureDescriptorDesc desc) { }

    public override void CopyTextureDescriptors(uint sourceIndex, GPUTextureDescriptorHeap destination, uint destinationIndex, uint count) { }

    public override void Destroy() { }
}

internal sealed class NoGPUSamplerDescriptorHeap : GPUSamplerDescriptorHeap
{
    internal NoGPUSamplerDescriptorHeap(uint capacity)
    {
        Capacity = capacity;
    }

    public override void WriteSamplerDescriptor(uint index, SamplerDesc desc) { }

    public override void CopySamplerDescriptors(uint sourceIndex, GPUSamplerDescriptorHeap destination, uint destinationIndex, uint count) { }

    public override void Destroy() { }
}

internal sealed class NoGPUSwapchain : GPUSwapchain
{
    private Extent2D _extent;
    private Format _format;
    private ColorSpace _colorSpace;
    private bool _vsync;
    internal readonly NoGPUTexture BackbufferTexture;
    internal readonly NoGPURenderView BackbufferView;

    internal NoGPUSwapchain(in SwapchainDesc desc)
    {
        _extent = desc.Extent;
        _format = desc.Format == Format.Undefined ? Format.Bgra8Unorm : desc.Format;
        _colorSpace = desc.ColorSpace;
        _vsync = desc.VSync;
        BackbufferTexture = new NoGPUTexture(new TextureDesc
        {
            Extent = new Extent3D(Math.Max(1u, _extent.X), Math.Max(1u, _extent.Y), 1),
            Format = _format,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferDestination,
        });
        BackbufferView = new NoGPURenderView(_format, 1u);
    }

    public override Extent2D DrawableExtent => _extent;

    public override Format Format => _format;

    public override ColorSpace ColorSpace => _colorSpace;

    public override bool VSync => _vsync;

    public override bool TryResize(Extent2D extent, out Error error)
    {
        _extent = extent;
        error = Error.None;
        return true;
    }

    public override bool TrySetFormat(Format format, ColorSpace colorSpace, out Error error)
    {
        _format = format == Format.Undefined ? Format.Bgra8Unorm : format;
        BackbufferView.ViewFormat = _format;
        _colorSpace = colorSpace;
        error = Error.None;
        return true;
    }

    public override bool TrySetVSync(bool enabled, out Error error)
    {
        _vsync = enabled;
        error = Error.None;
        return true;
    }

    public override void Destroy()
    {
        BackbufferView.Destroy();
        BackbufferTexture.Destroy();
    }
}

internal sealed class NoGPUCommandPool : GPUCommandPool
{
    private readonly NoGPUCommandBuffer _commandBuffer = new();

    public override GPUCommandBuffer BeginCommands() => _commandBuffer;

    public override void Reset() { }

    public override void ReadTimestamps() { }

    public override void Destroy() { }
}

internal sealed class NoGPUCommandBuffer : GPUCommandBuffer
{
    public override SwapchainFrame Acquire(GPUSwapchain swapchain)
    {
        if (swapchain is NoGPUSwapchain noGpu && noGpu.DrawableExtent.X > 0 && noGpu.DrawableExtent.Y > 0)
        {
            return new SwapchainFrame
            {
                Extent = noGpu.DrawableExtent,
                RenderView = noGpu.BackbufferView,
                Texture = noGpu.BackbufferTexture,
            };
        }
        return default;
    }

    public override void WriteTimestamp(Span<ulong> destination, Stage stage = Stage.AllCommands)
    {
        if (!destination.IsEmpty)
            destination[0] = 0;
    }

    public override void BindPipeline(GPUPipeline pipeline) { }

    public override void SetTextureDescriptorHeap(GPUTextureDescriptorHeap heap) { }

    public override void SetSamplerDescriptorHeap(GPUSamplerDescriptorHeap heap) { }

    public override void Barrier(Stage before, Access beforeAccess, Stage after, Access afterAccess) { }

    public override void CopyMemory(GPURange source, GPURange destination) { }

    public override void CopyMemoryToTexture(GPURange source, GPUTexture destination, TextureCopyDesc copy = default) { }

    public override void CopyTextureToMemory(GPUTexture source, GPURange destination, TextureCopyDesc copy = default) { }

    public override void CopyTexture(GPUTexture source, GPUTexture destination, TextureRegionCopyDesc copy = default) { }

    public override void BeginRenderPass(RenderingDesc desc, RenderingFlags flags = RenderingFlags.None) { }

    public override void EndRenderPass() { }

    public override void SetViewport(Viewport viewport) { }

    public override void SetScissor(Scissor scissor) { }

    public override void SetDepthStencil(DepthStencilState state) { }

    public override void Draw(GPURange root, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) { }

    public override void DrawIndexed(GPURange root, GPURange indices, IndexType type, uint indexCount, uint instanceCount = 1,
        uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) { }

    public override void DrawIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0) { }

    public override void DrawIndexedIndirect(GPURange root, GPURange indices, IndexType type, GPURange arguments, uint drawCount = 1, uint stride = 0) { }

    public override void Dispatch(GPURange root, Extent3D groupCount) { }

    public override void DispatchIndirect(GPURange root, GPURange arguments) { }

    public override void DrawMeshlets(GPURange root, Extent3D groupCount) { }

    public override void DrawMeshletsIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0) { }

    public override void InitializeTexture(GPUTexture texture) { }

    public override void BeginDebugGroup(ReadOnlySpan<char> name) { }

    public override void EndDebugGroup() { }

    public override void InsertDebugMarker(ReadOnlySpan<char> name) { }

    public override void EndCommands() { }
}
