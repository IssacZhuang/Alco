// D3D12 command pools, enhanced-barrier recording, render passes, indirect commands, and allocation-free warm submission.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static D3D12.D3D12Native;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class D3D12Queue
{
    internal nint* CommandSubmission;
    internal uint CommandSubmissionCapacity;

    internal void DestroyCommandStorage()
    {
        NativeMemory.Free(CommandSubmission);
        CommandSubmission = null;
        CommandSubmissionCapacity = 0;
    }
}

internal sealed unsafe partial class D3D12Device
{
    /// <summary>Creates an independent command pool for the selected queue.</summary>
    /// <param name="queueIndex">The recording queue index.</param>
    /// <returns>The command pool.</returns>
    public override GPUCommandPool CreateCommandPool(uint queueIndex)
    {
        Debug.Assert(queueIndex < QueueCount);
        D3D12CommandPool pool = new() { Owner = this, QueueIndex = queueIndex };
        D3D12CommandBuffer commands = pool.CreateCommands();
        pool.First = commands;
        pool.Last = commands;
        pool.NextBuffer = commands;
        return pool;
    }

    /// <summary>Submits recorded command buffers without presenting.</summary>
    /// <param name="desc">The command buffers, GPU waits, and required completion point.</param>
    /// <param name="queueIndex">The submission queue index.</param>
    public override void Submit(SubmitDesc desc, uint queueIndex = 0) => SubmitCommands(desc, queueIndex, null);

    /// <summary>Submits work on queue zero and presents the selected swapchain.</summary>
    /// <param name="desc">The command buffers, GPU waits, and required completion point; commands may be empty.</param>
    /// <param name="swapchain">The acquired swapchain to present.</param>
    public override void SubmitAndPresent(SubmitDesc desc, GPUSwapchain swapchain)
    {
        D3D12Swapchain target = (D3D12Swapchain)swapchain;
        Debug.Assert(target.Owner == this);
        SubmitCommands(desc, 0, target);
    }

    private void SubmitCommands(SubmitDesc desc, uint queueIndex, D3D12Swapchain? present)
    {
        Debug.Assert(queueIndex < QueueCount);
        D3D12Queue queue = Queues[queueIndex];
        D3D12TimelineSemaphore completion = (D3D12TimelineSemaphore)desc.Completion.Semaphore!;
        Debug.Assert(completion.Owner == this);
        uint count = (uint)desc.Commands.Length;
        bool hasGpuWork = false;
        for (int i = 0; i < desc.Commands.Length; ++i)
        {
            D3D12CommandBuffer commands = (D3D12CommandBuffer)desc.Commands[i];
            Debug.Assert(commands.Owner == this && Queues[commands.Pool.QueueIndex].Type == queue.Type && commands.Ended && !commands.Submitted);
            Debug.Assert(commands.SwapchainCount == 0 || queueIndex == 0);
            hasGpuWork |= commands.HasGpuWork;
            if (commands.ResolvePending) ++count;
        }
        if (count > queue.CommandSubmissionCapacity)
        {
            queue.CommandSubmissionCapacity = Math.Max(count, Math.Max(4u, queue.CommandSubmissionCapacity * 2));
            queue.CommandSubmission = (nint*)NativeMemory.Realloc(queue.CommandSubmission, (nuint)queue.CommandSubmissionCapacity * (nuint)sizeof(nint));
        }
        for (int i = 0; i < desc.Waits.Length; ++i)
        {
            TimelinePoint point = desc.Waits[i];
            D3D12TimelineSemaphore wait = (D3D12TimelineSemaphore)point.Semaphore!;
            Debug.Assert(wait.Owner == this);
            D3D12Interop.CheckExecution(CommandWait(queue.Native, wait.Native, point.Value), "ID3D12CommandQueue.Wait");
        }
        uint next = (uint)desc.Commands.Length;
        for (int i = 0; i < desc.Commands.Length; ++i)
        {
            D3D12CommandBuffer commands = (D3D12CommandBuffer)desc.Commands[i];
            if (!hasGpuWork) commands.PrepareBarrierOnlySubmission();
            queue.CommandSubmission[i] = commands.Native;
            // Query resolves cannot intervene between a suspended pass and its resumption, even across native command lists.
            if (commands.ResolvePending) queue.CommandSubmission[next++] = commands.ResolveNative;
            if (commands.Swapchain != null)
            {
                Debug.Assert(queueIndex == 0 && commands.Swapchain.AcquireCommands == commands);
                commands.Swapchain.AcquireCommands = null;
                commands.Swapchain = null;
            }
        }
        if (count != 0) CommandExecute(queue.Native, count, queue.CommandSubmission);
        if (present != null)
        {
            present.PreparePresent();
            present.Present();
        }
        ulong retirement = ++queue.Value;
        D3D12Interop.CheckExecution(CommandSignal(queue.Native, queue.Completion, retirement), "ID3D12CommandQueue.Signal (retirement)");
        D3D12Interop.CheckExecution(CommandSignal(queue.Native, completion.Native, desc.Completion.Value), "ID3D12CommandQueue.Signal (completion)");
        for (int i = 0; i < desc.Commands.Length; ++i)
        {
            D3D12CommandBuffer commands = (D3D12CommandBuffer)desc.Commands[i];
            commands.Retirement = queue.Completion;
            commands.RetirementValue = retirement;
            commands.TimestampFrequency = queue.TimestampFrequency;
            commands.Submitted = true;
            for (uint j = 0; j < commands.SwapchainCount; ++j) commands.UsedSwapchains[j]!.MarkSubmitted(retirement);
        }
        present?.MarkSubmitted(retirement);
    }
}

internal sealed unsafe partial class D3D12CommandPool : GPUCommandPool
{
    internal required D3D12Device Owner;
    internal uint QueueIndex;
    internal D3D12CommandBuffer? First;
    internal D3D12CommandBuffer? Last;
    internal D3D12CommandBuffer? NextBuffer;
    private IndirectSignature* Signatures;
    private uint SignatureCount;
    private uint SignatureCapacity;

    private struct IndirectSignature
    {
        internal uint Type;
        internal uint Stride;
        internal nint Native;
    }

    internal D3D12CommandBuffer CreateCommands()
    {
        D3D12CommandBuffer commands = new() { Owner = Owner, Pool = this };
        bool created = false;
        try
        {
            nint allocator = 0;
            D3D12Interop.Check(CommandCreateAllocator(Owner.Native, Owner.Queues[QueueIndex].Type, &allocator), "CreateCommandAllocator");
            commands.Allocator = allocator;
            nint native = 0;
            D3D12Interop.Check(CommandCreateList(Owner.Native, Owner.Queues[QueueIndex].Type, allocator, &native), "CreateCommandList8");
            commands.Native = native;
            D3D12Interop.Check(CommandClose(native), "ID3D12GraphicsCommandList.Close (creation)");
            if (Owner.Queues[QueueIndex].Type == 0)
                commands.ResolveParameters = (CommandResolveSubresource*)NativeMemory.Alloc(8, (nuint)sizeof(CommandResolveSubresource));
            if (Owner.TimestampQueryCount != 0 && Owner.Queues[QueueIndex].TimestampFrequency != 0)
            {
                nint heap = 0;
                D3D12Interop.Check(CommandCreateQueryHeap(Owner.Native, Owner.Queues[QueueIndex].Type, Owner.TimestampQueryCount, &heap),
                    "CreateQueryHeap (timestamps)");
                commands.TimestampHeap = heap;
                nint readback = 0;
                D3D12Interop.Check(CommandCreateReadback(Owner.Native, (ulong)Owner.TimestampQueryCount * sizeof(ulong), &readback),
                    "CreateCommittedResource (timestamp readback)");
                commands.TimestampReadback = readback;
                void* mapping = null;
                D3D12Interop.Check(CommandMapReadback(readback, (nuint)Owner.TimestampQueryCount * sizeof(ulong), &mapping), "Map (timestamp readback)");
                commands.TimestampResults = (ulong*)mapping;
                commands.TimestampDestinations = (ulong**)NativeMemory.Alloc((nuint)Owner.TimestampQueryCount, (nuint)sizeof(ulong*));
            }
            created = true;
            return commands;
        }
        finally
        {
            if (!created) commands.DestroyStorage();
        }
    }

