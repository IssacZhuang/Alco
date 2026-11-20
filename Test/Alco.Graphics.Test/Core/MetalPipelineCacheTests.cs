// Metal shader-library reuse, bounded eviction, and opt-in Metal 4 archive persistence regressions.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Mtl = Metal;

internal static unsafe class MetalPipelineCacheTests
{
    private const string CacheVariable = "ALCO_GRAPHICS_METAL_PIPELINE_CACHE";
    private const int ByteCapacity = 8 * 1024 * 1024;
    private const string Source = """
        #include <metal_stdlib>
        using namespace metal;
        struct CacheRoot { uint2 Output; uint Value; uint Padding; };
        kernel void cacheMain(constant CacheRoot& root [[buffer(0)]], device const ulong* buffers [[buffer(3)]])
        {
            device uint* output = reinterpret_cast<device uint*>(buffers[root.Output.x] + ulong(root.Output.y));
            output[0] = root.Value + 1u;
        }
        kernel void cacheOther(constant CacheRoot& root [[buffer(0)]], device const ulong* buffers [[buffer(3)]])
        {
            device uint* output = reinterpret_cast<device uint*>(buffers[root.Output.x] + ulong(root.Output.y));
            output[0] = root.Value + 9u;
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct CacheRoot
    {
        internal GPUBufferView Output;
        internal uint Value;
        internal uint Padding;
    }

    internal static void Run(Action<bool, string> check)
    {
        using Mtl.NSAutoreleasePool pool = new();
        Console.WriteLine("Metal pipeline caches:");
        string? previousDirectory = Environment.GetEnvironmentVariable(CacheVariable);
        FieldInfo callback = typeof(Alco.Graphics.LowLevel.Gpu).GetField("ErrorCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
        ErrorCallback? previousCallback = (ErrorCallback?)callback.GetValue(null);
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Alco.Graphics.MetalCache-");
        try
        {
            Environment.SetEnvironmentVariable(CacheVariable, null);
            RunLibraries(check);
            RunByteBudget(check);
            check(temporary.GetFileSystemInfos().Length == 0, "disabled Metal archive caching creates no files");
            RunArchives(temporary.FullName, check);
        }
        finally
        {
            Gpu.SetErrorCallback(previousCallback);
            Environment.SetEnvironmentVariable(CacheVariable, previousDirectory);
            temporary.Delete(true);
        }
    }

    private static void RunLibraries(Action<bool, string> check)
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc());
        try
        {
            check(Field<Mtl.MTL4PipelineDataSetSerializer>(device, "PipelineSerializer").NativePtr == 0 &&
                Field<Mtl.MTL4Archive>(device, "PipelineArchive").NativePtr == 0 &&
                Field<Mtl.MTL4CompilerTaskOptions>(device, "PipelineTaskOptions").NativePtr == 0 &&
                Field<string?>(device, "PipelineCachePath") == null && !Field<bool>(device, "PipelineCacheDirty"),
                "unset archive directory leaves the serializer, archive, lookup options, and path disabled");
            RunConcurrent(device, check);
            RunReuse(device, check);
            RunEviction(device, check);
            check(!Field<bool>(device, "PipelineCacheDirty") && Field<string?>(device, "PipelineCachePath") == null,
                "library and pipeline creation do not enable disabled archive persistence");
        }
        finally
        {
            device.WaitIdle();
            device.Destroy();
        }
    }

    private static void RunConcurrent(GPUDevice device, Action<bool, string> check)
    {
        const int count = 4;
        byte[] code = CommentedSource(90);
        using Barrier gate = new(count);
        Thread[] threads = new Thread[count];
        GPUPipeline?[] pipelines = new GPUPipeline?[count];
        GPUException?[] failures = new GPUException?[count];
        try
        {
            for (int index = 0; index < count; ++index)
            {
                int worker = index;
                threads[index] = new Thread(() =>
                {
                    byte[] copy = (byte[])code.Clone();
                    gate.SignalAndWait();
                    try
                    {
                        pipelines[worker] = device.CreateComputePipeline(new ShaderStage { Code = copy, EntryPoint = "cacheMain" });
                    }
                    catch (GPUException exception)
                    {
                        failures[worker] = exception;
                    }
                });
                threads[index].Start();
            }
            for (int index = 0; index < count; ++index) threads[index].Join();
            for (int index = 0; index < count; ++index)
            {
                GPUPipeline? pipeline = pipelines[index];
                bool independent = pipeline != null && failures[index] == null;
                for (int previous = 0; previous < index; ++previous) independent &= !ReferenceEquals(pipeline, pipelines[previous]);
                check(independent && pipeline != null && Field<Mtl.MTLComputePipelineState>(pipeline, "Compute").NativePtr != 0,
                    $"concurrent first-use pipeline {index} has an independent live handle ({failures[index]?.Message ?? "success"})");
            }
            check(CacheMatches(device, 1, code.Length), "concurrent first use publishes one retained library entry, not one entry per compilation");
            if (pipelines[0] != null)
            {
                check(Execute(device, pipelines[0]!, 20) == 21, "a concurrently created pipeline executes before sibling destruction");
                pipelines[0]!.Destroy();
                pipelines[0] = null;
            }
            for (int index = 1; index < count; ++index)
                if (pipelines[index] != null)
                    check(Execute(device, pipelines[index]!, (uint)(30 + index)) == (uint)(31 + index),
                        $"concurrent pipeline {index} still executes after another handle is destroyed");
        }
        finally
        {
            device.WaitIdle();
            for (int index = 0; index < count; ++index) pipelines[index]?.Destroy();
        }
    }

    private static void RunReuse(GPUDevice device, Action<bool, string> check)
    {
        byte[] caller = Encoding.UTF8.GetBytes(Source);
        byte[] identical = Encoding.UTF8.GetBytes(Source);
        GPUPipeline? first = null;
        GPUPipeline? reused = null;
        GPUPipeline? other = null;
        GPUPipeline? collision = null;
        try
        {
            first = device.CreateComputePipeline(new ShaderStage { Code = caller, EntryPoint = "cacheMain" });
            check(Execute(device, first, 40) == 41, "the original MSL pipeline produces the expected integer before library reuse");
            Array entries = Field<Array>(device, "ShaderLibraries");
            int slot = FindLibrary(entries, identical);
            check(slot >= 0, "compiled MSL source is retained by content");
            if (slot < 0) return;
            object entry = entries.GetValue(slot)!;
            nint library = Field<Mtl.MTLLibrary>(entry, "Library").NativePtr;
            byte* stored = (byte*)Pointer.Unbox(Field<object>(entry, "Code"));
            fixed (byte* original = caller)
                check(stored != original && new ReadOnlySpan<byte>(stored, caller.Length).SequenceEqual(identical),
                    "the library cache owns a native copy rather than retaining the caller's source buffer");
            int bytes = Field<int>(device, "ShaderLibraryBytes");
            int mutation = Source.IndexOf("root.Value + 1u", StringComparison.Ordinal) + "root.Value + ".Length;
            caller[mutation] = (byte)'2';
            check(new ReadOnlySpan<byte>(stored, identical.Length).SequenceEqual(identical), "caller mutation cannot change the stored source key");
            reused = device.CreateComputePipeline(new ShaderStage { Code = identical, EntryPoint = "cacheMain" });
            other = device.CreateComputePipeline(new ShaderStage { Code = (byte[])identical.Clone(), EntryPoint = "cacheOther" });
            check(CacheMatches(device, 2, bytes) && FindLibrary(entries, identical) == slot &&
                Field<Mtl.MTLLibrary>(entries.GetValue(slot)!, "Library").NativePtr == library,
                "different byte arrays and entrypoints in one MSL source reuse the same retained library");
            check(Execute(device, reused, 50) == 51 && Execute(device, other, 50) == 59,
                "content reuse preserves both entrypoints' distinct integer results after caller mutation");

            HashCode hash = new();
            hash.AddBytes(caller);
            FieldInfo hashField = entry.GetType().GetField("Hash", BindingFlags.Instance | BindingFlags.NonPublic)!;
            int previousHash = Field<int>(entry, "Hash");
            try
            {
                hashField.SetValue(entry, hash.ToHashCode());
                entries.SetValue(entry, slot);
                collision = device.CreateComputePipeline(new ShaderStage { Code = caller, EntryPoint = "cacheMain" });
                int collisionSlot = FindLibrary(entries, caller);
                check(caller.Length == identical.Length && collisionSlot >= 0 && collisionSlot != slot &&
                    CacheMatches(device, 3, bytes + caller.Length), "same-size sources with a forced hash collision retain separate content keys");
                check(Execute(device, collision, 60) == 62 && Execute(device, first, 60) == 61,
                    "hash collisions cannot alias the changed shader or alter an existing pipeline's execution");
            }
            finally
            {
                hashField.SetValue(entry, previousHash);
                entries.SetValue(entry, slot);
            }
        }
        finally
        {
            device.WaitIdle();
            collision?.Destroy();
            other?.Destroy();
            reused?.Destroy();
            first?.Destroy();
        }
    }

    private static void RunEviction(GPUDevice device, Action<bool, string> check)
    {
        byte[] original = Encoding.UTF8.GetBytes(Source);
        byte[] oldest = CommentedSource(0);
        GPUPipeline retained = device.CreateComputePipeline(new ShaderStage { Code = original, EntryPoint = "cacheMain" });
        try
        {
            check(Execute(device, retained, 70) == 71, "the retained pipeline executes before its library is evicted");
            for (int index = 0; index < 64; ++index)
            {
                GPUPipeline pipeline = device.CreateComputePipeline(new ShaderStage { Code = CommentedSource(index), EntryPoint = "cacheMain" });
                pipeline.Destroy();
            }
            Array entries = Field<Array>(device, "ShaderLibraries");
            check(CacheMatches(device, 64, 64 * oldest.Length) && FindLibrary(entries, original) < 0 && FindLibrary(entries, oldest) >= 0,
                "64 newer source keys evict prior libraries without growing the 64-entry cache");
            GPUPipeline touched = device.CreateComputePipeline(new ShaderStage { Code = oldest, EntryPoint = "cacheMain" });
            touched.Destroy();
            byte[] newest = CommentedSource(64);
            GPUPipeline replacement = device.CreateComputePipeline(new ShaderStage { Code = newest, EntryPoint = "cacheMain" });
            replacement.Destroy();
            check(CacheMatches(device, 64, 64 * oldest.Length) && FindLibrary(entries, oldest) < 0 &&
                FindLibrary(entries, CommentedSource(1)) >= 0 && FindLibrary(entries, newest) >= 0,
                "entry eviction is FIFO even when the oldest library was just reused");
            check(Execute(device, retained, 80) == 81, "an old pipeline still executes correctly after its source library is evicted");
        }
        finally
        {
            device.WaitIdle();
            retained.Destroy();
        }
    }

    private static void RunByteBudget(Action<bool, string> check)
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc());
        GPUPipeline? oversized = null;
        try
        {
            byte[] first = CommentedSource(70, ByteCapacity / 2);
            byte[] second = CommentedSource(71, ByteCapacity / 2);
            byte[] third = CommentedSource(72, ByteCapacity / 2);
            GPUPipeline pipeline = device.CreateComputePipeline(new ShaderStage { Code = first, EntryPoint = "cacheMain" });
            pipeline.Destroy();
            pipeline = device.CreateComputePipeline(new ShaderStage { Code = second, EntryPoint = "cacheMain" });
            pipeline.Destroy();
            check(CacheMatches(device, 2, ByteCapacity), "two 4 MiB source keys exactly fill the 8 MiB native code budget");
            pipeline = device.CreateComputePipeline(new ShaderStage { Code = third, EntryPoint = "cacheMain" });
            pipeline.Destroy();
            Array entries = Field<Array>(device, "ShaderLibraries");
            const int count = 2;
            int bytes = ByteCapacity;
            int thirdSlot = FindLibrary(entries, third);
            check(CacheMatches(device, count, bytes) && FindLibrary(entries, first) < 0 && FindLibrary(entries, second) >= 0 && thirdSlot >= 0,
                "byte pressure evicts older native source copies and never exceeds 8 MiB");
            if (thirdSlot < 0) return;
            nint library = Field<Mtl.MTLLibrary>(entries.GetValue(thirdSlot)!, "Library").NativePtr;
            int next = Field<int>(device, "NextShaderLibrary");
            byte[] code = CommentedSource(73, ByteCapacity + 1);
            oversized = device.CreateComputePipeline(new ShaderStage { Code = code, EntryPoint = "cacheMain" });
            check(CacheMatches(device, count, bytes) && FindLibrary(entries, code) < 0 && Field<int>(device, "NextShaderLibrary") == next &&
                Field<Mtl.MTLLibrary>(entries.GetValue(thirdSlot)!, "Library").NativePtr == library,
                "a source larger than 8 MiB bypasses caching without evicting or advancing existing entries");
            check(Execute(device, oversized, 90) == 91, "an oversized uncached shader still compiles and executes correctly");
        }
        finally
        {
            device.WaitIdle();
            oversized?.Destroy();
            device.Destroy();
        }
    }

    private static void RunArchives(string temporary, Action<bool, string> check)
    {
        string directory = Path.Combine(temporary, "archives");
        Environment.SetEnvironmentVariable(CacheVariable, directory);
        byte[] code = Encoding.UTF8.GetBytes(Source);
        string path = string.Empty;
        GPUDevice cold = Gpu.CreateDevice(new DeviceDesc());
        GPUPipeline? pipeline = null;
        try
        {
            path = Field<string?>(cold, "PipelineCachePath") ?? string.Empty;
            Mtl.MTL4PipelineDataSetSerializer serializer = Field<Mtl.MTL4PipelineDataSetSerializer>(cold, "PipelineSerializer");
            check(path.Length != 0 && serializer.NativePtr != 0 &&
                Field<Mtl.MTL4Compiler>(cold, "MtlCompiler").PipelineDataSetSerializer.NativePtr == serializer.NativePtr &&
                Field<Mtl.MTL4Archive>(cold, "PipelineArchive").NativePtr == 0 &&
                Field<Mtl.MTL4CompilerTaskOptions>(cold, "PipelineTaskOptions").NativePtr == 0 && !Directory.Exists(directory),
                "cold archive configuration attaches a serializer without loading an archive or writing the directory");
            byte* system = stackalloc byte[256];
            nuint length = 256;
            fixed (byte* name = "kern.osversion\0"u8)
            {
                int status = SysctlByName(name, system, &length, null, 0);
                string build = status == 0 && length > 1 ? Encoding.UTF8.GetString(new ReadOnlySpan<byte>(system, (int)length - 1)) : string.Empty;
                Mtl.MTLDevice native = Field<Mtl.MTLDevice>(cold, "MtlDevice");
                string model = Convert.ToHexString(Encoding.UTF8.GetBytes($"{native.Name}"));
                check(build.Length != 0 && path == Path.Combine(directory, $"metal4-v2-{native.RegistryID:x16}-{model}-{build}.metallib"),
                    "archive filenames partition the format version, GPU identity, and actual kern.osversion build");
            }
            pipeline = cold.CreateComputePipeline(new ShaderStage { Code = code, EntryPoint = "cacheMain" });
            check(Execute(cold, pipeline, 100) == 101 && Field<bool>(cold, "PipelineCacheDirty") && !Directory.Exists(directory),
                "cold pipelines execute and mark captured binaries dirty without saving before Destroy");
        }
        finally
        {
            cold.WaitIdle();
            pipeline?.Destroy();
            pipeline = null;
            cold.Destroy();
        }
        check(File.Exists(path) && new FileInfo(path).Length > 0 && File.Exists(path + ".sha256") &&
            Directory.GetFileSystemEntries(directory).Length == 2, "Destroy saves an archive and its checksum without temporary snapshot files");
        if (!File.Exists(path)) return;
        byte[] saved = File.ReadAllBytes(path);
        string sentinel = Path.Combine(directory, "keep.bin");
        byte[] sentinelBytes = "unrelated cache-directory sentinel"u8.ToArray();
        File.WriteAllBytes(sentinel, sentinelBytes);

        GPUDevice warm = Gpu.CreateDevice(new DeviceDesc());
        GPUPipeline? miss = null;
        try
        {
            Mtl.MTL4Archive archive = Field<Mtl.MTL4Archive>(warm, "PipelineArchive");
            Mtl.MTL4CompilerTaskOptions options = Field<Mtl.MTL4CompilerTaskOptions>(warm, "PipelineTaskOptions");
            check(Field<string?>(warm, "PipelineCachePath") == path && archive.NativePtr != 0 && options.NativePtr != 0 &&
                options.LookupArchives.Count == 1 && options.LookupArchives.Object(0) == archive.NativePtr && !Field<bool>(warm, "PipelineCacheDirty"),
                "a warm device loads the archive and configures compiler task lookup archives");
            pipeline = warm.CreateComputePipeline(new ShaderStage { Code = code, EntryPoint = "cacheMain" });
            check(Execute(warm, pipeline, 110) == 111, "the warm archived pipeline preserves integer execution");
            CheckArchiveHit(warm, code, check);
            byte[] changed = Encoding.UTF8.GetBytes(Source.Replace("root.Value + 1u", "root.Value + 7u", StringComparison.Ordinal));
            miss = warm.CreateComputePipeline(new ShaderStage { Code = changed, EntryPoint = "cacheMain" });
            check(Execute(warm, miss, 120) == 127 && Field<bool>(warm, "PipelineCacheDirty"),
                "an archive-miss source compiles and executes while archive lookup remains configured");
            check(File.ReadAllBytes(path).AsSpan().SequenceEqual(saved), "warm pipeline compilation leaves the prior archive unchanged until Destroy");
        }
        finally
        {
            warm.WaitIdle();
            miss?.Destroy();
            pipeline?.Destroy();
            pipeline = null;
            warm.Destroy();
        }
        check(File.ReadAllBytes(sentinel).AsSpan().SequenceEqual(sentinelBytes) && Directory.GetFileSystemEntries(directory).Length == 3,
            "atomic archive replacement preserves unrelated files and removes its unique temporary file");

        byte[] corrupt = "not a Metal archive"u8.ToArray();
        File.WriteAllBytes(path, corrupt);
        int diagnostics = 0;
        bool nonfatalCacheDiagnostics = true;
        Gpu.SetErrorCallback((string message, bool fatal) =>
        {
            ++diagnostics;
            nonfatalCacheDiagnostics &= !fatal && message.Contains("pipeline cache", StringComparison.OrdinalIgnoreCase);
        });
        GPUDevice recovered = Gpu.CreateDevice(new DeviceDesc());
        try
        {
            check(Field<Mtl.MTL4Archive>(recovered, "PipelineArchive").NativePtr == 0 &&
                Field<Mtl.MTL4CompilerTaskOptions>(recovered, "PipelineTaskOptions").NativePtr == 0 &&
                Field<Mtl.MTL4PipelineDataSetSerializer>(recovered, "PipelineSerializer").NativePtr != 0,
                "a corrupted archive is rejected without disabling fresh binary capture");
            pipeline = recovered.CreateComputePipeline(new ShaderStage { Code = code, EntryPoint = "cacheMain" });
            check(Execute(recovered, pipeline, 130) == 131, "corrupted archive recovery still executes a newly compiled pipeline");
        }
        finally
        {
            recovered.WaitIdle();
            pipeline?.Destroy();
            pipeline = null;
            recovered.Destroy();
        }
        check(diagnostics > 0 && nonfatalCacheDiagnostics && !File.ReadAllBytes(path).AsSpan().SequenceEqual(corrupt) &&
            File.ReadAllBytes(sentinel).AsSpan().SequenceEqual(sentinelBytes) && Directory.GetFileSystemEntries(directory).Length == 3,
            "corruption reports a nonfatal cache diagnostic and replaces only the damaged archive");
        saved = File.ReadAllBytes(path);
        GPUDevice repaired = Gpu.CreateDevice(new DeviceDesc());
        try
        {
            check(Field<Mtl.MTL4Archive>(repaired, "PipelineArchive").NativePtr != 0 && !Field<bool>(repaired, "PipelineCacheDirty"),
                "the recovered archive loads successfully on the next device");
        }
        finally
        {
            repaired.WaitIdle();
            repaired.Destroy();
        }
        check(File.ReadAllBytes(path).AsSpan().SequenceEqual(saved), "Destroy without pipeline creation does not rewrite a clean archive");

        for (int variant = 0; variant < 3; variant++)
        {
            if (variant == 0) File.Delete(path + ".sha256");
            else File.WriteAllBytes(path + ".sha256", new byte[variant == 1 ? 1 : 32]);
            diagnostics = 0;
            nonfatalCacheDiagnostics = true;
            GPUDevice invalidChecksum = Gpu.CreateDevice(new DeviceDesc());
            try
            {
                check(Field<Mtl.MTL4Archive>(invalidChecksum, "PipelineArchive").NativePtr == 0 && diagnostics > 0 && nonfatalCacheDiagnostics,
                    $"missing, truncated, or mismatched checksum {variant} rejects the snapshot before native archive loading");
                pipeline = invalidChecksum.CreateComputePipeline(new ShaderStage { Code = code, EntryPoint = "cacheMain" });
                check(Execute(invalidChecksum, pipeline, 135) == 136, $"invalid checksum {variant} preserves normal pipeline compilation and execution");
            }
            finally
            {
                invalidChecksum.WaitIdle();
                pipeline?.Destroy();
                pipeline = null;
                invalidChecksum.Destroy();
            }
        }

        string blocked = Path.Combine(temporary, "blocked-directory");
        File.WriteAllBytes(blocked, sentinelBytes);
        Environment.SetEnvironmentVariable(CacheVariable, blocked);
        diagnostics = 0;
        nonfatalCacheDiagnostics = true;
        GPUDevice unwritable = Gpu.CreateDevice(new DeviceDesc());
        try
        {
            pipeline = unwritable.CreateComputePipeline(new ShaderStage { Code = code, EntryPoint = "cacheMain" });
            check(Execute(unwritable, pipeline, 140) == 141 && Field<bool>(unwritable, "PipelineCacheDirty"),
                "a regular file blocking the cache directory does not prevent pipeline creation or execution");
        }
        finally
        {
            unwritable.WaitIdle();
            pipeline?.Destroy();
            unwritable.Destroy();
        }
        check(diagnostics > 0 && nonfatalCacheDiagnostics && File.ReadAllBytes(blocked).AsSpan().SequenceEqual(sentinelBytes) &&
            File.ReadAllBytes(sentinel).AsSpan().SequenceEqual(sentinelBytes) && Directory.GetFileSystemEntries(temporary).Length == 2,
            "blocked-directory save failure remains nonfatal, preserves both sentinels, and leaves no temporary files");
    }

    private static void CheckArchiveHit(GPUDevice device, byte[] code, Action<bool, string> check)
    {
        using Mtl.NSAutoreleasePool pool = new();
        Array entries = Field<Array>(device, "ShaderLibraries");
        int slot = FindLibrary(entries, code);
        Mtl.MTL4Archive archive = Field<Mtl.MTL4Archive>(device, "PipelineArchive");
        if (slot < 0 || archive.NativePtr == 0)
        {
            check(false, "a warm archive hit requires the cached library and loaded archive");
            return;
        }
        using Mtl.MTL4LibraryFunctionDescriptor function = new()
        {
            Library = Field<Mtl.MTLLibrary>(entries.GetValue(slot)!, "Library"), Name = "cacheMain",
        };
        using Mtl.MTL4ComputePipelineDescriptor descriptor = new()
        {
            ComputeFunctionDescriptor = function,
            RequiredThreadsPerThreadgroup = new Mtl.MTLSize { width = 1, height = 1, depth = 1 },
        };
        Mtl.NSError error = default;
        using Mtl.MTLComputePipelineState state = archive.NewComputePipelineState(descriptor, ref error);
        check(state.NativePtr != 0,
            $"direct Metal 4 archive lookup hits the exact compute descriptor ({(error.NativePtr == 0 ? "success" : error.LocalizedDescription.ToString())})");
    }

    private static uint Execute(GPUDevice device, GPUPipeline pipeline, uint value)
    {
        GPUHeap roots = default;
        GPUHeap output = default;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            roots = device.CreateGPUHeap((ulong)sizeof(CacheRoot));
            output = device.CreateGPUHeap(sizeof(uint), MemoryType.GpuOnly);
            readback = device.CreateGPUHeap(sizeof(uint), MemoryType.Readback);
            CacheRoot root = new() { Output = output.Range.View, Value = value };
            *(CacheRoot*)roots.Range.Cpu = root;
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            GPUCommandBuffer commands = pool.BeginCommands();
            commands.BindPipeline(pipeline);
            commands.Dispatch(roots.Range.GPURange, new Extent3D(1, 1, 1));
            commands.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer, Access.TransferRead);
            commands.CopyMemory(output.Range.GPURange, readback.Range.GPURange);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            completion.Wait();
            return *(uint*)readback.Range.Cpu;
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            output.Destroy();
            roots.Destroy();
        }
    }

    private static byte[] CommentedSource(int tag, int size = 0)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(Source + $"\n/* cache {tag:D2} ");
        byte[] code = new byte[Math.Max(size, prefix.Length + 2)];
        prefix.CopyTo(code, 0);
        code.AsSpan(prefix.Length, code.Length - prefix.Length - 2).Fill((byte)'x');
        code[^2] = (byte)'*';
        code[^1] = (byte)'/';
        return code;
    }

    private static int FindLibrary(Array entries, ReadOnlySpan<byte> code)
    {
        for (int index = 0; index < entries.Length; ++index)
        {
            object entry = entries.GetValue(index)!;
            int size = Field<int>(entry, "Size");
            if (size == 0 || size != code.Length) continue;
            byte* stored = (byte*)Pointer.Unbox(Field<object>(entry, "Code"));
            if (stored != null && new ReadOnlySpan<byte>(stored, size).SequenceEqual(code)) return index;
        }
        return -1;
    }

    private static bool CacheMatches(GPUDevice device, int expectedCount, int expectedBytes)
    {
        Array entries = Field<Array>(device, "ShaderLibraries");
        int count = 0;
        int bytes = 0;
        for (int index = 0; index < entries.Length; ++index)
        {
            object entry = entries.GetValue(index)!;
            int size = Field<int>(entry, "Size");
            bool live = Field<Mtl.MTLLibrary>(entry, "Library").NativePtr != 0;
            if (live != (size > 0) || live != (Pointer.Unbox(Field<object>(entry, "Code")) != null)) return false;
            if (live) ++count;
            bytes += size;
        }
        return entries.Length == 64 && count == expectedCount && bytes == expectedBytes && bytes <= ByteCapacity &&
            Field<int>(device, "ShaderLibraryBytes") == bytes;
    }

    private static T Field<T>(object owner, string name)
    {
        FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
            throw new MissingFieldException(owner.GetType().FullName, name);
        return (T)field.GetValue(owner)!;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sysctlbyname")]
    private static extern int SysctlByName(byte* name, byte* value, nuint* size, void* newValue, nuint newSize);
}
