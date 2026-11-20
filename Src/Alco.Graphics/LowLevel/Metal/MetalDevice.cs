// Metal backend part 1: device creation and destruction, capabilities, timelines, memory heaps, and waitIdle.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Mtl = Metal;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal enum MetalQueueKind : byte
{
    General,
    Compute,
    Copy,
}

// Port of the .mm Queue struct: one MTL4 submission lane with its completion event and feedback queue.
internal sealed unsafe class MetalQueue
{
    internal Mtl.MTL4CommandQueue Handle;
    internal nint Feedback;
    internal Mtl.MTLSharedEvent Completion;
    internal ulong SubmittedValue;
    internal Mtl.MTL4CommandBuffer[] Submission = Array.Empty<Mtl.MTL4CommandBuffer>();
    internal MetalQueueKind Kind = MetalQueueKind.General;
}

// One MTL4CounterHeap plus the 4096 bits tracking its assigned timestamp slots.
internal sealed class CounterPage
{
    internal Mtl.MTL4CounterHeap Heap;
    internal CounterPage Next = null!;
    internal ulong[] occupied = new ulong[64];
}

internal sealed unsafe class NativeCommandBuffer
{
    internal Mtl.MTL4CommandBuffer Buffer;
    internal Mtl.MTL4CommandAllocator Allocator;
    internal NativeCommandBuffer Next = null!;
}

internal unsafe struct MetalTimestampSlot
{
    internal Mtl.MTL4CounterHeap Heap;
    internal CounterPage Page;
    internal ulong* destination;
    internal uint Index;
}

internal struct MetalDepthStateEntry
{
    internal DepthStencilState Desc;
    internal Mtl.MTLDepthStencilState State;
}

internal sealed class DepthStateChunk
{
    internal MetalDepthStateEntry[] Entries = new MetalDepthStateEntry[64];
    internal uint Count;
    internal DepthStateChunk Next = null!;
}

internal sealed unsafe partial class MetalTimelineSemaphore : GPUTimelineSemaphore
{
    internal MetalDevice Owner = null!;
    internal Mtl.MTLSharedEvent SharedEvent;

    /// <summary>Releases the semaphore.</summary>
    public override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        SharedEvent.Dispose();
    }

    /// <summary>The latest completed value signaled by the GPU.</summary>
    public override ulong CompletedValue
    {
        get
        {
            using Mtl.NSAutoreleasePool pool = new();
            return SharedEvent.SignaledValue;
        }
    }

    internal override void Wait(ulong value)
    {
        using Mtl.NSAutoreleasePool pool = new();
        while (!SharedEvent.WaitUntilSignaledValue(value, 1000)) { }
    }
}

internal sealed unsafe partial class MetalDevice : GPUDevice
{
    internal Mtl.MTLDevice MtlDevice;
    internal Mtl.MTL4Compiler MtlCompiler;
    internal Mtl.MTLResidencySet MtlResidency;
    internal MetalQueue[] MtlQueues = Array.Empty<MetalQueue>();
    internal bool Headless;
    internal int SwapchainCount;
    internal readonly Lock ResidencyLock = new();
    internal CounterPage CounterPages = null!;
    internal int CounterPageCount;
    internal bool ShaderValidation;
    internal Mtl.MTLBuffer BufferViewTable;
    internal BufferViewSlots? BufferViews;

    private MetalDevice() : base(GraphicsBackend.Metal) { }