    internal nint GetSignature(uint type, uint stride)
    {
        for (uint i = 0; i < SignatureCount; ++i)
            if (Signatures[i].Type == type && Signatures[i].Stride == stride) return Signatures[i].Native;
        nint signature = 0;
        D3D12Interop.Check(CommandCreateSignature(Owner.Native, type, stride, &signature), "CreateCommandSignature");
        if (SignatureCount == SignatureCapacity)
        {
            SignatureCapacity = Math.Max(4u, SignatureCapacity * 2);
            Signatures = (IndirectSignature*)NativeMemory.Realloc(Signatures, (nuint)SignatureCapacity * (nuint)sizeof(IndirectSignature));
        }
        Signatures[SignatureCount++] = new IndirectSignature { Type = type, Stride = stride, Native = signature };
        return signature;
    }

    /// <summary>Begins recording a primary command buffer.</summary>
    /// <returns>The recording command buffer.</returns>
    public override GPUCommandBuffer BeginCommands()
    {
        D3D12CommandBuffer? commands = NextBuffer;
        if (commands == null)
        {
            commands = CreateCommands();
            if (Last != null) Last.Next = commands;
            else First = commands;
            Last = commands;
        }
        Debug.Assert(!commands.Recording && !commands.Ended && commands.Retirement == 0);
        D3D12Interop.Check(CommandResetList(commands.Native, commands.Allocator), "ID3D12GraphicsCommandList.Reset");
        NextBuffer = commands.Next;
        commands.TimestampCount = 0;
        commands.Pipeline = null;
        commands.NativePipeline = 0;
        commands.PipelineDirty = true;
        commands.BoundRoot = 0;
        commands.DepthState = new DepthStencilState();
        commands.HasIndices = false;
        commands.BoundIndices = default;
        commands.ResolvePending = false;
        commands.Suspended = false;
        commands.HasGpuWork = false;
        commands.PrefixCount = 0;
        commands.Recording = true;
        if (Owner.Queues[QueueIndex].Type != 3)
        {
            commands.ResourceHeap = Owner.DefaultResourceHeap;
            commands.SamplerHeap = Owner.DefaultSamplerHeap;
            commands.BindHeaps();
        }
        return commands;
    }

    /// <summary>Recycles command storage after every submitted use has completed.</summary>
    public override void Reset()
    {
        for (D3D12CommandBuffer? commands = First; commands != null; commands = commands.Next)
        {
            Debug.Assert(commands.Retirement == 0 || D3D12Interop.CompletedValue(Owner, commands.Retirement) >= commands.RetirementValue);
            if (commands.Recording)
            {
                if (commands.InRenderPass) CommandEndPass(commands.Native);
                D3D12Interop.CheckExecution(CommandClose(commands.Native), "ID3D12GraphicsCommandList.Close (discard)");
            }
            if (commands.Swapchain != null) commands.CancelSwapchainAcquire(commands.Swapchain);
            D3D12Interop.CheckExecution(CommandResetAllocator(commands.Allocator), "ID3D12CommandAllocator.Reset");
            Array.Clear(commands.UsedSwapchains, 0, (int)commands.SwapchainCount);
            Array.Clear(commands.ColorViews);
            Array.Clear(commands.ResolveViews);
            commands.SwapchainCount = 0;
            commands.DepthView = null;
            commands.StencilView = null;
            commands.Pipeline = null;
            commands.HasIndices = false;
            commands.BoundIndices = default;
            commands.TimestampCount = 0;
            commands.Retirement = 0;
            commands.RetirementValue = 0;
            commands.Recording = false;
            commands.Ended = false;
            commands.Submitted = false;
            commands.InRenderPass = false;
            commands.HasGpuWork = false;
            commands.PrefixCount = 0;
        }
        NextBuffer = First;
    }

    /// <summary>Copies completed query results into their stationary CPU destinations.</summary>
    public override void ReadTimestamps()
    {
        ulong referenceFrequency = Owner.Queues[0].TimestampFrequency;
        for (D3D12CommandBuffer? commands = First; commands != null; commands = commands.Next)
        {
            if (!commands.Submitted || commands.TimestampCount == 0) continue;
            Debug.Assert(D3D12Interop.CompletedValue(Owner, commands.Retirement) >= commands.RetirementValue);
            ulong frequency = commands.TimestampFrequency;
            if (frequency == referenceFrequency)
            {
                for (uint i = 0; i < commands.TimestampCount; ++i) *commands.TimestampDestinations[i] = commands.TimestampResults[i];
            }
            else
            {
                for (uint i = 0; i < commands.TimestampCount; ++i)
                    *commands.TimestampDestinations[i] = (ulong)((UInt128)commands.TimestampResults[i] * referenceFrequency / frequency);
            }
            commands.TimestampCount = 0;
        }
    }

    /// <summary>Releases the pool after all submitted command buffers have retired.</summary>
    public override void Destroy()
    {
        for (D3D12CommandBuffer? commands = First; commands != null;)
        {
            Debug.Assert(commands.Retirement == 0 || D3D12Interop.CompletedValue(Owner, commands.Retirement) >= commands.RetirementValue);
            D3D12CommandBuffer? next = commands.Next;
            if (commands.Swapchain != null) commands.CancelSwapchainAcquire(commands.Swapchain);
            commands.DestroyStorage();
            commands = next;
        }
        for (uint i = 0; i < SignatureCount; ++i) Release(Signatures[i].Native);
        NativeMemory.Free(Signatures);
        Signatures = null;
        SignatureCount = SignatureCapacity = 0;
        First = Last = NextBuffer = null;
    }
}

internal sealed unsafe partial class D3D12CommandBuffer : GPUCommandBuffer
{
    internal required D3D12Device Owner;
    internal required D3D12CommandPool Pool;
    internal D3D12CommandBuffer? Next;
    internal nint Native;
    internal nint Allocator;
    internal nint ResolveNative;
    internal nint TimestampHeap;
    internal nint TimestampReadback;
    internal ulong* TimestampResults;
    internal ulong** TimestampDestinations;
    internal ulong TimestampFrequency;
    internal nint Retirement;
    internal ulong RetirementValue;
    internal bool Recording;
    internal bool Ended;
    internal bool Submitted;
    internal bool InRenderPass;
    internal bool Suspended;
    internal bool ResolvePending;
    internal bool HasGpuWork;
    internal uint PrefixCount;
    private CommandTextureBarrier* PrefixBarriers;
    private uint PrefixCapacity;
    internal D3D12Swapchain? Swapchain;
    internal D3D12Swapchain?[] UsedSwapchains = [];
    internal uint SwapchainCount;
    internal D3D12Pso? Pipeline;
    internal nint NativePipeline;
    internal bool PipelineDirty;
    internal ulong BoundRoot;
    internal bool HasIndices;
    internal nint ResourceHeap;
    internal nint SamplerHeap;
    internal readonly D3D12RenderView?[] ColorViews = new D3D12RenderView?[8];
    internal readonly D3D12RenderView?[] ResolveViews = new D3D12RenderView?[8];
    // EndRenderPass still reads the subresource parameters supplied at BeginRenderPass.
    internal CommandResolveSubresource* ResolveParameters;
    internal D3D12RenderView? DepthView;
    internal D3D12RenderView? StencilView;
    private uint ColorCount;
    private RenderingFlags RenderFlags;
    internal DepthStencilState DepthState;
    private IndexType PipelineIndexType;
    internal GPURange BoundIndices;
    private IndexType BoundIndexType;

    internal void DestroyStorage()
    {
        Release(ResolveNative);
        Release(Native);
        Release(Allocator);
        Release(TimestampHeap);
        if (TimestampResults != null) CommandUnmapReadback(TimestampReadback);
        Release(TimestampReadback);
        NativeMemory.Free(TimestampDestinations);
        NativeMemory.Free(PrefixBarriers);
        NativeMemory.Free(ResolveParameters);
        ResolveNative = Native = Allocator = TimestampHeap = TimestampReadback = 0;
        TimestampResults = null;
        TimestampDestinations = null;
        PrefixBarriers = null;
        ResolveParameters = null;
        PrefixCount = PrefixCapacity = 0;
    }

