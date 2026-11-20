// Metal backend part 3: command recording, submission, presentation, timestamps, and render-pass state.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Mtl = Metal;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class MetalDevice : GPUDevice
{
    /// <summary>Submits recorded command buffers without presenting or releasing acquired images.</summary>
    /// <param name="desc">The command buffers, waits, and required completion point.</param>
    /// <param name="queueIndex">The submission queue index; swapchain images require queue zero.</param>
    public override void Submit(SubmitDesc desc, uint queueIndex = 0) => SubmitCommands(desc, queueIndex, null);

    /// <summary>Submits work on queue zero and presents only the specified swapchain's acquired image.</summary>
    /// <param name="desc">The command buffers, waits, and required completion point; commands may be empty.</param>
    /// <param name="swapchain">The swapchain to present after all prior uses of its acquired image.</param>
    public override void SubmitAndPresent(SubmitDesc desc, GPUSwapchain swapchain)
    {
        MetalSwapchain metalSwapchain = (MetalSwapchain)swapchain;
        Debug.Assert(metalSwapchain.Owner == this && metalSwapchain.Drawable.NativePtr != 0);
        SubmitCommands(desc, 0, metalSwapchain);
    }

    private void SubmitCommands(SubmitDesc desc, uint queueIndex, MetalSwapchain? swapchain)
    {
        MetalTimelineSemaphore completion = (MetalTimelineSemaphore)desc.Completion.Semaphore!;
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(queueIndex < Capabilities.QueueCount && desc.Completion.Semaphore != null && completion.Owner == this);
        MetalQueue queue = MtlQueues[queueIndex];
        ulong count = (ulong)desc.Commands.Length;
        for (int i = 0; i < desc.Commands.Length; ++i) count += ((MetalCommandBuffer)desc.Commands[i]).NativeCount - 1;
        if ((ulong)queue.Submission.Length < count) queue.Submission = new Mtl.MTL4CommandBuffer[count];
        ulong nativeIndex = 0;
        for (int i = 0; i < desc.Commands.Length; ++i)
        {
            MetalCommandBuffer commands = (MetalCommandBuffer)desc.Commands[i];
            Debug.Assert(commands.Owner == this && commands.Pool.Kind == queue.Kind && commands.Ended && commands.Retirement.NativePtr == 0);
            Debug.Assert(!commands.UsesSwapchain || queueIndex == 0);
            commands.Retirement = queue.Completion;
            commands.RetirementValue = queue.SubmittedValue + 1;
            NativeCommandBuffer native = commands.NativeBuffers;
            for (uint j = 0; j < commands.NativeCount; ++j, native = native.Next) queue.Submission[nativeIndex++] = native.Buffer;
            if (commands.AcquiredSwapchain != null)
            {
                MetalSwapchain acquired = commands.AcquiredSwapchain;
                Debug.Assert(acquired.AcquiringCommands == commands && acquired.Drawable.NativePtr != 0 && !acquired.DrawableNeedsSignal);
                queue.Handle.Wait(acquired.Drawable);
                acquired.DrawableNeedsSignal = true;
                commands.AcquiredSwapchain = null;
                acquired.AcquiringCommands = null;
            }
        }
        Debug.Assert(swapchain == null || (swapchain.AcquiringCommands == null && swapchain.DrawableNeedsSignal));
        for (int i = 0; i < desc.Waits.Length; ++i)
        {
            TimelinePoint wait = desc.Waits[i];
            MetalTimelineSemaphore semaphore = (MetalTimelineSemaphore)wait.Semaphore!;
            Debug.Assert(wait.Semaphore != null && semaphore.Owner == this);
            queue.Handle.Wait(semaphore.SharedEvent, wait.Value);
        }
        // MetalTools enumerates residency on the CPU during commit without synchronizing concurrent updates.
        if (ShaderValidation)
        {
            lock (ResidencyLock)
            {
                if (count != 0) queue.Handle.Commit(queue.Submission, count);
            }
        }
        else if (count != 0) queue.Handle.Commit(queue.Submission, count);
        if (swapchain != null)
        {
            queue.Handle.SignalDrawable(swapchain.Drawable);
            swapchain.DrawableNeedsSignal = false;
            if (swapchain.PresentationQueue.NativePtr != 0)
            {
                // Steam's overlay requires legacy presentation; join it back into queue zero's completion timeline.
                queue.Handle.SignalEvent(queue.Completion, ++queue.SubmittedValue);
                Mtl.MTLCommandBuffer presentation = swapchain.PresentationQueue.CommandBuffer();
                presentation.EncodeWait(queue.Completion, queue.SubmittedValue);
                presentation.PresentDrawable(swapchain.Drawable);
                presentation.EncodeSignalEvent(swapchain.PresentationCompletion, ++swapchain.PresentationValue);
                presentation.Commit();
                queue.Handle.Wait(swapchain.PresentationCompletion, swapchain.PresentationValue);
            }
            else swapchain.Drawable.Present();
        }
        queue.Handle.SignalEvent(queue.Completion, ++queue.SubmittedValue);
        queue.Handle.SignalEvent(completion.SharedEvent, desc.Completion.Value);
        if (swapchain != null)
        {
            swapchain.LastFrameValue = queue.SubmittedValue;
            swapchain.ReleaseDrawable();
        }
    }
}

