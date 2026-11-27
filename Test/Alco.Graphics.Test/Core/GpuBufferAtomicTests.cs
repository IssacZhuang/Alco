using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>Validates Core's u32 buffer atomics in generated shaders and on headless GPU devices.</summary>
[TestFixture]
[NonParallelizable]
[Category("GpuBufferAtomics")]
public sealed class GpuBufferAtomicTests
{
    private const string ModuleName = "GpuBufferAtomicProbe";
    private const int ThreadsPerGroup = 32;
    private const int WorkgroupCount = 8;
    private const int ThreadCount = ThreadsPerGroup * WorkgroupCount;
    private const int IndependentCaseCount = 2;
    private const int OperationsPerCase = 6;
    private const int ContendedCellCount = 16;
    private const int CellStride = 2; // One u32 guard word after each atomic cell.
    private const uint Guard = 0x5a9b_73e4;
    private const uint CasExpected = 0x8000_0001;

    private static readonly string AtomicSource = $$"""
        module {{ModuleName}};
        import Core;

        [GPURoot]
        struct AtomicRoot
        {
            GPUBufferView IndependentCells;
            GPUBufferView IndependentPrevious;
            GPUBufferView ContendedCells;
            GPUBufferView ContendedPrevious;
        };

        [shader("compute")]
        [numthreads({{ThreadsPerGroup}}, 1, 1)]
        void csMain(uint3 id : SV_DispatchThreadID)
        {
            AtomicRoot root = gpu_load_root<AtomicRoot>();
            const uint cellStrideBytes = {{CellStride * sizeof(uint)}}u;
            if (id.x < {{IndependentCaseCount}}u)
            {
                const uint operand = 0x80000010u;
                uint previousIndex = id.x * {{OperationsPerCase}}u;
                GPUBufferView cells = gpu_offset_bytes(root.IndependentCells, previousIndex * cellStrideBytes);
                gpu_store(root.IndependentPrevious, previousIndex, gpu_atomic_add_u32(cells, operand));
                gpu_store(root.IndependentPrevious, previousIndex + 1u, gpu_atomic_max_u32(gpu_offset_bytes(cells, cellStrideBytes), operand));
                gpu_store(root.IndependentPrevious, previousIndex + 2u, gpu_atomic_min_u32(gpu_offset_bytes(cells, 2u * cellStrideBytes), operand));
                gpu_store(root.IndependentPrevious, previousIndex + 3u, gpu_atomic_or_u32(gpu_offset_bytes(cells, 3u * cellStrideBytes), operand));
                gpu_store(root.IndependentPrevious, previousIndex + 4u, gpu_atomic_exchange_u32(gpu_offset_bytes(cells, 4u * cellStrideBytes), operand));
                gpu_store(root.IndependentPrevious, previousIndex + 5u, gpu_atomic_cas_u32(gpu_offset_bytes(cells, 5u * cellStrideBytes), {{CasExpected}}u, 0xffffffffu));
            }

            // Each thread tries once. Every workgroup competes for every cell without waiting for other groups.
            uint cell = id.x % {{ContendedCellCount}}u;
            GPUBufferView address = gpu_offset_bytes(root.ContendedCells, cell * cellStrideBytes);
            uint previous = gpu_atomic_cas_u32(address, {{CasExpected}}u, id.x + 1u);
            gpu_store(root.ContendedPrevious, id.x, previous);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct AtomicRoot(
        GPUBufferView IndependentCells, GPUBufferView IndependentPrevious, GPUBufferView ContendedCells, GPUBufferView ContendedPrevious);

    /// <summary>Compiles the atomic kernel for the requested target without requiring a GPU.</summary>
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Dxil)]
    [TestCase(ShaderTarget.MetalSource)]
    public void ValidateShader_EmitsBufferAtomics(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
            Assert.Ignore($"The bundled shader compiler cannot emit {target} on this host.");

        CompiledShader compiled = CompileAtomics(target);
        CompiledShaderStage stage = compiled.GetStage(ShaderStageKind.Compute);
        Assert.That(stage.ThreadgroupSize, Is.EqualTo(new Extent3D(ThreadsPerGroup, 1, 1)));
        Assert.That(compiled.Reflection.Root?.MatchesSize<AtomicRoot>(), Is.True);

        ReadOnlySpan<byte> code = stage.Code.Span;
        switch (target)
        {
            case ShaderTarget.Spirv:
            case ShaderTarget.SpirvCompact:
                AssertSpirvAtomics(code);
                break;
            case ShaderTarget.Dxil:
                Assert.That(code.StartsWith("DXBC"u8) && code.IndexOf("DXIL"u8) >= 0, Is.True, "Expected a DXIL container.");
                break;
            case ShaderTarget.MetalSource:
                AssertMetalAtomics(Encoding.UTF8.GetString(code));
                break;
        }
    }

    /// <summary>Checks fetch-old results, unsigned updates, aligned views, and strong CAS across workgroups.</summary>
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.Metal)]
    public unsafe void Readback_VerifiesAtomicValuesAndContention(GraphicsBackend backend)
    {
        const int independentCellStart = 3; // Byte offset 12: aligned for u32, but not for wider types.
        const int independentPreviousStart = independentCellStart + IndependentCaseCount * OperationsPerCase * CellStride + 1;
        const int contendedCellStart = independentPreviousStart + IndependentCaseCount * OperationsPerCase + 1;
        const int contendedPreviousStart = contendedCellStart + ContendedCellCount * CellStride + 1;
        const int bufferWords = contendedPreviousStart + ThreadCount + 1;
        const int bufferBytes = bufferWords * sizeof(uint);
        const int rootOffset = (bufferBytes + 255) & ~255;
        int rootBytes = sizeof(AtomicRoot);

        GPUDevice device = CreateHeadlessDeviceOrIgnore(backend);
        GPUPipeline? pipeline = null;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap upload = default;
        GPUHeap storage = default;
        GPUHeap readback = default;
        try
        {
            Assert.That(device.ActiveBackend, Is.EqualTo(backend));
            TestContext.Progress.WriteLine($"{backend} headless GPU: {device.Caps.DeviceName}");
            CompiledShader shader = CompileAtomics(ShaderCompiler.TargetForBackend(backend));
            pipeline = device.CreateComputePipeline(shader.GetGpuStage(ShaderStageKind.Compute));
            upload = device.CreateGPUHeap((ulong)(rootOffset + rootBytes), MemoryType.CpuVisible);
            storage = device.CreateGPUHeap(bufferBytes, MemoryType.GpuOnly);
            readback = device.CreateGPUHeap(bufferBytes, MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();

            GPURange storageRange = storage.Range.GPURange;
            AtomicRoot root = new(
                storageRange.Slice(independentCellStart * sizeof(uint)).View,
                storageRange.Slice(independentPreviousStart * sizeof(uint)).View,
                storageRange.Slice(contendedCellStart * sizeof(uint)).View,
                storageRange.Slice(contendedPreviousStart * sizeof(uint)).View);
            Assert.That(root.IndependentCells.Index, Is.Not.Zero, "The atomic storage must have a shader-visible buffer view.");
            Assert.That(root.IndependentCells.Offset % 8, Is.EqualTo(4u), "Independent cells use 4-byte rather than 8-byte alignment.");
            Assert.That(root.ContendedCells.Offset % 8, Is.EqualTo(4u), "Contended cells use 4-byte rather than 8-byte alignment.");
            MemoryMarshal.Write(upload.Range.CpuSpan.Slice(rootOffset, rootBytes), in root);

            Span<uint> initial = MemoryMarshal.Cast<byte, uint>(upload.Range.CpuSpan[..bufferBytes]);
            initial.Fill(Guard);
            uint[] expected = new uint[bufferWords];
            Array.Fill(expected, Guard);

            // One independent target per operation and lane: add, max, min, OR, exchange, CAS.
            ReadOnlySpan<uint> oldWords =
            [
                0xffff_fff0, 8, 9, 0x1020_3040, 0x83, CasExpected,
                0xffff_fff0, 0xffff_fff0, 0xffff_fff0, 0x1020_3040, 0x83, CasExpected + 1,
            ];
            ReadOnlySpan<uint> newWords =
            [
                0x8000_0000, 0x8000_0010, 9, 0x9020_3050, 0x8000_0010, 0xffff_ffff,
                0x8000_0000, 0xffff_fff0, 0x8000_0010, 0x9020_3050, 0x8000_0010, CasExpected + 1,
            ];
            for (int index = 0; index < oldWords.Length; ++index)
            {
                int cell = independentCellStart + index * CellStride;
                initial[cell] = oldWords[index];
                expected[cell] = newWords[index];
                expected[independentPreviousStart + index] = oldWords[index];
            }
            for (int cell = 0; cell < ContendedCellCount; ++cell)
                initial[contendedCellStart + cell * CellStride] = CasExpected;

            GPUCommandBuffer commands = pool.BeginCommands();
            commands.CopyMemory(upload.Range.GPURange.Slice(0, bufferBytes), storageRange);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
            commands.BindPipeline(pipeline);
            commands.Dispatch(upload.Range.GPURange.Slice(rootOffset, (ulong)rootBytes), new Extent3D(WorkgroupCount, 1, 1));
            commands.Barrier(Stage.Compute | Stage.Transfer, Access.ShaderWrite | Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            commands.CopyMemory(storageRange, readback.Range.GPURange);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            completion.Wait();

            ReadOnlySpan<uint> actual = MemoryMarshal.Cast<byte, uint>(readback.Range.CpuSpan[..bufferBytes]);
            for (int cell = 0; cell < ContendedCellCount; ++cell)
            {
                int slot = contendedCellStart + cell * CellStride;
                uint claim = actual[slot];
                Assert.That(claim, Is.InRange(1u, (uint)ThreadCount), $"Cell {cell} must have a winner.");
                int winner = (int)claim - 1;
                Assert.That(winner % ContendedCellCount, Is.EqualTo(cell), $"Winner {winner} must have attempted cell {cell}.");
                expected[slot] = claim;
                for (int thread = cell; thread < ThreadCount; thread += ContendedCellCount)
                    expected[contendedPreviousStart + thread] = thread == winner ? CasExpected : claim;
            }
            Assert.That(actual.ToArray(), Is.EqualTo(expected),
                "All updated cells and adjacent guards must match; per contended cell, only the winning thread may observe the initial value.");
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            pipeline?.Destroy();
            readback.Destroy();
            storage.Destroy();
            upload.Destroy();
            device.Destroy();
        }
    }

    private static GPUDevice CreateHeadlessDeviceOrIgnore(GraphicsBackend backend)
    {
        try
        {
            return Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = true, TimestampQueryCount = 0 });
        }
        catch (GPUException exception) when (exception.Error == Error.Unsupported)
        {
            Assert.Ignore($"{backend} is unavailable: {exception.Message}");
            throw;
        }
    }

    private static CompiledShader CompileAtomics(ShaderTarget target)
    {
        using ShaderCompiler compiler = new(target, new ShaderCompilerOptions { SearchPaths = [] });
        return compiler.CompileSource(ModuleName, ModuleName + ".slang", AtomicSource, ShaderPipelineKind.Compute);
    }

    private static void AssertSpirvAtomics(ReadOnlySpan<byte> code)
    {
        const int headerWords = 5;
        Assert.That(code.Length, Is.GreaterThanOrEqualTo(headerWords * sizeof(uint)));
        Assert.That(code.Length % sizeof(uint), Is.Zero);
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(code);
        Assert.That(words[0], Is.EqualTo(0x0723_0203u));
        HashSet<uint> opcodes = [];
        for (int index = headerWords; index < words.Length;)
        {
            uint instruction = words[index];
            int wordCount = (int)(instruction >> 16);
            Assert.That(wordCount, Is.InRange(1, words.Length - index), $"Invalid SPIR-V instruction at word {index}.");
            opcodes.Add(instruction & 0xffffu);
            index += wordCount;
        }

        ReadOnlySpan<(uint Opcode, string Name)> required =
        [
            (229, "OpAtomicExchange"), (230, "OpAtomicCompareExchange"), (234, "OpAtomicIAdd"),
            (237, "OpAtomicUMin"), (239, "OpAtomicUMax"), (241, "OpAtomicOr"),
        ];
        foreach ((uint opcode, string name) in required)
            Assert.That(opcodes.Contains(opcode), Is.True, $"Missing {name}.");
        Assert.That(opcodes.Contains(231), Is.False, "Unexpected OpAtomicCompareExchangeWeak.");
    }

    private static void AssertMetalAtomics(string source)
    {
        ReadOnlySpan<string> intrinsics =
        [
            "atomic_fetch_add_explicit", "atomic_fetch_max_explicit", "atomic_fetch_min_explicit",
            "atomic_fetch_or_explicit", "atomic_exchange_explicit",
        ];
        foreach (string intrinsic in intrinsics)
            Assert.That(source, Does.Contain(intrinsic + "("));

        Assert.That(source, Does.Match(@"\bdevice\s+atomic_uint\s*\*"));
        Assert.That(source, Does.Match(
            @"while\s*\(\s*!atomic_compare_exchange_weak_explicit\s*\([^)]*,\s*memory_order_relaxed,\s*memory_order_relaxed\s*\)\s*&&\s*observed\s*==\s*expected\s*\)"));
        Assert.That(source, Does.Contain("return observed;"));
        Assert.That(Regex.Matches(source, @"\balco_atomic_cas_u32\s*\(").Count, Is.GreaterThan(1), "Expected a call to the strong CAS helper.");
    }
}
