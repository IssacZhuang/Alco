// Fixed shared-sampler ownership, descriptor recycling, and GPU sampling through sampler-free material roots.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Buffer = Alco.Graphics.Buffer;

internal static unsafe class SharedSamplerTests
{
    private const string ModuleName = "shared_sampler_test";
    private const string ShaderSource = """
        #include <NoGraphicsRendering/samplers.slang>

        struct SampleData
        {
            float2 Uv;
            uint Helper;
            float Reference;
        };

        struct SharedSamplerRoot
        {
            GpuTextureId Color;
            GpuTextureId Depth;
            GPUBufferView Samples;
        };

        GPU_ROOT(SharedSamplerRoot, root);

        struct ScreenVertex
        {
            float4 Position : SV_Position;
        };

        [shader("vertex")]
        ScreenVertex vertexMain(uint vertexIndex : SV_VertexID)
        {
            static const float2 positions[3] = { float2(-1, -1), float2(3, -1), float2(-1, 3) };
            ScreenVertex output;
            output.Position = float4(positions[vertexIndex], 0, 1);
            return output;
        }

        [shader("pixel")]
        float4 pixelMain(ScreenVertex input) : SV_Target0
        {
            SampleData data = gpu_load<SampleData>(root.Samples, uint(input.Position.x));
            if (data.Helper == 6)
            {
                Texture2D<float> depth = gpu_texture<Texture2D<float>>(root.Depth.index);
                float visibility = depth.SampleCmpLevelZero(shared_sampler_depth_comparison(), data.Uv, data.Reference);
                return float4(visibility, visibility, visibility, 1);
            }

            Texture2D<float4> color = gpu_texture<Texture2D<float4>>(root.Color.index);
            // Magnification isolates anisotropic addressing from vendor-specific minification kernels.
            float2 gradientX = float2(1.0 / 64.0, 0);
            float2 gradientY = float2(0, 1.0 / 256.0);
            switch (data.Helper)
            {
            case 0: return color.SampleLevel(shared_sampler_linear_repeat(), data.Uv, 0);
            case 1: return color.SampleLevel(shared_sampler_linear_clamp(), data.Uv, 0);
            case 2: return color.SampleLevel(shared_sampler_nearest_repeat(), data.Uv, 0);
            case 3: return color.SampleLevel(shared_sampler_nearest_clamp(), data.Uv, 0);
            case 4: return color.SampleGrad(shared_sampler_anisotropic_repeat(), data.Uv, gradientX, gradientY);
            case 5: return color.SampleGrad(shared_sampler_anisotropic_clamp(), data.Uv, gradientX, gradientY);
            default: return float4(1, 0, 1, 1);
            }
        }
        """;

    internal static void RunCpu(Action<bool, string> check)
    {
        Console.WriteLine("Shared sampler shader (Metal source):");
        check(sizeof(SampleData) == 16 && (int)Marshal.OffsetOf<SampleData>(nameof(SampleData.Helper)) == 8 &&
            (int)Marshal.OffsetOf<SampleData>(nameof(SampleData.Reference)) == 12,
            "shared sampler cases retain the shader's 16-byte UV/helper/reference stride");
        check(File.Exists(Path.Combine(AppContext.BaseDirectory, "NoGraphicsRendering", "samplers.slang")),
            "the shared sampler include is copied recursively beside the test executable");
        try
        {
            using ShaderCompiler compiler = new(ShaderTarget.MetalSource, new ShaderCompilerOptions { SearchPaths = [AppContext.BaseDirectory] });
            CompiledShader compiled = compiler.CompileSource(ModuleName, ModuleName + ".slang", ShaderSource, ShaderPipelineKind.Graphics);
            ReadOnlySpan<string> entryPoints = ["vertexMain", "pixelMain"];
            for (int index = 0; index < entryPoints.Length; ++index)
            {
                CompiledShaderStage stage = compiled.GetStage(entryPoints[index]);
                string source = Encoding.UTF8.GetString(stage.Code.Span);
                check(source.Contains("#include <metal_stdlib>", StringComparison.Ordinal) &&
                    source.Contains($" {entryPoints[index]}(", StringComparison.Ordinal) &&
                    ShaderCompiler.GetCompiledEntryPointName(ShaderTarget.MetalSource, entryPoints[index]) == entryPoints[index] &&
                    stage.CompiledEntryPoint == entryPoints[index],
                    $"shared sampler {entryPoints[index]} compiles to Metal source with its original entry name");
            }
        }
        catch (ShaderCompilationException exception)
        {
            check(false, $"shared sampler Metal source compilation: {exception.Message}");
        }
    }

