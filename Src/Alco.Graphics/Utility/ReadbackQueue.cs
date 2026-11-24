// Fixed-capacity caller-recorded readbacks with nonblocking delivery into stable CPU storage.

using System.Diagnostics;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

/// <summary>Reusable readback state; externally synchronize access and reset only after completion or safe queue destruction.</summary>
public sealed class ReadbackRequest
{
    internal ReadbackQueue? Owner;
    internal bool Completed;

    /// <summary>Whether the queue still borrows the request's CPU destination.</summary>
    public bool IsPending => Owner != null;

    /// <summary>Whether Tick has copied the complete result into the CPU destination.</summary>
    public bool IsCompleted => Completed;

    /// <summary>Returns a completed or safely abandoned request to its initial state; pending requests cannot be reset.</summary>
    public void Reset()
    {
        Debug.Assert(Owner == null);
        Completed = false;
    }
}

/// <summary>Owns bounded readback staging; externally synchronize operations and keep the device alive until destruction.</summary>
public sealed unsafe class ReadbackQueue
{
    private struct PendingReadback
    {
        internal ReadbackRequest? Request;
        internal HeapAllocation<byte> Allocation;
        internal TimelinePoint Completion;
        internal byte* Destination;
        internal ulong RowBytes;
        internal ulong RowPitch;
        internal ulong SlicePitch;
        internal uint Rows;
        internal uint Slices;
    }

    private GPUHeap Heap;
    private HeapAllocator? Allocator;
    private PendingReadback[] Pending;
    private uint Count;

    /// <summary>Creates fixed-capacity readback staging and request bookkeeping without owning command pools or a GPU queue.</summary>
    /// <param name="device">The device retained until destruction.</param>
    /// <param name="capacity">The staging capacity, a positive multiple of 16 bytes no larger than HeapAllocator.MaximumSize.</param>
    /// <param name="maxPendingRequests">The nonzero request limit, no larger than HeapAllocator.MaximumAllocationCount.</param>
    /// <exception cref="GPUException">The readback heap could not be allocated or mapped.</exception>
    public ReadbackQueue(GPUDevice device, ulong capacity = 64ul * 1024 * 1024, uint maxPendingRequests = 64)
    {
        Debug.Assert(capacity >= HeapAllocator.Alignment && capacity <= HeapAllocator.MaximumSize && capacity % HeapAllocator.Alignment == 0);
        Debug.Assert(maxPendingRequests != 0 && maxPendingRequests <= HeapAllocator.MaximumAllocationCount);
        Pending = new PendingReadback[maxPendingRequests];
        Heap = device.CreateGPUHeap(capacity, MemoryType.Readback);
        Allocator = new HeapAllocator(Heap.Range, maxPendingRequests);
    }

    /// <summary>Releases staging without waiting or delivering results; first finish or discard every command referencing it.</summary>
    public void Destroy()
    {
        Debug.Assert(Allocator != null);
        for (uint index = 0; index < Count; ++index)
        {
            ReadbackRequest? request = Pending[index].Request;
            Debug.Assert(request != null && request.Owner == this);
            request.Owner = null;
            request.Completed = false;
        }
        Pending = [];
        Count = 0;
        Allocator = null;
        Heap.Destroy();
        Heap = default;
    }

    /// <summary>Records a buffer readback and its host-read barrier; source synchronization and submission remain caller-owned.</summary>
    /// <param name="commands">The command buffer recording the copy outside render passes.</param>
    /// <param name="source">The nonempty source range, kept alive through copy completion.</param>
    /// <param name="completion">The live timeline point signaled by the submission containing this copy.</param>
    /// <param name="destination">Stable cached native or pinned CPU storage, kept valid until completion or safe queue destruction.</param>
    /// <param name="destinationSize">The writable destination capacity, at least source.Size bytes.</param>
    /// <param name="request">An idle request retained by the queue until Tick delivers its data.</param>
    /// <returns>True when recorded; false without changing the request or commands when staging or request slots are exhausted.</returns>
    public bool TryReadBuffer(GPUCommandBuffer commands, GPURange source, TimelinePoint completion,
        void* destination, ulong destinationSize, ReadbackRequest request)
    {
        Debug.Assert(source.Buffer != null && source.Size != 0);
        PendingReadback pending = new()
        {
            Request = request,
            Completion = completion,
            Destination = (byte*)destination,
            RowBytes = source.Size,
            RowPitch = source.Size,
            SlicePitch = source.Size,
            Rows = 1,
            Slices = 1,
        };
        if (!TryReserve(ref pending, destinationSize))
            return false;
        commands.CopyMemory(source, pending.Allocation.Range.GPURange);
        commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
        return true;
    }

