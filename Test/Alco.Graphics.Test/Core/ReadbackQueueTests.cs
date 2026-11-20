// Caller-recorded readback regressions for bounded staging, CPU delivery, texture pitches, abandonment, and zero-GC reuse.

using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

internal static unsafe class ReadbackQueueTests
{
    private const byte Sentinel = 0xa5;
    private const int Guard = 16;

    internal static void Run(GPUDevice device, Action<bool, string> check)
    {
        Console.WriteLine("ReadbackQueue:");
        RunBuffers(device, check);
        RunOutOfOrder(device, check);
        const TextureUsage usage = TextureUsage.TransferSource | TextureUsage.TransferDestination;
        RunTexture(device, new TextureDesc { Extent = new Extent3D(13, 7, 1), Usage = usage }, default, check, "packed RGBA8");
        RunTexture(device, new TextureDesc
        {
            Type = TextureType.TwoDArray, Extent = new Extent3D(32, 16, 1), MipLevels = 2, LayerCount = 3, Usage = usage,
        }, new TextureCopyDesc
        {
            MipLevel = 1, BaseSlice = 1, Offset = new Extent3D(3, 2, 0), Extent = new Extent3D(5, 3, 1),
            RowPitchBytes = 23, SlicePitchBytes = 79,
        }, check, "pitched RGBA8 array mip region with remaining slices");
        RunTexture(device, new TextureDesc
        {
            Type = TextureType.ThreeD, Extent = new Extent3D(20, 16, 8), MipLevels = 2, Usage = usage,
        }, new TextureCopyDesc
        {
            MipLevel = 1, Offset = new Extent3D(2, 1, 1), Extent = new Extent3D(5, 3, 0), SlicePitchBytes = 67,
        }, check, "slice-padded RGBA8 3D mip region with remaining depth");
        if (device.Caps.TextureCompressionBc)
            RunTexture(device, new TextureDesc
            {
                Type = TextureType.TwoDArray, Extent = new Extent3D(12, 12, 1), MipLevels = 2, LayerCount = 3,
                Format = Format.Bc1Unorm, Usage = usage,
            }, new TextureCopyDesc
            {
                MipLevel = 1, BaseSlice = 1, SliceCount = 2, Offset = new Extent3D(4, 0, 0), Extent = new Extent3D(2, 6, 1),
                RowPitchBytes = 11, SlicePitchBytes = 31,
            }, check, "pitched BC1 array mip-edge blocks");
        else
            Console.WriteLine("  [SKIP] compressed readbacks require the existing BC capability");
        RunDestroy(device, check);
        RunAllocationFree(device, check);
    }

