// Fixed-capacity upload staging, submission tracking, and explicit cleanup with a finalizer safeguard.

using System.Diagnostics;

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

public struct UploadQueueStats
{
    public ulong Capacity;
    public ulong BytesInUse;
    public ulong PeakBytes;
    public ulong Submissions;
    public ulong Waits;
    public uint PendingBatches;
    public uint PendingOperations;
}

// Optional synchronous submission handoff. The callback must submit the batch exactly once before returning,
// using the supplied queue and completion point. It must not access this uploader. Change only with exclusive ownership.
public unsafe delegate void SubmitCallback(void* context, GPUDevice device, GPUCommandBuffer commands, TimelinePoint completion, uint queueIndex);

// Externally synchronize this uploader and its selected queue. Destination and callback resource lifetimes remain caller-owned.
// Order texture initialization and prior resource use on that queue before enqueuing uploads, including any waits on other queues.
/// <summary>Stages uploads through a fixed-capacity ring with explicit submission and destruction.</summary>
public sealed unsafe class UploadQueue
{
    public const uint OperationLimit = 4096;
    public const ulong Alignment = 16;

    internal sealed class Batch
    {
        public GPUCommandPool? Pool;
        public ulong End;
        public ulong Value;
    }

    // Port note: C++ keeps this as a nested aggregate with private access for the friended uploadTexture
    // free function; here the nested State is internal for the TextureUpload helper in the same assembly.
    internal sealed class State
    {
        public GPUDevice? Device = null;
        public GPUHeap Heap;
        public TimelinePoint Completion;
        public GPUCommandBuffer? Commands = null;
        public ulong Head = 0;
        public ulong Tail = 0;
        public Batch[]? Batches = null;
        public uint MaxPendingBatches = 0;
        public uint QueueIndex = 0;
        public Stage UploadStages = Stage.Transfer;
        public Access UploadAccess = Access.TransferRead | Access.TransferWrite;
        public Access QueueAccess = Access.TransferRead | Access.TransferWrite;
        public Extent3D TextureGranularity = new(1, 1, 1);
        public uint RetirementFirst = 0;
        public uint RetirementCount = 0;
        public uint OperationCount = 0;
        public bool InCallback = false;
        public ulong Submissions = 0;
        public ulong Waits = 0;
        public ulong PeakBytes = 0;
        public SubmitCallback? SubmitCallback = null;
        public void* SubmitContext = null;
    }

    internal State _state = new();

    private const Access GpuAccess = Access.ShaderRead | Access.ShaderWrite | Access.TransferRead | Access.TransferWrite
        | Access.ColorRead | Access.ColorWrite | Access.DepthStencilRead | Access.DepthStencilWrite
        | Access.IndirectRead | Access.IndexRead | Access.DescriptorRead;