internal sealed unsafe partial class MetalCommandPool : GPUCommandPool
{
    /// <summary>Begins recording a primary command buffer.</summary>
    /// <returns>The recording command buffer.</returns>
    public override GPUCommandBuffer BeginCommands()
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        MetalCommandBuffer? context = NextBuffer;
        if (context != null) NextBuffer = context.Next;
        else
        {
            context = CreateContext();
            if (Last != null) Last.Next = context;
            else First = context;
            Last = context;
        }
        Debug.Assert(!context.Recording && !context.Ended);
        context.Native = context.NativeBuffers;
        context.Commands = context.Native.Buffer;
        context.NativeCount = 1;
        context.Commands.BeginCommandBuffer(context.Native.Allocator);
        context.RootAddress = 0;
        context.Arguments.SetAddress(0, MetalInterop.ArgRootIndex);
        context.Arguments.SetAddress(0, MetalInterop.ArgTextureHeapIndex);
        context.Arguments.SetAddress(0, MetalInterop.ArgSamplerHeapIndex);
        context.Arguments.SetAddress(Owner.BufferViewTable.GpuAddress, MetalInterop.ArgBufferViewTableIndex);
        context.TimestampCount = 0;
        context.Pipeline = null;
        context.Recording = true;
        context.RenderContinuation = false;
        return context;
    }

    /// <summary>Collects completed timestamp results for the pool command buffers.</summary>
    public override void ReadTimestamps()
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        for (MetalCommandBuffer? commands = First; commands != null; commands = commands.Next)
        {
            if (commands.Retirement.NativePtr == 0 || commands.TimestampCount == 0) continue;
            Debug.Assert(commands.Retirement.SignaledValue >= commands.RetirementValue);
            for (uint i = 0; i < commands.TimestampCount;)
            {
                Mtl.MTL4CounterHeap heap = commands.Timestamps![i].Heap;
                uint firstIndex = commands.Timestamps[i].Index;
                uint count = 1;
                while (i + count < commands.TimestampCount && commands.Timestamps[i + count].Heap.NativePtr == heap.NativePtr &&
                       commands.Timestamps[i + count].Index == firstIndex + count) ++count;
                // The resolved counter data is the one sanctioned readback: values were produced by completed GPU work.
                Mtl.NSData data = heap.ResolveCounterRange(new Mtl.NSRange { location = firstIndex, length = count });
                Debug.Assert(data.Length == (nuint)(count * sizeof(Mtl.MTL4TimestampHeapEntry)));
                Mtl.MTL4TimestampHeapEntry* values = (Mtl.MTL4TimestampHeapEntry*)MetalInterop.NsdataBytes(data.NativePtr);
                for (uint j = 0; j < count; ++j) *commands.Timestamps[i + j].destination = values[j].timestamp;
                i += count;
            }
            commands.TimestampCount = 0;
        }
    }
}

internal sealed unsafe partial class MetalCommandBuffer : GPUCommandBuffer
{
    internal void DestroyContext()
    {
        for (NativeCommandBuffer native = NativeBuffers; native != null;)
        {
            NativeCommandBuffer next = native.Next;
            native.Buffer.Dispose();
            native.Allocator.Dispose();
            native = next;
        }
        Arguments.Dispose();
        Pass.Dispose();
        for (uint i = 0; i < Owner.TimestampQueryCount && Timestamps != null && Timestamps[i].Page != null;)
        {
            CounterPage page = Timestamps[i].Page;
            uint word = Timestamps[i].Index / 64;
            ulong release = 0;
            do { release |= 1ul << (int)(Timestamps[i++].Index % 64); }
            while (i < Owner.TimestampQueryCount && Timestamps[i].Page == page && Timestamps[i].Index / 64 == word);
            Interlocked.And(ref page.occupied[(int)word], ~release);
        }
        Timestamps = null;
    }

    internal void EndCompute()
    {
        if (Compute.NativePtr != 0)
        {
            Compute.EndEncoding();
            Compute.Dispose();
            Compute = default;
        }
    }

    private Mtl.MTL4CommandBuffer NativeCommands()
    {
        if (Commands.NativePtr == 0)
        {
            if (Native.Next == null)
                Native.Next = new NativeCommandBuffer { Buffer = Owner.MtlDevice.NewCommandBuffer, Allocator = Owner.MtlDevice.NewCommandAllocator() };
            Native = Native.Next;
            Commands = Native.Buffer;
            ++NativeCount;
            Commands.BeginCommandBuffer(Native.Allocator);
        }
        return Commands;
    }

    private Mtl.MTL4ComputeCommandEncoder ComputeEncoder()
    {
        Debug.Assert(Recording && Render.NativePtr == 0);
        if (Compute.NativePtr == 0)
        {
            Compute = new Mtl.MTL4ComputeCommandEncoder(MetalInterop.retain(NativeCommands().ComputeCommandEncoder.NativePtr));
            Compute.SetArgumentTable(Arguments);
            if (Pipeline != null && Pipeline.Compute.NativePtr != 0) Compute.SetComputePipelineState(Pipeline.Compute);
        }
        return Compute;
    }

    /// <summary>Initializes a newly placed texture for rendering use.</summary>
    /// <param name="texture">The newly placed texture, not an acquired swapchain image.</param>
    public override void InitializeTexture(GPUTexture texture)
    {
        MetalTexture metalTexture = (MetalTexture)texture;
        Debug.Assert(metalTexture != null && Owner == metalTexture.Owner && Recording && Render.NativePtr == 0 && metalTexture.Swapchain == null);
    }

