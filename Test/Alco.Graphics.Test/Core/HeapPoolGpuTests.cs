// GPU heap-pool regressions for growth, budgets, reuse, native failure rollback, and completed transfer readbacks.

using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

internal static unsafe class HeapPoolGpuTests
{
    internal static void Run(GPUDevice device, Action<bool, string> check)
    {
        Console.WriteLine("GPU heap pools:");
        RunBufferGrowth(device, check);
        RunBufferReuse(device, check);
        RunGpuOnlyDedicated(device, check);
        RunLargeBuffer(device, check);
        RunTextures(device, check);
        if (device.GetType().FullName == "Alco.Graphics.LowLevel.VulkanDevice")
            RunCreationFailures(device, check);
        else
            Console.WriteLine("  [SKIP] Vulkan-native heap-pool failure injection requires a VulkanDevice");
    }

    private static void RunBufferGrowth(GPUDevice device, Action<bool, string> check)
    {
        const int heapCount = 70;
        const int payloadSize = 16;
        const ulong heapSize = 4096;
        HeapPoolDesc desc = new()
        {
            InitialHeapSize = heapSize,
            MaxHeapSize = heapSize,
            Budget = heapSize * heapCount,
            InitialAllocationCapacity = 1,
        };
        GPUHeapPool allocator = new(device, desc);
        HeapAllocation<byte>[] allocations = new HeapAllocation<byte>[heapCount];
        GPUHeap readback = default;
        GPUCommandPool? commandsPool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            HeapPoolStatistics empty = allocator.GetStatistics();
            check(empty.ReservedBytes == 0 && empty.UsedBytes == 0 && empty.HeapCount == 0 && empty.AllocationCount == 0,
                "buffer pools allocate no native backing before their first request");
            byte[] expected = new byte[heapCount * payloadSize];
            for (int index = 0; index < expected.Length; ++index)
                expected[index] = unchecked((byte)(index * 17 + index / payloadSize * 31));
            readback = device.CreateGPUHeap((ulong)expected.Length, MemoryType.Readback);
            commandsPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            allocations[0] = allocator.Allocate(heapSize);
            GPUAllocation<byte> oldest = allocations[0].Range;
            check(oldest.Cpu != null && oldest.Buffer != null && oldest.SizeBytes == heapSize && oldest.View.Index != 0 && oldest.View.Offset == 0,
                "the default buffer pool exposes CPU-visible native storage and a nonempty shader buffer view");
            if (oldest.Cpu == null || oldest.Buffer == null) return;
            expected.AsSpan(0, payloadSize).CopyTo(oldest.CpuSpan);
            GPUCommandBuffer commands = commandsPool.BeginCommands();
            commands.CopyMemory(oldest.GPURange.Slice(0, payloadSize), readback.Range.GPURange);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            completion.Wait();
            check(readback.Range.CpuSpan[..payloadSize].SequenceEqual(expected.AsSpan(0, payloadSize)),
                "the oldest pooled GPU range copies correctly before heap growth");
            commandsPool.Reset();

            bool allocated = true;
            for (int index = 1; index < allocations.Length; ++index)
            {
                allocations[index] = allocator.Allocate(heapSize);
                GPUAllocation<byte> range = allocations[index].Range;
                allocated &= range.Cpu != null && range.Buffer != null && range.SizeBytes == heapSize && range.View.Index != 0 && range.View.Offset == 0;
                for (int previous = 0; previous < index; ++previous)
                    allocated &= range.View.Index != allocations[previous].Range.View.Index;
                if (range.Cpu != null)
                    expected.AsSpan(index * payloadSize, payloadSize).CopyTo(range.CpuSpan);
            }
            check(allocated, "buffer pools grow beyond 64 native heaps from one initial metadata allocation slot");
            if (!allocated) return;
            HeapPoolStatistics grown = allocator.GetStatistics();
            check(grown.HeapCount == heapCount && grown.AllocationCount == heapCount && grown.ReservedBytes == desc.Budget &&
                grown.UsedBytes == desc.Budget && grown.PeakReservedBytes == desc.Budget && grown.PeakUsedBytes == desc.Budget &&
                grown.LargestFreeRange == 0, "70 small native heaps account for requested backing capacity rather than driver padding");
            check(ReferenceEquals(allocations[0].Range.Buffer, oldest.Buffer) && allocations[0].Range.Offset == oldest.Offset &&
                allocations[0].Range.SizeBytes == oldest.SizeBytes && allocations[0].Range.Cpu == oldest.Cpu &&
                allocations[0].Range.View.Index == oldest.View.Index && allocations[0].Range.View.Offset == oldest.View.Offset,
                "growing buffer storage and metadata preserves the oldest allocation identity, offset, size and shader view");
            commands = commandsPool.BeginCommands();
            for (int index = 0; index < allocations.Length; ++index)
                commands.CopyMemory(allocations[index].Range.GPURange.Slice(0, payloadSize),
                    readback.Range.GPURange.Slice((ulong)(index * payloadSize), payloadSize));
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            completion.Value = 2;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            completion.Wait();
            check(readback.Range.CpuSpan.SequenceEqual(expected),
                "completed GPU copies preserve every heap's contents, including the oldest range, after backing-storage growth");
            commandsPool.Reset();

            HeapAllocation<byte> rejected = allocator.Allocate(16);
            check(rejected.Range.Cpu == null && rejected.Range.Buffer == null && rejected.Range.SizeBytes == 0 &&
                rejected.Range.View.Index == 0 && rejected.Range.View.Offset == 0 && SameStatistics(grown, allocator.GetStatistics()),
                "an exact buffer backing budget returns an empty view without changing any statistics");
            if (rejected.Range.Buffer != null) allocator.Free(rejected);
            GPUAllocation<byte> released = allocations[heapCount / 2].Range;
            allocator.Free(allocations[heapCount / 2]);
            allocations[heapCount / 2] = default;
            allocations[heapCount / 2] = allocator.Allocate(heapSize);
            check(ReferenceEquals(allocations[heapCount / 2].Range.Buffer, released.Buffer) &&
                allocations[heapCount / 2].Range.Offset == released.Offset && allocations[heapCount / 2].Range.SizeBytes == released.SizeBytes &&
                allocations[heapCount / 2].Range.Cpu == released.Cpu &&
                allocations[heapCount / 2].Range.View.Index == released.View.Index && allocations[heapCount / 2].Range.View.Offset == released.View.Offset &&
                SameStatistics(grown, allocator.GetStatistics()), "a freed whole heap and its shader view are reused within the full backing budget");
            for (int index = 0; index < allocations.Length; ++index)
            {
                if (allocations[index].Range.Buffer != null) allocator.Free(allocations[index]);
                allocations[index] = default;
            }
            HeapPoolStatistics freed = allocator.GetStatistics();
            check(freed.UsedBytes == 0 && freed.AllocationCount == 0 && freed.HeapCount <= 1 &&
                freed.ReservedBytes == freed.HeapCount * heapSize && freed.LargestFreeRange == freed.ReservedBytes,
                "freeing many ordinary buffer heaps retains at most one empty heap");
            allocator.Trim();
            HeapPoolStatistics trimmed = allocator.GetStatistics();
            check(trimmed.ReservedBytes == 0 && trimmed.UsedBytes == 0 && trimmed.HeapCount == 0 && trimmed.LargestFreeRange == 0 &&
                trimmed.PeakReservedBytes == grown.PeakReservedBytes && trimmed.PeakUsedBytes == grown.PeakUsedBytes,
                "buffer Trim releases the cached empty heap while preserving high-water statistics");
        }
        finally
        {
            device.WaitIdle();
            commandsPool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            for (int index = 0; index < allocations.Length; ++index)
                if (allocations[index].Range.Buffer != null) allocator.Free(allocations[index]);
            allocator.Destroy();
        }
    }

    private static void RunBufferReuse(GPUDevice device, Action<bool, string> check)
    {
        HeapPoolDesc desc = new() { InitialHeapSize = 4096, MaxHeapSize = 4096, Budget = 4096, InitialAllocationCapacity = 1 };
        GPUHeapPool allocator = new(device, desc);
        HeapAllocation<uint> held = default;
        HeapAllocation<uint> temporary = default;
        try
        {
            held = allocator.Allocate<uint>();
            check(held.Range.Cpu != null && held.Range.Buffer != null && held.Range.SizeBytes == sizeof(uint) &&
                held.Range.View.Index != 0 && held.Range.View.Offset == 0 && allocator.GetStatistics().UsedBytes == 16,
                "typed default buffer requests preserve byte size and shader views while reserving 16-byte-aligned storage");
            if (held.Range.Buffer == null) return;
            temporary = allocator.Allocate<uint>(16);
            GPUAllocation<uint> original = temporary.Range;
            check(original.Buffer != null && original.SizeBytes == 16 * sizeof(uint) && original.View.Index == held.Range.View.Index &&
                original.View.Offset == held.Range.View.Offset + 16,
                "typed pooled buffer counts preserve exact byte size, backing index and aligned suballocation offset");
            if (original.Buffer == null) return;
            allocator.Free(temporary);
            temporary = default;
            temporary = allocator.Allocate<uint>(16);
            check(ReferenceEquals(temporary.Range.Buffer, original.Buffer) && temporary.Range.Offset == original.Offset &&
                temporary.Range.SizeBytes == original.SizeBytes && temporary.Range.Cpu == original.Cpu &&
                temporary.Range.View.Index == original.View.Index && temporary.Range.View.Offset == original.View.Offset,
                "a freed suballocation and its shader view are reused while another allocation keeps the native heap live");
            if (temporary.Range.Buffer != null) allocator.Free(temporary);
            temporary = default;
            for (int iteration = 0; iteration < 128; ++iteration)
            {
                temporary = allocator.Allocate<uint>(16);
                if (temporary.Range.Buffer != null) allocator.Free(temporary);
                temporary = default;
            }
            HeapPoolStatistics before = allocator.GetStatistics();
            bool valid = true;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 2048; ++iteration)
            {
                temporary = allocator.Allocate<uint>(16);
                valid &= temporary.Range.Buffer != null && temporary.Range.SizeBytes == 16 * sizeof(uint) &&
                    temporary.Range.View.Index == original.View.Index && temporary.Range.View.Offset == original.View.Offset;
                if (temporary.Range.Buffer != null) allocator.Free(temporary);
                temporary = default;
            }
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            HeapPoolStatistics after = allocator.GetStatistics();
            check(valid && allocatedBytes == 0 && SameStatistics(before, after),
                $"warmed buffer allocation/free in a held heap performs no managed allocations ({allocatedBytes} bytes)");
            allocator.Trim();
            check(SameStatistics(after, allocator.GetStatistics()), "buffer Trim preserves occupied native heaps and allocations");
        }
        finally
        {
            if (temporary.Range.Buffer != null) allocator.Free(temporary);
            if (held.Range.Buffer != null) allocator.Free(held);
            allocator.Destroy();
        }
    }

    private static void RunGpuOnlyDedicated(GPUDevice device, Action<bool, string> check)
    {
        const ulong heapSize = 4096;
        const ulong dedicatedSize = heapSize * 2;
        HeapPoolDesc desc = new()
        {
            InitialHeapSize = heapSize,
            MaxHeapSize = heapSize,
            Budget = heapSize + dedicatedSize,
            InitialAllocationCapacity = 1,
        };
        GPUHeapPool allocator = new(device, desc, MemoryType.GpuOnly);
        HeapAllocation<uint> ordinary = default;
        HeapAllocation<byte> dedicated = default;
        GPUHeap upload = default;
        GPUHeap readback = default;
        GPUCommandPool? commandsPool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            ordinary = allocator.Allocate<uint>();
            dedicated = allocator.Allocate(dedicatedSize);
            check(ordinary.Range.Buffer != null && ordinary.Range.Cpu == null && ordinary.Range.CpuSpan.IsEmpty &&
                dedicated.Range.Buffer != null && dedicated.Range.Cpu == null && dedicated.Range.CpuSpan.IsEmpty &&
                dedicated.Range.SizeBytes == dedicatedSize, "native GPU-only pooled allocations expose buffer identities and ranges without CPU mappings");
            if (ordinary.Range.Buffer == null || dedicated.Range.Buffer == null) return;
            HeapPoolStatistics allocated = allocator.GetStatistics();
            check(allocated.ReservedBytes == desc.Budget && allocated.UsedBytes == dedicatedSize + 16 && allocated.HeapCount == 2 &&
                allocated.AllocationCount == 2, "a buffer request larger than MaxHeapSize receives exact dedicated backing within the budget");
            byte[] expected = new byte[256];
            for (int index = 0; index < expected.Length; ++index) expected[index] = unchecked((byte)(index * 7 + 19));
            upload = device.CreateGPUHeap((ulong)expected.Length);
            readback = device.CreateGPUHeap(512, MemoryType.Readback);
            expected.AsSpan().CopyTo(upload.Range.CpuSpan);
            commandsPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            GPUCommandBuffer commands = commandsPool.BeginCommands();
            commands.CopyMemory(upload.Range.GPURange.Slice(0, sizeof(uint)), ordinary.Range.GPURange);
            commands.CopyMemory(upload.Range.GPURange, dedicated.Range.GPURange);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            commands.CopyMemory(ordinary.Range.GPURange, readback.Range.GPURange);
            commands.CopyMemory(dedicated.Range.GPURange.Slice(0, (ulong)expected.Length),
                readback.Range.GPURange.Slice((ulong)(256), (ulong)expected.Length));
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            completion.Wait();
            check(readback.Range.CpuSpan[..sizeof(uint)].SequenceEqual(expected.AsSpan(0, sizeof(uint))) &&
                readback.Range.CpuSpan.Slice(256, expected.Length).SequenceEqual(expected),
                "ordinary and dedicated GPU-only allocations survive a completed upload/copy/readback round trip");
            commandsPool.Reset();
            allocator.Free(dedicated);
            dedicated = default;
            HeapPoolStatistics freed = allocator.GetStatistics();
            check(freed.ReservedBytes == heapSize && freed.UsedBytes == 16 && freed.HeapCount == 1 && freed.AllocationCount == 1 &&
                freed.PeakReservedBytes == allocated.PeakReservedBytes && freed.PeakUsedBytes == allocated.PeakUsedBytes,
                "freeing a dedicated buffer immediately destroys its backing without disturbing an ordinary allocation");
            allocator.Free(ordinary);
            ordinary = default;
            allocator.Trim();
            check(allocator.GetStatistics().ReservedBytes == 0, "GPU-only pool storage is fully reclaimable");
        }
        finally
        {
            device.WaitIdle();
            commandsPool?.Destroy();
            timeline?.Destroy();
            upload.Destroy();
            readback.Destroy();
            if (dedicated.Range.Buffer != null) allocator.Free(dedicated);
            if (ordinary.Range.Buffer != null) allocator.Free(ordinary);
            allocator.Destroy();
        }
    }

    private static void RunLargeBuffer(GPUDevice device, Action<bool, string> check)
    {
        const ulong allocationSize = 128UL * 1024 * 1024;
        HeapPoolDesc desc = new()
        {
            InitialHeapSize = 64UL * 1024 * 1024,
            MaxHeapSize = 256UL * 1024 * 1024,
            Budget = allocationSize,
            InitialAllocationCapacity = 1,
        };
        GPUHeapPool allocator = new(device, desc, MemoryType.GpuOnly);
        HeapAllocation<byte> allocation = default;
        GPUHeap upload = default;
        GPUHeap readback = default;
        GPUCommandPool? commandsPool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            allocation = allocator.Allocate(allocationSize);
            check(allocation.Range.Buffer != null && allocation.Range.Cpu == null && allocation.Range.SizeBytes == allocationSize,
                "a 128 MiB GPU-only buffer grows beyond the initial 64 MiB heap size");
            if (allocation.Range.Buffer == null || allocation.Range.SizeBytes != allocationSize) return;
            HeapPoolStatistics allocated = allocator.GetStatistics();
            check(allocated.ReservedBytes == allocationSize && allocated.UsedBytes == allocationSize && allocated.HeapCount == 1 &&
                allocated.AllocationCount == 1 && allocated.LargestFreeRange == 0,
                "one 128 MiB buffer exactly fills its 128 MiB backing budget without an unused initial heap");
            Span<byte> expected = stackalloc byte[32];
            for (int index = 0; index < expected.Length; ++index) expected[index] = unchecked((byte)(index * 19 + 7));
            upload = device.CreateGPUHeap((ulong)expected.Length);
            readback = device.CreateGPUHeap((ulong)expected.Length, MemoryType.Readback);
            expected.CopyTo(upload.Range.CpuSpan);
            commandsPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            GPURange first = allocation.Range.GPURange.Slice(0, 16);
            GPURange last = allocation.Range.GPURange.Slice(allocationSize - 16, 16);
            GPUCommandBuffer commands = commandsPool.BeginCommands();
            commands.CopyMemory(upload.Range.GPURange.Slice(0, 16), first);
            commands.CopyMemory(upload.Range.GPURange.Slice((ulong)(16), 16), last);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            commands.CopyMemory(first, readback.Range.GPURange.Slice(0, 16));
            commands.CopyMemory(last, readback.Range.GPURange.Slice((ulong)(16), 16));
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            completion.Wait();
            check(readback.Range.CpuSpan.SequenceEqual(expected),
                "completed GPU copies preserve distinct first/last 16-byte markers across the full 128 MiB buffer address range");
            commandsPool.Reset();
            allocator.Free(allocation);
            allocation = default;
            allocator.Trim();
            HeapPoolStatistics trimmed = allocator.GetStatistics();
            check(trimmed.ReservedBytes == 0 && trimmed.UsedBytes == 0 && trimmed.HeapCount == 0 && trimmed.AllocationCount == 0 &&
                trimmed.LargestFreeRange == 0 && trimmed.PeakReservedBytes == allocationSize && trimmed.PeakUsedBytes == allocationSize,
                "Free followed by Trim releases the 128 MiB heap and retains its exact peak statistics");
        }
        finally
        {
            device.WaitIdle();
            commandsPool?.Destroy();
            timeline?.Destroy();
            upload.Destroy();
            readback.Destroy();
            if (allocation.Range.Buffer != null) allocator.Free(allocation);
            allocator.Destroy();
        }
    }

    private static void RunTextures(GPUDevice device, Action<bool, string> check)
    {
        TextureDesc smallDesc = new()
        {
            Extent = new Extent3D(13, 7, 1),
            Format = Format.R8Unorm,
            Usage = TextureUsage.TransferSource | TextureUsage.TransferDestination,
        };
        TextureDesc colorDesc = new()
        {
            Extent = new Extent3D(257, 129, 1),
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
        };
        ulong heapAlignment = device.Caps.TextureHeapAlignment;
        SizeAlign smallRequirements = device.GetTextureSizeAlign(smallDesc);
        SizeAlign colorRequirements = device.GetTextureSizeAlign(colorDesc);
        while (colorRequirements.Size != 0 && colorRequirements.Size < heapAlignment)
        {
            colorDesc.Extent = new Extent3D(colorDesc.Extent.X * 2, colorDesc.Extent.Y * 2, 1);
            colorRequirements = device.GetTextureSizeAlign(colorDesc);
        }
        check(smallRequirements.Size != 0 && smallRequirements.Align != 0 && colorRequirements.Size != 0 && colorRequirements.Align != 0 &&
            smallRequirements.Size != colorRequirements.Size, "mixed texture fixtures expose distinct native sizes and per-resource placement alignments");
        if (smallRequirements.Size == 0 || smallRequirements.Align == 0 || colorRequirements.Size == 0 || colorRequirements.Align == 0) return;
        ulong colorOffset = (smallRequirements.Size + colorRequirements.Align - 1) & ~(colorRequirements.Align - 1);
        ulong heapSize = (colorOffset + colorRequirements.Size + heapAlignment - 1) & ~(heapAlignment - 1);
        HeapPoolDesc desc = new()
        {
            InitialHeapSize = heapSize,
            MaxHeapSize = heapSize,
            Budget = heapSize * 2,
            InitialAllocationCapacity = 1,
        };
        TextureHeapPool allocator = new(device, desc);
        PlacedTexture[] textures = new PlacedTexture[4];
        GPURenderView?[] views = new GPURenderView?[2];
        GPUHeap upload = default;
        GPUHeap readback = default;
        GPUCommandPool? commandsPool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            byte[] expected = new byte[smallDesc.Extent.X * smallDesc.Extent.Y];
            for (int index = 0; index < expected.Length; ++index) expected[index] = unchecked((byte)(index * 11 + 23));
            upload = device.CreateGPUHeap((ulong)expected.Length);
            readback = device.CreateGPUHeap(512, MemoryType.Readback);
            expected.AsSpan().CopyTo(upload.Range.CpuSpan);
            commandsPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            for (int pair = 0; pair < 2; ++pair)
            {
                textures[pair * 2] = allocator.Allocate(smallDesc);
                textures[pair * 2 + 1] = allocator.Allocate(colorDesc);
                GPUTexture? small = textures[pair * 2].Texture;
                GPUTexture? color = textures[pair * 2 + 1].Texture;
                check(small != null && color != null, $"mixed texture pair {pair} allocates from a pool with one initial metadata slot");
                if (small == null || color == null) return;
                if (pair == 0)
                {
                    HeapPoolStatistics first = allocator.GetStatistics();
                    check(first.HeapCount == 1 && first.ReservedBytes == heapSize && first.AllocationCount == 2 &&
                        first.UsedBytes == smallRequirements.Size + colorRequirements.Size,
                        "differently sized textures pack into one heap using their native placement requirements");
                }
                views[pair] = color.CreateRenderView(new RenderViewDesc());
                GPUCommandBuffer uploaded = commandsPool.BeginCommands();
                uploaded.InitializeTexture(small);
                uploaded.InitializeTexture(color);
                uploaded.CopyMemoryToTexture(upload.Range.GPURange, small);
                uploaded.BeginRenderPass(new GPURenderingDesc
                {
                    Colors = [new GPUColorAttachment
                    {
                        RenderView = views[pair],
                        Load = LoadOp.Clear,
                        Clear = pair == 0 ? new Vector4(1, 0, 1, 1) : new Vector4(0, 1, 0, 1),
                    }],
                });
                uploaded.EndRenderPass();
                uploaded.EndCommands();
                TimelinePoint completion = new() { Semaphore = timeline, Value = (ulong)pair + 1 };
                if (pair == 0)
                {
                    device.Submit(new SubmitDesc { Commands = [uploaded], Completion = completion });
                    completion.Wait();
                    commandsPool.Reset();
                    continue;
                }
                // Address-based texture uploads/readbacks must use separate command buffers on affected NVIDIA drivers.
                GPUCommandBuffer downloaded = commandsPool.BeginCommands();
                downloaded.Barrier(Stage.Transfer | Stage.ColorOutput, Access.TransferWrite | Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                for (int index = 0; index < 2; ++index)
                {
                    GPUTexture? retainedSmall = textures[index * 2].Texture;
                    GPUTexture? retainedColor = textures[index * 2 + 1].Texture;
                    if (retainedSmall == null || retainedColor == null) return;
                    downloaded.CopyTextureToMemory(retainedSmall,
                        readback.Range.GPURange.Slice((ulong)(index * 256), (ulong)expected.Length));
                    downloaded.CopyTextureToMemory(retainedColor,
                        readback.Range.GPURange.Slice((ulong)(index * 256 + 128), 4),
                        new TextureCopyDesc { Extent = new Extent3D(1, 1, 1) });
                    downloaded.CopyTextureToMemory(retainedColor,
                        readback.Range.GPURange.Slice((ulong)(index * 256 + 132), 4),
                        new TextureCopyDesc
                        {
                            Offset = new Extent3D(colorDesc.Extent.X - 1, colorDesc.Extent.Y - 1, 0),
                            Extent = new Extent3D(1, 1, 1),
                        });
                }
                downloaded.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                downloaded.EndCommands();
                device.Submit(new SubmitDesc { Commands = [uploaded, downloaded], Completion = completion });
                completion.Wait();
                commandsPool.Reset();
            }
            for (int pair = 0; pair < 2; ++pair)
            {
                ReadOnlySpan<byte> expectedColor = pair == 0 ? [255, 0, 255, 255] : [0, 255, 0, 255];
                check(readback.Range.CpuSpan.Slice(pair * 256, expected.Length).SequenceEqual(expected) &&
                    readback.Range.CpuSpan.Slice(pair * 256 + 128, 4).SequenceEqual(expectedColor) &&
                    readback.Range.CpuSpan.Slice(pair * 256 + 132, 4).SequenceEqual(expectedColor),
                    $"pooled texture pair {pair} preserves transfer data and attachment clears across heap growth without descriptors or shaders");
                views[pair]?.Destroy();
                views[pair] = null;
            }
            HeapPoolStatistics grown = allocator.GetStatistics();
            check(grown.ReservedBytes == desc.Budget && grown.UsedBytes == 2 * (smallRequirements.Size + colorRequirements.Size) &&
                grown.HeapCount == 2 && grown.AllocationCount == 4, "texture heap growth accounts for native sizes and requested backing capacity");
            TextureDesc oversized = colorDesc;
            oversized.Extent = new Extent3D(colorDesc.Extent.X * 2, colorDesc.Extent.Y * 2, 1);
            PlacedTexture rejected = allocator.Allocate(oversized);
            check(rejected.Texture == null && SameStatistics(grown, allocator.GetStatistics()),
                "a full texture backing budget rejects an additional large texture without changing any statistics");
            allocator.Free(ref rejected);
            allocator.Free(ref textures[2]);
            textures[2] = allocator.Allocate(smallDesc);
            HeapPoolStatistics reused = allocator.GetStatistics();
            check(textures[2].Texture != null && reused.ReservedBytes == grown.ReservedBytes && reused.UsedBytes == grown.UsedBytes &&
                reused.HeapCount == grown.HeapCount && reused.AllocationCount == grown.AllocationCount,
                "texture Free makes a native-sized placement reusable without increasing the full backing budget");
            allocator.Trim();
            check(SameStatistics(reused, allocator.GetStatistics()), "texture Trim does not reclaim heaps with live placements");
            for (int index = 0; index < textures.Length; ++index) allocator.Free(ref textures[index]);
            HeapPoolStatistics freed = allocator.GetStatistics();
            check(freed.UsedBytes == 0 && freed.AllocationCount == 0 && freed.HeapCount <= 1 &&
                freed.ReservedBytes == freed.HeapCount * heapSize && freed.LargestFreeRange == freed.ReservedBytes,
                "texture Free clears placement handles and retains at most one empty ordinary heap");
            bool cleared = true;
            for (int index = 0; index < textures.Length; ++index) cleared &= textures[index].Texture == null && textures[index].Token == 0;
            check(cleared, "freed placed textures are reset to default");
            allocator.Trim();
            HeapPoolStatistics trimmed = allocator.GetStatistics();
            check(trimmed.ReservedBytes == 0 && trimmed.HeapCount == 0 && trimmed.LargestFreeRange == 0 &&
                trimmed.PeakReservedBytes == grown.PeakReservedBytes && trimmed.PeakUsedBytes == grown.PeakUsedBytes,
                "texture Trim releases the final cached heap while preserving peak usage");
        }
        finally
        {
            device.WaitIdle();
            commandsPool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            upload.Destroy();
            for (int index = 0; index < views.Length; ++index) views[index]?.Destroy();
            for (int index = 0; index < textures.Length; ++index) allocator.Free(ref textures[index]);
            allocator.Destroy();
        }

        ulong dedicatedSize = (colorRequirements.Size + heapAlignment - 1) & ~(heapAlignment - 1);
        ulong ordinarySize = (smallRequirements.Size + heapAlignment - 1) & ~(heapAlignment - 1);
        HeapPoolDesc dedicatedDesc = new()
        {
            InitialHeapSize = smallRequirements.Size,
            MaxHeapSize = smallRequirements.Size,
            Budget = dedicatedSize + ordinarySize,
            InitialAllocationCapacity = 1,
        };
        TextureHeapPool dedicatedPool = new(device, dedicatedDesc);
        PlacedTexture dedicated = default;
        PlacedTexture retained = default;
        try
        {
            dedicated = dedicatedPool.Allocate(colorDesc);
            HeapPoolStatistics allocated = dedicatedPool.GetStatistics();
            check(dedicated.Texture != null && allocated.ReservedBytes == dedicatedSize && allocated.UsedBytes == dedicatedSize &&
                allocated.HeapCount == 1 && allocated.AllocationCount == 1,
                "a texture larger than MaxHeapSize reserves its entire aligned dedicated backing");
            retained = dedicatedPool.Allocate(smallDesc);
            HeapPoolStatistics held = dedicatedPool.GetStatistics();
            check(retained.Texture != null && held.ReservedBytes == dedicatedDesc.Budget && held.HeapCount == 2 && held.AllocationCount == 2 &&
                held.UsedBytes == dedicatedSize + smallRequirements.Size,
                "small textures receive separate ordinary backing rather than occupying a dedicated texture's alignment tail");
            dedicatedPool.Free(ref dedicated);
            HeapPoolStatistics freed = dedicatedPool.GetStatistics();
            check(dedicated.Texture == null && freed.ReservedBytes == ordinarySize && freed.UsedBytes == smallRequirements.Size &&
                freed.HeapCount == 1 && freed.AllocationCount == 1 && freed.PeakReservedBytes == held.PeakReservedBytes,
                "freeing a dedicated texture immediately releases its native heap while the small ordinary texture remains live");
            dedicatedPool.Free(ref retained);
            dedicatedPool.Trim();
            check(dedicatedPool.GetStatistics().ReservedBytes == 0, "dedicated and ordinary texture storage is fully reclaimable");
        }
        finally
        {
            dedicatedPool.Free(ref retained);
            dedicatedPool.Free(ref dedicated);
            dedicatedPool.Destroy();
        }
    }

    private static void RunCreationFailures(GPUDevice device, Action<bool, string> check)
    {
        device.WaitIdle();
        Vulkan.VkDeviceApi api = (Vulkan.VkDeviceApi)device.GetType().GetField("Api", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        FieldInfo allocate = typeof(Vulkan.VkDeviceApi).GetField(nameof(Vulkan.VkDeviceApi.vkAllocateMemory_ptr))!;
        FieldInfo bind = typeof(Vulkan.VkDeviceApi).GetField(nameof(Vulkan.VkDeviceApi.vkBindImageMemory_ptr))!;
        FieldInfo callback = typeof(Alco.Graphics.LowLevel.Gpu).GetField("ErrorCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
        ErrorCallback? previousCallback = (ErrorCallback?)callback.GetValue(null);
        Vulkan.PFN_vkVoidFunction originalAllocate = api.vkAllocateMemory_ptr;
        Vulkan.PFN_vkVoidFunction originalBind = api.vkBindImageMemory_ptr;
        HeapPoolDesc bufferDesc = new() { InitialHeapSize = 4096, MaxHeapSize = 4096, Budget = 8192, InitialAllocationCapacity = 1 };
        GPUHeapPool buffers = new(device, bufferDesc);
        HeapAllocation<byte> heldBuffer = default;
        HeapAllocation<byte> extraBuffer = default;
        TextureDesc textureDesc = new() { Extent = new Extent3D(257, 129, 1), Usage = TextureUsage.TransferSource | TextureUsage.TransferDestination };
        SizeAlign requirements = device.GetTextureSizeAlign(textureDesc);
        ulong heapAlignment = device.Caps.TextureHeapAlignment;
        while (requirements.Size < heapAlignment)
        {
            textureDesc.Extent = new Extent3D(textureDesc.Extent.X * 2, textureDesc.Extent.Y * 2, 1);
            requirements = device.GetTextureSizeAlign(textureDesc);
        }
        ulong secondOffset = (requirements.Size + requirements.Align - 1) & ~(requirements.Align - 1);
        ulong textureHeapSize = (secondOffset + requirements.Size + heapAlignment - 1) & ~(heapAlignment - 1);
        HeapPoolDesc poolDesc = new()
        {
            InitialHeapSize = textureHeapSize,
            MaxHeapSize = textureHeapSize,
            Budget = textureHeapSize * 2,
            InitialAllocationCapacity = 1,
        };
        TextureHeapPool textures = new(device, poolDesc);
        PlacedTexture first = default;
        PlacedTexture second = default;
        PlacedTexture extra = default;
        try
        {
            heldBuffer = buffers.Allocate(4096);
            first = textures.Allocate(textureDesc);
            check(heldBuffer.Range.Buffer != null && first.Texture != null, "creation rollback fixtures retain live buffer and texture allocations");
            if (heldBuffer.Range.Buffer == null || first.Texture == null) return;
            Gpu.SetErrorCallback(OnExpectedFailure);
            // This idle device is externally synchronized while its dispatch entries are replaced.
            for (int growth = 0; growth < 2; ++growth)
            {
                HeapPoolStatistics before = textures.GetStatistics();
                try
                {
                    bind.SetValue(api, new Vulkan.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice,
                        Vulkan.VkImage, Vulkan.VkDeviceMemory, ulong, Vulkan.VkResult>)&FailBindImageMemory));
                    try
                    {
                        extra = textures.Allocate(textureDesc);
                        check(false, "injected pooled texture binding failure throws GPUException");
                    }
                    catch (GPUException exception)
                    {
                        check(exception.Error == Error.DriverError && exception.Message.Contains("vkBindImageMemory", StringComparison.Ordinal),
                            $"pooled texture binding failure propagates the native GPUException (growth={growth})");
                    }
                }
                finally
                {
                    bind.SetValue(api, originalBind);
                }
                textures.Free(ref extra);
                check(SameLiveStatistics(before, textures.GetStatistics()),
                    growth == 0 ? "texture creation failure restores a reservation in an occupied heap" :
                    "texture creation failure removes newly created backing and restores the previous budget");
                if (growth == 0)
                {
                    second = textures.Allocate(textureDesc);
                    check(second.Texture != null, "texture allocation succeeds after an occupied-heap creation failure");
                }
            }
            HeapPoolStatistics bufferBefore = buffers.GetStatistics();
            HeapPoolStatistics textureBefore = textures.GetStatistics();
            try
            {
                allocate.SetValue(api, new Vulkan.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice,
                    Vulkan.VkMemoryAllocateInfo*, Vulkan.VkAllocationCallbacks*, Vulkan.VkDeviceMemory*, Vulkan.VkResult>)&FailAllocateMemory));
                try
                {
                    extraBuffer = buffers.Allocate(4096);
                    check(false, "injected buffer-pool backing failure throws GPUException");
                }
                catch (GPUException exception)
                {
                    check(exception.Error == Error.DriverError && exception.Message.Contains("vkAllocateMemory", StringComparison.Ordinal),
                        "buffer pool propagates native backing creation failure instead of reporting budget exhaustion");
                }
                try
                {
                    extra = textures.Allocate(textureDesc);
                    check(false, "injected texture-pool backing failure throws GPUException");
                }
                catch (GPUException exception)
                {
                    check(exception.Error == Error.DriverError && exception.Message.Contains("vkAllocateMemory", StringComparison.Ordinal),
                        "texture pool propagates native backing creation failure instead of reporting budget exhaustion");
                }
            }
            finally
            {
                allocate.SetValue(api, originalAllocate);
            }
            if (extraBuffer.Range.Buffer != null) buffers.Free(extraBuffer);
            extraBuffer = default;
            textures.Free(ref extra);
            check(SameLiveStatistics(bufferBefore, buffers.GetStatistics()) && SameLiveStatistics(textureBefore, textures.GetStatistics()),
                "failed native heap creation leaves existing allocations, backing budgets and free ranges unchanged");
            extraBuffer = buffers.Allocate(4096);
            extra = textures.Allocate(textureDesc);
            check(extraBuffer.Range.Buffer != null && extra.Texture != null && buffers.GetStatistics().ReservedBytes == bufferDesc.Budget &&
                textures.GetStatistics().ReservedBytes == poolDesc.Budget,
                "both pools recover failed growth reservations and can subsequently fill their exact budgets");
        }
        finally
        {
            bind.SetValue(api, originalBind);
            allocate.SetValue(api, originalAllocate);
            Gpu.SetErrorCallback(previousCallback);
            device.WaitIdle();
            textures.Free(ref extra);
            textures.Free(ref second);
            textures.Free(ref first);
            textures.Destroy();
            if (extraBuffer.Range.Buffer != null) buffers.Free(extraBuffer);
            if (heldBuffer.Range.Buffer != null) buffers.Free(heldBuffer);
            buffers.Destroy();
        }
    }

    private static bool SameLiveStatistics(HeapPoolStatistics left, HeapPoolStatistics right)
    {
        return left.ReservedBytes == right.ReservedBytes && left.UsedBytes == right.UsedBytes && left.AllocationCount == right.AllocationCount &&
            left.HeapCount == right.HeapCount && left.LargestFreeRange == right.LargestFreeRange;
    }

    private static bool SameStatistics(HeapPoolStatistics left, HeapPoolStatistics right)
    {
        return SameLiveStatistics(left, right) && left.PeakReservedBytes == right.PeakReservedBytes && left.PeakUsedBytes == right.PeakUsedBytes;
    }

    private static void OnExpectedFailure(string message, bool fatal)
    {
    }

    [UnmanagedCallersOnly]
    private static Vulkan.VkResult FailBindImageMemory(Vulkan.VkDevice device, Vulkan.VkImage image, Vulkan.VkDeviceMemory memory, ulong offset)
    {
        return Vulkan.VkResult.ErrorUnknown;
    }

    [UnmanagedCallersOnly]
    private static Vulkan.VkResult FailAllocateMemory(Vulkan.VkDevice device, Vulkan.VkMemoryAllocateInfo* info,
        Vulkan.VkAllocationCallbacks* allocator, Vulkan.VkDeviceMemory* memory)
    {
        *memory = default;
        return Vulkan.VkResult.ErrorUnknown;
    }
}