    /// <summary>Records a tightly packed texture readback; Tick applies the selected CPU output pitches without format conversion.</summary>
    /// <param name="commands">The command buffer recording the copy outside render passes; source synchronization remains caller-owned.</param>
    /// <param name="source">The initialized source texture, kept alive through copy completion.</param>
    /// <param name="description">The matching source description; combined depth/stencil formats are unsupported.</param>
    /// <param name="completion">The live timeline point signaled by the submission containing this copy.</param>
    /// <param name="destination">Stable cached native or pinned CPU storage, kept valid until completion or safe queue destruction.</param>
    /// <param name="destinationSize">The writable destination capacity, covering the last selected row without requiring trailing padding.</param>
    /// <param name="request">An idle request retained by the queue until Tick delivers its data.</param>
    /// <param name="copy">The selected region and CPU output pitches; depth/stencil transfers require whole mip subresources.</param>
    /// <returns>True when recorded; false without changing the request or commands when staging or request slots are exhausted.</returns>
    public bool TryReadTexture(GPUCommandBuffer commands, GPUTexture source, in TextureDesc description, TimelinePoint completion,
        void* destination, ulong destinationSize, ReadbackRequest request, in TextureCopyDesc copy = default)
    {
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(description.Format);
        Extent3D blocks = TextureUpload.GetRegionBlocks(description, copy, format, out TextureCopyDesc region);
        ulong rowBytes = (ulong)blocks.X * format.BytesPerBlock;
        ulong rowPitch = copy.RowPitchBytes != 0 ? copy.RowPitchBytes : rowBytes;
        PendingReadback pending = new()
        {
            Request = request,
            Completion = completion,
            Destination = (byte*)destination,
            RowBytes = rowBytes,
            RowPitch = rowPitch,
            SlicePitch = copy.SlicePitchBytes != 0 ? copy.SlicePitchBytes : rowPitch * blocks.Y,
            Rows = blocks.Y,
            Slices = blocks.Z,
        };
        if (!TryReserve(ref pending, destinationSize))
            return false;
        commands.CopyTextureToMemory(source, pending.Allocation.Range.GPURange, region);
        commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
        return true;
    }

    /// <summary>Polls without waiting, copies ready results to CPU destinations, and recycles staging before marking requests complete.</summary>
    public void Tick()
    {
        Debug.Assert(Allocator != null);
        GPUTimelineSemaphore? timeline = null;
        ulong completedValue = 0;
        uint remaining = 0;
        for (uint index = 0; index < Count; ++index)
        {
            ref PendingReadback pending = ref Pending[index];
            Debug.Assert(pending.Completion.Semaphore != null);
            if (timeline != pending.Completion.Semaphore)
            {
                timeline = pending.Completion.Semaphore;
                completedValue = timeline.CompletedValue;
            }
            if (completedValue < pending.Completion.Value)
            {
                if (remaining != index)
                    Pending[remaining] = pending;
                ++remaining;
                continue;
            }

            ulong sliceBytes = pending.RowBytes * pending.Rows;
            byte* source = pending.Allocation.Range.Cpu;
            if (pending.RowPitch == pending.RowBytes && pending.SlicePitch == sliceBytes)
            {
                ulong sizeBytes = sliceBytes * pending.Slices;
                System.Buffer.MemoryCopy(source, pending.Destination, sizeBytes, sizeBytes);
            }
            else
            {
                for (uint slice = 0; slice < pending.Slices; ++slice)
                {
                    byte* input = source + slice * sliceBytes;
                    byte* output = pending.Destination + slice * pending.SlicePitch;
                    if (pending.RowPitch == pending.RowBytes)
                        System.Buffer.MemoryCopy(input, output, sliceBytes, sliceBytes);
                    else
                        for (uint row = 0; row < pending.Rows; ++row)
                            System.Buffer.MemoryCopy(input + row * pending.RowBytes, output + row * pending.RowPitch, pending.RowBytes, pending.RowBytes);
                }
            }
            Allocator.Free(pending.Allocation);
            ReadbackRequest? request = pending.Request;
            Debug.Assert(request != null && request.Owner == this);
            request.Owner = null;
            request.Completed = true;
        }
        Array.Clear(Pending, (int)remaining, (int)(Count - remaining));
        Count = remaining;
    }

    private bool TryReserve(ref PendingReadback pending, ulong destinationSize)
    {
        Debug.Assert(Allocator != null);
        Debug.Assert(pending.Request != null && pending.Request.Owner == null && !pending.Request.Completed);
        Debug.Assert(pending.Completion.Semaphore != null && pending.Destination != null);
        Debug.Assert(pending.RowBytes != 0 && pending.Rows != 0 && pending.Slices != 0);
        Debug.Assert(pending.RowPitch >= pending.RowBytes && pending.SlicePitch >= pending.RowPitch * pending.Rows);
        Debug.Assert(destinationSize >= (pending.Slices - 1) * pending.SlicePitch + (pending.Rows - 1) * pending.RowPitch + pending.RowBytes);
        if (Count == Pending.Length)
            return false;
        pending.Allocation = Allocator.Allocate(pending.RowBytes * pending.Rows * pending.Slices);
        if (pending.Allocation.Range.SizeBytes == 0)
            return false;
        Pending[Count++] = pending;
        pending.Request.Owner = this;
        return true;
    }
}