    /// <summary>Opens a named debug group nesting all commands until the matching end.</summary>
    /// <param name="name">The group name shown by GPU capture tools.</param>
    public override void BeginDebugGroup(ReadOnlySpan<char> name)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording);
        NativeCommands().PushDebugGroup(name.ToString());
    }

    /// <summary>Closes the innermost open debug group.</summary>
    public override void EndDebugGroup()
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording);
        NativeCommands().PopDebugGroup();
    }

    /// <summary>Inserts a standalone debug marker at the current position.</summary>
    /// <param name="name">The marker name shown by GPU capture tools.</param>
    public override void InsertDebugMarker(ReadOnlySpan<char> name)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording);
        Mtl.MTL4CommandBuffer commands = NativeCommands();
        commands.PushDebugGroup(name.ToString());
        commands.PopDebugGroup();
    }

    /// <summary>Finishes recording for submission.</summary>
    public override void EndCommands()
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording && Render.NativePtr == 0);
        EndCompute();
        Commands.EndCommandBuffer();
        Recording = false;
        Ended = true;
    }

    /// <summary>Acquires a swapchain image for queue zero, or returns an empty frame while unavailable.</summary>
    /// <param name="swapchain">The swapchain to acquire; submit this command buffer before subsequent image uses.</param>
    /// <returns>A borrowed frame valid until presentation or reconfiguration, or an empty frame while unavailable.</returns>
    public override SwapchainFrame Acquire(GPUSwapchain swapchain)
    {
        MetalSwapchain metalSwapchain = (MetalSwapchain)swapchain;
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording && Render.NativePtr == 0 && Pool.QueueIndex == 0 && AcquiredSwapchain == null && metalSwapchain.Owner == Owner &&
                     metalSwapchain.Layer.NativePtr != 0 && metalSwapchain.Drawable.NativePtr == 0);
        if (metalSwapchain.CurrentExtent.X == 0 || metalSwapchain.CurrentExtent.Y == 0) return default;
        metalSwapchain.Drawable = new Mtl.CAMetalDrawable(MetalInterop.retain(metalSwapchain.Layer.NextDrawable.NativePtr));
        if (metalSwapchain.Drawable.NativePtr == 0) return default;
        metalSwapchain.AcquiringCommands = this;
        AcquiredSwapchain = metalSwapchain;
        UsesSwapchain = true;
        metalSwapchain.DrawableView.Texture = metalSwapchain.Drawable.Texture;
        metalSwapchain.CurrentExtent = new Extent2D((uint)metalSwapchain.DrawableView.Texture.Width, (uint)metalSwapchain.DrawableView.Texture.Height);
        metalSwapchain.DrawableTexture.Native = metalSwapchain.DrawableView.Texture;
        metalSwapchain.DrawableTexture.Desc = new TextureDesc
        {
            Extent = new Extent3D(metalSwapchain.CurrentExtent.X, metalSwapchain.CurrentExtent.Y, 1),
            Format = metalSwapchain.CurrentFormat,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource | TextureUsage.TransferDestination,
        };
        return new SwapchainFrame
        {
            RenderView = metalSwapchain.DrawableView,
            Extent = metalSwapchain.CurrentExtent,
            Texture = metalSwapchain.DrawableTexture,
        };
    }

    /// <summary>Records a timestamp for collection after GPU completion.</summary>
    public override void WriteTimestamp(Span<ulong> destination, Stage stage = Stage.AllCommands)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        if (Owner.TimestampQueryCount == 0) return;
        // The span's memory must stay valid and stationary until readTimestamps; only stack, native, or pinned storage qualifies.
        Debug.Assert(Recording && !destination.IsEmpty && TimestampCount < Owner.TimestampQueryCount);
        uint index = TimestampCount++;
        Timestamps![index].destination = (ulong*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination));
        if (Render.NativePtr != 0)
        {
            Mtl.MTLRenderStages after = Mtl.MTLRenderStages.RenderStageFragment;
            if (stage == Stage.Vertex || stage == Stage.IndexInput) after = Mtl.MTLRenderStages.RenderStageVertex;
            if (stage == Stage.Task) after = Mtl.MTLRenderStages.RenderStageObject;
            if (stage == Stage.Mesh) after = Mtl.MTLRenderStages.RenderStageMesh;
            Render.WriteTimestamp(Mtl.MTL4TimestampGranularity.Precise, after, Timestamps[index].Heap, Timestamps[index].Index);
        }
        else
        {
            EndCompute();
            NativeCommands().WriteTimestampIntoHeap(Timestamps[index].Heap, Timestamps[index].Index);
        }
    }

    /// <summary>Binds a pipeline for subsequent draws and dispatches.</summary>
    public override void BindPipeline(GPUPipeline pipeline)
    {
        MetalPso metalPso = (MetalPso)pipeline;
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording && metalPso.Owner == Owner);
        FillMode previousFill = Pipeline?.Rasterization.Fill ?? FillMode.Solid;
        Pipeline = metalPso;
        if (metalPso.Compute.NativePtr != 0)
        {
            Debug.Assert(Render.NativePtr == 0);
            if (Compute.NativePtr != 0) Compute.SetComputePipelineState(metalPso.Compute);
            else ComputeEncoder();
        }
        else
        {
            Debug.Assert(Render.NativePtr != 0);
            Render.SetRenderPipelineState(metalPso.Render);
            Render.SetCullMode(metalPso.Rasterization.Cull == CullMode.None ? Mtl.MTLCullMode.None :
                                         metalPso.Rasterization.Cull == CullMode.Clockwise ? Mtl.MTLCullMode.Back : Mtl.MTLCullMode.Front);
            Render.SetDepthBias(metalPso.Rasterization.DepthBiasConstant, metalPso.Rasterization.DepthBiasSlope, metalPso.Rasterization.DepthBiasClamp);
            if (metalPso.Rasterization.Fill != previousFill)
                Render.SetTriangleFillMode(metalPso.Rasterization.Fill == FillMode.Wireframe ? Mtl.MTLTriangleFillMode.Lines : Mtl.MTLTriangleFillMode.Fill);
        }
    }

    /// <summary>Binds the bindless texture descriptor heap.</summary>
    public override void SetTextureDescriptorHeap(GPUTextureDescriptorHeap heap)
    {
        MetalTextureDescriptorHeap textureHeap = (MetalTextureDescriptorHeap)heap;
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording && textureHeap.Owner == Owner);
        Arguments.SetAddress(textureHeap.BaseBuffer.GpuAddress, MetalInterop.ArgTextureHeapIndex);
    }

    /// <summary>Binds the sampler descriptor heap.</summary>
    public override void SetSamplerDescriptorHeap(GPUSamplerDescriptorHeap heap)
    {
        MetalSamplerDescriptorHeap samplerHeap = (MetalSamplerDescriptorHeap)heap;
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording && samplerHeap.Owner == Owner);
        Arguments.SetAddress(samplerHeap.Buffer.GpuAddress, MetalInterop.ArgSamplerHeapIndex);
    }

    /// <summary>Inserts an execution and memory dependency between command scopes.</summary>
    public override void Barrier(Stage before, Access beforeAccess, Stage after, Access afterAccess)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Render.NativePtr == 0);
        Mtl.MTLStages source = MetalInterop.BarrierStages(before, true);
        Mtl.MTLStages destination = MetalInterop.BarrierStages(after, false);
        // Shared-event completion provides host visibility; a host-only destination blocks no future GPU stages.
        if (source == 0 || destination == 0) return;
        ulong writes = (ulong)(Access.TransferWrite | Access.ShaderWrite | Access.ColorWrite | Access.DepthStencilWrite);
        Mtl.MTL4VisibilityOptions visibility = ((ulong)beforeAccess & writes) != 0 ? Mtl.MTL4VisibilityOptions.Device : Mtl.MTL4VisibilityOptions.None;
        ComputeEncoder().BarrierAfterStages(source, destination, visibility);
        EndCompute();
    }

    /// <summary>Records a linear GPU-memory copy.</summary>
    /// <param name="source">The source buffer range.</param>
    /// <param name="destination">The destination buffer range, at least as large as the source.</param>
    public override void CopyMemory(GPURange source, GPURange destination)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(source.Size <= destination.Size);
        MetalGpuHeapOwner sourceBuffer = (MetalGpuHeapOwner)source.Buffer!;
        MetalGpuHeapOwner destinationBuffer = (MetalGpuHeapOwner)destination.Buffer!;
        ComputeEncoder().CopyFromBuffer(sourceBuffer.Buffer, source.Offset, destinationBuffer.Buffer, destination.Offset, source.Size);
    }

    /// <summary>Records an upload into a texture region.</summary>
    /// <param name="source">The source buffer range.</param>
    /// <param name="destination">The destination texture.</param>
    /// <param name="copy">The texture region and source pitches.</param>
    public override void CopyMemoryToTexture(GPURange source, GPUTexture destination, TextureCopyDesc copy = default)
    {
        MetalTexture destinationTexture = (MetalTexture)destination;
        using Mtl.NSAutoreleasePool autorelease = new();
        MetalTextureCopy metrics = TextureCopy(destinationTexture, in copy);
        Debug.Assert(source.Size >= metrics.Size);
        Mtl.MTLBuffer buffer = ((MetalGpuHeapOwner)source.Buffer!).Buffer;
        for (uint slice = 0; slice < metrics.Slices; ++slice)
            ComputeEncoder().CopyFromBuffer(buffer, source.Offset + slice * metrics.SlicePitch, metrics.RowPitch, metrics.ImagePitch, metrics.Extent,
                                                      destinationTexture.Native, copy.BaseSlice + slice, copy.MipLevel, metrics.Origin);
    }

    /// <summary>Records a readback from a texture region.</summary>
    /// <param name="source">The source texture.</param>
    /// <param name="destination">The destination buffer range.</param>
    /// <param name="copy">The texture region and destination pitches.</param>
    public override void CopyTextureToMemory(GPUTexture source, GPURange destination, TextureCopyDesc copy = default)
    {
        MetalTexture sourceTexture = (MetalTexture)source;
        using Mtl.NSAutoreleasePool autorelease = new();
        MetalTextureCopy metrics = TextureCopy(sourceTexture, in copy);
        Debug.Assert(destination.Size >= metrics.Size);
        Mtl.MTLBuffer buffer = ((MetalGpuHeapOwner)destination.Buffer!).Buffer;
        for (uint slice = 0; slice < metrics.Slices; ++slice)
            ComputeEncoder().CopyFromTexture(sourceTexture.Native, copy.BaseSlice + slice, copy.MipLevel, metrics.Origin, metrics.Extent,
                                                       buffer, destination.Offset + slice * metrics.SlicePitch, metrics.RowPitch, metrics.ImagePitch);
    }

    /// <summary>Records a non-overlapping texture copy outside render passes; formats, texture types and selected mip extents must match.</summary>
    /// <param name="source">The initialized transfer-source texture; combined depth/stencil formats are not supported.</param>
    /// <param name="destination">The initialized transfer-destination texture.</param>
    /// <param name="copy">The source and destination regions; synchronization remains explicit.</param>
    public override void CopyTexture(GPUTexture source, GPUTexture destination, TextureRegionCopyDesc copy = default)
    {
        MetalTexture sourceTexture = (MetalTexture)source;
        MetalTexture destinationTexture = (MetalTexture)destination;
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(sourceTexture.Owner == Owner && destinationTexture.Owner == Owner);
        if (sourceTexture.Swapchain != null || destinationTexture.Swapchain != null)
        {
            Debug.Assert(Pool.QueueIndex == 0);
            Debug.Assert(sourceTexture.Swapchain == null || sourceTexture.Swapchain.Drawable.NativePtr != 0);
            Debug.Assert(destinationTexture.Swapchain == null || destinationTexture.Swapchain.Drawable.NativePtr != 0);
            UsesSwapchain = true;
        }
        Debug.Assert(sourceTexture.Desc.Format == destinationTexture.Desc.Format && sourceTexture.Desc.Type == destinationTexture.Desc.Type);
        Extent3D mipExtent = new()
        {
            X = Math.Max(sourceTexture.Desc.Extent.X >> (int)copy.SourceMipLevel, 1u),
            Y = Math.Max(sourceTexture.Desc.Extent.Y >> (int)copy.SourceMipLevel, 1u),
            Z = Math.Max(sourceTexture.Desc.Extent.Z >> (int)copy.SourceMipLevel, 1u),
        };
        Debug.Assert(mipExtent.X == Math.Max(destinationTexture.Desc.Extent.X >> (int)copy.DestinationMipLevel, 1u) &&
            mipExtent.Y == Math.Max(destinationTexture.Desc.Extent.Y >> (int)copy.DestinationMipLevel, 1u) &&
            mipExtent.Z == Math.Max(destinationTexture.Desc.Extent.Z >> (int)copy.DestinationMipLevel, 1u));
        Backend.AssertTextureCopyRegion(sourceTexture.Desc.Format, mipExtent, copy.SourceOffset, copy.Extent);
        Backend.AssertTextureCopyRegion(destinationTexture.Desc.Format, mipExtent, copy.DestinationOffset, copy.Extent);
        Mtl.MTLOrigin sourceOrigin = new() { x = copy.SourceOffset.X, y = copy.SourceOffset.Y, z = copy.SourceOffset.Z };
        Mtl.MTLOrigin destinationOrigin = new() { x = copy.DestinationOffset.X, y = copy.DestinationOffset.Y, z = copy.DestinationOffset.Z };
        Mtl.MTLSize extent = new()
        {
            width = copy.Extent.X == 0 ? mipExtent.X - copy.SourceOffset.X : copy.Extent.X,
            height = copy.Extent.Y == 0 ? mipExtent.Y - copy.SourceOffset.Y : copy.Extent.Y,
            depth = copy.Extent.Z == 0 ? mipExtent.Z - copy.SourceOffset.Z : copy.Extent.Z,
        };
        uint slices = copy.SliceCount == 0 ? sourceTexture.Desc.LayerCount - copy.SourceBaseSlice : copy.SliceCount;
        Mtl.MTL4ComputeCommandEncoder encoder = ComputeEncoder();
        for (uint slice = 0; slice < slices; ++slice)
            encoder.CopyFromTexture(sourceTexture.Native, copy.SourceBaseSlice + slice, copy.SourceMipLevel, sourceOrigin, extent,
                destinationTexture.Native, copy.DestinationBaseSlice + slice, copy.DestinationMipLevel, destinationOrigin);
    }

    /// <summary>Begins a render pass with the given attachments.</summary>
    /// <param name="desc">The render attachments and their operations.</param>
    /// <param name="flags">The render pass continuation flags.</param>
    public override void BeginRenderPass(RenderingDesc desc, RenderingFlags flags = RenderingFlags.None)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording && Render.NativePtr == 0 && Pool.Kind == MetalQueueKind.General && desc.Colors.Length <= 8);
        EndCompute();
        Mtl.MTL4RenderPassDescriptor pass = Pass;
        MetalRenderView? area = (MetalRenderView?)(desc.Colors.Length != 0 ? desc.Colors[0].RenderView : desc.Depth.RenderView ?? desc.Stencil.RenderView);
        for (uint i = 0; i < 8; ++i)
        {
            Mtl.MTLRenderPassColorAttachmentDescriptor attachment = pass.ColorAttachments.Object(i);
            if (i < (uint)desc.Colors.Length)
            {
                ColorAttachment color = desc.Colors[(int)i];
                MetalRenderView? view = (MetalRenderView?)color.RenderView;
                area ??= view;
                RenderAttachment(attachment, view, color.Load, color.Store);
                attachment.ClearColor = new Mtl.MTLClearColor { red = color.Clear.X, green = color.Clear.Y, blue = color.Clear.Z, alpha = color.Clear.W };
                MetalRenderView? resolve = (MetalRenderView?)color.ResolveView;
                if (resolve != null)
                {
                    Debug.Assert(view != null && view.Source.Desc.SampleCount == 4 && resolve.Source.Desc.SampleCount == 1,
                        "color resolve requires a four-sample source and single-sample target");
                    Debug.Assert(resolve.Source.Desc.Format == view.Source.Desc.Format && Backend.SupportsColorResolve(view.Source.Desc.Format),
                        "resolve views match the attachment format and require a supported resolve format");
                    Debug.Assert(Backend.IsColorResolveTarget(resolve.Source.Desc.Type, resolve.Source.Desc.Usage),
                        "resolve targets are 2D color attachments without storage usage");
                    Debug.Assert(area != null && view.Extent.X == area.Extent.X && view.Extent.Y == area.Extent.Y &&
                        resolve.Extent.X == area.Extent.X && resolve.Extent.Y == area.Extent.Y, "resolve views match the render area");
                    Debug.Assert(flags == RenderingFlags.None, "color resolve does not support pass continuation");
                    attachment.ResolveTexture = resolve.Texture;
                    ColorResolveMask |= 1u << (int)i;
                    attachment.ResolveLevel = resolve.Mip;
                    attachment.ResolveSlice = resolve.Slice;
                    if (resolve.Swapchain != null)
                    {
                        Debug.Assert(Pool.QueueIndex == 0 && resolve.Swapchain.Drawable.NativePtr != 0);
                        UsesSwapchain = true;
                    }
                    attachment.StoreAction = color.Store == StoreOp.Store
                        ? Mtl.MTLStoreAction.StoreAndMultisampleResolve : Mtl.MTLStoreAction.MultisampleResolve;
                }
            }
            else attachment.Texture = default;
        }
        Mtl.MTLRenderPassDepthAttachmentDescriptor depthAttachment = pass.DepthAttachment;
        RenderAttachment(depthAttachment, (MetalRenderView?)desc.Depth.RenderView, desc.Depth.Load, desc.Depth.Store);
        depthAttachment.ClearDepth = desc.Depth.Clear;
        Mtl.MTLRenderPassStencilAttachmentDescriptor stencilAttachment = pass.StencilAttachment;
        RenderAttachment(stencilAttachment, (MetalRenderView?)desc.Stencil.RenderView, desc.Stencil.Load, desc.Stencil.Store);
        stencilAttachment.ClearStencil = desc.Stencil.Clear;
        Debug.Assert(area != null);
        Extent2D extent = area.Extent;
        pass.RenderTargetWidth = extent.X;
        pass.RenderTargetHeight = extent.Y;
        RenderContinuation = flags != RenderingFlags.None;
        Mtl.MTL4RenderEncoderOptions options = (flags & RenderingFlags.Suspending) != 0 ?
            Mtl.MTL4RenderEncoderOptions.Suspending : Mtl.MTL4RenderEncoderOptions.None;
        if ((flags & RenderingFlags.Resuming) != 0) options |= Mtl.MTL4RenderEncoderOptions.Resuming;
        Render = new Mtl.MTL4RenderCommandEncoder(MetalInterop.retain(NativeCommands().RenderCommandEncoder(Pass, options).NativePtr));
        Render.SetArgumentTable(Arguments, MetalInterop.RenderStages);
        Render.SetFrontFacingWinding(Mtl.MTLWinding.CounterClockwise);
        Render.SetViewport(new Mtl.MTLViewport { width = extent.X, height = extent.Y, zfar = 1 });
        Render.SetScissorRect(new Mtl.MTLScissorRect { width = extent.X, height = extent.Y });
        Render.SetDepthStencilState(Pool.DepthState(default));
        Pipeline = null;
    }

    /// <summary>Ends the current render pass.</summary>
    public override void EndRenderPass()
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Render.NativePtr != 0);
        Mtl.MTL4RenderPassDescriptor pass = Pass;
        Render.EndEncoding();
        Render.Dispose();
        Render = default;
        Pipeline = null;
        if (RenderContinuation)
        {
            Commands.EndCommandBuffer();
            Commands = default;
        }
        uint resolveMask = ColorResolveMask;
        for (uint i = 0; i < 8; ++i)
        {
            Mtl.MTLRenderPassColorAttachmentDescriptor attachment = pass.ColorAttachments.Object(i);
            attachment.Texture = default;
            if ((resolveMask & (1u << (int)i)) != 0) attachment.ResolveTexture = default;
        }
        ColorResolveMask = 0;
        Mtl.MTLRenderPassDepthAttachmentDescriptor depthAttachment = pass.DepthAttachment;
        depthAttachment.Texture = default;
        Mtl.MTLRenderPassStencilAttachmentDescriptor stencilAttachment = pass.StencilAttachment;
        stencilAttachment.Texture = default;
    }

    /// <summary>Sets the top-left framebuffer viewport for upward-positive clip Y.</summary>
    /// <param name="viewport">The viewport rectangle and depth range.</param>
    public override void SetViewport(Viewport viewport)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording);
        if (Render.NativePtr == 0) return;
        Render.SetViewport(new Mtl.MTLViewport { originX = viewport.X, originY = viewport.Y, width = viewport.Width,
                                                          height = viewport.Height, znear = viewport.MinDepth, zfar = viewport.MaxDepth });
    }

    /// <summary>Sets the scissor rectangle.</summary>
    public override void SetScissor(Scissor scissor)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording);
        if (Render.NativePtr == 0) return;
        Render.SetScissorRect(new Mtl.MTLScissorRect { x = (ulong)scissor.X, y = (ulong)scissor.Y, width = scissor.Width, height = scissor.Height });
    }

    /// <summary>Sets depth and stencil tests for the current render pass.</summary>
    public override void SetDepthStencil(DepthStencilState state)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Recording);
        Mtl.MTLDepthStencilState depth = Pool.DepthState(in state);
        if (Render.NativePtr == 0) return;
        Render.SetDepthStencilState(depth);
        Render.SetStencilReferenceValues(state.Front.Reference, state.Back.Reference);
    }

    /// <summary>Records a non-indexed draw using the bound graphics pipeline's topology.</summary>
    /// <param name="root">The root range at a 16-byte-aligned buffer offset, or default to preserve its binding; retain until GPU completion.</param>
    /// <param name="vertexCount">The number of vertices per instance.</param>
    /// <param name="instanceCount">The number of instances.</param>
    /// <param name="firstVertex">The first vertex index.</param>
    /// <param name="firstInstance">The first instance index.</param>
    public override void Draw(GPURange root, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Render.NativePtr != 0 && Pipeline != null && !Pipeline.Mesh);
        RootData(root);
        Render.DrawPrimitives(Pipeline.PrimitiveType, firstVertex, vertexCount, instanceCount, firstInstance);
    }

    /// <summary>Records an indexed draw using the bound graphics pipeline's topology.</summary>
    /// <param name="root">The root range at a 16-byte-aligned buffer offset, or default to preserve its binding; retain until GPU completion.</param>
    /// <param name="indices">The GPU index-buffer range.</param>
    /// <param name="type">The index element type.</param>
    /// <param name="indexCount">The number of indices per instance, including strip restart markers.</param>
    /// <param name="instanceCount">The number of instances.</param>
    /// <param name="firstIndex">The first index-buffer element.</param>
    /// <param name="vertexOffset">The offset added to each non-restart index.</param>
    /// <param name="firstInstance">The first instance index.</param>
    public override void DrawIndexed(GPURange root, GPURange indices, IndexType type, uint indexCount, uint instanceCount = 1,
                                     uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Render.NativePtr != 0 && Pipeline != null && !Pipeline.Mesh);
        RootData(root);
        ulong offset = firstIndex * (type == IndexType.UInt16 ? 2u : 4u);
        Render.DrawIndexedPrimitives(Pipeline.PrimitiveType, indexCount,
                                              type == IndexType.UInt16 ? Mtl.MTLIndexType.UInt16 : Mtl.MTLIndexType.UInt32,
                                              ((MetalGpuHeapOwner)indices.Buffer!).Address + indices.Offset + offset,
                                              indices.Size - offset, instanceCount, vertexOffset, firstInstance);
    }

    /// <summary>Records draws with GPU arguments using the bound graphics pipeline's topology.</summary>
    /// <param name="root">The root range at a 16-byte-aligned buffer offset, or default to preserve its binding; retain until GPU completion.</param>
    /// <param name="arguments">The GPU range of non-indexed draw arguments.</param>
    /// <param name="drawCount">The number of draws.</param>
    /// <param name="stride">The byte stride between draws; zero uses the native argument size.</param>
    public override void DrawIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Render.NativePtr != 0 && Pipeline != null && !Pipeline.Mesh);
        RootData(root);
        if (stride == 0) stride = (uint)sizeof(Mtl.MTLDrawPrimitivesIndirectArguments);
        ulong address = ((MetalGpuHeapOwner)arguments.Buffer!).Address + arguments.Offset;
        for (uint i = 0; i < drawCount; ++i)
            Render.DrawPrimitives(Pipeline.PrimitiveType, address + i * stride);
    }

    /// <summary>Records indexed draws with GPU arguments using the bound graphics pipeline's topology.</summary>
    /// <param name="root">The root range at a 16-byte-aligned buffer offset, or default to preserve its binding; retain until GPU completion.</param>
    /// <param name="indices">The GPU index-buffer range.</param>
    /// <param name="type">The index element type.</param>
    /// <param name="arguments">The GPU range of indexed draw arguments.</param>
    /// <param name="drawCount">The number of draws.</param>
    /// <param name="stride">The byte stride between draws; zero uses the native argument size.</param>
    public override void DrawIndexedIndirect(GPURange root, GPURange indices, IndexType type, GPURange arguments, uint drawCount = 1, uint stride = 0)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Render.NativePtr != 0 && Pipeline != null && !Pipeline.Mesh);
        RootData(root);
        if (stride == 0) stride = (uint)sizeof(Mtl.MTLDrawIndexedPrimitivesIndirectArguments);
        ulong indexAddress = ((MetalGpuHeapOwner)indices.Buffer!).Address + indices.Offset;
        ulong argumentAddress = ((MetalGpuHeapOwner)arguments.Buffer!).Address + arguments.Offset;
        for (uint i = 0; i < drawCount; ++i)
            Render.DrawIndexedPrimitives(Pipeline.PrimitiveType, type == IndexType.UInt16 ? Mtl.MTLIndexType.UInt16 : Mtl.MTLIndexType.UInt32,
                                                  indexAddress, indices.Size, argumentAddress + i * stride);
    }

    /// <summary>Records a compute dispatch.</summary>
    /// <param name="root">The root range at a 16-byte-aligned buffer offset, or default to preserve its binding; retain until GPU completion.</param>
    /// <param name="groupCount">The workgroup count in each dimension.</param>
    public override void Dispatch(GPURange root, Extent3D groupCount)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Pipeline != null && Pipeline.Compute.NativePtr != 0);
        RootData(root);
        ComputeEncoder().DispatchThreadgroups(MetalInterop.MetalSize(groupCount), Pipeline.Threads);
    }

    /// <summary>Records a compute dispatch with arguments read from GPU memory.</summary>
    /// <param name="root">The root range at a 16-byte-aligned buffer offset, or default to preserve its binding; retain until GPU completion.</param>
    /// <param name="arguments">The buffer range containing three 32-bit workgroup counts.</param>
    public override void DispatchIndirect(GPURange root, GPURange arguments)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Pipeline != null && Pipeline.Compute.NativePtr != 0);
        RootData(root);
        ComputeEncoder().DispatchThreadgroups(((MetalGpuHeapOwner)arguments.Buffer!).Address + arguments.Offset, Pipeline.Threads);
    }

    /// <summary>Records a task/mesh-shading draw.</summary>
    /// <param name="root">The root range at a 16-byte-aligned buffer offset, or default to preserve its binding; retain until GPU completion.</param>
    /// <param name="groupCount">The task workgroup count, or mesh workgroup count when no task stage is bound.</param>
    public override void DrawMeshlets(GPURange root, Extent3D groupCount)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Render.NativePtr != 0 && Pipeline != null && Pipeline.Mesh);
        RootData(root);
        Render.DrawMeshThreadgroups(MetalInterop.MetalSize(groupCount), Pipeline.ObjectThreads, Pipeline.Threads);
    }

    /// <summary>Records indirect task/mesh-shading draws on supported devices.</summary>
    /// <param name="root">The root range at a 16-byte-aligned buffer offset, or default to preserve its binding; retain until GPU completion.</param>
    /// <param name="arguments">The buffer range containing indirect task/mesh draw arguments.</param>
    /// <param name="drawCount">The number of draws.</param>
    /// <param name="stride">The byte stride between draws; zero uses the native argument size.</param>
    public override void DrawMeshletsIndirect(GPURange root, GPURange arguments, uint drawCount = 1, uint stride = 0)
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Debug.Assert(Render.NativePtr != 0 && Pipeline != null && Pipeline.Mesh);
        Debug.Assert(Owner.Capabilities.IndirectMeshDraw);
        RootData(root);
        if (stride == 0) stride = 12; // sizeof(MTLDispatchThreadgroupsIndirectArguments): three UInt32 threadgroups per grid.
        ulong address = ((MetalGpuHeapOwner)arguments.Buffer!).Address + arguments.Offset;
        // Metal 4 reuses the previous record address for consecutive mesh indirect draws unless an argument-table write separates them.
        for (uint i = 0; i < drawCount; ++i)
        {
            if (i != 0 || root.Buffer == null) Arguments.SetAddress(RootAddress, MetalInterop.ArgRootIndex);
            Render.DrawMeshThreadgroups(address + i * stride, Pipeline.ObjectThreads, Pipeline.Threads);
        }
    }

    private struct MetalTextureCopy
    {
        internal Mtl.MTLOrigin Origin;
        internal Mtl.MTLSize Extent;
        internal ulong RowPitch;
        internal ulong ImagePitch;
        internal ulong SlicePitch;
        internal ulong Size;
        internal uint Slices;
    }

    private MetalTextureCopy TextureCopy(MetalTexture texture, in TextureCopyDesc desc)
    {
        Debug.Assert(texture.Owner == Owner);
        if (texture.Swapchain != null)
        {
            Debug.Assert(Pool.QueueIndex == 0 && texture.Swapchain.Drawable.NativePtr != 0);
            UsesSwapchain = true;
        }
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(texture.Desc.Format);
        uint width = texture.Desc.Extent.X >> (int)desc.MipLevel;
        uint height = texture.Desc.Extent.Y >> (int)desc.MipLevel;
        uint depth = texture.Desc.Extent.Z >> (int)desc.MipLevel;
        Backend.AssertTextureCopyRegion(texture.Desc.Format, new Extent3D(Math.Max(width, 1u), Math.Max(height, 1u), Math.Max(depth, 1u)),
            desc.Offset, desc.Extent);
        MetalTextureCopy copy = new()
        {
            Origin = new Mtl.MTLOrigin { x = desc.Offset.X, y = desc.Offset.Y, z = desc.Offset.Z },
            Extent = new Mtl.MTLSize { width = desc.Extent.X != 0 ? desc.Extent.X : (width != 0 ? width : 1) - desc.Offset.X,
                                       height = desc.Extent.Y != 0 ? desc.Extent.Y : (height != 0 ? height : 1) - desc.Offset.Y,
                                       depth = desc.Extent.Z != 0 ? desc.Extent.Z : (depth != 0 ? depth : 1) - desc.Offset.Z },
            Slices = desc.SliceCount != 0 ? desc.SliceCount : texture.Desc.LayerCount - desc.BaseSlice,
        };
        ulong rowBytes = ((copy.Extent.width + format.BlockExtent.X - 1) / format.BlockExtent.X) * format.BytesPerBlock;
        ulong rows = (copy.Extent.height + format.BlockExtent.Y - 1) / format.BlockExtent.Y;
        copy.RowPitch = desc.RowPitchBytes != 0 ? desc.RowPitchBytes : rowBytes;
        copy.ImagePitch = copy.RowPitch * rows;
        copy.SlicePitch = desc.SlicePitchBytes != 0 ? desc.SlicePitchBytes : copy.ImagePitch * copy.Extent.depth;
        if (texture.Desc.Type == TextureType.ThreeD && desc.SlicePitchBytes != 0) copy.ImagePitch = desc.SlicePitchBytes;
        copy.Size = copy.SlicePitch * (copy.Slices - 1) + copy.ImagePitch * (copy.Extent.depth - 1) + copy.RowPitch * (rows - 1) + rowBytes;
        return copy;
    }

    private void RenderAttachment(Mtl.MTLRenderPassAttachmentDescriptor attachment, MetalRenderView? view, LoadOp load, StoreOp store)
    {
        attachment.Texture = view != null ? view.Texture : default;
        if (view == null) return;
        Debug.Assert(view.Owner == Owner);
        if (view.Swapchain != null)
        {
            Debug.Assert(Pool.QueueIndex == 0 && view.Swapchain.Drawable.NativePtr != 0);
            UsesSwapchain = true;
        }
        attachment.Level = view.Mip;
        attachment.Slice = view.Texture.TextureType == Mtl.MTLTextureType.Type3D ? 0 : view.Slice;
        attachment.DepthPlane = view.Texture.TextureType == Mtl.MTLTextureType.Type3D ? view.Slice : 0;
        attachment.LoadAction = MetalInterop.LoadAction(load);
        attachment.StoreAction = store == StoreOp.Store ? Mtl.MTLStoreAction.Store : Mtl.MTLStoreAction.DontCare;
    }

    private void RootData(GPURange root)
    {
        if (root.Buffer == null) return;
        Debug.Assert((root.Offset & 15) == 0);
        RootAddress = ((MetalGpuHeapOwner)root.Buffer).Address + root.Offset;
        Arguments.SetAddress(RootAddress, MetalInterop.ArgRootIndex);
    }
}