    /// <summary>Creates staging storage and prewarms command recording; failed creation releases partial resources.</summary>
    /// <param name="device">The device, kept alive until this queue is destroyed.</param>
    /// <param name="capacity">The staging capacity, a positive multiple of <see cref="Alignment"/>.</param>
    /// <param name="queueIndex">A queue index less than <see cref="DeviceCaps.QueueCount"/>.</param>
    /// <param name="maxPendingBatches">The nonzero batch limit, normally the number of frames in flight.</param>
    /// <exception cref="GPUException">Staging or command resources could not be created.</exception>
    public UploadQueue(GPUDevice device, ulong capacity = 64ul * 1024 * 1024, uint queueIndex = 0, uint maxPendingBatches = 2)
    {
        Debug.Assert(device != null && capacity >= Alignment && capacity % Alignment == 0 && maxPendingBatches != 0);
        DeviceCaps caps = device.Caps;
        Debug.Assert(queueIndex < caps.QueueCount);
        _state.Device = device;
        _state.QueueIndex = queueIndex;
        if (queueIndex < caps.GeneralQueueCount + caps.ComputeQueueCount)
        {
            _state.UploadStages |= Stage.Compute;
            _state.UploadAccess |= Access.ShaderRead | Access.ShaderWrite | Access.DescriptorRead;
            _state.QueueAccess = _state.UploadAccess | Access.IndirectRead;
            if (queueIndex < caps.GeneralQueueCount) _state.QueueAccess = GpuAccess;
        }
        else
        {
            _state.TextureGranularity = caps.CopyTextureGranularity;
        }
        _state.Batches = new Batch[maxPendingBatches];
        for (uint index = 0; index < maxPendingBatches; ++index)
            _state.Batches[index] = new Batch();
        _state.MaxPendingBatches = maxPendingBatches;
        try
        {
            _state.Heap = device.CreateGPUHeap(capacity, MemoryType.CpuVisible);
            _state.Completion.Semaphore = device.CreateTimelineSemaphore();
            for (uint index = 0; index < maxPendingBatches; ++index)
            {
                Batch batch = _state.Batches[index];
                batch.Pool = device.CreateCommandPool(queueIndex);
                GPUCommandBuffer commands = batch.Pool.BeginCommands();
                commands.EndCommands();
                batch.Pool.Reset();
            }
        }
        catch (GPUException)
        {
            Destroy();
            GC.SuppressFinalize(this);
            throw;
        }
    }

    ~UploadQueue()
    {
        Destroy();
    }

    /// <summary>Whether this queue has not been destroyed.</summary>
    public bool Valid => _state.Device != null;

    // Flushes and waits for uploads. Finish external submissions using returned timeline points before destruction or move assignment.
    public void Destroy()
    {
        Debug.Assert(!_state.InCallback);
        if (_state.Completion.Semaphore != null) Wait();
        if (_state.Batches != null)
            for (uint index = 0; index < _state.MaxPendingBatches; ++index)
                _state.Batches[index].Pool?.Destroy();
        _state.Batches = null;
        _state.Heap.Destroy();
        _state.Completion.Semaphore?.Destroy();
        _state = new State();
    }

    // Retrieves timestamps and reclaims completed batches without waiting or flushing pending uploads. Wait() also reclaims all batches.
    public void Reclaim()
    {
        State state = _state;
        Debug.Assert(!state.InCallback);
        if (state.RetirementCount == 0) return;
        ulong completed = state.Completion.Semaphore!.CompletedValue;
        while (state.RetirementCount != 0 && state.Batches![state.RetirementFirst].Value <= completed)
        {
            Batch batch = state.Batches[state.RetirementFirst];
            state.Tail = batch.End;
            batch.Pool!.ReadTimestamps();
            batch.Pool.Reset();
            state.RetirementFirst = (state.RetirementFirst + 1) % state.MaxPendingBatches;
            --state.RetirementCount;
        }
    }

    private void WaitOldest()
    {
        Debug.Assert(_state.RetirementCount != 0);
        new TimelinePoint { Semaphore = _state.Completion.Semaphore, Value = _state.Batches![_state.RetirementFirst].Value }.Wait();
        ++_state.Waits;
        Reclaim();
    }

    internal GPUAllocation<byte> Reserve(ulong byteSize)
    {
        State state = _state;
        GPUAllocation<byte> storage = state.Heap.Range;
        Debug.Assert(state.Completion.Semaphore != null && !state.InCallback && byteSize != 0 && byteSize <= storage.SizeBytes);
        if (state.OperationCount >= OperationLimit) Flush();
        for (;;)
        {
            ulong offset = (state.Head % storage.SizeBytes + Alignment - 1) & ~(Alignment - 1);
            if (byteSize > storage.SizeBytes - offset) offset = storage.SizeBytes;
            ulong start = state.Head - state.Head % storage.SizeBytes + offset;
            if (state.Head == state.Tail && state.RetirementCount == 0) { state.Head = start; state.Tail = start; }
            if (start + byteSize - state.Tail <= storage.SizeBytes)
            {
                state.Head = start + byteSize;
                if (state.Head - state.Tail > state.PeakBytes) state.PeakBytes = state.Head - state.Tail;
                ulong byteOffset = start % storage.SizeBytes;
                return new GPUAllocation<byte>
                {
                    Cpu = storage.Cpu == null ? null : storage.Cpu + byteOffset,
                    Buffer = storage.Buffer,
                    Offset = storage.Offset + byteOffset,
                    SizeBytes = byteSize,
                };
            }
            uint previousCount = state.RetirementCount;
            Reclaim();
            if (state.RetirementCount != previousCount) continue;
            if (state.Commands != null)
            {
                Flush();
                continue;
            }
            WaitOldest();
        }
    }