    internal void RecordTextureBarriers(CommandTextureBarrier* barriers, uint count)
    {
        if (!HasGpuWork)
        {
            uint required = PrefixCount + count;
            if (required > PrefixCapacity)
            {
                PrefixCapacity = Math.Max(required, Math.Max(4u, PrefixCapacity * 2));
                PrefixBarriers = (CommandTextureBarrier*)NativeMemory.Realloc(PrefixBarriers, (nuint)PrefixCapacity * (nuint)sizeof(CommandTextureBarrier));
            }
            Backend.NativeCopy(PrefixBarriers + PrefixCount, barriers, (nuint)count * (nuint)sizeof(CommandTextureBarrier));
            PrefixCount = required;
        }
        CommandBarrierGroup group = new() { Type = 1, Count = count, Barriers = barriers };
        CommandBarriers(Native, &group);
    }

    internal void PrepareBarrierOnlySubmission()
    {
        Debug.Assert(Ended && !Submitted && !HasGpuWork);
        D3D12Interop.Check(CommandResetList(Native, Allocator), "ID3D12GraphicsCommandList.Reset (barrier-only submission)");
        // With no work anywhere in this ExecuteCommandLists scope, only texture layout and metadata operations remain meaningful.
        for (uint i = 0; i < PrefixCount; ++i)
        {
            CommandTextureBarrier* source = PrefixBarriers + i;
            CommandTextureBarrier barrier = new()
            {
                AccessBefore = CommandNoAccess,
                AccessAfter = CommandNoAccess,
                LayoutBefore = source->LayoutBefore,
                LayoutAfter = source->LayoutAfter,
                Resource = source->Resource,
                Subresources = source->Subresources,
                Flags = source->Flags,
            };
            CommandBarrierGroup group = new() { Type = 1, Count = 1, Barriers = &barrier };
            CommandBarriers(Native, &group);
        }
        D3D12Interop.CheckExecution(CommandClose(Native), "ID3D12GraphicsCommandList.Close (barrier-only submission)");
    }

    internal void CancelSwapchainAcquire(D3D12Swapchain swapchain)
    {
        if (Swapchain != swapchain) return;
        if (swapchain.AcquireCommands == this) swapchain.AcquireCommands = null;
        Swapchain = null;
    }

    internal void UseSwapchain(D3D12Swapchain swapchain)
    {
        Debug.Assert(Pool.QueueIndex == 0 && swapchain.Owner == Owner);
        for (uint i = 0; i < SwapchainCount; ++i)
            if (UsedSwapchains[i] == swapchain) return;
        if (SwapchainCount == UsedSwapchains.Length) Array.Resize(ref UsedSwapchains, Math.Max(4, UsedSwapchains.Length * 2));
        UsedSwapchains[SwapchainCount++] = swapchain;
    }

    internal void BindHeaps()
    {
        nint* heaps = stackalloc nint[2];
        heaps[0] = ResourceHeap;
        heaps[1] = SamplerHeap;
        CommandSetHeaps(Native, 2, heaps);
        // Direct-indexed heaps must be bound before the root signature; heap changes must retain the root SRV on both bind points.
        CommandRootSignature(Native, Owner.RootSignature, false);
        CommandRootAddress(Native, BoundRoot, false);
        if (Owner.Queues[Pool.QueueIndex].Type == 0)
        {
            CommandRootSignature(Native, Owner.RootSignature, true);
            CommandRootAddress(Native, BoundRoot, true);
        }
    }

    /// <summary>Acquires an image and records its initial queue-zero transition.</summary>
    /// <param name="swapchain">The swapchain to acquire.</param>
    /// <returns>The borrowed frame, or an empty frame while unavailable.</returns>
    public override SwapchainFrame Acquire(GPUSwapchain swapchain)
    {
        Debug.Assert(Recording && !InRenderPass && Pool.QueueIndex == 0 && Swapchain == null);
        return ((D3D12Swapchain)swapchain).Acquire(this);
    }