    internal static void RunGpu(GPUDevice device, Action<bool, string> check)
    {
        Console.WriteLine("Shared sampler bank and sampling:");
        (string Name, SampleData Data, Vector3 Expected)[] samples = CreateSamples();
        RenderingSystem? system = null;
        RenderContext? context = null;
        Shader? shader = null;
        Texture? color = null;
        Texture? depth = null;
        Texture? target = null;
        Buffer? sampleBuffer = null;
        GPUHeap readback = default;
        TimelinePoint completion = default;
        WeakReference[] bank = [];
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024, bufferHeapByteCount: 1024 * 1024);
            bank = InspectBank(system, check);
            context = new RenderContext(system, 256);
            using (ShaderCompiler compiler = new(device))
            {
                ShaderModule module = compiler.LoadModuleFromSource(ModuleName, ModuleName + ".slang", ShaderSource);
                shader = new Shader(system, module);
            }
            RootLayout root = shader.Root;
            check(root.SizeBytes == 16 && root.Fields.Length == 3 &&
                root.Find("Color") is { Offset: 0, SizeBytes: 4, Kind: MaterialFieldKind.Texture } &&
                root.Find("Depth") is { Offset: 4, SizeBytes: 4, Kind: MaterialFieldKind.Texture } &&
                root.Find("Samples") is { Offset: 8, SizeBytes: 8, Kind: MaterialFieldKind.Buffer },
                "shared sampler roots classify only two texture IDs and a sample-data buffer view at their C offsets");
            bool noSamplerFields = true;
            foreach (RootField field in root.Fields)
                noSamplerFields &= field.Kind != MaterialFieldKind.Sampler;
            check(noSamplerFields, "all seven shared helpers require no sampler fields or material sampler bindings");