    internal static MetalDevice Create(DeviceDesc desc)
    {
        using Mtl.NSAutoreleasePool pool = new();
        MetalDevice device = new() { Headless = desc.Headless };
        try
        {
            // MetalTools on macOS 26 cannot enumerate placed resources through heap residency.
            string? shaderValidation = Environment.GetEnvironmentVariable("MTL_SHADER_VALIDATION");
            device.ShaderValidation = int.TryParse(shaderValidation, out int validationValue) && validationValue != 0;
            device.MtlDevice = Mtl.MTLDevice.CreateSystemDefaultDevice();
            if (device.MtlDevice.NativePtr == 0) throw MetalBackend.CreationFailure(default, "system default device", Error.Unsupported);
            if (!device.MtlDevice.SupportsFamily(Mtl.MTLGPUFamily.Apple7) || !device.MtlDevice.SupportsFamily(Mtl.MTLGPUFamily.Metal4))
                throw MetalBackend.CreationFailure(default, "Apple7 and Metal4 GPU support is required", Error.Unsupported);
            Mtl.NSError error = default;
            Debug.Assert(desc.DesiredQueueCount != 0);
            device.Capabilities.GeneralQueueCount = desc.DesiredQueueCount;
            device.Capabilities.ComputeQueueCount = desc.DesiredComputeQueueCount;
            device.Capabilities.CopyQueueCount = desc.DesiredCopyQueueCount;
            device.Capabilities.QueueCount = desc.DesiredQueueCount + desc.DesiredComputeQueueCount + desc.DesiredCopyQueueCount;
            device.QueueCount = device.Capabilities.QueueCount;
            device.MtlQueues = new MetalQueue[device.Capabilities.QueueCount];
            for (uint i = 0; i < device.Capabilities.QueueCount; ++i) device.MtlQueues[i] = new MetalQueue();
            for (uint i = 0; i < device.Capabilities.QueueCount; ++i)
            {
                MetalQueue queue = device.MtlQueues[i];
                queue.Kind = i < desc.DesiredQueueCount ? MetalQueueKind.General :
                             i < desc.DesiredQueueCount + desc.DesiredComputeQueueCount ? MetalQueueKind.Compute : MetalQueueKind.Copy;
                ReadOnlySpan<byte> feedbackLabel = "Alco.Graphics Metal feedback"u8;
                fixed (byte* label = feedbackLabel)
                {
                    queue.Feedback = MetalInterop.CreateFeedbackQueue(label);
                }
                if (queue.Feedback == 0) throw MetalBackend.CreationFailure(default, $"command queue {i} feedback queue");
                Mtl.MTL4CommandQueueDescriptor descriptor = new() { FeedbackQueue = queue.Feedback };
                try
                {
                    if (descriptor.NativePtr == 0) throw MetalBackend.CreationFailure(default, $"command queue {i} descriptor");
                    error = default;
                    queue.Handle = device.MtlDevice.NewMTL4CommandQueue(descriptor, ref error);
                }
                finally
                {
                    // This SDK's destructor releases its assign-only feedbackQueue; retain ownership in MetalQueue.
                    descriptor.FeedbackQueue = nint.Zero;
                    descriptor.Dispose();
                }
                if (queue.Handle.NativePtr == 0) throw MetalBackend.CreationFailure(error, $"command queue {i}");
                queue.Completion = device.MtlDevice.NewSharedEvent();
                if (queue.Completion.NativePtr == 0) throw MetalBackend.CreationFailure(default, $"command queue {i} completion event");
            }
            using Mtl.MTL4CompilerDescriptor compiler = new();
            if (compiler.NativePtr == 0) throw MetalBackend.CreationFailure(default, "pipeline compiler descriptor");
            device.InitializePipelineCache(compiler);
            error = default;
            device.MtlCompiler = device.MtlDevice.NewCompiler(compiler, ref error);
            if (device.MtlCompiler.NativePtr == 0) throw MetalBackend.CreationFailure(error, "pipeline compiler");
            using Mtl.MTLResidencySetDescriptor residency = new() { InitialCapacity = 256 };
            if (residency.NativePtr == 0) throw MetalBackend.CreationFailure(default, "residency set descriptor");
            error = default;
            device.MtlResidency = device.MtlDevice.NewResidencySet(residency, ref error);
            if (device.MtlResidency.NativePtr == 0) throw MetalBackend.CreationFailure(error, "residency set");
            for (uint i = 0; i < device.Capabilities.QueueCount; ++i) device.MtlQueues[i].Handle.AddResidencySet(device.MtlResidency);
            device.BufferViewTable = device.MtlDevice.NewBuffer(BufferViewSlots.Capacity * sizeof(ulong),
                Mtl.MTLResourceOptions.ResourceStorageModeShared | Mtl.MTLResourceOptions.ResourceCPUCacheModeWriteCombined |
                Mtl.MTLResourceOptions.ResourceHazardTrackingModeUntracked);
            if (device.BufferViewTable.NativePtr == 0) throw MetalBackend.CreationFailure(default, "buffer-view address table");
            MetalBackend.AddResident(device, device.BufferViewTable);
            NativeMemory.Clear((void*)device.BufferViewTable.Contents, BufferViewSlots.Capacity * sizeof(ulong));
            device.BufferViews = new BufferViewSlots((ulong*)device.BufferViewTable.Contents);
            device.TimestampQueryCount = desc.TimestampQueryCount;
            device.Capabilities = new DeviceCaps
            {
                DeviceName = device.MtlDevice.Name.ToString()!,
                QueueCount = device.Capabilities.QueueCount,
                GeneralQueueCount = desc.DesiredQueueCount,
                ComputeQueueCount = desc.DesiredComputeQueueCount,
                CopyQueueCount = desc.DesiredCopyQueueCount,
                MaxPushDataSize = 256,
                TextureDescriptorHeapCapacity = Backend.BindlessTextureDescriptorCapacity,
                SamplerDescriptorHeapCapacity = Math.Min(Backend.BindlessSamplerDescriptorCapacity,
                    (uint)device.MtlDevice.MaxArgumentBufferSamplerCount),
                TextureHeapAlignment = 16384,
                TimestampPeriodNs = MetalInterop.MachTimestampPeriodNs(),
                SubTexelPrecisionBits = 8,
                TextureCompressionBc = device.MtlDevice.SupportsBCTextureCompression,
                TextureCompressionAstc = true,
                StorageInputOutput16 = true,
                IndirectMeshDraw = device.MtlDevice.SupportsFamily(Mtl.MTLGPUFamily.Apple9),
            };
            return device;
        }
        catch (GPUException)
        {
            device.Destroy();
            throw;
        }
    }

