// Example-level rendering regressions: storage growth, texture upload ordering and lifetime, budgets, and concurrent resource reclamation.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Buffer = Alco.Graphics.Buffer;

internal static class RenderingSystemTests
{
    internal static void Run(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        Texture? sampled = null;
        Texture? storage = null;
        Texture? volume = null;
        Texture? recycledVolume = null;
        Texture? target = null;
        Texture? replacement = null;
        Sampler? linearRepeat = null;
        Sampler? nearestClamp = null;
        Sampler? recycledSampler = null;
        GPUTimelineSemaphore? timeline = null;
        RenderContext? context = null;
        try
        {
            system = new RenderingSystem(device, 32 * 1024 * 1024);
            sampled = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(64, 64, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });
            storage = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            });
            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment,
            });
            check(sampled.SampledIndex != Texture.NoIndex && sampled.StorageIndex == Texture.NoIndex,
                "a sampled texture owns one sampled descriptor slot");
            check(storage.SampledIndex != sampled.SampledIndex && storage.StorageIndex != Texture.NoIndex &&
                storage.StorageIndex != storage.SampledIndex,
                "a sampled storage texture owns distinct sampled and storage descriptor slots");
            check(target.SampledIndex == Texture.NoIndex && target.StorageIndex == Texture.NoIndex,
                "an attachment-only texture owns no bindless descriptor slots");

            TextureDesc volumeDesc = new()
            {
                Type = TextureType.ThreeD,
                Extent = new Extent3D(8, 8, 8),
                MipLevels = 4,
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            };
            volume = system.CreateTexture(volumeDesc);
            Span<uint> volumeIndices = stackalloc uint[(int)volume.MipLevels + 1];
            volumeIndices[0] = volume.SampledIndex;
            for (uint mipLevel = 0; mipLevel < volume.MipLevels; ++mipLevel)
                volumeIndices[(int)mipLevel + 1] = volume.GetStorageIndex(mipLevel);
            bool uniqueVolumeIndices = true;
            for (int index = 0; index < volumeIndices.Length; ++index)
            {
                uniqueVolumeIndices &= volumeIndices[index] != Texture.NoIndex;
                for (int previous = 0; previous < index; ++previous)
                    uniqueVolumeIndices &= volumeIndices[index] != volumeIndices[previous];
            }
            check(uniqueVolumeIndices, "a multi-mip 3D texture owns distinct sampled and per-mip storage descriptor slots");
            check(volume.GetStorageIndex(0) == volume.StorageIndex && storage.GetStorageIndex(0) == storage.StorageIndex &&
                sampled.GetStorageIndex(0) == Texture.NoIndex,
                "storage mip zero preserves StorageIndex for multi-mip, single-mip, and sampled-only textures");

            timeline = device.CreateTimelineSemaphore();
            context = new RenderContext(system, 256);
            context.BeginFrame();
            GPUCommandBuffer commands = context.EndFrame();
            TimelinePoint initialization = system.FlushInitializations();
            check(initialization.Semaphore != null && initialization.Value != 0,
                "EndFrame flushes new texture initializations on queue zero before the frame submission");
            ulong firstInitializationValue = initialization.Value;
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            completion.Wait();
            check(target.RenderView != null, "a render-target texture creates its owned attachment view lazily");

            linearRepeat = system.CreateSampler(new SamplerDesc());
            nearestClamp = system.CreateSampler(new SamplerDesc
            {
                MinFilter = Filter.Nearest,
                MagFilter = Filter.Nearest,
                MipFilter = Filter.Nearest,
                AddressU = AddressMode.ClampToEdge,
                AddressV = AddressMode.ClampToEdge,
                AddressW = AddressMode.ClampToEdge,
            });
            check(linearRepeat.Index != nearestClamp.Index, "samplers receive distinct descriptor slots");
            check((uint)sampled == sampled.SampledIndex && (uint)linearRepeat == linearRepeat.Index,
                "texture and sampler wrappers convert to their descriptor heap indices");

            uint releasedTextureIndex = sampled.SampledIndex;
            sampled.Destroy();
            sampled = null;
            replacement = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(16, 16, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });
            check(replacement.SampledIndex == releasedTextureIndex, "a destroyed texture returns its sampled descriptor slot for reuse");

            uint releasedSamplerIndex = linearRepeat.Index;
            linearRepeat.Destroy();
            linearRepeat = null;
            recycledSampler = system.CreateSampler(new SamplerDesc());
            check(recycledSampler.Index == releasedSamplerIndex, "a destroyed sampler returns its descriptor slot for reuse");

            volume.Destroy();
            volume = null;
            recycledVolume = system.CreateTexture(volumeDesc);
            Span<uint> recycledVolumeIndices = stackalloc uint[(int)recycledVolume.MipLevels + 1];
            recycledVolumeIndices[0] = recycledVolume.SampledIndex;
            for (uint mipLevel = 0; mipLevel < recycledVolume.MipLevels; ++mipLevel)
                recycledVolumeIndices[(int)mipLevel + 1] = recycledVolume.GetStorageIndex(mipLevel);
            volumeIndices.Sort();
            recycledVolumeIndices.Sort();
            check(recycledVolumeIndices.SequenceEqual(volumeIndices),
                "destroying a multi-mip 3D texture recycles every descriptor slot");
            context.BeginFrame();
            commands = context.EndFrame();
            TimelinePoint recycledInitialization = system.FlushInitializations();
            check(ReferenceEquals(recycledInitialization.Semaphore, initialization.Semaphore) &&
                recycledInitialization.Value > firstInitializationValue,
                "a recycled multi-mip 3D texture advances the system-owned initialization timeline");
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            completion.Wait();

            CheckBuffers(device, check);
            CheckBufferBudget(device, check);
            CheckTextureGrowth(device, check);
            CheckTextureBudget(device, check);
            CheckConcurrentResources(device, check);
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            recycledSampler?.Destroy();
            nearestClamp?.Destroy();
            linearRepeat?.Destroy();
            replacement?.Destroy();
            target?.Destroy();
            recycledVolume?.Destroy();
            volume?.Destroy();
            storage?.Destroy();
            sampled?.Destroy();
            timeline?.Destroy();
            system?.Destroy();
        }

        CheckUnflushedTextureLifetime(device, check);
        CheckTextureInitializationShutdown(device, check);
        CheckRepeatedTextureUploads(device, check);
        CheckDiscardedTextureUploads(device, check);
        CheckTextureUploadCapacity(device, check);
        CheckLargeTextureUpload(device, check);
        CheckDedicatedTextureUploads(device.ActiveBackend, check);
    }

    private static unsafe void CheckBuffers(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        Buffer? uploaded = null;
        Buffer? updated = null;
        Buffer? metadata = null;
        Buffer? grown = null;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 128, bufferCapacity: 2);
            ReadOnlySpan<uint> original = [11, 22, 33, 44, 55, 66, 77];
            Span<uint> source = stackalloc uint[7];
            original.CopyTo(source);
            uploaded = system.CreateBuffer<uint>(source);
            updated = system.CreateBuffer(28);
            updated.SetData<uint>(source);
            source.Fill(uint.MaxValue);
            GPURange uploadedRange = uploaded.Range;
            GPURange updatedRange = updated.Range;
            check(uploaded.SizeBytes == 28 && uploadedRange.Size == 28 && updated.SizeBytes == 28 && updatedRange.Size == 28 &&
                uploadedRange.Buffer != null && updatedRange.Buffer != null &&
                (!ReferenceEquals(uploadedRange.Buffer, updatedRange.Buffer) || uploadedRange.Offset != updatedRange.Offset),
                "typed and byte-sized buffers expose distinct, exact-sized GPU ranges");

            ReadOnlySpan<uint> metadataContents = [101, 202, 303, 404];
            metadata = system.CreateBuffer(metadataContents);
            GPURange metadataRange = metadata.Range;
            check(ReferenceEquals(metadataRange.Buffer, uploadedRange.Buffer) && metadataRange.Offset == uploadedRange.Offset + 64 && metadataRange.Size == 16,
                "allocation metadata grows beyond bufferCapacity while reusing free bytes in the original heap");
            metadata.Destroy();
            metadata = system.CreateBuffer(metadataContents);
            check(metadata.Range.Equals(metadataRange),
                "allocations from grown metadata are returned and reused independently of backing heap growth");

            Span<uint> grownContents = stackalloc uint[32];
            for (int index = 0; index < grownContents.Length; ++index)
                grownContents[index] = (uint)(1000 + index);
            grown = system.CreateBuffer<uint>(grownContents);
            check(grown.SizeBytes == 128 && grown.Range.Buffer != null &&
                uploaded.Range.Equals(uploadedRange) && updated.Range.Equals(updatedRange) && metadata.Range.Equals(metadataRange),
                "exhausting the initial buffer heap adds backing storage without moving any live allocation");

            readback = device.CreateGPUHeap(224, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            GPUAllocation<uint> uploadedResult = results.Allocate<uint>(7);
            GPUAllocation<uint> updatedResult = results.Allocate<uint>(7);
            GPUAllocation<uint> metadataResult = results.Allocate<uint>(4);
            GPUAllocation<uint> grownResult = results.Allocate<uint>(32);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            GPUCommandBuffer commands = pool.BeginCommands();
            commands.CopyMemory(uploadedRange, uploadedResult.GPURange);
            commands.CopyMemory(updatedRange, updatedResult.GPURange);
            commands.CopyMemory(metadataRange, metadataResult.GPURange);
            commands.CopyMemory(grown.Range, grownResult.GPURange);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            completion.Wait();
            check(uploadedResult.CpuSpan.SequenceEqual(original) && updatedResult.CpuSpan.SequenceEqual(original) &&
                metadataResult.CpuSpan.SequenceEqual(metadataContents) && grownResult.CpuSpan.SequenceEqual(grownContents),
                "completed GPU copies through pre-growth ranges preserve old uploads and independent data in the new heap");

            ReadOnlySpan<uint> patch = [333, 444];
            updated.SetData(patch, byteOffset: 2 * sizeof(uint));
            check(uploaded.SizeBytes == 28 && uploaded.Range.Equals(uploadedRange) && uploaded.Range.Size == uploadedRange.Size &&
                updated.SizeBytes == 28 && updated.Range.Equals(updatedRange) && updated.Range.Size == updatedRange.Size,
                "partial buffer uploads preserve sizes and GPU buffer identities and offsets after pool growth");
            pool.Reset();
            commands = pool.BeginCommands();
            commands.CopyMemory(updatedRange, updatedResult.GPURange);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            completion.Wait();
            ReadOnlySpan<uint> expected = [11, 22, 333, 444, 55, 66, 77];
            check(updatedResult.CpuSpan.SequenceEqual(expected) && uploadedResult.CpuSpan.SequenceEqual(original),
                "a byte-offset SetData patch changes only its requested elements after prior GPU use completes");
        }
        finally
        {
            device.WaitIdle();
            grown?.Destroy();
            metadata?.Destroy();
            updated?.Destroy();
            uploaded?.Destroy();
            timeline?.Destroy();
            pool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static unsafe void CheckBufferBudget(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        Buffer? first = null;
        Buffer? second = null;
        Buffer? recycled = null;
        Buffer? unexpected = null;
        try
        {
            HeapPoolDesc bufferPool = new()
            {
                InitialHeapSize = 128,
                MaxHeapSize = 128,
                Budget = 128,
                InitialAllocationCapacity = 1,
            };
            HeapPoolDesc texturePool = new() { InitialHeapSize = 4 * 1024 * 1024 };
            system = new RenderingSystem(device, bufferPool, texturePool);
            first = system.CreateBuffer(64);
            second = system.CreateBuffer(64);
            GPURange firstRange = first.Range;
            GPURange secondRange = second.Range;
            check(ReferenceEquals(secondRange.Buffer, firstRange.Buffer) && secondRange.Offset == firstRange.Offset + 64 && secondRange.Size == 64,
                "metadata grows even when an explicit byte budget forbids adding another backing heap");
            bool exhausted = false;
            try
            {
                unexpected = system.CreateBuffer(16);
            }
            catch (InvalidOperationException exception)
            {
                exhausted = exception.Message.Contains("buffer pool budget is exhausted", StringComparison.Ordinal);
            }
            check(exhausted, "an explicit buffer pool byte budget reports exhaustion instead of limiting allocation count");
            unexpected?.Destroy();
            unexpected = null;

            first.Destroy();
            first = null;
            recycled = system.CreateBuffer(64);
            check(ReferenceEquals(recycled.Range.Buffer, firstRange.Buffer) && recycled.Range.Offset == firstRange.Offset && second.Range.Equals(secondRange),
                "budget exhaustion leaves freed storage reusable without moving a surviving buffer");
            recycled.Destroy();
            recycled = null;
            second.Destroy();
            second = null;
            recycled = system.CreateBuffer(128);
            check(ReferenceEquals(recycled.Range.Buffer, firstRange.Buffer) && recycled.Range.Offset == firstRange.Offset && recycled.SizeBytes == 128,
                "freed neighboring allocations coalesce into the complete budgeted buffer heap");
        }
        finally
        {
            device.WaitIdle();
            unexpected?.Destroy();
            recycled?.Destroy();
            second?.Destroy();
            first?.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckTextureGrowth(GPUDevice device, Action<bool, string> check)
    {
        const int pixelBytes = 64 * 64 * 4;
        RenderingSystem? system = null;
        Texture? first = null;
        Texture? second = null;
        Texture? third = null;
        Texture?[] attachments = new Texture?[6];
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        RenderContext? context = null;
        try
        {
            TextureDesc whiteDesc = new()
            {
                Extent = new Extent3D(1, 1, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            ulong initialSize = device.GetTextureSizeAlign(whiteDesc).Size;
            initialSize = (initialSize + device.Caps.TextureHeapAlignment - 1) & ~(device.Caps.TextureHeapAlignment - 1);
            system = new RenderingSystem(device, initialSize, bufferHeapByteCount: 128, bufferCapacity: 1);
            TextureDesc desc = new()
            {
                Extent = new Extent3D(64, 64, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            };
            first = system.CreateTexture(desc);
            GPUTexture firstNative = first.NativeTexture;
            uint firstIndex = first.SampledIndex;
            byte[] firstContents = new byte[pixelBytes];
            byte[] lastContents = new byte[pixelBytes];
            for (int index = 0; index < pixelBytes; ++index)
            {
                firstContents[index] = (byte)(index * 13 + 7);
                lastContents[index] = (byte)(index * 5 + 91);
            }
            readback = device.CreateGPUHeap(2 * pixelBytes, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            GPUAllocation<byte> firstResult = results.Allocate<byte>(pixelBytes);
            GPUAllocation<byte> lastResult = results.Allocate<byte>(pixelBytes);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            context = new RenderContext(system, 256);
            context.BeginFrame();
            context.SetTextureData(first, firstContents);
            GPUCommandBuffer commands = context.EndFrame();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            completion.Wait();

            second = system.CreateTexture(desc);
            third = system.CreateTexture(desc);
            check(third.SampledIndex == 3, "textures beyond the initial heap receive sequential descriptor slots from grown storage");

            TextureDesc attachmentDesc = desc;
            attachmentDesc.Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource | TextureUsage.TransferDestination;
            bool noDescriptors = true;
            for (int index = 0; index < attachments.Length; ++index)
            {
                Texture attachment = system.CreateTexture(attachmentDesc);
                attachments[index] = attachment;
                noDescriptors &= attachment.SampledIndex == Texture.NoIndex && attachment.StorageIndex == Texture.NoIndex;
            }
            check(noDescriptors && first.NativeTexture == firstNative && first.SampledIndex == firstIndex,
                "attachment-only textures claim no bindless descriptor slots while pool growth preserves the first native texture and descriptor");

            Texture last = attachments[^1]!;
            context.BeginFrame();
            context.SetTextureData(last, lastContents);
            commands = context.EndFrame();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            completion.Wait();

            pool.Reset();
            commands = pool.BeginCommands();
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            commands.CopyTextureToMemory(firstNative, firstResult.GPURange);
            commands.CopyTextureToMemory(last.NativeTexture, lastResult.GPURange);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            completion.Wait();
            check(firstResult.CpuSpan.SequenceEqual(firstContents) && lastResult.CpuSpan.SequenceEqual(lastContents),
                "completed GPU readback preserves pre-growth texture contents and independent data in newly added texture storage");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            foreach (Texture? attachment in attachments)
                attachment?.Destroy();
            third?.Destroy();
            second?.Destroy();
            first?.Destroy();
            timeline?.Destroy();
            pool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckUnflushedTextureLifetime(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        Texture? texture = null;
        Texture? finalized = null;
        RenderContext? context = null;
        GPUCommandPool? readbackPool = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap readback = default;
        try
        {
            TextureDesc whiteDesc = new()
            {
                Extent = new Extent3D(1, 1, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            TextureDesc desc = new()
            {
                Extent = new Extent3D(1024, 1024, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            };
            SizeAlign requirements = device.GetTextureSizeAlign(desc);
            ulong heapSize = device.GetTextureSizeAlign(whiteDesc).Size;
            heapSize = ((heapSize + requirements.Align - 1) & ~(requirements.Align - 1)) + requirements.Size;
            heapSize = (heapSize + device.Caps.TextureHeapAlignment - 1) & ~(device.Caps.TextureHeapAlignment - 1);
            HeapPoolDesc bufferPool = new() { InitialHeapSize = 128, MaxHeapSize = 128, Budget = 128 };
            HeapPoolDesc texturePool = new() { InitialHeapSize = heapSize, MaxHeapSize = heapSize, Budget = heapSize };
            system = new RenderingSystem(device, bufferPool, texturePool);
            uint sampledIndex = Texture.NoIndex;
            uint storageIndex = Texture.NoIndex;
            bool reusable = true;
            for (int iteration = 0; iteration < 16; ++iteration)
            {
                texture = system.CreateTexture(desc);
                if (iteration == 0)
                {
                    sampledIndex = texture.SampledIndex;
                    storageIndex = texture.StorageIndex;
                }
                reusable &= (texture.SampledIndex == sampledIndex && texture.StorageIndex == storageIndex) ||
                    (texture.SampledIndex == storageIndex && texture.StorageIndex == sampledIndex);
                texture.Destroy();
                reusable &= texture.IsDestroyed;
                texture = null;
            }
            check(reusable, "destroying never-flushed textures immediately recycles both descriptor roles and the single-texture pool budget");

            WeakReference reference = LeakTexture(system, desc);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            finalized = reference.Target as Texture;
            check(finalized is { IsDestroyed: false }, "pending initialization metadata does not keep an unused texture wrapper alive past finalization");
            system.Tick();
            check(finalized is { IsDestroyed: true }, "Tick cancels finalized texture initialization before the first upload flush");
            texture = system.CreateTexture(desc);
            check((texture.SampledIndex == sampledIndex && texture.StorageIndex == storageIndex) ||
                (texture.SampledIndex == storageIndex && texture.StorageIndex == sampledIndex),
                "a finalized never-flushed texture immediately returns its budget and descriptor slots on Tick");

            ReadOnlySpan<byte> contents = [0x17, 0x39, 0x5b, 0x7d];
            TextureCopyDesc pixel = new() { Extent = new Extent3D(1, 1, 1) };
            readback = device.CreateGPUHeap(32, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            GPUAllocation<byte> result = results.Allocate<byte>(4);
            readbackPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            context = new RenderContext(system, 256);
            context.BeginFrame();
            context.SetTextureData(texture, contents, pixel);
            GPUCommandBuffer commands = context.EndFrame();
            // Address-based upload/readback uses separate command buffers on affected NVIDIA drivers.
            GPUCommandBuffer readbackCommands = readbackPool.BeginCommands();
            readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            readbackCommands.CopyTextureToMemory(texture.NativeTexture, result.GPURange, pixel);
            readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            readbackCommands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands, readbackCommands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            check(result.CpuSpan[..contents.Length].SequenceEqual(contents),
                "the first flush records only surviving texture initialization and preserves its upload");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            if (texture is { IsDestroyed: false }) texture.Destroy();
            if (finalized is { IsDestroyed: false }) finalized.Destroy();
            timeline?.Destroy();
            readbackPool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckTextureInitializationShutdown(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        Texture? destroyed = null;
        Texture? finalized = null;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 128);
            TextureDesc desc = new()
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            destroyed = system.CreateTexture(desc);
            destroyed.Destroy();
            check(destroyed.IsDestroyed, "Destroy cancels a texture's pending initialization without recording a frame");
            WeakReference reference = LeakTexture(system, desc);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            finalized = reference.Target as Texture;
            check(finalized is { IsDestroyed: false }, "a finalized uninitialized texture awaits reclamation without EndFrame or Tick");
            device.WaitIdle();
            system.Destroy();
            system = null;
            check(destroyed.IsDestroyed && finalized is { IsDestroyed: true },
                "shutdown cancels pending initialization and reclaims explicitly destroyed and finalized texture native state");
        }
        finally
        {
            device.WaitIdle();
            if (system != null)
            {
                if (destroyed is { IsDestroyed: false }) destroyed.Destroy();
                if (finalized is { IsDestroyed: false }) finalized.Destroy();
                system.Destroy();
            }
        }
    }

    // Pending metadata must not root the wrapper, and collection must happen after the helper's locals leave the stack.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LeakTexture(RenderingSystem system, TextureDesc desc)
    {
        Texture texture = system.CreateTexture(desc);
        return new WeakReference(texture, trackResurrection: true);
    }

    private static void CheckRepeatedTextureUploads(GPUDevice device, Action<bool, string> check)
    {
        const int pixelBytes = 64 * 64 * 4;
        const int patchRowBytes = 8 * 4;
        const int patchRowPitch = patchRowBytes + 16;
        RenderingSystem? system = null;
        Texture? first = null;
        Texture? second = null;
        RenderContext? context = null;
        GPUCommandPool? readbackPool = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap readback = default;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 128);
            TextureDesc desc = new()
            {
                Extent = new Extent3D(64, 64, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            };
            first = system.CreateTexture(desc);
            second = system.CreateTexture(desc);
            byte[] original = new byte[pixelBytes];
            byte[] source = new byte[pixelBytes];
            byte[] updated = new byte[pixelBytes];
            byte[] other = new byte[pixelBytes];
            byte[] patched = new byte[pixelBytes];
            byte[] patch = new byte[patchRowPitch * 4];
            for (int index = 0; index < pixelBytes; ++index)
            {
                original[index] = (byte)(index * 3 + 11);
                updated[index] = (byte)(index * 7 + 29);
                other[index] = (byte)(index * 13 + 47);
            }
            updated.CopyTo(patched, 0);
            patch.AsSpan().Fill(0xe5);
            for (int row = 0; row < 4; ++row)
            {
                for (int index = 0; index < patchRowBytes; ++index)
                    patch[row * patchRowPitch + index] = (byte)(row * 31 + index * 17 + 53);
                patch.AsSpan(row * patchRowPitch, patchRowBytes).CopyTo(patched.AsSpan(((row + 8) * 64 + 16) * 4, patchRowBytes));
            }
            TextureCopyDesc region = new()
            {
                Offset = new Extent3D(16, 8, 0),
                Extent = new Extent3D(8, 4, 1),
                RowPitchBytes = patchRowPitch,
            };
            readback = device.CreateGPUHeap(3 * pixelBytes, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            GPUAllocation<byte> originalResult = results.Allocate<byte>(pixelBytes);
            GPUAllocation<byte> firstResult = results.Allocate<byte>(pixelBytes);
            GPUAllocation<byte> secondResult = results.Allocate<byte>(pixelBytes);
            readbackPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            context = new RenderContext(system, 256);
            original.CopyTo(source, 0);
            context.BeginFrame();
            context.SetTextureData(first, source);
            source.AsSpan().Fill(0xff);
            GPUCommandBuffer firstSlotCommands = context.EndFrame();
            GPUCommandBuffer readbackCommands = readbackPool.BeginCommands();
            readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            readbackCommands.CopyTextureToMemory(first.NativeTexture, originalResult.GPURange);
            readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            readbackCommands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [firstSlotCommands, readbackCommands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            check(originalResult.CpuSpan.SequenceEqual(original),
                "SetTextureData copies the source before returning, so mutation before frame submission does not change the uploaded texels");

            for (int phase = 0; phase < 2; ++phase)
            {
                readbackPool.Reset();
                context.BeginFrame();
                context.SetTextureData(first, original);
                if (phase != 0)
                    context.SetTextureData(second, other);
                context.SetTextureData(first, updated);
                if (phase != 0)
                    context.SetTextureData(first, patch, region);
                GPUCommandBuffer commands = context.EndFrame();
                check(ReferenceEquals(commands, firstSlotCommands) == (phase != 0),
                    "repeated texture updates use the other slot's command buffer, then reuse the first slot's buffer");
                readbackCommands = readbackPool.BeginCommands();
                readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                readbackCommands.CopyTextureToMemory(first.NativeTexture, firstResult.GPURange);
                if (phase != 0)
                    readbackCommands.CopyTextureToMemory(second.NativeTexture, secondResult.GPURange);
                readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                readbackCommands.EndCommands();
                completion.Value++;
                device.Submit(new SubmitDesc { Commands = [commands, readbackCommands], Completion = completion });
                context.MarkSubmitted(completion);
                context.Wait();
                check(firstResult.CpuSpan.SequenceEqual(phase == 0 ? updated : patched) &&
                    (phase == 0 || secondResult.CpuSpan.SequenceEqual(other)), phase == 0
                    ? "overlapping frame-local SetTextureData writes execute in command order without separate upload submissions"
                    : "A/B/A updates and a padded regional patch preserve B and all texels outside the selected region");
            }
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            second?.Destroy();
            first?.Destroy();
            timeline?.Destroy();
            readbackPool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckDiscardedTextureUploads(GPUDevice device, Action<bool, string> check)
    {
        const int pixelBytes = 32 * 32 * 4;
        RenderingSystem? system = null;
        Texture? texture = null;
        Texture? fresh = null;
        RenderContext? context = null;
        GPUCommandPool? readbackPool = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap readback = default;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 128);
            TextureDesc desc = new()
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            };
            texture = system.CreateTexture(desc);
            byte[] original = new byte[pixelBytes];
            byte[] discarded = new byte[pixelBytes];
            original.AsSpan().Fill(0x37);
            discarded.AsSpan().Fill(0xc9);
            readback = device.CreateGPUHeap(pixelBytes, MemoryType.Readback);
            readbackPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            context = new RenderContext(system, 256, pixelBytes);
            context.BeginFrame();
            context.SetTextureData(texture, original);
            GPUCommandBuffer commands = context.EndFrame();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();

            for (int ended = 0; ended < 2; ++ended)
            {
                context.BeginFrame();
                context.SetTextureData(texture, discarded);
                if (ended != 0)
                    context.EndFrame();
                context.DiscardFrame();
                context.BeginFrame();
                commands = context.EndFrame();
                readbackPool.Reset();
                GPUCommandBuffer readbackCommands = readbackPool.BeginCommands();
                readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                readbackCommands.CopyTextureToMemory(texture.NativeTexture, readback.Range.GPURange);
                readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                readbackCommands.EndCommands();
                completion.Value++;
                device.Submit(new SubmitDesc { Commands = [commands, readbackCommands], Completion = completion });
                context.MarkSubmitted(completion);
                context.Wait();
                check(readback.Range.CpuSpan.SequenceEqual(original), ended == 0
                    ? "discarding a recording with a texture upload leaves existing GPU texels unchanged"
                    : "EndFrame does not submit texture data, so discarding an ended frame also preserves existing texels");
            }

            fresh = system.CreateTexture(desc);
            context.BeginFrame();
            context.SetTextureData(fresh, discarded);
            context.EndFrame();
            context.DiscardFrame();
            context.BeginFrame();
            context.SetTextureData(fresh, original);
            commands = context.EndFrame();
            readbackPool.Reset();
            GPUCommandBuffer freshReadbackCommands = readbackPool.BeginCommands();
            freshReadbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            freshReadbackCommands.CopyTextureToMemory(fresh.NativeTexture, readback.Range.GPURange);
            freshReadbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            freshReadbackCommands.EndCommands();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands, freshReadbackCommands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            check(readback.Range.CpuSpan.SequenceEqual(original),
                "a fresh texture remains initialized after a discarded EndFrame, and the slot's full staging budget is reusable");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            fresh?.Destroy();
            texture?.Destroy();
            timeline?.Destroy();
            readbackPool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckTextureUploadCapacity(GPUDevice device, Action<bool, string> check)
    {
        const int width = 1024;
        const int height = 1025;
        const int rowBytes = width * 4;
        const int pixelBytes = rowBytes * height;
        RenderingSystem? system = null;
        Texture? texture = null;
        RenderContext? defaultContext = null;
        RenderContext? context = null;
        GPUCommandPool? readbackPool = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap readback = default;
        try
        {
            system = new RenderingSystem(device, 16 * 1024 * 1024, bufferHeapByteCount: 128);
            texture = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(width, height, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            });
            byte[] source = new byte[pixelBytes];
            for (int index = 0; index < source.Length; ++index)
                source[index] = (byte)(index * 19 + (index / rowBytes) * 7 + 31);
            defaultContext = new RenderContext(system, 256);
            defaultContext.BeginFrame();
            GPURange firstRoot = defaultContext.FrameData.Allocate(16).GPURange;
            bool rejected = false;
            try
            {
                defaultContext.SetTextureData(texture, source);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            GPURange nextRoot = defaultContext.FrameData.Allocate(16).GPURange;
            check(rejected && ReferenceEquals(nextRoot.Buffer, firstRoot.Buffer) && nextRoot.Offset == firstRoot.Offset + 16 && nextRoot.Size == 16,
                "the default 4 MiB frame upload budget rejects an oversized upload without disturbing the active frame's root storage");
            defaultContext.DiscardFrame();

            context = new RenderContext(system, 256, pixelBytes + 256);
            readback = device.CreateGPUHeap(3 * rowBytes, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            GPUAllocation<byte> firstRow = results.Allocate<byte>(rowBytes);
            GPUAllocation<byte> lastRow = results.Allocate<byte>(rowBytes);
            GPUAllocation<byte> patchedRow = results.Allocate<byte>(rowBytes);
            readbackPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            context.BeginFrame();
            context.SetTextureData(texture, source);
            firstRoot = context.FrameData.Allocate(16).GPURange;
            byte[] rejectedContents = new byte[512];
            rejectedContents.AsSpan().Fill(0xed);
            rejected = false;
            try
            {
                context.SetTextureData(texture, rejectedContents, new TextureCopyDesc { Extent = new Extent3D(128, 1, 1) });
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            nextRoot = context.FrameData.Allocate(16).GPURange;
            check(rejected && ReferenceEquals(nextRoot.Buffer, firstRoot.Buffer) && nextRoot.Offset == firstRoot.Offset + 16 && nextRoot.Size == 16,
                "cumulative staging exhaustion rejects a region without disturbing the active frame's root storage");
            ReadOnlySpan<byte> patch = rejectedContents.AsSpan(0, 256);
            context.SetTextureData(texture, patch,
                new TextureCopyDesc { Offset = new Extent3D(0, height / 2, 0), Extent = new Extent3D(64, 1, 1) });
            GPUCommandBuffer commands = context.EndFrame();
            TextureCopyDesc first = new() { Extent = new Extent3D(width, 1, 1) };
            TextureCopyDesc last = new() { Offset = new Extent3D(0, height - 1, 0), Extent = new Extent3D(width, 1, 1) };
            TextureCopyDesc middle = new() { Offset = new Extent3D(0, height / 2, 0), Extent = new Extent3D(width, 1, 1) };
            GPUCommandBuffer readbackCommands = readbackPool.BeginCommands();
            readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            readbackCommands.CopyTextureToMemory(texture.NativeTexture, firstRow.GPURange, first);
            readbackCommands.CopyTextureToMemory(texture.NativeTexture, lastRow.GPURange, last);
            readbackCommands.CopyTextureToMemory(texture.NativeTexture, patchedRow.GPURange, middle);
            readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            readbackCommands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands, readbackCommands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            check(firstRow.CpuSpan.SequenceEqual(source.AsSpan(0, rowBytes)) &&
                lastRow.CpuSpan.SequenceEqual(source.AsSpan(pixelBytes - rowBytes, rowBytes)),
                "an increased upload budget accepts more than 4 MiB, and a rejected region never overwrites previously staged texels");
            check(patchedRow.CpuSpan[..256].SequenceEqual(patch) &&
                patchedRow.CpuSpan[256..].SequenceEqual(source.AsSpan(height / 2 * rowBytes + 256, rowBytes - 256)),
                "capacity rejection reserves no staging, leaving the remaining bytes usable for a valid regional update");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            defaultContext?.DiscardFrame();
            defaultContext?.Destroy();
            texture?.Destroy();
            timeline?.Destroy();
            readbackPool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckLargeTextureUpload(GPUDevice device, Action<bool, string> check)
    {
        const int width = 4096;
        const int height = 4097;
        const int rowBytes = width * 4;
        const int rowPitch = rowBytes + 256;
        const ulong stagingBytes = 1024 * 1024;
        RenderingSystem? system = null;
        Texture? texture = null;
        UploadQueue? uploads = null;
        RenderContext? context = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap readback = default;
        try
        {
            TextureDesc desc = new()
            {
                Extent = new Extent3D(width, height, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            };
            TextureDesc whiteDesc = new()
            {
                Extent = new Extent3D(1, 1, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            SizeAlign requirements = device.GetTextureSizeAlign(desc);
            ulong heapSize = device.GetTextureSizeAlign(whiteDesc).Size;
            heapSize = ((heapSize + requirements.Align - 1) & ~(requirements.Align - 1)) + requirements.Size;
            heapSize = (heapSize + device.Caps.TextureHeapAlignment - 1) & ~(device.Caps.TextureHeapAlignment - 1);
            HeapPoolDesc bufferPool = new() { InitialHeapSize = 128, MaxHeapSize = 128, Budget = 128 };
            HeapPoolDesc texturePool = new() { InitialHeapSize = heapSize, MaxHeapSize = heapSize, Budget = heapSize };
            system = new RenderingSystem(device, bufferPool, texturePool);
            texture = system.CreateTexture(desc);
            byte[] source = new byte[rowPitch * height];
            source.AsSpan().Fill(0xe5);
            for (int row = 0; row < height; ++row)
            {
                for (int index = 0; index < rowBytes; ++index)
                    source[row * rowPitch + index] = (byte)(row * 17 + (row >> 8) * 23 + index * 7 + (index >> 8) * 13);
            }
            TimelinePoint initialization = system.FlushInitializations();
            uploads = new UploadQueue(device, stagingBytes);
            TextureUpload.UploadTexture(uploads, texture.NativeTexture, desc, source, new TextureCopyDesc { RowPitchBytes = rowPitch });
            TimelinePoint uploaded = uploads.Flush();
            check(initialization.Semaphore != null && initialization.Value != 0 && uploads.Stats.Submissions > 1,
                "a caller-owned uploader tiles a padded texture larger than 64 MiB through its fixed 1 MiB staging ring");

            // Selected rows straddle the staging tile boundary without allocating another full-size GPU buffer.
            ReadOnlySpan<int> rows = [0, 1, 63, 64, height / 2, height - 2, height - 1];
            readback = device.CreateGPUHeap((ulong)(rows.Length * rowBytes), MemoryType.Readback);
            timeline = device.CreateTimelineSemaphore();
            context = new RenderContext(system, 256);
            context.BeginFrame();
            for (int index = 0; index < rows.Length; ++index)
            {
                context.CopyTextureToMemory(texture.NativeTexture, readback.Range.GPURange.Slice((ulong)(index * rowBytes), rowBytes),
                    new TextureCopyDesc { Offset = new Extent3D(0, (uint)rows[index], 0), Extent = new Extent3D(width, 1, 1) });
            }
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            GPUCommandBuffer commands = context.EndFrame();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Waits = [uploaded], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            bool matches = true;
            for (int index = 0; index < rows.Length; ++index)
            {
                matches &= readback.Range.CpuSpan.Slice(index * rowBytes, rowBytes).SequenceEqual(source.AsSpan(rows[index] * rowPitch, rowBytes));
            }
            check(matches, "caller-owned TextureUpload preserves texels and skips padded source rows across staging reuse boundaries");
            uploads.Reclaim();
            check(uploads.Valid && uploads.Stats.BytesInUse == 0 && uploads.Stats.PendingBatches == 0,
                "the caller reclaims its uploader after the consuming frame completes while keeping its borrowed completion alive");
            uploads.Destroy();
            check(!uploads.Valid, "the caller explicitly destroys its uploader after every GPU consumer of its completion finishes");
            uploads = null;
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            uploads?.Destroy();
            texture?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckDedicatedTextureUploads(GraphicsBackend backend, Action<bool, string> check)
    {
        GPUDevice? device = null;
        try
        {
            device = Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = true, DesiredQueueCount = 2, DesiredCopyQueueCount = 1 });
            CheckDepthTextureUpload(device, check);
            if (device.Caps.GeneralQueueCount < 2 || device.Caps.CopyQueueCount == 0)
            {
                Console.WriteLine("  [SKIP] gated texture upload regressions require two general queues and one dedicated copy queue");
                return;
            }
            CheckTextureInitializationRetirement(device, check);
            CheckTextureInitializationBatches(device, check);
            CheckTextureUploadSlots(device, check);
            CheckRecordedTextureLifetime(device, check);
            CheckExternalTextureUploads(device, check);
        }
        catch (GPUException exception) when (device == null && exception.Error == Error.Unsupported)
        {
            Console.WriteLine("  [SKIP] dedicated copy texture upload regressions require a dedicated copy queue");
        }
        finally
        {
            device?.WaitIdle();
            device?.Destroy();
        }
    }

    private static void CheckDepthTextureUpload(GPUDevice device, Action<bool, string> check)
    {
        const int texelCount = 32 * 32;
        RenderingSystem? system = null;
        Texture? color = null;
        Texture? depth = null;
        RenderContext? context = null;
        GPUCommandPool? readbackPool = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap readback = default;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 128);
            color = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            });
            depth = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.D32Float,
                Usage = TextureUsage.Sampled | TextureUsage.DepthStencilAttachment | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            });
            byte[] colorContents = new byte[texelCount * 4];
            float[] depthContents = new float[texelCount];
            for (int index = 0; index < colorContents.Length; ++index)
                colorContents[index] = (byte)(index * 19 + 37);
            for (int index = 0; index < depthContents.Length; ++index)
                depthContents[index] = (index + 1) / 2048f;
            readback = device.CreateGPUHeap(texelCount * 8, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            GPUAllocation<byte> colorResult = results.Allocate<byte>(texelCount * 4);
            GPUAllocation<float> depthResult = results.Allocate<float>(texelCount);
            readbackPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            context = new RenderContext(system, 256);
            context.BeginFrame();
            context.SetTextureData(color, colorContents);
            context.SetTextureData(depth, MemoryMarshal.AsBytes(depthContents.AsSpan()));
            GPUCommandBuffer commands = context.EndFrame();
            GPUCommandBuffer readbackCommands = readbackPool.BeginCommands();
            readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            readbackCommands.CopyTextureToMemory(color.NativeTexture, colorResult.GPURange);
            readbackCommands.CopyTextureToMemory(depth.NativeTexture, depthResult.GPURange);
            readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            readbackCommands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands, readbackCommands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            check(colorResult.CpuSpan.SequenceEqual(colorContents) && depthResult.CpuSpan.SequenceEqual(depthContents),
                "frame-local color and D32 depth uploads stay on the general queue even when a dedicated copy queue is available");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            depth?.Destroy();
            color?.Destroy();
            timeline?.Destroy();
            readbackPool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckTextureInitializationRetirement(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        Texture? texture = null;
        Texture? spare = null;
        Texture? recycled = null;
        GPUTimelineSemaphore? gate = null;
        GPUTimelineSemaphore? marker = null;
        bool released = false;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 128);
            TimelinePoint bootstrap = system.FlushInitializations();
            bootstrap.Wait();
            system.Tick();
            gate = device.CreateTimelineSemaphore();
            marker = device.CreateTimelineSemaphore();
            device.Submit(new SubmitDesc
            {
                Waits = [new TimelinePoint { Semaphore = gate, Value = 1 }],
                Completion = new TimelinePoint { Semaphore = marker, Value = 1 },
            });
            TextureDesc desc = new()
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            texture = system.CreateTexture(desc);
            uint releasedIndex = texture.SampledIndex;
            TimelinePoint initialization = system.FlushInitializations();
            check(ReferenceEquals(initialization.Semaphore, bootstrap.Semaphore) && initialization.Value > bootstrap.Value &&
                !WaitForPoints([initialization], TimeSpan.FromMilliseconds(100)),
                "FlushInitializations returns a borrowed system timeline ordered on the gated general queue");
            texture.Destroy();
            check(texture.IsDestroyed, "Destroy invalidates the wrapper while its submitted initialization remains pending");
            system.Tick();
            spare = system.CreateTexture(desc);
            check(spare.SampledIndex != releasedIndex,
                "initialization retirement preserves native storage and descriptors until that initialization completes");
            device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = gate, Value = 1 } }, 1);
            released = true;
            initialization.Wait();
            system.Tick();
            recycled = system.CreateTexture(desc);
            check(recycled.SampledIndex == releasedIndex,
                "completed initialization plus Tick recycles an explicitly destroyed texture without a frame submission");
        }
        finally
        {
            if (gate != null && !released)
                device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = gate, Value = 1 } }, 1);
            device.WaitIdle();
            recycled?.Destroy();
            spare?.Destroy();
            if (texture is { IsDestroyed: false }) texture.Destroy();
            marker?.Destroy();
            gate?.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckTextureInitializationBatches(GPUDevice device, Action<bool, string> check)
    {
        const int textureCount = (int)UploadQueue.OperationLimit + 1;
        if (device.Caps.TextureDescriptorHeapCapacity <= textureCount)
        {
            Console.WriteLine("  [SKIP] initialization batch stress test requires more texture slots than the device's standard heap provides");
            return;
        }
        RenderingSystem? system = null;
        Texture?[] textures = new Texture?[textureCount];
        GPUTimelineSemaphore? gate = null;
        GPUTimelineSemaphore? marker = null;
        bool released = false;
        try
        {
            TextureDesc desc = new()
            {
                Extent = new Extent3D(1, 1, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            SizeAlign requirements = device.GetTextureSizeAlign(desc);
            ulong stride = (requirements.Size + requirements.Align - 1) & ~(requirements.Align - 1);
            ulong heapSize = stride * (textureCount + 1UL);
            heapSize = (heapSize + device.Caps.TextureHeapAlignment - 1) & ~(device.Caps.TextureHeapAlignment - 1);
            HeapPoolDesc bufferPool = new() { InitialHeapSize = 128, MaxHeapSize = 128, Budget = 128 };
            HeapPoolDesc texturePool = new() { InitialHeapSize = heapSize, MaxHeapSize = heapSize, Budget = heapSize };
            system = new RenderingSystem(device, bufferPool, texturePool);
            TimelinePoint bootstrap = system.FlushInitializations();
            bootstrap.Wait();
            for (int index = 0; index < textures.Length; ++index)
                textures[index] = system.CreateTexture(desc);
            gate = device.CreateTimelineSemaphore();
            marker = device.CreateTimelineSemaphore();
            device.Submit(new SubmitDesc
            {
                Waits = [new TimelinePoint { Semaphore = gate, Value = 1 }],
                Completion = new TimelinePoint { Semaphore = marker, Value = 1 },
            });
            TimelinePoint initialization = system.FlushInitializations();
            bool exact = initialization.Value == bootstrap.Value + 2;
            for (int index = 0; index < textures.Length; ++index)
            {
                TimelinePoint point = textures[index]!.Initialization.Completion;
                exact &= ReferenceEquals(point.Semaphore, initialization.Semaphore) &&
                    point.Value == initialization.Value - (index == 0 ? 0UL : 1UL);
            }
            check(exact && !WaitForPoints([initialization], TimeSpan.FromMilliseconds(100)),
                "more than 4096 pending initializations track each texture's actual batch point across the operation-limit autoflush");
            device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = gate, Value = 1 } }, 1);
            released = true;
            initialization.Wait();
            check(initialization.Semaphore!.CompletedValue >= initialization.Value,
                "the borrowed final initialization point covers every batch within the fixed texture budget");
        }
        finally
        {
            if (gate != null && !released)
                device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = gate, Value = 1 } }, 1);
            device.WaitIdle();
            foreach (Texture? texture in textures)
                texture?.Destroy();
            marker?.Destroy();
            gate?.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckTextureUploadSlots(GPUDevice device, Action<bool, string> check)
    {
        const int pixelBytes = 32 * 32 * 4;
        RenderingSystem? system = null;
        Texture? texture = null;
        RenderContext? context = null;
        GPUCommandPool? readbackPool = null;
        GPUTimelineSemaphore? gate = null;
        GPUTimelineSemaphore? marker = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap readback = default;
        bool released = false;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 128);
            texture = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            });
            system.FlushInitializations().Wait();
            context = new RenderContext(system, 256, pixelBytes);
            readback = device.CreateGPUHeap(3 * pixelBytes, MemoryType.Readback);
            readbackPool = device.CreateCommandPool(0);
            gate = device.CreateTimelineSemaphore();
            marker = device.CreateTimelineSemaphore();
            timeline = device.CreateTimelineSemaphore();
            device.Submit(new SubmitDesc
            {
                Waits = [new TimelinePoint { Semaphore = gate, Value = 1 }],
                Completion = new TimelinePoint { Semaphore = marker, Value = 1 },
            });
            byte[] source = new byte[pixelBytes];
            for (int frame = 0; frame < 3; ++frame)
            {
                if (frame == 2)
                {
                    check(timeline.CompletedValue == 0, "both frame slots accept texture uploads while their submitted frames are gated");
                    device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = gate, Value = 1 } }, 1);
                    released = true;
                }
                source.AsSpan().Fill((byte)(0x23 + frame * 0x37));
                context.BeginFrame();
                context.SetTextureData(texture, source);
                GPUCommandBuffer commands = context.EndFrame();
                GPUCommandBuffer readbackCommands = readbackPool.BeginCommands();
                readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                readbackCommands.CopyTextureToMemory(texture.NativeTexture, readback.Range.GPURange.Slice((ulong)(frame * pixelBytes), pixelBytes));
                readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                readbackCommands.EndCommands();
                TimelinePoint completion = new() { Semaphore = timeline, Value = (ulong)frame + 1 };
                device.Submit(new SubmitDesc { Commands = [commands, readbackCommands], Completion = completion });
                context.MarkSubmitted(completion);
            }
            context.Wait();
            bool matches = true;
            for (int frame = 0; frame < 3; ++frame)
            {
                source.AsSpan().Fill((byte)(0x23 + frame * 0x37));
                matches &= readback.Range.CpuSpan.Slice(frame * pixelBytes, pixelBytes).SequenceEqual(source);
            }
            check(matches, "each slot keeps its staged source through completion and safely reuses its full budget on the third frame");
        }
        finally
        {
            if (gate != null && !released)
                device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = gate, Value = 1 } }, 1);
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            texture?.Destroy();
            timeline?.Destroy();
            marker?.Destroy();
            gate?.Destroy();
            readbackPool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static void CheckRecordedTextureLifetime(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        RenderContext? context = null;
        GPUCommandPool? readbackPool = null;
        WeakReference? reference = null;
        GPUTimelineSemaphore? gate = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap readback = default;
        bool released = false;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 128);
            context = new RenderContext(system, 256);
            readback = device.CreateGPUHeap(32 * 32 * 4, MemoryType.Readback);
            readbackPool = device.CreateCommandPool(0);
            gate = device.CreateTimelineSemaphore();
            timeline = device.CreateTimelineSemaphore();
            byte[] source = new byte[32 * 32 * 4];
            source.AsSpan().Fill(0x79);
            TextureDesc desc = new()
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            };
            context.BeginFrame();
            GPUCommandBuffer readbackCommands = readbackPool.BeginCommands();
            reference = LeakRecordedTexture(system, context, readbackCommands, desc, source, readback.Range.GPURange);
            readbackCommands.EndCommands();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            system.Tick();
            bool retained = IsLiveTexture(reference);
            check(retained, "a recorded SetTextureData retains its texture wrapper before submission registration");
            if (!retained) return;
            GPUCommandBuffer commands = context.EndFrame();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc
            {
                Commands = [commands, readbackCommands],
                Waits = [new TimelinePoint { Semaphore = gate, Value = 1 }],
                Completion = completion,
            });
            context.MarkSubmitted(completion);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            system.Tick();
            check(IsLiveTexture(reference), "a finalized upload target retires through the submitted frame, not a separate data-upload lifetime");
            device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = gate, Value = 1 } }, 1);
            released = true;
            context.Wait();
            check(readback.Range.CpuSpan.SequenceEqual(source), "a collected texture upload target remains valid until its frame's GPU readback completes");
            system.Tick();
            check(reference.Target is Texture { IsDestroyed: true }, "Tick reclaims the collected upload target after its frame completion");
        }
        finally
        {
            if (gate != null && !released)
                device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = gate, Value = 1 } }, 1);
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            if (reference?.Target is Texture { IsDestroyed: false } texture) texture.Destroy();
            timeline?.Destroy();
            gate?.Destroy();
            readbackPool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    // Collection must occur after the helper releases its local target, while SetTextureData supplies the only strong recording reference.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LeakRecordedTexture(RenderingSystem system, RenderContext context, GPUCommandBuffer readbackCommands,
        TextureDesc desc, ReadOnlySpan<byte> source, GPURange readback)
    {
        Texture texture = system.CreateTexture(desc);
        context.SetTextureData(texture, source);
        readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
        readbackCommands.CopyTextureToMemory(texture.NativeTexture, readback);
        readbackCommands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
        return new WeakReference(texture, trackResurrection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsLiveTexture(WeakReference reference)
    {
        return reference.Target is Texture { IsDestroyed: false };
    }

    private static void CheckExternalTextureUploads(GPUDevice device, Action<bool, string> check)
    {
        const int pixelBytes = 32 * 32 * 4;
        RenderingSystem? system = null;
        Texture? texture = null;
        Texture? unrelated = null;
        RenderContext? consumer = null;
        RenderContext? independentContext = null;
        UploadQueue? uploads = null;
        GPUTimelineSemaphore? copyGate = null;
        GPUTimelineSemaphore? frameGate = null;
        GPUTimelineSemaphore? copyMarker = null;
        GPUHeap readback = default;
        bool copyReleased = false;
        bool frameReleased = false;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 128);
            TextureDesc desc = new()
            {
                Extent = new Extent3D(32, 32, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
            };
            texture = system.CreateTexture(desc);
            unrelated = system.CreateTexture(desc);
            byte[] original = new byte[pixelBytes];
            byte[] updated = new byte[pixelBytes];
            byte[] other = new byte[pixelBytes];
            for (int index = 0; index < pixelBytes; ++index)
            {
                original[index] = (byte)(index * 5 + 13);
                updated[index] = (byte)(index * 11 + 71);
                other[index] = (byte)(index * 17 + 113);
            }
            consumer = new RenderContext(system, 256);
            independentContext = new RenderContext(system, 256);
            readback = device.CreateGPUHeap(3 * pixelBytes, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            GPUAllocation<byte> oldResult = results.Allocate<byte>(pixelBytes);
            GPUAllocation<byte> newResult = results.Allocate<byte>(pixelBytes);
            GPUAllocation<byte> unrelatedResult = results.Allocate<byte>(pixelBytes);
            copyGate = device.CreateTimelineSemaphore();
            frameGate = device.CreateTimelineSemaphore();
            copyMarker = device.CreateTimelineSemaphore();
            uint copyQueue = device.Caps.GeneralQueueCount + device.Caps.ComputeQueueCount;
            uploads = new UploadQueue(device, pixelBytes, copyQueue);
            TimelinePoint initialization = system.FlushInitializations();
            // Queue this dependency before staging: TextureUpload may flush intermediate batches itself.
            device.Submit(new SubmitDesc
            {
                Waits = [initialization, new TimelinePoint { Semaphore = copyGate, Value = 1 }],
                Completion = new TimelinePoint { Semaphore = copyMarker, Value = 1 },
            }, copyQueue);
            TextureUpload.UploadTexture(uploads, texture.NativeTexture, desc, original);
            TimelinePoint uploaded = uploads.Flush();

            independentContext.BeginFrame();
            independentContext.SetTextureData(unrelated, other);
            // Address-based upload/readback uses separate command buffers on affected NVIDIA drivers.
            consumer.BeginFrame();
            consumer.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            consumer.CopyTextureToMemory(unrelated.NativeTexture, unrelatedResult.GPURange);
            consumer.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            TimelinePoint independentCompletion = system.Submit([independentContext, consumer]);
            bool independent = WaitForPoints([independentCompletion], TimeSpan.FromSeconds(2));
            check(independent && !WaitForPoints([uploaded], TimeSpan.FromMilliseconds(100)),
                "an unrelated frame completes without being globally gated by a caller-owned copy-queue upload");
            if (!independent) return;
            independentContext.Wait();
            check(unrelatedResult.CpuSpan.SequenceEqual(other),
                "the independent frame reads its own frame-local texture upload while the copy queue is blocked");

            consumer.BeginFrame();
            consumer.CopyTextureToMemory(texture.NativeTexture, oldResult.GPURange);
            consumer.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            TimelinePoint oldCompletion = consumer.Submit(waits: [uploaded, new TimelinePoint { Semaphore = frameGate, Value = 1 }]);
            device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = copyGate, Value = 1 } }, 1);
            copyReleased = true;
            uploaded.Wait();

            // Prior rendering is a caller-supplied GPU dependency, not an automatic render-to-upload wait.
            device.Submit(new SubmitDesc
            {
                Waits = [oldCompletion],
                Completion = new TimelinePoint { Semaphore = copyMarker, Value = 2 },
            }, copyQueue);
            TextureUpload.UploadTexture(uploads, texture.NativeTexture, desc, updated);
            TimelinePoint updatedUpload = uploads.Flush();
            check(!WaitForPoints([updatedUpload], TimeSpan.FromMilliseconds(100)) && oldCompletion.Semaphore?.CompletedValue < oldCompletion.Value,
                "a copy-queue wait on the managed frame completion keeps a later update behind its gated prior consumer");
            device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = frameGate, Value = 1 } }, 1);
            frameReleased = true;
            consumer.Wait();
            bool oldCompleted = oldCompletion.Semaphore?.CompletedValue >= oldCompletion.Value;
            check(oldCompleted && oldResult.CpuSpan.SequenceEqual(original),
                "explicit upload-to-render and render-to-upload waits preserve the prior consumer's original texels");
            if (!oldCompleted) return;

            consumer.BeginFrame();
            consumer.CopyTextureToMemory(texture.NativeTexture, newResult.GPURange);
            consumer.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            TimelinePoint newCompletion = consumer.Submit(waits: [updatedUpload]);
            consumer.Wait();
            bool newCompleted = newCompletion.Semaphore?.CompletedValue >= newCompletion.Value;
            check(newCompleted && newResult.CpuSpan.SequenceEqual(updated),
                "the next frame explicitly waits on the caller-owned upload and reads the updated texels");
            if (!newCompleted) return;
            uploads.Reclaim();
            check(uploads.Valid && uploads.Stats.BytesInUse == 0 && uploads.Stats.PendingBatches == 0,
                "external upload staging retires under caller control after the consuming frame completes");
            uploads.Destroy();
            check(!uploads.Valid, "the caller destroys the external uploader only after all consumers finish using its borrowed timeline points");
            uploads = null;
        }
        finally
        {
            if (copyGate != null && !copyReleased)
                device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = copyGate, Value = 1 } }, 1);
            if (frameGate != null && !frameReleased)
                device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = frameGate, Value = 1 } }, 1);
            device.WaitIdle();
            independentContext?.DiscardFrame();
            independentContext?.Destroy();
            consumer?.DiscardFrame();
            consumer?.Destroy();
            uploads?.Destroy();
            unrelated?.Destroy();
            texture?.Destroy();
            copyMarker?.Destroy();
            frameGate?.Destroy();
            copyGate?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    private static bool WaitForPoints(ReadOnlySpan<TimelinePoint> waits, TimeSpan timeout)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        for (;;)
        {
            bool completed = true;
            foreach (TimelinePoint wait in waits)
                completed &= wait.Semaphore != null && wait.Semaphore.CompletedValue >= wait.Value;
            if (completed) return true;
            if (elapsed.Elapsed >= timeout) return false;
            Thread.Sleep(1);
        }
    }

    private static unsafe void CheckConcurrentResources(GPUDevice device, Action<bool, string> check)
    {
        const int workerCount = 4;
        const int resourcesPerWorker = 8;
        const int resourceCount = workerCount * resourcesPerWorker;
        const int finalizedCount = 4;
        const int elementCount = 16;
        RenderingSystem? system = null;
        Buffer?[] buffers = new Buffer?[resourceCount];
        Texture?[] textures = new Texture?[resourceCount];
        Sampler?[] samplers = new Sampler?[resourceCount];
        RenderingResource?[] finalized = [];
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            TextureDesc whiteDesc = new()
            {
                Extent = new Extent3D(1, 1, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            ulong heapAlignment = device.Caps.TextureHeapAlignment;
            ulong initialTextureSize = (device.GetTextureSizeAlign(whiteDesc).Size + heapAlignment - 1) & ~(heapAlignment - 1);
            HeapPoolDesc bufferPool = new() { InitialHeapSize = 128, InitialAllocationCapacity = 1 };
            HeapPoolDesc texturePool = new() { InitialHeapSize = initialTextureSize, InitialAllocationCapacity = 1 };
            system = new RenderingSystem(device, bufferPool, texturePool);
            TextureDesc textureDesc = new()
            {
                Extent = new Extent3D(64, 64, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            };
            readback = device.CreateGPUHeap(resourceCount * elementCount * sizeof(uint), MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            GPUAllocation<uint> contents = results.Allocate<uint>(resourceCount * elementCount);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            for (int phase = 0; phase < 2; ++phase)
            {
                if (phase != 0)
                {
                    WeakReference[] references = LeakConcurrentResources(system, textureDesc, finalizedCount);
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                    GC.WaitForPendingFinalizers();
                    finalized = new RenderingResource?[references.Length];
                    bool queued = true;
                    for (int index = 0; index < references.Length; ++index)
                    {
                        finalized[index] = references[index].Target as RenderingResource;
                        queued &= finalized[index] is { IsDestroyed: false };
                    }
                    check(queued, "unsubmitted finalized buffers, textures, and samplers await Tick before parallel churn");
                }

                int rounds = phase == 0 ? 1 : 24;
                using ManualResetEventSlim start = new(false);
                using CountdownEvent ready = new(workerCount);
                Thread[] threads = new Thread[workerCount];
                Exception?[] failures = new Exception?[workerCount];
                string?[] errors = new string?[workerCount];
                bool[] completed = new bool[workerCount];
                int started = 0;
                try
                {
                    for (int index = 0; index < workerCount; ++index)
                    {
                        int worker = index;
                        threads[index] = new Thread(() =>
                        {
                            bool entered = false;
                            try
                            {
                                Gpu.SetErrorCallback((string message, bool fatal) => errors[worker] ??= message);
                                start.Wait();
                                Span<uint> source = stackalloc uint[elementCount];
                                for (int round = 0; round < rounds; ++round)
                                {
                                    for (int local = 0; local < resourcesPerWorker; ++local)
                                    {
                                        int slot = worker * resourcesPerWorker + local;
                                        buffers[slot]?.Destroy();
                                        buffers[slot] = null;
                                        textures[slot]?.Destroy();
                                        textures[slot] = null;
                                        samplers[slot]?.Destroy();
                                        samplers[slot] = null;
                                        for (int element = 0; element < elementCount; ++element)
                                            source[element] = (uint)(phase * 100000 + (round * resourceCount + slot) * elementCount + element + 1);
                                        int uploadCount = phase == 0 || (round & 1) != 0 ? elementCount : elementCount / 2;
                                        buffers[slot] = system.CreateBuffer<uint>(source[..uploadCount]);
                                        textures[slot] = system.CreateTexture(textureDesc);
                                        samplers[slot] = system.CreateSampler(new SamplerDesc());
                                        if (round == 0 && local == 0)
                                        {
                                            entered = true;
                                            ready.Signal();
                                        }
                                    }
                                    Thread.Yield();
                                }
                                completed[worker] = true;
                            }
                            catch (Exception exception) when (exception is not OutOfMemoryException)
                            {
                                failures[worker] = exception;
                            }
                            finally
                            {
                                if (!entered) ready.Signal();
                                Gpu.SetErrorCallback(null);
                            }
                        });
                        threads[index].Start();
                        ++started;
                    }
                    start.Set();
                    ready.Wait();
                    if (phase != 0)
                    {
                        for (int tick = 0; tick < rounds; ++tick)
                        {
                            system.Tick();
                            Thread.Yield();
                        }
                    }
                }
                finally
                {
                    start.Set();
                    for (int index = 0; index < started; ++index)
                        threads[index].Join();
                }

                bool succeeded = true;
                for (int worker = 0; worker < workerCount; ++worker)
                {
                    bool success = completed[worker] && failures[worker] == null && errors[worker] == null;
                    check(success, $"parallel resource phase {phase}, worker {worker}: {errors[worker] ?? "no diagnostics"}; {failures[worker]}");
                    succeeded &= success;
                }
                if (!succeeded) return;

                bool disjoint = true;
                bool uniqueTextures = true;
                bool uniqueSamplers = true;
                bool[] textureSlots = new bool[2 * (resourceCount + finalizedCount) + 1];
                bool[] samplerSlots = new bool[resourceCount + finalizedCount + (int)SharedSamplers.Count];
                textureSlots[system.TextureWhite.SampledIndex] = true;
                samplerSlots.AsSpan(0, (int)SharedSamplers.Count).Fill(true);
                for (int index = 0; index < resourceCount; ++index)
                {
                    GPURange range = buffers[index]!.Range;
                    disjoint &= range.Buffer != null && range.Offset % 16 == 0 && range.Size == elementCount * sizeof(uint);
                    for (int previous = 0; previous < index; ++previous)
                    {
                        GPURange other = buffers[previous]!.Range;
                        disjoint &= !ReferenceEquals(range.Buffer, other.Buffer) || range.Offset + range.Size <= other.Offset ||
                            other.Offset + other.Size <= range.Offset;
                    }
                    for (int role = 0; role < 2; ++role)
                    {
                        uint slot = role == 0 ? textures[index]!.SampledIndex : textures[index]!.StorageIndex;
                        if (slot >= textureSlots.Length || textureSlots[slot]) uniqueTextures = false;
                        else textureSlots[slot] = true;
                    }
                    uint samplerSlot = samplers[index]!.Index;
                    if (samplerSlot >= samplerSlots.Length || samplerSlots[samplerSlot]) uniqueSamplers = false;
                    else samplerSlots[samplerSlot] = true;
                }
                check(disjoint, $"parallel resource phase {phase} keeps every live buffer range aligned and disjoint");
                check(uniqueTextures, $"parallel resource phase {phase} keeps all sampled/storage slots unique and bounded by peak live demand");
                check(uniqueSamplers, $"parallel resource phase {phase} keeps all sampler slots unique and bounded by peak live demand");
                if (!disjoint || !uniqueTextures || !uniqueSamplers) return;

                if (phase != 0) pool.Reset();
                GPUCommandBuffer commands = pool.BeginCommands();
                for (int index = 0; index < resourceCount; ++index)
                {
                    commands.CopyMemory(buffers[index]!.Range,
                        contents.GPURange.Slice((ulong)(index * elementCount * sizeof(uint)), elementCount * sizeof(uint)));
                }
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                TimelinePoint completion = new() { Semaphore = timeline, Value = (ulong)phase + 1 };
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                completion.Wait();
                bool uploadsMatch = true;
                for (int index = 0; index < resourceCount; ++index)
                {
                    for (int element = 0; element < elementCount; ++element)
                    {
                        uint expected = (uint)(phase * 100000 + ((rounds - 1) * resourceCount + index) * elementCount + element + 1);
                        uploadsMatch &= contents.Cpu[index * elementCount + element] == expected;
                    }
                }
                check(uploadsMatch, $"parallel resource phase {phase} preserves distinct uploads through completed GPU buffer copies");
                if (phase != 0)
                {
                    bool reclaimed = true;
                    foreach (RenderingResource? resource in finalized)
                        reclaimed &= resource is { IsDestroyed: true };
                    check(reclaimed, "Tick reclaims queued buffers, textures, and samplers while independent workers create and destroy resources");
                }
            }
        }
        finally
        {
            device.WaitIdle();
            for (int index = 0; index < resourceCount; ++index)
            {
                if (samplers[index] is { IsDestroyed: false } sampler) sampler.Destroy();
                if (textures[index] is { IsDestroyed: false } texture) texture.Destroy();
                if (buffers[index] is { IsDestroyed: false } buffer) buffer.Destroy();
            }
            foreach (RenderingResource? resource in finalized)
            {
                if (resource is { IsDestroyed: false }) resource.Destroy();
            }
            timeline?.Destroy();
            pool?.Destroy();
            readback.Destroy();
            system?.Destroy();
        }
    }

    // The helper must return before collection so tier-0 locals cannot keep the intentionally leaked wrappers reachable.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] LeakConcurrentResources(RenderingSystem system, TextureDesc textureDesc, int count)
    {
        RenderingResource?[] resources = new RenderingResource?[3 * count];
        try
        {
            for (int index = 0; index < count; ++index)
            {
                resources[3 * index] = system.CreateBuffer(64);
                resources[3 * index + 1] = system.CreateTexture(textureDesc);
                resources[3 * index + 2] = system.CreateSampler(new SamplerDesc());
            }
            WeakReference[] references = new WeakReference[resources.Length];
            for (int index = 0; index < references.Length; ++index)
                references[index] = new WeakReference(resources[index], trackResurrection: true);
            return references;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            foreach (RenderingResource? resource in resources)
                resource?.Destroy();
            throw;
        }
    }

    private static void CheckTextureBudget(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        Texture? texture = null;
        Texture? unexpected = null;
        try
        {
            TextureDesc whiteDesc = new()
            {
                Extent = new Extent3D(1, 1, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            TextureDesc desc = new()
            {
                Extent = new Extent3D(64, 64, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            };
            SizeAlign requirements = device.GetTextureSizeAlign(desc);
            ulong heapSize = device.GetTextureSizeAlign(whiteDesc).Size;
            heapSize = ((heapSize + requirements.Align - 1) & ~(requirements.Align - 1)) + requirements.Size;
            heapSize = (heapSize + device.Caps.TextureHeapAlignment - 1) & ~(device.Caps.TextureHeapAlignment - 1);
            HeapPoolDesc bufferPool = new() { InitialHeapSize = 128, MaxHeapSize = 128, Budget = 128 };
            HeapPoolDesc texturePool = new()
            {
                InitialHeapSize = heapSize,
                MaxHeapSize = heapSize,
                Budget = heapSize,
                InitialAllocationCapacity = 1,
            };
            system = new RenderingSystem(device, bufferPool, texturePool);
            TextureDesc oversized = desc;
            oversized.Extent = new Extent3D(1024, 1024, 1);
            bool exhausted = false;
            try
            {
                unexpected = system.CreateTexture(oversized);
            }
            catch (InvalidOperationException exception)
            {
                exhausted = exception.Message.Contains("texture pool budget is exhausted", StringComparison.Ordinal);
            }
            check(exhausted, "an explicit texture byte budget reports exhaustion after reserving both requested descriptor roles");
            unexpected?.Destroy();
            unexpected = null;
            texture = system.CreateTexture(desc);
            check(texture.SampledIndex != Texture.NoIndex && texture.StorageIndex != Texture.NoIndex && texture.SampledIndex != texture.StorageIndex,
                "texture budget failure returns both descriptor slots and leaves smaller placement and metadata growth usable");
            uint sampledIndex = texture.SampledIndex;
            uint storageIndex = texture.StorageIndex;
            texture.Destroy();
            texture = system.CreateTexture(desc);
            check((texture.SampledIndex == sampledIndex && texture.StorageIndex == storageIndex) ||
                (texture.SampledIndex == storageIndex && texture.StorageIndex == sampledIndex),
                "destroying a budgeted texture returns its placement and both descriptor roles for reuse");
        }
        finally
        {
            device.WaitIdle();
            unexpected?.Destroy();
            texture?.Destroy();
            system?.Destroy();
        }
    }
}