    internal GPUCommandBuffer Begin()
    {
        State state = _state;
        Debug.Assert(state.Completion.Semaphore != null && !state.InCallback);
        if (state.Commands == null)
        {
            if (state.RetirementCount == state.MaxPendingBatches)
            {
                Reclaim();
                if (state.RetirementCount == state.MaxPendingBatches) WaitOldest();
            }
            GPUCommandBuffer commands = state.Batches![(state.RetirementFirst + state.RetirementCount) % state.MaxPendingBatches].Pool!.BeginCommands();
            state.Commands = commands;
            commands.Barrier(Stage.AllCommands, state.QueueAccess, state.UploadStages, state.UploadAccess);
        }
        return state.Commands;
    }

    // Flush between overlapping transfer writes. Buffer copies follow CopyMemory's requirements.
    /// <summary>Stages buffer uploads immediately, splitting sources to fit the fixed capacity and waiting for staging reuse when needed.</summary>
    /// <param name="destination">The destination range, large enough to contain the source bytes.</param>
    /// <param name="source">The bytes copied immediately into staging.</param>
    public void UploadBuffer(GPURange destination, ReadOnlySpan<byte> source)
    {
        Debug.Assert(_state.Completion.Semaphore != null && !_state.InCallback && (ulong)source.Length <= destination.Size);
        fixed (byte* sourceData = source)
        {
            for (ulong offset = 0; offset < (ulong)source.Length;)
            {
                ulong size = (ulong)source.Length - offset < _state.Heap.Range.SizeBytes ? (ulong)source.Length - offset : _state.Heap.Range.SizeBytes;
                GPUAllocation<byte> staging = Reserve(size);
                System.Buffer.MemoryCopy(sourceData + offset, staging.Cpu, (long)size, (long)size);
                Begin().CopyMemory(staging.GPURange, destination.Slice(offset, size));
                ++_state.OperationCount;
                offset += size;
            }
        }
    }

    /// <summary>Records the one-time initialization of a newly placed texture; order it before that texture's uploads.</summary>
    /// <param name="texture">The newly placed texture to initialize on this queue.</param>
    public void InitializeTexture(GPUTexture texture)
    {
        if (_state.OperationCount >= OperationLimit) Flush();
        Begin().InitializeTexture(texture);
        ++_state.OperationCount;
    }

    /// <summary>Uploads a texture region; depth/stencil transfers cover complete mip subresources with zero texture origin.</summary>
    /// <param name="destination">The destination texture; combined depth/stencil formats are unsupported.</param>
    /// <param name="source">The region data including pitch padding, no larger than this queue's staging capacity.</param>
    /// <param name="copy">The texture region and memory pitches.</param>
    public void UploadTexture(GPUTexture destination, ReadOnlySpan<byte> source, in TextureCopyDesc copy = default)
    {
        Debug.Assert(!source.IsEmpty); // C++ asserts source.data != null
        GPUAllocation<byte> staging = Reserve((ulong)source.Length);
        fixed (byte* sourceData = source)
        {
            System.Buffer.MemoryCopy(sourceData, staging.Cpu, (long)staging.SizeBytes, (long)staging.SizeBytes);
        }
        Begin().CopyMemoryToTexture(staging.GPURange, destination, copy);
        ++_state.OperationCount;
    }