    /// <summary>Releases the device after every submitted frame has completed.</summary>
    public override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(Volatile.Read(ref SwapchainCount) == 0);
        for (int i = 0; i < MtlQueues.Length; ++i)
        {
            MetalQueue queue = MtlQueues[i];
            Debug.Assert(queue.Completion.SignaledValue >= queue.SubmittedValue);
            if (MtlResidency.NativePtr != 0 && queue.Handle.NativePtr != 0) queue.Handle.RemoveResidencySet(MtlResidency);
            queue.Completion.Dispose();
            queue.Handle.Dispose();
            if (queue.Feedback != 0) MetalInterop.DispatchRelease(queue.Feedback);
        }
        for (CounterPage page = CounterPages; page != null;)
        {
            CounterPage next = page.Next;
            for (int word = 0; word < 64; ++word) Debug.Assert(Volatile.Read(ref page.occupied[word]) == 0);
            page.Heap.Dispose();
            page = next;
        }
        BufferViews = null;
        if (BufferViewTable.NativePtr != 0)
        {
            MetalBackend.RemoveResident(this, BufferViewTable);
            BufferViewTable.Dispose();
            BufferViewTable = default;
        }
        DestroyShaderCache();
        MtlResidency.Dispose();
        MtlCompiler.Dispose();
        MtlDevice.Dispose();
    }

    /// <summary>Returns whether textures of the given format and usage can be created.</summary>
    /// <param name="format">The texture format.</param>
    /// <param name="usage">The required texture uses.</param>
    /// <returns>Whether the format supports every requested use.</returns>
    public override bool SupportsTextureFormat(Format format, TextureUsage usage)
    {
        if (MetalInterop.PixelFormat(format) == Mtl.MTLPixelFormat.Invalid || format == Format.D24UnormS8Uint) return false;
        TextureFormatInfo info = Gpu.GetTextureFormatInfo(format);
        if (info.Depth && info.Stencil &&
            (Backend.HasFlag(usage, TextureUsage.TransferSource) || Backend.HasFlag(usage, TextureUsage.TransferDestination))) return false;
        bool compressed = info.BlockExtent.X > 1;
        if (Backend.FormatCompression(format) == TextureCompression.Bc && !Capabilities.TextureCompressionBc) return false;
        if (Backend.HasFlag(usage, TextureUsage.Storage))
        {
            if (compressed || info.Depth || info.Stencil || format == Format.R8Srgb || format == Format.Rg8Srgb ||
                format == Format.Rgba8Srgb || format == Format.Bgra8Srgb || format == Format.Rgba4Unorm ||
                format == Format.R5g5b5a1Unorm || format == Format.R5g6b5Unorm) return false;
        }
        if (Backend.HasFlag(usage, TextureUsage.ColorAttachment) && (compressed || info.Depth || info.Stencil)) return false;
        if (Backend.HasFlag(usage, TextureUsage.DepthStencilAttachment) && !(info.Depth || info.Stencil)) return false;
        return true;
    }

    /// <summary>Blocks until every submitted command has completed.</summary>
    public override void WaitIdle()
    {
        using Mtl.NSAutoreleasePool pool = new();
        for (uint i = 0; i < Capabilities.QueueCount; ++i)
        {
            MetalQueue queue = MtlQueues[i];
            queue.Handle.SignalEvent(queue.Completion, ++queue.SubmittedValue);
        }
        for (uint i = 0; i < Capabilities.QueueCount; ++i)
        {
            MetalQueue queue = MtlQueues[i];
            while (!queue.Completion.WaitUntilSignaledValue(queue.SubmittedValue, 1000)) { }
        }
    }

    /// <summary>Creates a timeline semaphore.</summary>
    /// <param name="initialValue">The initial completed value.</param>
    /// <returns>The semaphore.</returns>
    public override GPUTimelineSemaphore CreateTimelineSemaphore(ulong initialValue = 0)
    {
        using Mtl.NSAutoreleasePool pool = new();
        MetalTimelineSemaphore result = new() { Owner = this, SharedEvent = MtlDevice.NewSharedEvent() };
        if (result.SharedEvent.NativePtr == 0) throw MetalBackend.CreationFailure(default, "timeline semaphore");
        result.SharedEvent.SignaledValue = initialValue;
        return result;
    }

    /// <summary>Allocates a linear GPU heap with a buffer-backed range.</summary>
    /// <param name="size">The heap size in bytes.</param>
    /// <param name="memory">The heap memory type.</param>
    /// <returns>The allocated heap and its optional CPU mapping.</returns>
    public override GPUHeap AllocateGPUHeap(ulong size, MemoryType memory)
    {
        using Mtl.NSAutoreleasePool pool = new();
        Mtl.MTLResourceOptions options = memory == MemoryType.GpuOnly ? Mtl.MTLResourceOptions.ResourceStorageModePrivate :
                                                                              Mtl.MTLResourceOptions.ResourceStorageModeShared;
        if (memory == MemoryType.CpuVisible) options |= Mtl.MTLResourceOptions.ResourceCPUCacheModeWriteCombined;
        options |= Mtl.MTLResourceOptions.ResourceHazardTrackingModeUntracked;
        Mtl.MTLSizeAndAlign requirements = MtlDevice.HeapBufferSizeAndAlign(size, options);
        if (requirements.size == 0 || requirements.align == 0) throw MetalBackend.CreationFailure(default, "GPU heap memory requirements");
        using Mtl.MTLHeapDescriptor heapDescriptor = new()
        {
            Type = Mtl.MTLHeapType.Placement,
            Size = Backend.AlignUp(requirements.size, requirements.align),
            ResourceOptions = options,
        };
        if (heapDescriptor.NativePtr == 0) throw MetalBackend.CreationFailure(default, "GPU heap descriptor");
        MetalGpuHeapOwner owner = new() { Device = this, Heap = MtlDevice.NewHeap(heapDescriptor) };
        try
        {
            if (owner.Heap.NativePtr == 0) throw MetalBackend.CreationFailure(default, "GPU heap");
            owner.Buffer = owner.Heap.NewBuffer(size, options, 0);
            if (owner.Buffer.NativePtr == 0) throw MetalBackend.CreationFailure(default, "GPU heap buffer");
            owner.Address = owner.Buffer.GpuAddress;
            owner.ViewIndex = BufferViews!.Allocate(owner.Address);
            if (owner.ViewIndex == 0) throw MetalBackend.CreationFailure(default, "65,535 backing-buffer view slots exhausted", Error.Unsupported);
        }
        catch (GPUException)
        {
            owner.Buffer.Dispose();
            owner.Heap.Dispose();
            throw;
        }
        MetalBackend.AddResident(this, ShaderValidation ? (Mtl.MTLAllocation)(Mtl.MTLResource)owner.Buffer : (Mtl.MTLAllocation)owner.Heap);
        return new GPUHeap
        {
            Range = new GPUAllocation<byte>
            {
                Cpu = memory == MemoryType.GpuOnly ? null : (byte*)owner.Buffer.Contents,
                Buffer = owner,
                SizeBytes = size,
            },
            Owner = owner,
        };
    }

    /// <summary>Allocates a dedicated texture-placement heap.</summary>
    /// <param name="byteCount">The heap size in bytes.</param>
    /// <returns>The allocated heap.</returns>
    public override GPUTextureHeap CreateTextureHeap(ulong byteCount)
    {
        using Mtl.NSAutoreleasePool pool = new();
        using Mtl.MTLHeapDescriptor heapDescriptor = new()
        {
            Type = Mtl.MTLHeapType.Placement,
            Size = byteCount,
            StorageMode = Mtl.MTLStorageMode.Private,
            HazardTrackingMode = Mtl.MTLHazardTrackingMode.Untracked,
        };
        if (heapDescriptor.NativePtr == 0) throw MetalBackend.CreationFailure(default, "texture heap descriptor");
        MetalTextureHeapOwner owner = new() { Device = this, Heap = MtlDevice.NewHeap(heapDescriptor) };
        if (owner.Heap.NativePtr == 0) throw MetalBackend.CreationFailure(default, "texture heap");
        if (!ShaderValidation) MetalBackend.AddResident(this, owner.Heap);
        return new GPUTextureHeap { Size = byteCount, Owner = owner };
    }
}

internal static unsafe partial class MetalBackend
{
    internal static GPUException CreationFailure(Mtl.NSError error, string operation, Error code = Error.DriverError)
    {
        return new GPUException(code, MetalInterop.ReportError(error, operation));
    }

    internal static void AddResident(MetalDevice device, Mtl.MTLAllocation allocation)
    {
        lock (device.ResidencyLock)
        {
            device.MtlResidency.AddAllocation(allocation);
            device.MtlResidency.Commit();
        }
    }

    internal static void AddResident(MetalDevice device, Mtl.MTLResource resource) => AddResident(device, (Mtl.MTLAllocation)resource);

    internal static void RemoveResident(MetalDevice device, Mtl.MTLAllocation allocation)
    {
        lock (device.ResidencyLock)
        {
            device.MtlResidency.RemoveAllocation(allocation);
            device.MtlResidency.Commit();
        }
    }

    internal static void RemoveResident(MetalDevice device, Mtl.MTLResource resource) => RemoveResident(device, (Mtl.MTLAllocation)resource);
}