    private static void RunBuffers(GPUDevice device, Action<bool, string> check)
    {
        GPUHeap source = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        ReadbackQueue? queue = null;
        byte* output = (byte*)NativeMemory.Alloc(512);
        try
        {
            byte[] pattern = new byte[256];
            for (int index = 0; index < pattern.Length; ++index) pattern[index] = unchecked((byte)(index * 17 + index / 7 + 23));
            byte[] expected = new byte[512];
            source = device.CreateGPUHeap((ulong)pattern.Length);
            pattern.AsSpan().CopyTo(source.Range.CpuSpan);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            TimelinePoint completion = new() { Semaphore = timeline };
            for (int mode = 0; mode < 2; ++mode)
            {
                ulong capacity = mode == 0 ? 64ul : 128ul;
                string label = mode == 0 ? "staging capacity" : "request slots with spare staging";
                queue = new ReadbackQueue(device, capacity, mode == 0 ? 4u : 1u);
                ReadbackRequest request = new();
                ReadbackRequest refused = new();
                check(!request.IsPending && !request.IsCompleted, $"{label}: new requests are idle");
                for (int round = 0; round < 2; ++round)
                {
                    NativeMemory.Fill(output, 512, Sentinel);
                    expected.AsSpan().Fill(Sentinel);
                    ++completion.Value;
                    GPUCommandBuffer commands = pool.BeginCommands();
                    bool oversized = queue.TryReadBuffer(commands, source.Range.GPURange.Slice(0, capacity + 16), completion,
                        output + 128, capacity + 16, refused);
                    bool accepted = queue.TryReadBuffer(commands, source.Range.GPURange.Slice((ulong)(32 + round * 64), 64), completion,
                        output + Guard, 64, request);
                    bool pressure = queue.TryReadBuffer(commands, source.Range.GPURange.Slice(0, 16), completion, output + 128, 16, refused);
                    queue.Tick();
                    check(!oversized && accepted && !pressure && request.IsPending && !request.IsCompleted &&
                        !refused.IsPending && !refused.IsCompleted && IsFilled(output, 512),
                        $"{label}: oversized/full requests fail unchanged and pre-submit Tick preserves the entire CPU sentinel");
                    commands.EndCommands();
                    if (oversized || !accepted || pressure) return;
                    device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                    completion.Wait();
                    check(request.IsPending && !request.IsCompleted && IsFilled(output, 512),
                        $"{label}: GPU completion alone does not publish bytes or complete the request");
                    commands = pool.BeginCommands();
                    TimelinePoint nextPoint = new() { Semaphore = timeline, Value = completion.Value + 1 };
                    bool reclaimed = queue.TryReadBuffer(commands, source.Range.GPURange.Slice(0, 16), nextPoint, output + 128, 16, refused);
                    commands.EndCommands();
                    check(!reclaimed && !refused.IsPending && !refused.IsCompleted && request.IsPending && IsFilled(output, 512),
                        $"{label}: recording does not implicitly reclaim completed staging or request slots");
                    if (reclaimed) return;
                    pool.Reset();
                    queue.Tick();
                    pattern.AsSpan(32 + round * 64, 64).CopyTo(expected.AsSpan(Guard));
                    check(!request.IsPending && request.IsCompleted && new ReadOnlySpan<byte>(output, 512).SequenceEqual(expected),
                        $"{label}: Tick delivers exact subrange bytes, preserves guards and failed destinations, and recycles storage");
                    queue.Tick();
                    check(request.IsCompleted && new ReadOnlySpan<byte>(output, 512).SequenceEqual(expected),
                        $"{label}: repeated Tick leaves completed data and state unchanged");
                    if (round == 0)
                    {
                        request.Reset();
                        check(!request.IsPending && !request.IsCompleted, $"{label}: Reset permits reuse of the same request and destination");
                    }
                }
                device.WaitIdle();
                queue.Destroy();
                queue = null;
                check(request.IsCompleted && !request.IsPending, $"{label}: destroying an empty queue preserves completed requests");
            }
        }
        finally
        {
            device.WaitIdle();
            pool?.Reset();
            queue?.Destroy();
            pool?.Destroy();
            source.Destroy();
            timeline?.Destroy();
            NativeMemory.Free(output);
        }
    }

