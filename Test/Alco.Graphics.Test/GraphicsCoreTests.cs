using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>Core low-level GPU, utility allocator, Slang compiler, and backend regression tests.</summary>
[TestFixture]
[NonParallelizable]
[Category("Core")]
public sealed class GraphicsCoreTests
{
    internal static void RunChecks(Action<Action<bool, string>> body)
    {
        List<string> failures = [];
        int passed = 0;

        void Check(bool condition, string name)
        {
            if (condition)
            {
                passed++;
            }
            else
            {
                failures.Add(name);
                TestContext.Progress.WriteLine($"  [FAIL] {name}");
            }
        }

        body(Check);
        TestContext.Progress.WriteLine($"Checks: {passed} passed, {failures.Count} failed.");
        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} check(s) failed:\n  - " + string.Join("\n  - ", failures));
    }

    private static bool IsSpirv(ReadOnlySpan<byte> code)
    {
        return code.Length >= 20 && code.Length % 4 == 0 &&
            code[0] == 0x03 && code[1] == 0x02 && code[2] == 0x23 && code[3] == 0x07;
    }

    /// <summary>Runs CPU-only, binding, range, and pipeline-cache checks.</summary>
    [Test]
    public void CpuAndBindings_PassAllChecks()
    {
        RunChecks(check =>
        {
            RegressionTests.RunCpu(check);
            VulkanPipelineCacheTests.Run(check);
            VulkanBindingTests.Run(check);
            D3D12BindingTests.Run(check);
            MetalBindingTests.Run(check);
            GPURangeTests.Run(check);
            HeapPoolRangeTests.Run(check);
        });
    }

    /// <summary>Runs format metadata, synchronization flags, BumpAllocator, and HeapAllocator checks.</summary>
    [Test]
    public unsafe void FormatInfoAndAllocators_PassAllChecks()
    {
        RunChecks(check =>
        {
            TextureFormatInfo rgba8 = Gpu.GetTextureFormatInfo(Format.Rgba8Unorm);
            check(rgba8.BytesPerBlock == 4 && rgba8.BlockExtent.X == 1 && !rgba8.Depth && !rgba8.Stencil, "Rgba8Unorm is a 4-byte uncompressed format");
            TextureFormatInfo bc7 = Gpu.GetTextureFormatInfo(Format.Bc7Srgb);
            check(bc7.BytesPerBlock == 16 && bc7.BlockExtent.X == 4 && bc7.BlockExtent.Y == 4, "Bc7Srgb is a 16-byte 4x4 block format");
            TextureFormatInfo d32s8 = Gpu.GetTextureFormatInfo(Format.D32FloatS8Uint);
            check(d32s8.Depth && d32s8.Stencil && d32s8.BytesPerBlock == 8, "D32FloatS8Uint reports depth and stencil");
            ReadOnlySpan<Format> bcFormats = [Format.Bc1Srgb, Format.Bc1Unorm, Format.Bc2Srgb, Format.Bc2Unorm, Format.Bc4Unorm, Format.Bc4Snorm];
            foreach (Format format in bcFormats)
            {
                TextureFormatInfo info = Gpu.GetTextureFormatInfo(format);
                uint bytesPerBlock = format is Format.Bc2Srgb or Format.Bc2Unorm ? 16u : 8u;
                check(info.BytesPerBlock == bytesPerBlock && info.BlockExtent.X == 4 && info.BlockExtent.Y == 4 && !info.Depth && !info.Stencil,
                    $"{format} is a {bytesPerBlock}-byte 4x4 color block format");
            }
            check((uint)Format.Bc3Srgb == 40 && (uint)Format.Bc7Unorm == 46 && (uint)Format.Bc1Srgb == 47 && (uint)Format.Undefined == 53,
                "new BC formats follow existing concrete values and precede the Undefined format-count sentinel");

            Stage stages = Stage.Indirect | Stage.Vertex | Stage.Mesh;
            check((stages & Stage.Mesh) != 0 && (stages & Stage.Compute) == 0, "Stage flags combine and test as bitmasks");

            {
                const ulong storageSize = 4096;
                byte* memory = (byte*)NativeMemory.AlignedAlloc((nuint)storageSize, 16);
                NativeMemory.Clear(memory, (nuint)storageSize);
                GPUAllocation<byte> storage = new() { Cpu = memory, Offset = 256, SizeBytes = storageSize };
                BumpAllocator bump = new(storage);
                GPUAllocation<byte> a = bump.Allocate(1);
                GPUAllocation<byte> b = bump.Allocate(31);
                GPUAllocation<byte> c = bump.AllocateAtomic(16);
                check((ulong)(a.Cpu - memory) == 0, "first allocation starts at storage base");
                check((ulong)(b.Cpu - memory) == 16, "second allocation starts 16-byte aligned after the first");
                check((ulong)(c.Cpu - memory) == 48, "atomic allocation continues at the next 16-byte boundary");
                check(a.Buffer == null && a.Offset == 256 && a.SizeBytes == 1 && b.Buffer == null && b.Offset == 272 && b.SizeBytes == 31 &&
                    c.Buffer == null && c.Offset == 304 && c.SizeBytes == 16, "ordinary and atomic CPU bump allocations preserve aligned byte metadata");
                check(a.View.Equals(default(GPUBufferView)) && b.View.Equals(default(GPUBufferView)) && c.View.Equals(default(GPUBufferView)) &&
                    a.GPURange.Equals(default(GPURange)) && b.GPURange.Equals(default(GPURange)) && c.GPURange.Equals(default(GPURange)),
                    "CPU-only bump storage exposes neither shader views nor GPU command ranges despite nonzero offsets and sizes");
                GPUAllocation<uint> typed = bump.Allocate<uint>(3);
                GPUAllocation<System.Numerics.Vector4> atomicTyped = bump.AllocateAtomic<System.Numerics.Vector4>(2);
                check(typed.Buffer == null && typed.Offset == 320 && typed.SizeBytes == 12 &&
                    atomicTyped.Buffer == null && atomicTyped.Offset == 336 && atomicTyped.SizeBytes == 32,
                    "generic ordinary/atomic CPU bump allocations preserve byte offsets without scaling them by element size");
                GPUAllocation<byte> exhausted = bump.Allocate(storageSize);
                check(exhausted.Cpu == null && exhausted.Buffer == null && exhausted.Offset == 0 && exhausted.SizeBytes == 0 &&
                    exhausted.View.Equals(default(GPUBufferView)) && exhausted.GPURange.Equals(default(GPURange)),
                    "exhausted storage reports empty metadata, range, and view");
                bump.Reset();
                GPUAllocation<byte> afterReset = bump.Allocate(8);
                check((ulong)(afterReset.Cpu - memory) == 0 && afterReset.Buffer == null && afterReset.Offset == 256 && afterReset.SizeBytes == 8,
                    "reset returns CPU allocations and byte metadata to the storage base");
                for (int iteration = 0; iteration < 128; ++iteration)
                {
                    bump.Reset();
                    bump.Allocate<uint>();
                    bump.AllocateAtomic<ulong>();
                }
                bool viewsValid = true;
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 2048; ++iteration)
                {
                    bump.Reset();
                    GPUAllocation<uint> ordinary = bump.Allocate<uint>();
                    GPUAllocation<ulong> atomic = bump.AllocateAtomic<ulong>();
                    viewsValid &= ordinary.Buffer == null && ordinary.Offset == 256 && ordinary.SizeBytes == sizeof(uint) &&
                        atomic.Buffer == null && atomic.Offset == 272 && atomic.SizeBytes == sizeof(ulong) &&
                        ordinary.View.Index == 0 && ordinary.View.Offset == 0 && ordinary.GPURange.Buffer == null && ordinary.GPURange.Size == 0 &&
                        atomic.View.Index == 0 && atomic.View.Offset == 0 && atomic.GPURange.Buffer == null && atomic.GPURange.Size == 0;
                }
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                check(viewsValid && allocatedBytes == 0, $"warmed ordinary/atomic typed view allocation performs no managed allocations ({allocatedBytes} bytes)");
                NativeMemory.AlignedFree(memory);
            }

            {
                const ulong storageSize = 1 << 20;
                byte* memory = (byte*)NativeMemory.AlignedAlloc((nuint)storageSize, 16);
                NativeMemory.Clear(memory, (nuint)storageSize);
                GPUAllocation<byte> storage = new() { Cpu = memory, Offset = 512, SizeBytes = storageSize };
                HeapAllocator heap = new(storage, 64);
                HeapAllocation<byte> first = heap.Allocate(100);
                HeapAllocation<byte> second = heap.Allocate(100);
                check(first.Range.Cpu != null && second.Range.Cpu != null && first.Range.Cpu != second.Range.Cpu, "two allocations return disjoint ranges");
                ulong offsetSecond = (ulong)(second.Range.Cpu - memory);
                check(offsetSecond % 16 == 0, "allocations are 16-byte aligned");
                check(first.Range.Buffer == null && first.Range.Offset == 512 && first.Range.SizeBytes == 100 && second.Range.Buffer == null &&
                    second.Range.Offset == 512 + offsetSecond && second.Range.SizeBytes == 100, "CPU heap allocations preserve aligned byte metadata");
                check(first.Range.View.Equals(default(GPUBufferView)) && second.Range.View.Equals(default(GPUBufferView)) &&
                    first.Range.GPURange.Equals(default(GPURange)) && second.Range.GPURange.Equals(default(GPURange)),
                    "CPU-only heap storage exposes neither shader views nor GPU command ranges despite nonzero offsets and sizes");
                HeapAllocation<uint> typed = heap.Allocate<uint>(3);
                check(typed.Range.Buffer == null && typed.Range.Offset == 512 + (ulong)((byte*)typed.Range.Cpu - memory) &&
                    typed.Range.SizeBytes == 12 && typed.Range.View.Equals(default(GPUBufferView)) && typed.Range.GPURange.Equals(default(GPURange)),
                    "generic CPU heap allocations preserve byte offsets and exact element byte sizes without inventing a GPU range");
                heap.Free(first);
                HeapAllocation<byte> reused = heap.Allocate(100);
                check(reused.Range.Cpu == first.Range.Cpu && reused.Range.Buffer == null && reused.Range.Offset == first.Range.Offset &&
                    reused.Range.SizeBytes == first.Range.SizeBytes, "freed CPU ranges reuse their mappings and byte metadata");
                heap.Reset();
                HeapAllocation<byte> afterReset = heap.Allocate(64);
                check((ulong)(afterReset.Range.Cpu - memory) == 0 && afterReset.Range.Buffer == null && afterReset.Range.Offset == 512 &&
                    afterReset.Range.SizeBytes == 64, "reset restarts CPU allocation and byte metadata from the storage base");
                NativeMemory.AlignedFree(memory);
            }
        });
    }

    /// <summary>Runs Slang compilation, reflection, macro isolation, and pin-leak checks.</summary>
    [Test]
    public void SlangCompilerAndMacros_PassAllChecks()
    {
        RunChecks(check =>
        {
            RegressionTests.RunSlang(check);

            ShaderCompilerOptions options = new()
            {
                SearchPaths = [AppContext.BaseDirectory],
                Defines =
                [
                    ("TEST_NUMBER", "37"),
                    ("TEST_ALIAS", "TEST_NUMBER"),
                    ("TEST_INCLUDE", "\"préprocesseur_宏.h\""),
                ],
                FileResolver = (string path) => path.Replace('\\', '/').EndsWith("préprocesseur_宏.h", StringComparison.Ordinal) ? "#define TEST_INCLUDED 73\n" : null,
            };
            check(ShaderCompilerOptions.Default.Defines.Count == 0, "default shader compiler options define no preprocessor macros");
            try
            {
                using ShaderCompiler compiler = new(ShaderTarget.Spirv, options);
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                CompiledShader compiled = compiler.CompileSource("test_preprocessor", "test_preprocessor.slang", """
                    #if TEST_NUMBER != 37 || TEST_ALIAS != 37
                    #error Session macro names or values were not preserved
                    #endif
                    #include TEST_INCLUDE
                    #if TEST_INCLUDED != 73
                    #error The UTF-8 macro-valued include was not resolved
                    #endif
                    [shader("compute")]
                    [numthreads(1,1,1)]
                    void computeMain() {}
                    """, ShaderPipelineKind.Compute);
                check(IsSpirv(compiled.GetStage(ShaderStageKind.Compute).Code.Span),
                    "compiler macro names, values, expansion and UTF-8 includes survive session creation and compacting GC");
            }
            catch (ShaderCompilationException exception)
            {
                check(false, $"Slang preprocessor macro regression: {exception.Message}");
            }
            try
            {
                using ShaderCompiler isolatedBase = new(ShaderTarget.Spirv);
                isolatedBase.CompileSource("test_per_call_macros", "test_per_call_macros.slang", """
                    #if PER_CALL_MACRO != 19
                    #error Per-call macro was not applied
                    #endif
                    [shader("compute")]
                    [numthreads(1,1,1)]
                    void computeMain() {}
                    """, ShaderPipelineKind.Compute, new ShaderCompileOptions { Defines = [("PER_CALL_MACRO", "19")] });

                CompiledShader clean = isolatedBase.CompileSource("test_preprocessor_scope", "test_preprocessor_scope.slang", """
                    #if defined(TEST_NUMBER) || defined(TEST_ALIAS) || defined(TEST_INCLUDE) || defined(PER_CALL_MACRO)
                    #error Preprocessor macros leaked from another session
                    #endif
                    [shader("compute")]
                    [numthreads(1,1,1)]
                    void computeMain() {}
                    """, ShaderPipelineKind.Compute);
                check(IsSpirv(clean.GetStage(ShaderStageKind.Compute).Code.Span),
                    "preprocessor macros do not leak across compilers or pooled per-define sessions");
            }
            catch (ShaderCompilationException exception)
            {
                check(false, $"Slang preprocessor macro isolation regression: {exception.Message}");
            }
            const string pinProbeSource = "[shader(\"compute\")] [numthreads(1,1,1)] void computeMain() {}";
            for (int iteration = 0; iteration < 2; ++iteration)
            {
                using ShaderCompiler warmup = new(ShaderTarget.Spirv, options);
                warmup.LoadModuleFromSource("pin_warmup", "pin_warmup.slang", pinProbeSource);
            }
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            long before = GC.GetGCMemoryInfo().PinnedObjectsCount;
            for (int iteration = 0; iteration < 16; ++iteration)
            {
                using ShaderCompiler compiler = new(ShaderTarget.Spirv, options);
                compiler.LoadModuleFromSource("pin_probe", "pin_probe.slang", pinProbeSource);
            }
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            long after = GC.GetGCMemoryInfo().PinnedObjectsCount;
            check(after <= before + 2, $"disposed macro compilers do not retain UTF-8 name/value pins (before={before}, after={after})");

            ShaderCompilerTests.Run(check);
        });
    }

    /// <summary>Runs the full portable GPU regression suite against the VulkanCompact backend.</summary>
    [Test]
    public void VulkanCompact_FullGpuSuite_PassesAllChecks()
    {
        RunGpuSuiteForBackend(GraphicsBackend.VulkanCompact);
    }

    /// <summary>Runs the full portable GPU regression suite against the platform default backend (Auto).</summary>
    [Test]
    public void DefaultBackend_FullGpuSuite_PassesAllChecks()
    {
        RunGpuSuiteForBackend(GraphicsBackend.Auto);
    }

    /// <summary>Runs the full portable GPU regression suite against full Vulkan when supported by the host driver.</summary>
    [Test]
    public void VulkanFull_FullGpuSuite_PassesAllChecks()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            Assert.Ignore("Vulkan full backend is only supported on Windows/Linux.");
        RunGpuSuiteForBackend(GraphicsBackend.Vulkan);
    }

    private static unsafe void RunGpuSuiteForBackend(GraphicsBackend backend)
    {
        GPUDevice? device;
        try
        {
            device = Gpu.CreateDevice(new DeviceDesc { Backend = backend });
        }
        catch (GPUException ex) when (ex.Error == Error.Unsupported)
        {
            Assert.Ignore($"Backend {backend} is unsupported on this host: {ex.Message}");
            return;
        }

        RunChecks(check =>
        {
            try
            {
                DeviceCaps caps = device.Caps;
                check(caps.QueueCount >= 1 && caps.GeneralQueueCount >= 1, "device exposes at least one general queue");
                check(caps.MaxPushDataSize >= 128, "descriptor heap reports a sane push data size");
                if (device.ActiveBackend == GraphicsBackend.VulkanCompact)
                {
                    check(!caps.IndirectMeshDraw, $"{device.ActiveBackend} reports no indirect mesh drawing");
                    check(!caps.StorageInputOutput16, $"{device.ActiveBackend} reports no native 16-bit shader input/output");
                }

                RegressionTests.RunGpu(device, check);
                HeapPoolGpuTests.Run(device, check);
                ReadbackQueueTests.Run(device, check);
                MultisampleTests.Run(device, check);

                GPUHeap heap = device.CreateGPUHeap(1 << 20, MemoryType.CpuVisible);
                try
                {
                    check(heap.Range.Cpu != null && ReferenceEquals(heap.Range.Buffer, heap.Owner) && heap.Range.SizeBytes == (1 << 20),
                        "cpu-visible GPU heap retains its mapping and backing-buffer identity");
                    heap.Range.CpuSpan[..8].Fill(0x11);
                }
                finally
                {
                    device.WaitIdle();
                    heap.Destroy();
                }
            }
            finally
            {
                device.WaitIdle();
                device.Destroy();
            }
        });
    }
}