    /// <summary>Records a timestamp for readback after GPU completion.</summary>
    /// <param name="destination">Stationary CPU storage retained until the pool reads its timestamps.</param>
    /// <param name="stage">The pipeline stage whose completion precedes the timestamp.</param>
    public override void WriteTimestamp(Span<ulong> destination, Stage stage = Stage.AllCommands)
    {
        if (TimestampHeap == 0) return;
        Debug.Assert(Recording && !destination.IsEmpty && TimestampCount < Owner.TimestampQueryCount);
        TimestampDestinations[TimestampCount] = (ulong*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination));
        HasGpuWork = true;
        CommandTimestamp(Native, TimestampHeap, TimestampCount++);
    }

    /// <summary>Binds a graphics, mesh, or compute pipeline without changing root storage.</summary>
    /// <param name="pipeline">The pipeline to bind.</param>
    public override void BindPipeline(GPUPipeline pipeline)
    {
        D3D12Pso target = (D3D12Pso)pipeline;
        Debug.Assert(Recording && target.Owner == Owner && Owner.Queues[Pool.QueueIndex].Type != 3);
        Pipeline = target;
        PipelineDirty = true;
        if (target.Compute)
        {
            Debug.Assert(!InRenderPass);
            BindNativePipeline(IndexType.UInt16);
        }
        else if (!target.Mesh) CommandSetTopology(Native, target.NativeTopology);
    }

    /// <summary>Binds a texture heap together with the current sampler heap.</summary>
    /// <param name="heap">The texture descriptor heap.</param>
    public override void SetTextureDescriptorHeap(GPUTextureDescriptorHeap heap)
    {
        D3D12TextureDescriptorHeap target = (D3D12TextureDescriptorHeap)heap;
        Debug.Assert(Recording && target.Owner == Owner && Owner.Queues[Pool.QueueIndex].Type != 3);
        if (ResourceHeap == target.Native) return;
        ResourceHeap = target.Native;
        BindHeaps();
    }

    /// <summary>Binds a sampler heap together with the current resource heap.</summary>
    /// <param name="heap">The sampler descriptor heap.</param>
    public override void SetSamplerDescriptorHeap(GPUSamplerDescriptorHeap heap)
    {
        D3D12SamplerDescriptorHeap target = (D3D12SamplerDescriptorHeap)heap;
        Debug.Assert(Recording && target.Owner == Owner && Owner.Queues[Pool.QueueIndex].Type != 3);
        if (SamplerHeap == target.Native) return;
        SamplerHeap = target.Native;
        BindHeaps();
    }

    /// <summary>Inserts an enhanced global execution and memory dependency.</summary>
    /// <param name="before">The source stages, including logically earlier stages.</param>
    /// <param name="beforeAccess">The source accesses.</param>
    /// <param name="after">The destination stages, including logically later stages.</param>
    /// <param name="afterAccess">The destination accesses.</param>
    public override void Barrier(Stage before, Access beforeAccess, Stage after, Access afterAccess)
    {
        Debug.Assert(Recording);
        uint source = BarrierStages(before, true);
        uint destination = BarrierStages(after, false);
        // Queue completion makes readbacks host-visible; a host-only destination has no subsequent GPU scope.
        if (source == 0 || destination == 0) return;
        uint sourceAccess = BarrierAccess(beforeAccess);
        uint destinationAccess = BarrierAccess(afterAccess);
        CommandGlobalBarrier* barriers = stackalloc CommandGlobalBarrier[4];
        uint count = 0;
        bool execution = false;
        uint classes = InRenderPass ? 3u : 1u;
        for (uint i = 0; i < classes; ++i)
        {
            // Global barriers cannot combine accesses that require different texture layouts.
            uint mask = i == 0 ? ~(CommandNoAccess | 0x28u) : i == 1 ? 8u : 0x20u;
            uint from = sourceAccess & mask;
            uint to = destinationAccess & mask;
            if (from == 0 && to == 0) continue;
            // A depth-write release must retain the writable layout and depth-cache scope.
            bool depthRelease = i == 2 && from != 0 && to == 0;
            if (depthRelease) to = from;
            if (from == 0) from = CommandNoAccess;
            if (to == 0) to = CommandNoAccess;
            uint memorySource = depthRelease ? CommandSyncDepth : MemoryBarrierStages(source, from);
            uint memoryDestination = depthRelease ? CommandSyncDepth : MemoryBarrierStages(destination, to);
            barriers[count++] = new CommandGlobalBarrier
            {
                SyncBefore = memorySource,
                SyncAfter = memoryDestination,
                AccessBefore = from,
                AccessAfter = to,
            };
            execution |= memorySource != source || memoryDestination != destination;
        }
        if (execution || count == 0)
        {
            // Logical execution scopes include stages without these accesses; native memory scopes cannot include those extra stages.
            barriers[count++] = new CommandGlobalBarrier
            {
                SyncBefore = source,
                SyncAfter = destination,
                AccessBefore = CommandNoAccess,
                AccessAfter = CommandNoAccess,
            };
        }
        CommandBarrierGroup group = new() { Count = count, Barriers = barriers };
        CommandBarriers(Native, &group);
    }

    /// <summary>Records a non-overlapping buffer copy.</summary>
    /// <param name="source">The source buffer range.</param>
    /// <param name="destination">The destination range, at least as large as the source.</param>
    public override void CopyMemory(GPURange source, GPURange destination)
    {
        Debug.Assert(Recording && !InRenderPass && source.Size <= destination.Size);
        D3D12GpuHeapOwner from = (D3D12GpuHeapOwner)source.Buffer!;
        D3D12GpuHeapOwner to = (D3D12GpuHeapOwner)destination.Buffer!;
        Debug.Assert(from.Device == Owner && to.Device == Owner);
        HasGpuWork = true;
        CommandCopyBuffer(Native, to.Native, destination.Offset, from.Native, source.Offset, source.Size);
    }

    /// <summary>Uploads a texture region using the caller's native byte pitches.</summary>
    /// <param name="source">The source buffer range.</param>
    /// <param name="destination">The destination texture.</param>
    /// <param name="copy">The region and source pitches; depth/stencil copies select complete mip levels.</param>
    public override void CopyMemoryToTexture(GPURange source, GPUTexture destination, TextureCopyDesc copy = default) =>
        CopyBufferTexture(source, (D3D12Texture)destination, copy, true);

    /// <summary>Reads a texture region into the caller's buffer layout.</summary>
    /// <param name="source">The source texture.</param>
    /// <param name="destination">The destination buffer range.</param>
    /// <param name="copy">The region and destination pitches; depth/stencil copies select complete mip levels.</param>
    public override void CopyTextureToMemory(GPUTexture source, GPURange destination, TextureCopyDesc copy = default) =>
        CopyBufferTexture(destination, (D3D12Texture)source, copy, false);

    /// <summary>Records a non-overlapping texture-region copy in COMMON layout.</summary>
    /// <param name="source">The initialized source texture.</param>
    /// <param name="destination">The initialized destination texture with matching format, type, and selected mip extent.</param>
    /// <param name="copy">The regions; depth/stencil copies select complete mip levels and exclude combined formats.</param>
    public override void CopyTexture(GPUTexture source, GPUTexture destination, TextureRegionCopyDesc copy = default)
    {
        Debug.Assert(Recording && !InRenderPass);
        D3D12Texture from = (D3D12Texture)source;
        D3D12Texture to = (D3D12Texture)destination;
        UseTexture(from);
        UseTexture(to);
        Debug.Assert(from.Desc.Format == to.Desc.Format && from.Desc.Type == to.Desc.Type);
        Extent3D mip = MipExtent(from.Desc, copy.SourceMipLevel);
        Extent3D destinationMip = MipExtent(to.Desc, copy.DestinationMipLevel);
        Debug.Assert(mip.X == destinationMip.X && mip.Y == destinationMip.Y && mip.Z == destinationMip.Z);
        Extent3D extent = new()
        {
            X = copy.Extent.X != 0 ? copy.Extent.X : mip.X - copy.SourceOffset.X,
            Y = copy.Extent.Y != 0 ? copy.Extent.Y : mip.Y - copy.SourceOffset.Y,
            Z = copy.Extent.Z != 0 ? copy.Extent.Z : mip.Z - copy.SourceOffset.Z,
        };
        Backend.AssertTextureCopyRegion(from.Desc.Format, mip, copy.SourceOffset, extent);
        Backend.AssertTextureCopyRegion(to.Desc.Format, destinationMip, copy.DestinationOffset, extent);
        uint slices = copy.SliceCount != 0 ? copy.SliceCount : from.Desc.LayerCount - copy.SourceBaseSlice;
        Debug.Assert(slices != 0 && copy.SourceBaseSlice + slices <= from.Desc.LayerCount && copy.DestinationBaseSlice + slices <= to.Desc.LayerCount);
        Debug.Assert(from.Desc.Type != TextureType.ThreeD || (copy.SourceBaseSlice == 0 && copy.DestinationBaseSlice == 0 && slices == 1));
        AssertNonoverlap(from, to, copy, extent, slices);
        TextureFormatInfo info = Gpu.GetTextureFormatInfo(from.Desc.Format);
        bool wholeSource = copy.SourceOffset.X == 0 && copy.SourceOffset.Y == 0 && copy.SourceOffset.Z == 0 &&
            extent.X == mip.X && extent.Y == mip.Y && extent.Z == mip.Z;
        // Native compressed subresources include physical edge blocks beyond the logical mip dimensions.
        CommandBox box = new()
        {
            Left = copy.SourceOffset.X,
            Top = copy.SourceOffset.Y,
            Front = copy.SourceOffset.Z,
            Right = (uint)Backend.AlignUp(copy.SourceOffset.X + extent.X, info.BlockExtent.X),
            Bottom = (uint)Backend.AlignUp(copy.SourceOffset.Y + extent.Y, info.BlockExtent.Y),
            Back = copy.SourceOffset.Z + extent.Z,
        };
        HasGpuWork = true;
        for (uint i = 0; i < slices; ++i)
        {
            CommandCopyLocation sourceLocation = new()
            {
                Resource = from.Native,
                Subresource = TextureSubresource(from.Desc, copy.SourceMipLevel, copy.SourceBaseSlice + i),
            };
            CommandCopyLocation destinationLocation = new()
            {
                Resource = to.Native,
                Subresource = TextureSubresource(to.Desc, copy.DestinationMipLevel, copy.DestinationBaseSlice + i),
            };
            CommandCopyTexture(Native, &destinationLocation, copy.DestinationOffset.X, copy.DestinationOffset.Y, copy.DestinationOffset.Z,
                &sourceLocation, wholeSource || info.Depth || info.Stencil ? null : &box);
        }
    }

    /// <summary>Begins a native render pass with attachment-local layout transitions.</summary>
    /// <param name="desc">The attachment views and load/store operations.</param>
    /// <param name="flags">The suspension and resumption flags.</param>
    public override void BeginRenderPass(RenderingDesc desc, RenderingFlags flags = RenderingFlags.None)
    {
        Debug.Assert(Recording && !InRenderPass && Owner.Queues[Pool.QueueIndex].Type == 0 && desc.Colors.Length <= 8);
        ColorCount = (uint)desc.Colors.Length;
        DepthView = (D3D12RenderView?)desc.Depth.RenderView;
        StencilView = (D3D12RenderView?)desc.Stencil.RenderView;
        D3D12RenderView? area = ColorCount != 0 ? (D3D12RenderView?)desc.Colors[0].RenderView : DepthView ?? StencilView;
        Debug.Assert(area != null);
        Debug.Assert(DepthView == null || StencilView == null || (DepthView.Texture == StencilView.Texture &&
            DepthView.Mip == StencilView.Mip && DepthView.Slice == StencilView.Slice));
        CommandRenderTarget* colors = stackalloc CommandRenderTarget[8];
        for (uint i = 0; i < ColorCount; ++i)
        {
            ColorAttachment attachment = desc.Colors[(int)i];
            D3D12RenderView view = (D3D12RenderView)attachment.RenderView!;
            UseTexture(view.Texture);
            ColorViews[i] = view;
            int format = D3D12Interop.Format(view.Texture.Desc.Format);
            CommandEndingAccess ending = new() { Type = attachment.Store == StoreOp.Store ? 1u : 0u };
            D3D12RenderView? resolve = (D3D12RenderView?)attachment.ResolveView;
            if (resolve != null)
            {
                Debug.Assert(view.Texture.Desc.SampleCount == 4 && resolve.Texture.Desc.SampleCount == 1);
                Debug.Assert(resolve.Texture.Desc.Format == view.Texture.Desc.Format && Backend.SupportsColorResolve(view.Texture.Desc.Format));
                Debug.Assert(flags == RenderingFlags.None, "resolving attachments requires an uninterrupted pass");
                Debug.Assert(view.Width == area.Width && view.Height == area.Height && resolve.Width == area.Width && resolve.Height == area.Height);
                Debug.Assert(Backend.IsColorResolveTarget(resolve.Texture.Desc.Type, resolve.Texture.Desc.Usage),
                    "resolve targets are 2D color attachments without storage usage");
                UseTexture(resolve.Texture);
                ResolveViews[i] = resolve;
                ResolveParameters[i] = new CommandResolveSubresource
                {
                    SourceSubresource = TextureSubresource(view.Texture.Desc, view.Mip, view.Slice),
                    DestinationSubresource = TextureSubresource(resolve.Texture.Desc, resolve.Mip, resolve.Slice),
                    SourceRect = new CommandRect { Right = (int)view.Width, Bottom = (int)view.Height },
                };
                ending = new CommandEndingAccess
                {
                    Type = 2,
                    Resolve = new CommandEndingResolve
                    {
                        Source = view.Texture.Native,
                        Destination = resolve.Texture.Native,
                        SubresourceCount = 1,
                        Subresources = ResolveParameters + i,
                        Format = format,
                        ResolveMode = 3,
                        PreserveSource = attachment.Store == StoreOp.Store ? 1 : 0,
                    },
                };
            }
            colors[i] = new CommandRenderTarget
            {
                Handle = view.Handle,
                Beginning = new CommandBeginningAccess
                {
                    Type = BeginningAccess(attachment.Load),
                    Format = format,
                    Color = attachment.Clear,
                },
                Ending = ending,
            };
        }
        D3D12RenderView? depthStencilView = DepthView ?? StencilView;
        CommandDepthStencil depthStencil = new()
        {
            DepthBeginning = new CommandBeginningAccess { Type = 3 },
            StencilBeginning = new CommandBeginningAccess { Type = 3 },
            DepthEnding = new CommandEndingAccess { Type = 3 },
            StencilEnding = new CommandEndingAccess { Type = 3 },
        };
        if (depthStencilView != null)
        {
            UseTexture(depthStencilView.Texture);
            depthStencil.Handle = depthStencilView.Handle;
            int format = D3D12Interop.Format(depthStencilView.Texture.Desc.Format);
            if (DepthView != null)
            {
                depthStencil.DepthBeginning = new CommandBeginningAccess
                {
                    Type = BeginningAccess(desc.Depth.Load),
                    Format = format,
                    Depth = desc.Depth.Clear,
                };
                depthStencil.DepthEnding = new CommandEndingAccess { Type = desc.Depth.Store == StoreOp.Store ? 1u : 0u };
            }
            if (StencilView != null)
            {
                depthStencil.StencilBeginning = new CommandBeginningAccess
                {
                    Type = BeginningAccess(desc.Stencil.Load),
                    Format = format,
                    Stencil = desc.Stencil.Clear,
                };
                depthStencil.StencilEnding = new CommandEndingAccess { Type = desc.Stencil.Store == StoreOp.Store ? 1u : 0u };
            }
        }
        RenderFlags = flags;
        HasGpuWork = true;
        if ((flags & RenderingFlags.Resuming) == 0) TransitionAttachments(true);
        CommandBeginPass(Native, ColorCount, colors, depthStencilView != null ? &depthStencil : null, (uint)flags | 1u);
        InRenderPass = true;
        Suspended = false;
        DepthState = new DepthStencilState();
        PipelineDirty = true;
        SetViewport(new Viewport { Width = area.Width, Height = area.Height });
        SetScissor(new Scissor { Width = area.Width, Height = area.Height });
        CommandSetStencil(Native, 0, 0);
    }

    /// <summary>Ends the render pass, retaining attachment layouts when it is suspended.</summary>
    public override void EndRenderPass()
    {
        Debug.Assert(Recording && InRenderPass);
        CommandEndPass(Native);
        InRenderPass = false;
        Suspended = (RenderFlags & RenderingFlags.Suspending) != 0;
        if (!Suspended) TransitionAttachments(false);
        Array.Clear(ColorViews, 0, (int)ColorCount);
        Array.Clear(ResolveViews, 0, (int)ColorCount);
        ColorCount = 0;
        DepthView = StencilView = null;
    }

    /// <summary>Sets the viewport rectangle and depth range.</summary>
    /// <param name="viewport">The viewport rectangle.</param>
    public override void SetViewport(Viewport viewport)
    {
        Debug.Assert(Recording);
        CommandViewport native = new()
        {
            X = viewport.X,
            Y = viewport.Y,
            Width = viewport.Width,
            Height = viewport.Height,
            MinDepth = viewport.MinDepth,
            MaxDepth = viewport.MaxDepth,
        };
        CommandSetViewport(Native, &native);
    }

    /// <summary>Sets the scissor rectangle.</summary>
    /// <param name="scissor">The scissor rectangle.</param>
    public override void SetScissor(Scissor scissor)
    {
        Debug.Assert(Recording);
        CommandRect native = new()
        {
            Left = scissor.X,
            Top = scissor.Y,
            Right = scissor.X + (int)scissor.Width,
            Bottom = scissor.Y + (int)scissor.Height,
        };
        CommandSetScissor(Native, &native);
    }

    /// <summary>Sets depth and independent front/back stencil tests for subsequent draws.</summary>
    /// <param name="state">The depth and stencil test state.</param>
    public override void SetDepthStencil(DepthStencilState state)
    {
        Debug.Assert(Recording);
        DepthState = state;
        PipelineDirty = true;
        if (InRenderPass) CommandSetStencil(Native, state.Front.Reference, state.Back.Reference);
    }

    /// <summary>Records a non-indexed draw.</summary>
    /// <param name="root">The root range, or default to preserve its binding.</param>
    /// <param name="vertexCount">The vertex count per instance.</param>
    /// <param name="instanceCount">The instance count.</param>
    /// <param name="firstVertex">The first vertex index.</param>
    /// <param name="firstInstance">The first instance index.</param>
    public override void Draw(GPURange root, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        Debug.Assert(Recording && InRenderPass && Pipeline != null && !Pipeline.Compute && !Pipeline.Mesh);
        BindRoot(root);
        BindNativePipeline(IndexType.UInt16);
        HasGpuWork = true;
        CommandDraw(Native, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    /// <summary>Records an indexed draw with a cached native index-buffer binding.</summary>
    /// <param name="root">The root range, or default to preserve its binding.</param>
    /// <param name="indices">The GPU index-buffer range.</param>
    /// <param name="type">The index element type.</param>
    /// <param name="indexCount">The index count per instance.</param>
    /// <param name="instanceCount">The instance count.</param>
    /// <param name="firstIndex">The first index-buffer element.</param>
    /// <param name="vertexOffset">The base vertex offset.</param>
    /// <param name="firstInstance">The first instance index.</param>
    public override void DrawIndexed(GPURange root, GPURange indices, IndexType type, uint indexCount, uint instanceCount = 1,
        uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
    {
        Debug.Assert(Recording && InRenderPass && Pipeline != null && !Pipeline.Compute && !Pipeline.Mesh);
        BindRoot(root);
        BindIndices(indices, type);
        BindNativePipeline(type);
        HasGpuWork = true;
        CommandDrawIndexed(Native, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    /// <summary>Records indirect draws with an arbitrary native record stride.</summary>
    /// <param name="root">The root range, or default to preserve its binding.</param>
    /// <param name="arguments">The GPU draw-argument records.</param>
    /// <param name="drawCount">The number of draw records.</param>
    /// <param name="stride">The record stride; zero selects 16 bytes.</param>
    public override void DrawIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0)
    {
        Debug.Assert(Recording && InRenderPass && Pipeline != null && !Pipeline.Compute && !Pipeline.Mesh);
        BindRoot(root);
        BindNativePipeline(IndexType.UInt16);
        ExecuteIndirect(arguments, 0, drawCount, stride == 0 ? 16u : stride);
    }

    /// <summary>Records indexed indirect draws with an arbitrary native record stride.</summary>
    /// <param name="root">The root range, or default to preserve its binding.</param>
    /// <param name="indices">The GPU index-buffer range.</param>
    /// <param name="type">The index element type.</param>
    /// <param name="arguments">The GPU indexed-draw argument records.</param>
    /// <param name="drawCount">The number of draw records.</param>
    /// <param name="stride">The record stride; zero selects 20 bytes.</param>
    public override void DrawIndexedIndirect(GPURange root, GPURange indices, IndexType type, GPURange arguments, uint drawCount = 1, uint stride = 0)
    {
        Debug.Assert(Recording && InRenderPass && Pipeline != null && !Pipeline.Compute && !Pipeline.Mesh);
        BindRoot(root);
        BindIndices(indices, type);
        BindNativePipeline(type);
        ExecuteIndirect(arguments, 1, drawCount, stride == 0 ? 20u : stride);
    }

    /// <summary>Records a compute dispatch.</summary>
    /// <param name="root">The root range, or default to preserve its binding.</param>
    /// <param name="groupCount">The workgroup count in each dimension.</param>
    public override void Dispatch(GPURange root, Extent3D groupCount)
    {
        Debug.Assert(Recording && !InRenderPass && Pipeline != null && Pipeline.Compute);
        BindRoot(root);
        BindNativePipeline(IndexType.UInt16);
        HasGpuWork = true;
        CommandDispatch(Native, groupCount.X, groupCount.Y, groupCount.Z);
    }

    /// <summary>Records a compute dispatch using GPU-resident workgroup counts.</summary>
    /// <param name="root">The root range, or default to preserve its binding.</param>
    /// <param name="arguments">Three 32-bit workgroup counts.</param>
    public override void DispatchIndirect(GPURange root, GPURange arguments)
    {
        Debug.Assert(Recording && !InRenderPass && Pipeline != null && Pipeline.Compute);
        BindRoot(root);
        BindNativePipeline(IndexType.UInt16);
        ExecuteIndirect(arguments, 2, 1, 12);
    }

    /// <summary>Records a task/mesh dispatch.</summary>
    /// <param name="root">The root range, or default to preserve its binding.</param>
    /// <param name="groupCount">The task or mesh workgroup count.</param>
    public override void DrawMeshlets(GPURange root, Extent3D groupCount)
    {
        Debug.Assert(Recording && InRenderPass && Pipeline != null && Pipeline.Mesh);
        BindRoot(root);
        BindNativePipeline(IndexType.UInt16);
        HasGpuWork = true;
        CommandDispatchMesh(Native, groupCount.X, groupCount.Y, groupCount.Z);
    }

    /// <summary>Records indirect task/mesh dispatches with an arbitrary native record stride.</summary>
    /// <param name="root">The root range, or default to preserve its binding.</param>
    /// <param name="arguments">The GPU workgroup-count records.</param>
    /// <param name="drawCount">The number of records.</param>
    /// <param name="stride">The record stride; zero selects 12 bytes.</param>
    public override void DrawMeshletsIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0)
    {
        Debug.Assert(Recording && InRenderPass && Pipeline != null && Pipeline.Mesh && Owner.Capabilities.IndirectMeshDraw);
        BindRoot(root);
        BindNativePipeline(IndexType.UInt16);
        ExecuteIndirect(arguments, 10, drawCount, stride == 0 ? 12u : stride);
    }

    /// <summary>Activates a newly placed or aliased texture in COMMON layout without preserving its contents.</summary>
    /// <param name="texture">The newly placed texture, not a borrowed swapchain image.</param>
    public override void InitializeTexture(GPUTexture texture)
    {
        D3D12Texture target = (D3D12Texture)texture;
        Debug.Assert(Recording && !InRenderPass && target.Owner == Owner && target.Swapchain == null);
        CommandTextureBarrier barrier = new()
        {
            SyncAfter = CommandSyncAll,
            AccessBefore = CommandNoAccess,
            LayoutBefore = uint.MaxValue,
            Resource = target.Native,
            Subresources = new CommandSubresources { FirstMip = uint.MaxValue },
            Flags = 1,
        };
        if (Owner.Queues[Pool.QueueIndex].Type == 0 && target.Desc.SampleCount == 1 && Backend.SupportsColorResolve(target.Desc.Format) &&
            Backend.IsColorResolveTarget(target.Desc.Type, target.Desc.Usage))
        {
            // A resolve can be the first write to this attachment. The native discard keeps initialization out of barrier-only replay.
            HasGpuWork = true;
            barrier.SyncAfter = CommandSyncRender;
            barrier.AccessAfter = 8;
            barrier.LayoutAfter = 2;
            RecordTextureBarriers(&barrier, 1);
            CommandDiscard(Native, target.Native);
            barrier.SyncBefore = CommandSyncRender;
            barrier.SyncAfter = CommandSyncAll;
            barrier.AccessBefore = 8;
            barrier.AccessAfter = 0;
            barrier.LayoutBefore = 2;
            barrier.LayoutAfter = 0;
            barrier.Flags = 0;
        }
        RecordTextureBarriers(&barrier, 1);
    }

    /// <summary>Opens a named debug group nesting all commands until the matching end.</summary>
    /// <param name="name">The group name shown by GPU capture tools.</param>
    public override void BeginDebugGroup(ReadOnlySpan<char> name)
    {
        Debug.Assert(Recording);
        RecordPixEvent(true, name);
    }

    /// <summary>Closes the innermost open debug group.</summary>
    public override void EndDebugGroup()
    {
        Debug.Assert(Recording);
        CommandEndEvent(Native);
    }

    /// <summary>Inserts a standalone debug marker at the current position.</summary>
    /// <param name="name">The marker name shown by GPU capture tools.</param>
    public override void InsertDebugMarker(ReadOnlySpan<char> name)
    {
        Debug.Assert(Recording);
        RecordPixEvent(false, name);
    }

    private void RecordPixEvent(bool begin, ReadOnlySpan<char> name)
    {
        // Legacy PIX event encoding, the format the debug layer, PIX, and capture tools decode.
        int capacity = System.Text.Encoding.UTF8.GetMaxByteCount(name.Length);
        byte* utf8 = stackalloc byte[capacity];
        int length = System.Text.Encoding.UTF8.GetBytes(name, new Span<byte>(utf8, capacity));
        int qwords = 3 + (length + 8) / 8;
        ulong* payload = stackalloc ulong[qwords];
        for (int i = 0; i < qwords; ++i) payload[i] = 0;
        payload[0] = begin ? 0x002ul << 10 : 0x008ul << 10;
        payload[2] = 8ul << 55 | 1ul << 54; // ANSI string copied in 8-byte chunks
        for (int i = 0; i < length; ++i) payload[3 + i / 8] |= (ulong)utf8[i] << (i % 8) * 8;
        if (begin) CommandBeginEvent(Native, payload, (uint)(qwords * 8));
        else CommandSetMarker(Native, payload, (uint)(qwords * 8));
    }

    /// <summary>Closes recording and resolves timestamps into the command buffer's readback storage.</summary>
    public override void EndCommands()
    {
        Debug.Assert(Recording && !InRenderPass);
        if (TimestampCount != 0) HasGpuWork = true;
        if (TimestampCount != 0 && !Suspended) CommandResolveTimestamps(Native, TimestampHeap, TimestampCount, TimestampReadback);
        D3D12Interop.CheckExecution(CommandClose(Native), "ID3D12GraphicsCommandList.Close");
        Recording = false;
        if (TimestampCount != 0 && Suspended)
        {
            if (ResolveNative == 0)
            {
                nint resolve = 0;
                D3D12Interop.Check(CommandCreateList(Owner.Native, Owner.Queues[Pool.QueueIndex].Type, Allocator, &resolve),
                    "CreateCommandList8 (query resolve)");
                ResolveNative = resolve;
            }
            else D3D12Interop.Check(CommandResetList(ResolveNative, Allocator), "ID3D12GraphicsCommandList.Reset (query resolve)");
            CommandResolveTimestamps(ResolveNative, TimestampHeap, TimestampCount, TimestampReadback);
            D3D12Interop.CheckExecution(CommandClose(ResolveNative), "ID3D12GraphicsCommandList.Close (query resolve)");
            ResolvePending = true;
        }
        Ended = true;
    }

    private void BindRoot(GPURange root)
    {
        if (root.Buffer == null) return;
        D3D12GpuHeapOwner buffer = (D3D12GpuHeapOwner)root.Buffer;
        Debug.Assert(buffer.Device == Owner && (root.Offset & 15) == 0);
        ulong address = buffer.Address + root.Offset;
        if (address == BoundRoot) return;
        CommandRootAddress(Native, address, false);
        if (Owner.Queues[Pool.QueueIndex].Type == 0) CommandRootAddress(Native, address, true);
        BoundRoot = address;
    }

    private void BindIndices(GPURange indices, IndexType type)
    {
        if (HasIndices && BoundIndices.Buffer == indices.Buffer && BoundIndices.Offset == indices.Offset && BoundIndices.Size == indices.Size &&
            BoundIndexType == type) return;
        D3D12GpuHeapOwner buffer = (D3D12GpuHeapOwner)indices.Buffer!;
        Debug.Assert(buffer.Device == Owner);
        CommandIndexView view = new() { Address = buffer.Address + indices.Offset, Size = (uint)indices.Size, Format = type == IndexType.UInt16 ? 57 : 42 };
        CommandSetIndices(Native, &view);
        BoundIndices = indices;
        BoundIndexType = type;
        HasIndices = true;
    }

    private void BindNativePipeline(IndexType indexType)
    {
        Debug.Assert(Pipeline != null);
        if (!PipelineDirty && PipelineIndexType == indexType) return;
        nint native = Pipeline.GetPipeline(DepthState, indexType);
        if (native != NativePipeline)
        {
            CommandSetPipeline(Native, native);
            NativePipeline = native;
        }
        PipelineIndexType = indexType;
        PipelineDirty = false;
    }

    private void ExecuteIndirect(GPURange arguments, uint type, uint count, uint stride)
    {
        D3D12GpuHeapOwner buffer = (D3D12GpuHeapOwner)arguments.Buffer!;
        Debug.Assert(buffer.Device == Owner);
        if (count == 0) return;
        CommandIndirect(Native, Pool.GetSignature(type, stride), count, buffer.Native, arguments.Offset);
        HasGpuWork = true;
    }

    private void UseTexture(D3D12Texture texture)
    {
        Debug.Assert(texture.Owner == Owner);
        if (texture.Swapchain != null) UseSwapchain(texture.Swapchain);
    }

    private static Extent3D MipExtent(TextureDesc desc, uint mip) => new()
    {
        X = Math.Max(1u, desc.Extent.X >> (int)mip),
        Y = Math.Max(1u, desc.Extent.Y >> (int)mip),
        Z = Math.Max(1u, desc.Extent.Z >> (int)mip),
    };

    private static uint TextureSubresource(TextureDesc desc, uint mip, uint slice) =>
        mip + slice * desc.MipLevels + (desc.Format == Format.S8Uint ? desc.MipLevels * desc.LayerCount : 0);

    private void CopyBufferTexture(GPURange memory, D3D12Texture texture, TextureCopyDesc copy, bool upload)
    {
        Debug.Assert(Recording && !InRenderPass);
        UseTexture(texture);
        D3D12GpuHeapOwner buffer = (D3D12GpuHeapOwner)memory.Buffer!;
        Debug.Assert(buffer.Device == Owner);
        TextureFormatInfo info = Gpu.GetTextureFormatInfo(texture.Desc.Format);
        Extent3D mip = MipExtent(texture.Desc, copy.MipLevel);
        Extent3D extent = new()
        {
            X = copy.Extent.X != 0 ? copy.Extent.X : mip.X - copy.Offset.X,
            Y = copy.Extent.Y != 0 ? copy.Extent.Y : mip.Y - copy.Offset.Y,
            Z = copy.Extent.Z != 0 ? copy.Extent.Z : mip.Z - copy.Offset.Z,
        };
        Backend.AssertTextureCopyRegion(texture.Desc.Format, mip, copy.Offset, extent);
        uint slices = copy.SliceCount != 0 ? copy.SliceCount : texture.Desc.LayerCount - copy.BaseSlice;
        Debug.Assert(slices != 0 && copy.BaseSlice + slices <= texture.Desc.LayerCount);
        Debug.Assert(texture.Desc.Type != TextureType.ThreeD || (copy.BaseSlice == 0 && slices == 1));
        uint blocks = (uint)Backend.DivideUp(extent.X, info.BlockExtent.X);
        ulong rowBytes = (ulong)blocks * info.BytesPerBlock;
        uint rows = (uint)Backend.DivideUp(extent.Y, info.BlockExtent.Y);
        ulong rowPitch = copy.RowPitchBytes != 0 ? copy.RowPitchBytes : rowBytes;
        ulong imagePitch = texture.Desc.Type == TextureType.ThreeD && copy.SlicePitchBytes != 0 ? copy.SlicePitchBytes : rowPitch * rows;
        ulong slicePitch = copy.SlicePitchBytes != 0 ? copy.SlicePitchBytes : imagePitch * extent.Z;
        Debug.Assert(rowPitch >= rowBytes && imagePitch >= rowPitch * rows);
        Debug.Assert(memory.Size >= slicePitch * (slices - 1) + imagePitch * (extent.Z - 1) + rowPitch * (rows - 1) + rowBytes);
        int format = D3D12Interop.CopyFormat(texture.Desc.Format);
        bool wholeVolume = imagePitch == rowPitch * rows;
        uint copyDepth = wholeVolume ? extent.Z : 1;
        bool wholeSource = copy.Offset.X == 0 && copy.Offset.Y == 0 && copy.Offset.Z == 0 &&
            extent.X == mip.X && extent.Y == mip.Y && copyDepth == mip.Z;
        HasGpuWork = true;
        for (uint slice = 0; slice < slices; ++slice)
        {
            CommandCopyLocation image = new()
            {
                Resource = texture.Native,
                Subresource = TextureSubresource(texture.Desc, copy.MipLevel, copy.BaseSlice + slice),
            };
            for (uint z = 0; z < extent.Z; z += copyDepth)
            {
                CommandCopyLocation linear = new()
                {
                    Resource = buffer.Native,
                    Type = 1,
                    Placed = new CommandPlacedFootprint
                    {
                        Offset = memory.Offset + slice * slicePitch + z * imagePitch,
                        Footprint = new CommandFootprint
                        {
                            Format = format,
                            Width = blocks * info.BlockExtent.X,
                            Height = rows * info.BlockExtent.Y,
                            Depth = copyDepth,
                            RowPitch = (uint)rowPitch,
                        },
                    },
                };
                CommandBox box = new()
                {
                    Left = copy.Offset.X,
                    Top = copy.Offset.Y,
                    Front = copy.Offset.Z + z,
                    Right = copy.Offset.X + blocks * info.BlockExtent.X,
                    Bottom = copy.Offset.Y + rows * info.BlockExtent.Y,
                    Back = copy.Offset.Z + z + copyDepth,
                };
                if (upload) CommandCopyTexture(Native, &image, copy.Offset.X, copy.Offset.Y, copy.Offset.Z + z, &linear, null);
                else CommandCopyTexture(Native, &linear, 0, 0, 0, &image, wholeSource || info.Depth || info.Stencil ? null : &box);
            }
        }
    }

    private void TransitionAttachments(bool begin)
    {
        CommandTextureBarrier* barriers = stackalloc CommandTextureBarrier[2 * (int)ColorCount + 2];
        uint count = 0;
        for (uint i = 0; i < ColorCount; ++i)
        {
            barriers[count++] = AttachmentBarrier(ColorViews[i]!, begin, false);
            D3D12RenderView? resolve = ResolveViews[i];
            if (resolve != null) barriers[count++] = ResolveDestinationBarrier(resolve, begin);
        }
        if (DepthView != null) barriers[count++] = AttachmentBarrier(DepthView, begin, true);
        if (StencilView != null && (DepthView == null || StencilView.Texture != DepthView.Texture ||
            StencilView.Mip != DepthView.Mip || StencilView.Slice != DepthView.Slice))
            barriers[count++] = AttachmentBarrier(StencilView, begin, true);
        if (count == 0) return;
        RecordTextureBarriers(barriers, count);
    }

    private static CommandTextureBarrier ResolveDestinationBarrier(D3D12RenderView view, bool begin)
    {
        return new CommandTextureBarrier
        {
            SyncBefore = begin ? CommandSyncRender | CommandSyncResolve : CommandSyncResolve,
            SyncAfter = begin ? CommandSyncResolve : CommandSyncRender,
            AccessBefore = begin ? 0u : CommandResolveDestination,
            AccessAfter = begin ? CommandResolveDestination : 0u,
            LayoutBefore = begin ? 0u : CommandLayoutResolveDestination,
            LayoutAfter = begin ? CommandLayoutResolveDestination : 0u,
            Resource = view.Texture.Native,
            Subresources = new CommandSubresources
            {
                FirstMip = view.Mip,
                MipCount = 1,
                FirstSlice = view.Slice,
                SliceCount = 1,
                PlaneCount = 1,
            },
        };
    }

    private static CommandTextureBarrier AttachmentBarrier(D3D12RenderView view, bool begin, bool depth)
    {
        uint sync = depth ? CommandSyncDepth : CommandSyncRender;
        uint access = depth ? 0x20u : 8u;
        uint layout = depth ? 4u : (view.Texture.Desc.Usage & TextureUsage.Storage) != 0 ? 0u : 2u;
        bool combined = view.Texture.Desc.Format is Format.D24UnormS8Uint or Format.D32FloatS8Uint or Format.S8Uint;
        return new CommandTextureBarrier
        {
            SyncBefore = sync,
            SyncAfter = sync,
            AccessBefore = begin ? 0u : access,
            AccessAfter = begin ? access : 0u,
            LayoutBefore = begin ? 0u : layout,
            LayoutAfter = begin ? layout : 0u,
            Resource = view.Texture.Native,
            Subresources = new CommandSubresources
            {
                FirstMip = view.Mip,
                MipCount = 1,
                FirstSlice = view.Texture.Desc.Type == TextureType.ThreeD ? 0 : view.Slice,
                SliceCount = 1,
                PlaneCount = combined ? 2u : 1u,
            },
        };
    }

    private static uint BeginningAccess(LoadOp load) => load == LoadOp.Load ? 1u : load == LoadOp.Clear ? 2u : 0u;

    private uint BarrierStages(Stage stages, bool before)
    {
        if ((stages & Stage.AllCommands) != 0) return CommandSyncAll;
        uint result = 0;
        const uint geometry = CommandSyncIndex | CommandSyncVertex;
        // A pass-end resolve is part of the logical color-output stage.
        uint colorOutput = InRenderPass ? CommandSyncRender : CommandSyncRender | CommandSyncResolve;
        uint raster = CommandSyncDepth | CommandSyncPixel | colorOutput;
        if ((stages & Stage.Indirect) != 0)
            result |= CommandSyncIndirect | (before ? 0u : geometry | raster | CommandSyncCompute);
        if ((stages & Stage.IndexInput) != 0)
            result |= CommandSyncIndex | (before ? CommandSyncIndirect : CommandSyncVertex | raster);
        if ((stages & Stage.Vertex) != 0)
            result |= CommandSyncVertex | (before ? CommandSyncIndirect | CommandSyncIndex : raster);
        if ((stages & (Stage.Task | Stage.Mesh)) != 0)
            result |= CommandSyncVertex | (before ? CommandSyncIndirect : raster);
        if ((stages & (Stage.DepthStencilTests | Stage.Pixel)) != 0)
            result |= CommandSyncDepth | CommandSyncPixel | (before ? CommandSyncIndirect | geometry : colorOutput);
        if ((stages & Stage.ColorOutput) != 0)
            result |= colorOutput | (before ? CommandSyncIndirect | geometry | CommandSyncDepth | CommandSyncPixel : 0u);
        if ((stages & Stage.Compute) != 0) result |= CommandSyncCompute | (before ? CommandSyncIndirect : 0u);
        if ((stages & Stage.Transfer) != 0) result |= CommandSyncCopy;
        int queueType = Owner.Queues[Pool.QueueIndex].Type;
        if (queueType == 2) result &= CommandSyncCompute | CommandSyncIndirect | CommandSyncCopy;
        if (queueType == 3) result &= CommandSyncCopy;
        return result;
    }

    private static uint MemoryBarrierStages(uint stages, uint access)
    {
        if (stages == CommandSyncAll || access == CommandNoAccess) return stages;
        uint compatible = 0;
        if ((access & 0xc00) != 0) compatible |= CommandSyncCopy;
        if ((access & 0x90) != 0) compatible |= CommandSyncVertex | CommandSyncPixel | CommandSyncCompute;
        if ((access & 8) != 0) compatible |= CommandSyncRender;
        if ((access & 0x60) != 0) compatible |= CommandSyncDepth;
        if ((access & 0x200) != 0) compatible |= CommandSyncIndirect;
        if ((access & 4) != 0) compatible |= CommandSyncIndex;
        return stages & compatible;
    }

    private uint BarrierAccess(Access access)
    {
        uint result = 0;
        if ((access & Access.TransferRead) != 0) result |= 0x800;
        if ((access & Access.TransferWrite) != 0) result |= 0x400;
        if ((access & Access.ShaderRead) != 0) result |= 0x80 | 0x10;
        if ((access & Access.ShaderWrite) != 0) result |= 0x10;
        // Outside passes, attachment-local barriers already make their caches visible in COMMON.
        if (InRenderPass)
        {
            if ((access & (Access.ColorRead | Access.ColorWrite)) != 0) result |= 8;
            if ((access & (Access.DepthStencilRead | Access.DepthStencilWrite)) != 0) result |= 0x20;
        }
        if ((access & Access.IndirectRead) != 0) result |= 0x200;
        if ((access & Access.IndexRead) != 0) result |= 4;
        if ((access & Access.DescriptorRead) != 0) result |= 0x80;
        return result == 0 ? CommandNoAccess : result;
    }

    [Conditional("DEBUG")]
    private static void AssertNonoverlap(D3D12Texture source, D3D12Texture destination, TextureRegionCopyDesc copy, Extent3D extent, uint slices)
    {
        if (source.Native != destination.Native || copy.SourceMipLevel != copy.DestinationMipLevel) return;
        Debug.Assert(copy.SourceBaseSlice + slices <= copy.DestinationBaseSlice || copy.DestinationBaseSlice + slices <= copy.SourceBaseSlice ||
            copy.SourceOffset.X + extent.X <= copy.DestinationOffset.X || copy.DestinationOffset.X + extent.X <= copy.SourceOffset.X ||
            copy.SourceOffset.Y + extent.Y <= copy.DestinationOffset.Y || copy.DestinationOffset.Y + extent.Y <= copy.SourceOffset.Y ||
            copy.SourceOffset.Z + extent.Z <= copy.DestinationOffset.Z || copy.DestinationOffset.Z + extent.Z <= copy.SourceOffset.Z);
    }
}