    private static void RunOutOfOrder(GPUDevice device, Action<bool, string> check)
    {
        GPUHeap source = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? firstTimeline = null;
        GPUTimelineSemaphore? laterTimeline = null;
        ReadbackQueue? queue = null;
        byte* output = (byte*)NativeMemory.Alloc(288);
        try
        {
            byte[] pattern = new byte[192];
            for (int index = 0; index < pattern.Length; ++index) pattern[index] = unchecked((byte)(index * 13 + index / 11));
            byte[] expected = new byte[288];
            expected.AsSpan().Fill(Sentinel);
            NativeMemory.Fill(output, 288, Sentinel);
            source = device.CreateGPUHeap((ulong)pattern.Length);
            pattern.AsSpan().CopyTo(source.Range.CpuSpan);
            pool = device.CreateCommandPool(0);
            firstTimeline = device.CreateTimelineSemaphore();
            laterTimeline = device.CreateTimelineSemaphore();
            queue = new ReadbackQueue(device, 128, 3);
            ReadbackRequest first = new();
            ReadbackRequest later = new();
            ReadbackRequest refused = new();
            TimelinePoint firstPoint = new() { Semaphore = firstTimeline, Value = 1 };
            TimelinePoint laterPoint = new() { Semaphore = laterTimeline, Value = 1 };
            GPUCommandBuffer held = pool.BeginCommands();
            bool firstAccepted = queue.TryReadBuffer(held, source.Range.GPURange.Slice(0, 64), firstPoint, output + Guard, 64, first);
            held.EndCommands();
            GPUCommandBuffer commands = pool.BeginCommands();
            bool laterAccepted = queue.TryReadBuffer(commands, source.Range.GPURange.Slice(64, 64), laterPoint, output + 112, 64, later);
            bool pressure = queue.TryReadBuffer(commands, source.Range.GPURange.Slice(128, 64), laterPoint, output + 208, 64, refused);
            commands.EndCommands();
            check(firstAccepted && laterAccepted && !pressure && !refused.IsPending && !refused.IsCompleted,
                "two requests exactly fill staging while a third request slot remains available");
            if (!firstAccepted || !laterAccepted || pressure) return;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = laterPoint });
            laterPoint.Wait();
            queue.Tick();
            pattern.AsSpan(64, 64).CopyTo(expected.AsSpan(112));
            check(firstTimeline.CompletedValue == 0 && first.IsPending && !first.IsCompleted && later.IsCompleted && !later.IsPending &&
                new ReadOnlySpan<byte>(output, 288).SequenceEqual(expected),
                "Tick retires a later semaphore without waiting for an earlier unsubmitted request or writing its destination");
            if (later.IsPending || !later.IsCompleted) return;
            later.Reset();
            ++laterPoint.Value;
            commands = pool.BeginCommands();
            bool reused = queue.TryReadBuffer(commands, source.Range.GPURange.Slice(128, 64), laterPoint, output + 208, 64, later);
            commands.EndCommands();
            check(reused, "later completion makes the only free 64-byte staging range reusable behind an unsubmitted request");
            if (!reused) return;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = laterPoint });
            laterPoint.Wait();
            queue.Tick();
            pattern.AsSpan(128, 64).CopyTo(expected.AsSpan(208));
            check(first.IsPending && later.IsCompleted && new ReadOnlySpan<byte>(output, 288).SequenceEqual(expected),
                "reused staging delivers new data without corrupting the retained request or surrounding guards");
            device.Submit(new SubmitDesc { Commands = [held], Completion = firstPoint });
            firstPoint.Wait();
            queue.Tick();
            pattern.AsSpan(0, 64).CopyTo(expected.AsSpan(Guard));
            check(first.IsCompleted && !first.IsPending && new ReadOnlySpan<byte>(output, 288).SequenceEqual(expected),
                "the retained request still completes correctly after pending-list compaction and staging reuse");
        }
        finally
        {
            device.WaitIdle();
            pool?.Reset();
            queue?.Destroy();
            pool?.Destroy();
            source.Destroy();
            firstTimeline?.Destroy();
            laterTimeline?.Destroy();
            NativeMemory.Free(output);
        }
    }

    private static void RunTexture(GPUDevice device, in TextureDesc desc, TextureCopyDesc copy, Action<bool, string> check, string label)
    {
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(desc.Format);
        int[] offsets = new int[desc.MipLevels];
        int[] sizes = new int[desc.MipLevels];
        int sourceSize = 0;
        for (int mip = 0; mip < offsets.Length; ++mip)
        {
            uint columns = (Math.Max(1u, desc.Extent.X >> mip) + format.BlockExtent.X - 1) / format.BlockExtent.X;
            uint rowsInMip = (Math.Max(1u, desc.Extent.Y >> mip) + format.BlockExtent.Y - 1) / format.BlockExtent.Y;
            uint planesInMip = desc.Type == TextureType.ThreeD ? Math.Max(1u, desc.Extent.Z >> mip) : desc.LayerCount;
            offsets[mip] = sourceSize;
            sizes[mip] = (int)(columns * rowsInMip * planesInMip * format.BytesPerBlock);
            sourceSize = (sourceSize + sizes[mip] + 255) & ~255;
        }
        byte[] sourceData = new byte[sourceSize];
        for (int index = 0; index < sourceData.Length; ++index) sourceData[index] = unchecked((byte)(index * 17 + index / 7 + 23));
        uint mipWidth = Math.Max(1u, desc.Extent.X >> (int)copy.MipLevel);
        uint mipHeight = Math.Max(1u, desc.Extent.Y >> (int)copy.MipLevel);
        uint width = copy.Extent.X != 0 ? copy.Extent.X : mipWidth - copy.Offset.X;
        uint height = copy.Extent.Y != 0 ? copy.Extent.Y : mipHeight - copy.Offset.Y;
        uint depth = copy.Extent.Z != 0 ? copy.Extent.Z : Math.Max(1u, desc.Extent.Z >> (int)copy.MipLevel) - copy.Offset.Z;
        int planes = (int)(desc.Type == TextureType.ThreeD ? depth : copy.SliceCount != 0 ? copy.SliceCount : desc.LayerCount - copy.BaseSlice);
        int rowBytes = (int)((width + format.BlockExtent.X - 1) / format.BlockExtent.X * format.BytesPerBlock);
        int rows = (int)((height + format.BlockExtent.Y - 1) / format.BlockExtent.Y);
        int rowPitch = copy.RowPitchBytes != 0 ? (int)copy.RowPitchBytes : rowBytes;
        int slicePitch = copy.SlicePitchBytes != 0 ? (int)copy.SlicePitchBytes : rowPitch * rows;
        int outputSize = (planes - 1) * slicePitch + (rows - 1) * rowPitch + rowBytes;
        int sourceRow = (int)((mipWidth + format.BlockExtent.X - 1) / format.BlockExtent.X * format.BytesPerBlock);
        int sourceSlice = sourceRow * (int)((mipHeight + format.BlockExtent.Y - 1) / format.BlockExtent.Y);
        int sourceStart = offsets[copy.MipLevel] + (int)(desc.Type == TextureType.ThreeD ? copy.Offset.Z : copy.BaseSlice) * sourceSlice +
            (int)(copy.Offset.Y / format.BlockExtent.Y) * sourceRow + (int)(copy.Offset.X / format.BlockExtent.X * format.BytesPerBlock);
        byte[] expected = new byte[outputSize + 2 * Guard];
        expected.AsSpan().Fill(Sentinel);
        for (int plane = 0; plane < planes; ++plane)
            for (int row = 0; row < rows; ++row)
                sourceData.AsSpan(sourceStart + plane * sourceSlice + row * sourceRow, rowBytes)
                    .CopyTo(expected.AsSpan(Guard + plane * slicePitch + row * rowPitch));
        byte* output = (byte*)NativeMemory.Alloc((nuint)expected.Length);
        GPUTextureHeap heap = default;
        GPUTexture? texture = null;
        GPUHeap upload = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        ReadbackQueue? queue = null;
        try
        {
            NativeMemory.Fill(output, (nuint)expected.Length, Sentinel);
            heap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
            texture = device.CreateTexture(desc, heap, 0);
            upload = device.CreateGPUHeap((ulong)sourceSize);
            sourceData.AsSpan().CopyTo(upload.Range.CpuSpan);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            queue = new ReadbackQueue(device, ((ulong)(rowBytes * rows * planes) + 15) & ~15ul, 1);
            GPUCommandBuffer uploaded = pool.BeginCommands();
            uploaded.InitializeTexture(texture);
            for (int mip = 0; mip < offsets.Length; ++mip)
                uploaded.CopyMemoryToTexture(upload.Range.GPURange.Slice((ulong)offsets[mip], (ulong)sizes[mip]), texture,
                    new TextureCopyDesc { MipLevel = (uint)mip });
            uploaded.EndCommands();
            TimelinePoint uploadPoint = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [uploaded], Completion = uploadPoint });
            // Keep address-based texture uploads and readbacks in distinct command buffers and submissions on NVIDIA drivers.
            GPUCommandBuffer commands = pool.BeginCommands();
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            TimelinePoint completion = new() { Semaphore = timeline, Value = 2 };
            ReadbackRequest request = new();
            bool accepted = queue.TryReadTexture(commands, texture, desc, completion, output + Guard, (ulong)outputSize, request, copy);
            commands.EndCommands();
            check(accepted, $"{label}: only packed staging and the last output row are required");
            if (!accepted) return;
            device.Submit(new SubmitDesc { Commands = [commands], Waits = [uploadPoint], Completion = completion });
            completion.Wait();
            check(request.IsPending && !request.IsCompleted && IsFilled(output, expected.Length),
                $"{label}: GPU completion preserves CPU output until Tick");
            queue.Tick();
            check(request.IsCompleted && !request.IsPending && new ReadOnlySpan<byte>(output, expected.Length).SequenceEqual(expected),
                $"{label}: exact selected bytes preserve row/slice padding and both guards without trailing output padding");
        }
        finally
        {
            device.WaitIdle();
            pool?.Reset();
            queue?.Destroy();
            pool?.Destroy();
            texture?.Destroy();
            heap.Destroy();
            upload.Destroy();
            timeline?.Destroy();
            NativeMemory.Free(output);
        }
    }

    private static void RunDestroy(GPUDevice device, Action<bool, string> check)
    {
        GPUHeap source = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        ReadbackQueue? queue = null;
        byte* output = (byte*)NativeMemory.Alloc(192);
        try
        {
            byte[] expected = new byte[192];
            expected.AsSpan().Fill(Sentinel);
            for (int index = 0; index < 32; ++index) expected[Guard + index] = unchecked((byte)(index * 19 + 7));
            NativeMemory.Fill(output, 192, Sentinel);
            source = device.CreateGPUHeap(32);
            expected.AsSpan(Guard, 32).CopyTo(source.Range.CpuSpan);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            queue = new ReadbackQueue(device, 96, 3);
            ReadbackRequest completed = new();
            ReadbackRequest ready = new();
            ReadbackRequest discarded = new();
            ReadbackRequest[] requests = [completed, ready, discarded];
            TimelinePoint point = new() { Semaphore = timeline };
            bool accepted = true;
            for (int index = 0; index < requests.Length; ++index)
            {
                ++point.Value;
                GPUCommandBuffer commands = pool.BeginCommands();
                accepted &= queue.TryReadBuffer(commands, source.Range.GPURange, point, output + Guard + index * 64, 32, requests[index]);
                commands.EndCommands();
                if (index == 2) continue;
                device.Submit(new SubmitDesc { Commands = [commands], Completion = point });
                point.Wait();
                if (index == 0) queue.Tick();
            }
            check(accepted && completed.IsCompleted && ready.IsPending && discarded.IsPending &&
                new ReadOnlySpan<byte>(output, 192).SequenceEqual(expected),
                "destruction fixture retains delivered, GPU-completed undelivered, and unsubmitted requests independently");
            device.WaitIdle();
            pool.Reset();
            queue.Destroy();
            queue = null;
            check(completed.IsCompleted && !completed.IsPending && !ready.IsPending && !ready.IsCompleted &&
                !discarded.IsPending && !discarded.IsCompleted && new ReadOnlySpan<byte>(output, 192).SequenceEqual(expected),
                "safe Destroy abandons discarded and GPU-completed copies without writing their targets or changing completed requests");
            ready.Reset();
            discarded.Reset();
            check(!ready.IsPending && !ready.IsCompleted && !discarded.IsPending && !discarded.IsCompleted,
                "safely abandoned requests can be reset as idle requests");
        }
        finally
        {
            device.WaitIdle();
            pool?.Reset();
            queue?.Destroy();
            pool?.Destroy();
            source.Destroy();
            timeline?.Destroy();
            NativeMemory.Free(output);
        }
    }

    private static void RunAllocationFree(GPUDevice device, Action<bool, string> check)
    {
        GPUHeap source = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        ReadbackQueue? queue = null;
        byte* output = (byte*)NativeMemory.Alloc(96);
        try
        {
            Span<byte> expected = stackalloc byte[96];
            expected.Fill(Sentinel);
            for (int index = 0; index < 64; ++index) expected[Guard + index] = unchecked((byte)(index * 11 + 31));
            source = device.CreateGPUHeap(64);
            expected.Slice(Guard, 64).CopyTo(source.Range.CpuSpan);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            queue = new ReadbackQueue(device, 64, 1);
            ReadbackRequest request = new();
            GPUCommandBuffer[] submitted = new GPUCommandBuffer[1];
            TimelinePoint point = new() { Semaphore = timeline };
            bool valid = true;
            long allocatedBefore = 0;
            for (int iteration = -64; iteration < 256; ++iteration)
            {
                if (iteration == 0) allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                pool.Reset();
                NativeMemory.Fill(output, 96, Sentinel);
                ++point.Value;
                GPUCommandBuffer commands = pool.BeginCommands();
                bool accepted = queue.TryReadBuffer(commands, source.Range.GPURange, point, output + Guard, 64, request);
                commands.EndCommands();
                if (!accepted)
                {
                    valid = false;
                    break;
                }
                submitted[0] = commands;
                device.Submit(new SubmitDesc { Commands = submitted, Completion = point });
                point.Wait();
                queue.Tick();
                valid &= request.IsCompleted && !request.IsPending && new ReadOnlySpan<byte>(output, 96).SequenceEqual(expected);
                if (!valid) break;
                request.Reset();
                queue.Tick();
                valid &= !request.IsCompleted && !request.IsPending;
            }
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            check(valid && allocatedBytes == 0,
                $"warmed recording, submission, Tick, Reset and staging reuse preserve bytes/guards with no managed allocations ({allocatedBytes} bytes)");
        }
        finally
        {
            device.WaitIdle();
            pool?.Reset();
            queue?.Destroy();
            pool?.Destroy();
            source.Destroy();
            timeline?.Destroy();
            NativeMemory.Free(output);
        }
    }

    private static bool IsFilled(byte* bytes, int count)
    {
        for (int index = 0; index < count; ++index)
            if (bytes[index] != Sentinel) return false;
        return true;
    }
}