    // Requires a general or compute queue. The callback must not mutate the uploader or end/submit/retain its command buffer.
    // Staging cannot be used by later submissions. Bind required pipeline/heaps and record dependencies between dispatches inside the callback.
    /// <summary>Reserves staging with a 16-byte-aligned CPU mapping and buffer offset, then invokes the callback synchronously.</summary>
    /// <param name="byteSize">The positive staging size, no greater than this queue's capacity.</param>
    /// <param name="callback">Fills staging and records compute work; the reservation remains occupied until submission completes.</param>
    public void UploadWithCompute(ulong byteSize, Action<GPUCommandBuffer, GPUAllocation<byte>> callback)
    {
        GPUAllocation<byte> staging = BeginCompute(byteSize);
        callback(_state.Commands!, staging);
        EndCompute();
    }

    // The device's per-command-buffer timestamp limit applies to each batch. Keep CPU destinations valid until the batch is reclaimed.
    public void WriteTimestamp(Span<ulong> destination)
    {
        Begin().WriteTimestamp(destination, Stage.AllCommands);
    }

    // Returns the latest upload completion point, borrowing this uploader's semaphore. Other queues can consume it through SubmitDesc waits.
    public TimelinePoint Flush()
    {
        State state = _state;
        Debug.Assert(!state.InCallback);
        if (state.Commands == null) return state.Completion;
        Reclaim();
        state.Commands.Barrier(state.UploadStages, state.UploadAccess, Stage.AllCommands, state.QueueAccess);
        state.Commands.EndCommands();
        ++state.Completion.Value;
        if (state.SubmitCallback != null) state.SubmitCallback(state.SubmitContext, state.Device!, state.Commands, state.Completion, state.QueueIndex);
        else state.Device!.Submit(new SubmitDesc { Commands = [state.Commands], Completion = state.Completion }, state.QueueIndex);
        Batch batch = state.Batches![(state.RetirementFirst + state.RetirementCount) % state.MaxPendingBatches];
        batch.End = state.Head;
        batch.Value = state.Completion.Value;
        ++state.RetirementCount;
        ++state.Submissions;
        state.Commands = null;
        state.OperationCount = 0;
        return state.Completion;
    }

    public void SetSubmitCallback(SubmitCallback? callback, void* context = null)
    {
        Debug.Assert(!_state.InCallback);
        _state.SubmitCallback = callback;
        _state.SubmitContext = context;
    }

    public void Wait()
    {
        Flush();
        Reclaim();
        if (_state.RetirementCount != 0)
        {
            _state.Completion.Wait();
            ++_state.Waits;
            Reclaim();
        }
    }

    // Occupied bytes and pending batches reflect retirements observed by the most recent operation.
    public UploadQueueStats Stats
    {
        get
        {
            return new UploadQueueStats
            {
                Capacity = _state.Heap.Range.SizeBytes,
                BytesInUse = _state.Head - _state.Tail,
                PeakBytes = _state.PeakBytes,
                Submissions = _state.Submissions,
                Waits = _state.Waits,
                PendingBatches = _state.RetirementCount,
                PendingOperations = _state.OperationCount,
            };
        }
    }

    private GPUAllocation<byte> BeginCompute(ulong byteSize)
    {
        Debug.Assert(_state.UploadStages != Stage.Transfer, "compute uploads require a general or compute queue");
        GPUAllocation<byte> staging = Reserve(byteSize);
        Begin().Barrier(Stage.Transfer | Stage.Compute, Access.TransferWrite | Access.ShaderWrite,
            Stage.Compute, Access.ShaderRead | Access.ShaderWrite | Access.DescriptorRead);
        _state.InCallback = true;
        return staging;
    }

    private void EndCompute()
    {
        Debug.Assert(_state.InCallback);
        _state.Commands!.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer | Stage.Compute,
            Access.TransferRead | Access.TransferWrite | Access.ShaderRead | Access.ShaderWrite | Access.DescriptorRead);
        _state.InCallback = false;
        ++_state.OperationCount;
    }
}