            TextureDesc colorDesc = new()
            {
                Extent = new Extent3D(2, 2, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            color = system.CreateTexture(colorDesc);
            depth = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(2, 2, 1),
                Format = Format.D32Float,
                Usage = TextureUsage.Sampled | TextureUsage.DepthStencilAttachment | TextureUsage.TransferDestination,
            });
            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D((uint)samples.Length, 1, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            ReadOnlySpan<byte> colorData = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
            ReadOnlySpan<float> depthData = [0.25f, 0.75f, 0.75f, 0.25f];
            Span<SampleData> sampleData = stackalloc SampleData[samples.Length];
            for (int index = 0; index < samples.Length; ++index)
                sampleData[index] = samples[index].Data;
            sampleBuffer = system.CreateBuffer<SampleData>(sampleData);
            Material material = shader.CreateMaterial("shared samplers without sampler roots");
            material.SetTexture("Color", color);
            material.SetTexture("Depth", depth);
            material.SetBuffer("Samples", sampleBuffer);
            material.GetPipeline([Format.Rgba8Unorm], Format.Undefined);
            readback = device.CreateGPUHeap((ulong)samples.Length * 4, MemoryType.Readback);
            completion.Semaphore = device.CreateTimelineSemaphore();

            for (int frame = 0; frame < 3; ++frame)
            {
                if (frame != 0)
                {
                    ChurnSamplers(system, check);
                    InspectBank(system, check, bank);
                }
                context.BeginFrame();
                if (frame == 0)
                {
                    context.SetTextureData(color, colorData);
                    context.SetTextureData(depth, MemoryMarshal.AsBytes<float>(depthData));
                }
                else
                    context.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope render = context.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                }, [Format.Rgba8Unorm]))
                {
                    render.SetViewport(new Viewport { Width = samples.Length, Height = 1 });
                    render.SetScissor(new Scissor { Width = (uint)samples.Length, Height = 1 });
                    render.Draw(material, 3);
                }
                context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
                context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                GPUCommandBuffer commands = context.EndFrame();
                ++completion.Value;
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                context.MarkSubmitted(completion);
                completion.Wait();
                CheckPixels(readback.Range.CpuSpan, samples, frame, check);
            }
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Wait();
            system?.Tick();
            context?.Destroy();
            shader?.Destroy();
            sampleBuffer?.Destroy();
            target?.Destroy();
            depth?.Destroy();
            color?.Destroy();
            readback.Destroy();
            completion.Semaphore?.Destroy();
            system?.Destroy();
        }
        bool destroyed = bank.Length == SharedSamplers.Count;
        foreach (WeakReference reference in bank)
            destroyed &= reference.Target is Sampler { IsDestroyed: true };
        check(destroyed, "system destruction after WaitIdle releases every borrowed fixed sampler");
    }

    // No caller-owned sampler reference may survive this frame and mask the bank's ownership during collection.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] InspectBank(RenderingSystem system, Action<bool, string> check, WeakReference[]? previous = null)
    {
        SharedSamplers bank = system.Samplers;
        Sampler[] samplers =
        [
            bank.LinearRepeat, bank.LinearClamp, bank.NearestRepeat, bank.NearestClamp,
            bank.AnisotropicRepeat, bank.AnisotropicClamp, bank.DepthComparison,
        ];
        ReadOnlySpan<string> names =
        [
            "linear repeat", "linear clamp", "nearest repeat", "nearest clamp", "anisotropic repeat", "anisotropic clamp", "depth comparison",
        ];
        check(SharedSamplers.Count == 7 && samplers.Length == SharedSamplers.Count && ReferenceEquals(bank, system.Samplers),
            "the rendering system exposes one seven-slot shared sampler bank");
        check(ReferenceEquals(system.SamplerDefault, bank.LinearRepeat), "SamplerDefault is the identical borrowed linear-repeat sampler at slot zero");
        WeakReference[] references = previous ?? new WeakReference[samplers.Length];
        for (int index = 0; index < samplers.Length; ++index)
        {
            Sampler sampler = samplers[index];
            SamplerDesc desc = sampler.Desc;
            Filter filter = index is 2 or 3 ? Filter.Nearest : Filter.Linear;
            AddressMode address = index is 1 or 3 or 5 or 6 ? AddressMode.ClampToEdge : AddressMode.Repeat;
            check(!sampler.IsDestroyed && sampler.Index == (uint)index && (uint)sampler == (uint)index &&
                desc.MinFilter == filter && desc.MagFilter == filter && desc.MipFilter == filter &&
                desc.AddressU == address && desc.AddressV == address && desc.AddressW == address &&
                desc.Anisotropic == (index is 4 or 5) && desc.CompareEnabled == (index == 6) && desc.Compare == CompareOp.LessEqual,
                $"shared {names[index]} retains fixed slot {index} and every expected sampler description field");
            if (previous == null)
                references[index] = new WeakReference(sampler, trackResurrection: true);
            else
                check(ReferenceEquals(previous[index].Target, sampler) && !sampler.IsDestroyed,
                    $"shared {names[index]} stays system-owned and identical across GC, Tick, and custom churn");
        }
        return references;
    }

    private static void ChurnSamplers(RenderingSystem system, Action<bool, string> check)
    {
        Sampler?[] samplers = new Sampler?[8];
        Span<uint> indices = stackalloc uint[samplers.Length];
        Sampler? recycled = null;
        WeakReference? abandoned = null;
        try
        {
            SamplerDesc desc = new()
            {
                MinFilter = Filter.Nearest,
                MagFilter = Filter.Nearest,
                MipFilter = Filter.Nearest,
                AddressU = AddressMode.ClampToEdge,
                AddressV = AddressMode.ClampToEdge,
                AddressW = AddressMode.ClampToEdge,
                CompareEnabled = true,
                Compare = CompareOp.Greater,
            };
            bool outsideBank = true;
            bool sameSlots = true;
            for (int round = 0; round < 3; ++round)
            {
                for (int index = samplers.Length - 1; index >= 0; --index)
                {
                    samplers[index]?.Destroy();
                    samplers[index] = null;
                }
                for (int index = 0; index < samplers.Length; ++index)
                {
                    Sampler sampler = system.CreateSampler(desc);
                    samplers[index] = sampler;
                    outsideBank &= sampler.Index >= SharedSamplers.Count;
                    if (round == 0) indices[index] = sampler.Index;
                    else sameSlots &= sampler.Index == indices[index];
                    for (int previous = 0; previous < index; ++previous)
                        outsideBank &= sampler.Index != samplers[previous]!.Index;
                }
                system.Tick();
            }
            check(outsideBank && indices[0] == SharedSamplers.Count,
                "distinct custom sampler allocations start after all seven reserved slots, including during repeated churn");
            check(sameSlots, "destroyed custom samplers recycle the same nonreserved slots across repeated allocation rounds");

            (uint Index, WeakReference Reference) collectible = CreateCollectibleSampler(system);
            abandoned = collectible.Reference;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            Sampler? finalized = abandoned.Target as Sampler;
            check(finalized is { IsDestroyed: false }, "an unreferenced custom sampler awaits Tick after finalization");
            system.Tick();
            check(finalized is { IsDestroyed: true }, "Tick reclaims an unreferenced custom sampler after completed frame use");
            recycled = system.CreateSampler(desc);
            check(recycled.Index == collectible.Index && recycled.Index >= SharedSamplers.Count,
                "GC retirement recycles only the abandoned custom slot, not a shared bank slot");
        }
        finally
        {
            recycled?.Destroy();
            if (abandoned?.Target is Sampler { IsDestroyed: false } sampler)
                sampler.Destroy();
            for (int index = samplers.Length - 1; index >= 0; --index)
                samplers[index]?.Destroy();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (uint Index, WeakReference Reference) CreateCollectibleSampler(RenderingSystem system)
    {
        Sampler sampler = system.CreateSampler(new SamplerDesc());
        return (sampler.Index, new WeakReference(sampler, trackResurrection: true));
    }

    private static void CheckPixels(ReadOnlySpan<byte> pixels, ReadOnlySpan<(string Name, SampleData Data, Vector3 Expected)> samples,
        int frame, Action<bool, string> check)
    {
        for (int index = 0; index < samples.Length; ++index)
        {
            Vector3 expected = samples[index].Expected * 255;
            int offset = index * 4;
            check(MathF.Abs(pixels[offset] - expected.X) <= 2 && MathF.Abs(pixels[offset + 1] - expected.Y) <= 2 &&
                MathF.Abs(pixels[offset + 2] - expected.Z) <= 2 && pixels[offset + 3] == 255,
                $"frame {frame} {samples[index].Name}: RGB {pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]}, expected {expected}");
        }
    }

    private static (string Name, SampleData Data, Vector3 Expected)[] CreateSamples()
    {
        return
        [
            ("linear repeat +U", new() { Uv = new(1.125f, 0.375f) }, new(0.625f, 0.25f, 0.25f)),
            ("linear clamp +U", new() { Uv = new(1.125f, 0.375f), Helper = 1 }, new(0.25f, 1, 0.25f)),
            ("nearest repeat +U", new() { Uv = new(1.125f, 0.375f), Helper = 2 }, Vector3.UnitX),
            ("nearest clamp +U", new() { Uv = new(1.125f, 0.375f), Helper = 3 }, Vector3.UnitY),
            ("linear repeat -V", new() { Uv = new(0.375f, -0.125f) }, new(0.375f, 0.25f, 0.75f)),
            ("linear clamp -V", new() { Uv = new(0.375f, -0.125f), Helper = 1 }, new(0.75f, 0.25f, 0)),
            ("nearest repeat -V", new() { Uv = new(0.375f, -0.125f), Helper = 2 }, Vector3.UnitZ),
            ("nearest clamp -V", new() { Uv = new(0.375f, -0.125f), Helper = 3 }, Vector3.UnitX),
            ("anisotropic repeat +U", new() { Uv = new(1.25f, 0.25f), Helper = 4 }, Vector3.UnitX),
            ("anisotropic clamp +U", new() { Uv = new(1.25f, 0.25f), Helper = 5 }, Vector3.UnitY),
            ("anisotropic repeat -V", new() { Uv = new(0.25f, -0.25f), Helper = 4 }, Vector3.UnitZ),
            ("anisotropic clamp -V", new() { Uv = new(0.25f, -0.25f), Helper = 5 }, Vector3.UnitX),
            ("depth comparison fails", new() { Uv = new(0.25f, 0.25f), Helper = 6, Reference = 0.5f }, Vector3.Zero),
            ("depth comparison passes", new() { Uv = new(0.75f, 0.25f), Helper = 6, Reference = 0.5f }, Vector3.One),
            ("depth comparison includes equality", new() { Uv = new(0.25f, 0.25f), Helper = 6, Reference = 0.25f }, Vector3.One),
            ("depth comparison bilinear center", new() { Uv = new(0.5f, 0.5f), Helper = 6, Reference = 0.5f }, new(0.5f)),
            ("depth comparison clamps +U", new() { Uv = new(1.25f, 0.25f), Helper = 6, Reference = 0.5f }, Vector3.One),
            ("depth comparison clamps -V", new() { Uv = new(0.25f, -0.25f), Helper = 6, Reference = 0.5f }, Vector3.Zero),
            ("depth comparison bilinear clamped edge", new() { Uv = new(1.25f, 0.5f), Helper = 6, Reference = 0.5f }, new(0.5f)),
        ];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleData
    {
        internal Vector2 Uv;
        internal uint Helper;
        internal float Reference;
    }
}
