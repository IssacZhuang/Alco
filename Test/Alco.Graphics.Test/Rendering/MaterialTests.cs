// Material reflection, scoped compute and rendering, immutable render bundles, parallel recording, and frame-slot lifetime and allocation regressions.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Graphics.Slang;
using Buffer = Alco.Graphics.Buffer;

internal static class MaterialTests
{
    private const string MaterialShaderSource = """
        import Core;

        struct MaterialVertex
        {
            float4 position;
            float2 uv;
        };

        [GPURoot]
        struct MaterialRoot
        {
            GPUBufferView vertices;
            GpuTextureId albedo;
            GpuSamplerId sampler;
            float4 tint;
            float4x4 transform;
        };

        struct VertexOutput
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        };

        [shader("vertex")]
        VertexOutput vertexMain(uint vertex_id : SV_VertexID)
        {
            MaterialRoot root = gpu_load_root<MaterialRoot>();
            MaterialVertex vertex = gpu_load<MaterialVertex>(root.vertices, vertex_id);
            VertexOutput output;
            output.position = mul(root.transform, vertex.position);
            output.uv = vertex.uv;
            return output;
        }

        [shader("pixel")]
        float4 pixelMain(VertexOutput input) : SV_Target0
        {
            MaterialRoot root = gpu_load_root<MaterialRoot>();
            Texture2D<float4> albedo = gpu_texture<Texture2D<float4>>(root.albedo.index);
            SamplerState albedoSampler = gpu_sampler(root.sampler.index);
            return albedo.Sample(albedoSampler, input.uv) * root.tint;
        }
        """;

    private const uint RecordingSize = 16;
    private const string ScopedCommonSource = """
        import Core;

        [GPURoot]
        struct ScopeRoot
        {
            GPUBufferView Values;
            uint Index;
            uint Value;
        };

        """;

    private const string ScopedWriteSource = ScopedCommonSource + """
        [shader("compute")]
        [numthreads(4, 1, 1)]
        void writeMain(uint lane : SV_DispatchThreadID)
        {
            ScopeRoot root = gpu_load_root<ScopeRoot>();
            if (lane == 3) gpu_store<uint>(root.Values, root.Index, root.Value);
        }
        """;

    private const string ScopedWriteIndirectSource = ScopedCommonSource + """
        [shader("compute")]
        [numthreads(2, 2, 2)]
        void writeIndirectMain(uint3 thread : SV_DispatchThreadID)
        {
            ScopeRoot root = gpu_load_root<ScopeRoot>();
            if (all(thread == uint3(3, 1, 1))) gpu_store<uint>(root.Values, root.Index, root.Value * 2);
        }
        """;

    private const string ScopedShaderSource = ScopedCommonSource + """
        struct ScreenVertex
        {
            float4 Position : SV_Position;
            float4 Color : COLOR0;
        };
        ScreenVertex makeVertex(uint vertex)
        {
            ScopeRoot root = gpu_load_root<ScopeRoot>();
            ScreenVertex output;
            output.Position = float4(float2((vertex << 1) & 2, vertex & 2) * 2.0 - 1.0, 0.5, 1.0);
            output.Color = float4(gpu_load<uint>(root.Values, 0), gpu_load<uint>(root.Values, 1), gpu_load<uint>(root.Values, 2), 255) / 255.0;
            return output;
        }

        [shader("vertex")]
        ScreenVertex vertexMain(uint vertex : SV_VertexID) { return makeVertex(vertex); }

        [shader("mesh")]
        [outputtopology("triangle")]
        [numthreads(32, 1, 1)]
        void meshMain(uint lane : SV_GroupIndex, out vertices ScreenVertex vertices[3], out indices uint3 triangles[1])
        {
            SetMeshOutputCounts(3, 1);
            if (lane < 3)
            {
                ScreenVertex vertex = makeVertex(lane);
                vertex.Color = vertex.Color.bgra;
                vertices[lane] = vertex;
            }
            if (lane == 0) triangles[0] = uint3(0, 1, 2);
        }

        [shader("pixel")]
        float4 pixelMain(ScreenVertex input) : SV_Target0 { return input.Color; }

        [shader("compute")]
        [numthreads(1, 1, 1)]
        void genericCompute<let N : int>() { }
        """;

    internal static unsafe void Run(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        Shader? shader = null;
        Material? checkerMaterial = null;
        Material? flatMaterial = null;
        Texture? target = null;
        Texture? depth = null;
        Texture? checker = null;
        Sampler? nearest = null;
        Buffer? vertices = null;
        Buffer? indices = null;
        GPUHeap readback = default;
        RenderContext? context = null;
        try
        {
            system = new RenderingSystem(device, 16 * 1024 * 1024);
            using (ShaderCompiler compiler = new(device))
            {
                ShaderModule materialModule = compiler.LoadModuleFromSource("material", "material.slang", MaterialShaderSource);
                shader = new Shader(system, materialModule, name: "material_test");
            }

            RootLayout root = shader.Root;
            check(root.SizeBytes == 96, "root layout reflects the 96-byte C layout size");
            check(root.Find("vertices") is { Offset: 0, SizeBytes: 8, Kind: MaterialFieldKind.Buffer } &&
                root.Find("albedo") is { Offset: 8, SizeBytes: 4, Kind: MaterialFieldKind.Texture } &&
                root.Find("sampler") is { Offset: 12, SizeBytes: 4, Kind: MaterialFieldKind.Sampler } &&
                root.Find("tint") is { Offset: 16, SizeBytes: 16, Kind: MaterialFieldKind.Data } &&
                root.Find("transform") is { Offset: 32, SizeBytes: 64, Kind: MaterialFieldKind.Data },
                "root fields reflect buffer-view, id, and matrix kinds at their C offsets");
            check(root.Find("missing") == null, "root layout reports unknown field names");

            checkerMaterial = shader.CreateMaterial("checker");
            check(ReadUInt(checkerMaterial, 8) == system.TextureWhite.SampledIndex && ReadUInt(checkerMaterial, 12) == system.SamplerDefault.Index,
                "texture and sampler fields default to the system white texture and sampler");
            List<RenderingResource> defaults = [];
            checkerMaterial.RetainResources(defaults);
            check(defaults.Contains(system.TextureWhite), "default texture bindings retain the system white texture");
            MaterialInstance inheritedDefaults = checkerMaterial.CreateInstance();
            defaults.Clear();
            inheritedDefaults.RetainResources(defaults);
            check(defaults.Contains(system.TextureWhite), "material instances retain inherited default texture bindings");

            checker = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(16, 16, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            });
            nearest = system.CreateSampler(new SamplerDesc());
            uint version = checkerMaterial.Version;
            checkerMaterial.SetTexture("albedo", checker);
            checkerMaterial.SetSampler("sampler", nearest);
            Vector4 tint = Vector4.One;
            checkerMaterial.SetData("tint", ref tint);
            check(ReadUInt(checkerMaterial, 8) == checker.SampledIndex && ReadUInt(checkerMaterial, 12) == nearest.Index,
                "SetTexture and SetSampler write their descriptor indices");
            check(checkerMaterial.Version == version + 3, "each binding change increments the material version");
            defaults.Clear();
            inheritedDefaults.RetainResources(defaults);
            check(defaults.Contains(checker) && !defaults.Contains(system.TextureWhite),
                "an inherited explicit texture replaces the default texture in the effective resource snapshot");
            Material resetDefault = shader.CreateMaterial();
            resetDefault.SetTexture("albedo", checker);
            resetDefault.SetTexture("albedo", null);
            defaults.Clear();
            resetDefault.RetainResources(defaults);
            check(defaults.Contains(system.TextureWhite) && !defaults.Contains(checker),
                "removing an explicit texture binding restores default texture retention");

            readback = device.CreateGPUHeap(64 * 64 * 4, MemoryType.Readback);
            ReadOnlySpan<ushort> cubeIndices = [0, 2, 3, 3, 1, 0, 4, 5, 7, 7, 6, 4, 0, 4, 6, 6, 2, 0, 1, 3, 7, 7, 5, 1, 2, 6, 7, 7, 3, 2, 0, 1, 5, 5, 4, 0];
            indices = system.CreateBuffer(cubeIndices);
            Span<TestVertex> cubeVertices = stackalloc TestVertex[8];
            for (int index = 0; index < cubeVertices.Length; index++)
            {
                cubeVertices[index] = new TestVertex
                {
                    Position = new Vector4((index & 1) * 2 - 1, ((index >> 1) & 1) * 2 - 1, ((index >> 2) & 1) * 2 - 1, 1),
                    Uv = new Vector2(0.5f, 0.5f),
                };
            }
            vertices = system.CreateBuffer<TestVertex>(cubeVertices);
            checkerMaterial.SetBuffer("vertices", vertices);

            GPUPipeline textured = checkerMaterial.GetPipeline([Format.Rgba8Unorm], Format.D32Float);
            check(textured == checkerMaterial.GetPipeline([Format.Rgba8Unorm], Format.D32Float),
                "a repeated pass signature returns the cached pipeline");
            flatMaterial = shader.CreateMaterial("flat");
            check(flatMaterial.GetPipeline([Format.Rgba8Unorm], Format.D32Float) == textured,
                "two materials with equal state share one shader pipeline");
            check(checkerMaterial.GetPipeline([Format.Rgba8Unorm], Format.Undefined) != textured,
                "a different depth format creates a distinct pipeline");
            CheckPipelineCaching(checkerMaterial, textured, check);
            CheckDepthStencilEquality(check);

            context = new RenderContext(system);
            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(64, 64, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            depth = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(64, 64, 1),
                Format = Format.D32Float,
                Usage = TextureUsage.DepthStencilAttachment,
            });

            byte[] checkerPixels = new byte[16 * 16 * 4];
            for (int index = 0; index < checkerPixels.Length; index += 4)
            {
                checkerPixels[index] = 255;
                checkerPixels[index + 1] = 128;
                checkerPixels[index + 2] = 64;
            }

            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 3, 5), Vector3.Zero, Vector3.UnitY);
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, 1, 0.1f, 100);
            Matrix4x4 left = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(-1.1f, 0, 0) * view * projection);
            Matrix4x4 right = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(1.1f, 0, 0) * view * projection);
            checkerMaterial.SetData("transform", ref left);
            flatMaterial.SetBuffer("vertices", vertices);
            Vector4 flatTint = new(0.4f, 0.6f, 0.9f, 1.0f);
            flatMaterial.SetData("tint", ref flatTint);
            flatMaterial.SetData("transform", ref right);
            checkerMaterial.DepthStencilState = new DepthStencilState { DepthTest = true, DepthWrite = true };
            flatMaterial.DepthStencilState = checkerMaterial.DepthStencilState;

            checkerMaterial.GetPipeline([Format.Rgba8Unorm], Format.D32Float);
            flatMaterial.GetPipeline([Format.Rgba8Unorm], Format.D32Float);
            context.BeginFrame();
            context.SetTextureData(checker, checkerPixels);
            context.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
            context.Barrier(Stage.DepthStencilTests, Access.DepthStencilWrite, Stage.DepthStencilTests, Access.DepthStencilWrite);
            using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
            {
                Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                Depth = new DepthAttachment { RenderView = depth.RenderView, Load = LoadOp.Clear, Store = StoreOp.Discard },
            }, [Format.Rgba8Unorm], Format.D32Float))
            {
                pass.SetViewport(new Viewport { Width = 64, Height = 64 });
                pass.SetScissor(new Scissor { Width = 64, Height = 64 });

                GPUAllocation<byte> firstRoot = checkerMaterial.AllocateRoot(context.FrameData);
                GPUAllocation<byte> secondRoot = flatMaterial.AllocateRoot(context.FrameData);
                check(firstRoot.Buffer != null && ReferenceEquals(firstRoot.Buffer, secondRoot.Buffer) && firstRoot.Offset % 16 == 0 &&
                    secondRoot.Offset % 16 == 0 && firstRoot.Offset != secondRoot.Offset &&
                    firstRoot.SizeBytes == root.SizeBytes && secondRoot.SizeBytes == root.SizeBytes,
                    "root allocations are 16-byte aligned and distinct");

                pass.DrawIndexed(checkerMaterial, indices, IndexType.UInt16, 36);
                pass.DrawIndexed(flatMaterial, indices, IndexType.UInt16, 36);
            }
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit();
            context.Wait();

            ReadOnlySpan<byte> pixels = readback.Range.CpuSpan;
            int texturedPixels = 0;
            int flatPixels = 0;
            for (int index = 0; index < pixels.Length; index += 4)
            {
                if (pixels[index] == 255 && pixels[index + 1] == 128 && pixels[index + 2] == 64)
                    ++texturedPixels;
                if (Math.Abs(pixels[index] - 102) <= 2 && Math.Abs(pixels[index + 1] - 153) <= 2 && Math.Abs(pixels[index + 2] - 230) <= 2)
                    ++flatPixels;
            }
            check(texturedPixels > 500, "the bound checker material samples its texture through the reflected root");
            check(flatPixels > 500, "the unbound material renders its tint through the default white texture");

            checkerMaterial.WriteMask = 0;
            checkerMaterial.GetPipeline([Format.Rgba8Unorm], Format.D32Float);
            context.BeginFrame();
            context.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
            context.Barrier(Stage.DepthStencilTests, Access.DepthStencilWrite, Stage.DepthStencilTests, Access.DepthStencilWrite);
            using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
            {
                Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                Depth = new DepthAttachment { RenderView = depth.RenderView, Load = LoadOp.Clear, Store = StoreOp.Discard },
            }, [Format.Rgba8Unorm], Format.D32Float))
            {
                pass.SetViewport(new Viewport { Width = 64, Height = 64 });
                pass.SetScissor(new Scissor { Width = 64, Height = 64 });
                pass.DrawIndexed(checkerMaterial, indices, IndexType.UInt16, 36);
            }
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit();
            context.Wait();

            pixels = readback.Range.CpuSpan;
            bool clearPreserved = true;
            for (int index = 0; index < pixels.Length; index += 4)
                clearPreserved &= pixels[index] == 0 && pixels[index + 1] == 0 && pixels[index + 2] == 0 && pixels[index + 3] == 255;
            check(clearPreserved, "changing the material write mask after a draw preserves every cleared output byte");
            CheckScopedShaders(system, check);
            CheckParallelRecording(device, system, shader, check);
            CheckRenderBundles(device, system, shader, checker, nearest, check);
            CheckMaterialInstances(device, system, shader, check);
            CheckFrameLifecycle(device, system, target.RenderView, check);
            CheckSlotWait(device, system, check);
            CheckGcFallback(device, check);
            CheckRecordedBufferLifetime(device, check);
            CheckRecordedBufferLifetime(device, check, bundleDraw: true);
            CheckRecordedBufferLifetime(device, check, restoreOverride: true);
            CheckRecordedBufferLifetime(device, check, bundleDraw: true, restoreOverride: true);
            CheckReverseSubmissionRetirement(device, true, false, check);
            CheckReverseSubmissionRetirement(device, false, false, check);
            CheckReverseSubmissionRetirement(device, true, true, check);
            CheckBorrowedTimelineLifetime(device, TimelineRetirement.Wait, check);
            CheckBorrowedTimelineLifetime(device, TimelineRetirement.SlotReuse, check);
            CheckBorrowedTimelineLifetime(device, TimelineRetirement.Destroy, check);
            CheckVariantLayoutsAndCaching(system, vertices, indices, checker, check);
            CheckShaderSystemCompositionAndSpecialization(system, check);
            CheckAutomaticPipelinesAndWarmUp(device, check);
            CheckPrepackedDrawIndexed(device, system, shader, check);
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            shader?.Destroy();
            depth?.Destroy();
            target?.Destroy();
            checker?.Destroy();
            nearest?.Destroy();
            indices?.Destroy();
            vertices?.Destroy();
            system?.Destroy();
            readback.Destroy();
        }
    }

    private static unsafe void CheckScopedShaders(RenderingSystem system, Action<bool, string> check)
    {
        const string module = "material_scopes_test";
        const Format format = Format.Rgba8Unorm;
        const uint halfWidth = RecordingSize / 2;
        const int warmups = 4;
        GPUDevice device = system.Device;
        Shader? graphics = null;
        Shader? direct = null;
        Shader? indirect = null;
        Shader? mesh = null;
        Buffer? values = null;
        Buffer? arguments = null;
        Texture? target = null;
        RenderContext? context = null;
        GPUHeap readback = default;
        TimelinePoint completion = default;
        try
        {
            using (ShaderCompiler compiler = new(device))
            {
                ShaderModule rasterModule = compiler.LoadModuleFromSource(module, module + ".slang", ScopedShaderSource);
                graphics = new Shader(system, rasterModule, ShaderKind.Graphics);
                ShaderModule directModule = compiler.LoadModuleFromSource(module + "_write", module + "_write.slang", ScopedWriteSource);
                direct = new Shader(system, directModule);
                ShaderModule indirectModule = compiler.LoadModuleFromSource(module + "_indirect", module + "_indirect.slang",
                    ScopedWriteIndirectSource);
                indirect = new Shader(system, indirectModule);
                if (device.ActiveBackend != GraphicsBackend.VulkanCompact)
                    mesh = new Shader(system, rasterModule, ShaderKind.Mesh);
            }
            check(graphics.Kind == ShaderKind.Graphics && direct.Kind == ShaderKind.Compute && indirect.Kind == ShaderKind.Compute &&
                (mesh == null || mesh.Kind == ShaderKind.Mesh), "shader kinds select only their stages from a mixed module");
            GPUPipeline computePipeline = direct.GetComputePipeline();
            check(computePipeline != indirect.GetComputePipeline() && computePipeline == direct.GetComputePipeline(),
                "named compute shaders prepare separate pipelines and reuse them");

            values = system.CreateBuffer<uint>([0, 0, 0]);
            arguments = system.CreateBuffer<uint>([1, 1, 1, 0, 2, 1, 1]);
            Material write = direct.CreateMaterial();
            Material writeIndirect = indirect.CreateMaterial();
            Material draw = graphics.CreateMaterial();
            check(draw.Variant.Compiled.Stages.Count == 2 && draw.Root.Find(nameof(ScopeRoot.Values))?.Kind == MaterialFieldKind.Buffer,
                "selected non-generic graphics is material-ready after disposing the compiler of a mixed module with generic compute");
            Material? drawMesh = mesh?.CreateMaterial();
            uint index = 1;
            uint value = 64;
            write.SetBuffer(nameof(ScopeRoot.Values), values);
            write.SetData(nameof(ScopeRoot.Value), ref value);
            writeIndirect.SetBuffer(nameof(ScopeRoot.Values), values);
            writeIndirect.SetData(nameof(ScopeRoot.Index), ref index);
            writeIndirect.SetData(nameof(ScopeRoot.Value), ref value);
            draw.SetBuffer(nameof(ScopeRoot.Values), values);
            drawMesh?.SetBuffer(nameof(ScopeRoot.Values), values);
            ReadOnlySpan<Format> formats = [format];
            draw.GetPipeline(formats, Format.Undefined);
            drawMesh?.GetPipeline(formats, Format.Undefined);

            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                Format = format,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            ColorAttachment[] colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }];
            readback = device.CreateGPUHeap(RecordingSize * RecordingSize * 4, MemoryType.Readback);
            completion = new TimelinePoint { Semaphore = device.CreateTimelineSemaphore(), Value = 1 };
            context = new RenderContext(system, 1024);
            Stage readers = drawMesh == null ? Stage.Vertex : Stage.Vertex | Stage.Mesh;
            Extent3D oneGroup = new(1, 1, 1);
            for (int iteration = 0; iteration <= warmups; ++iteration)
            {
                context.BeginFrame();
                GPUAllocation<ScopeRoot> packed = context.FrameData.Allocate<ScopeRoot>();
                *packed.Cpu = new ScopeRoot { Values = values.View, Index = 2, Value = 192 };

                long before = GC.GetAllocatedBytesForCurrentThread();
                using (RenderContext.ComputeScope compute = context.BeginCompute())
                {
                    compute.Dispatch(write, oneGroup);
                    compute.DispatchIndirect(writeIndirect, arguments, byteOffset: 4 * sizeof(uint));
                    compute.Dispatch(direct, packed.GPURange, oneGroup);
                    compute.Barrier(Stage.Compute, Access.ShaderWrite, readers, Access.ShaderRead);
                }

                using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc { Colors = colors }, formats))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = halfWidth, Height = RecordingSize });
                    pass.Draw(draw, 3);
                    if (drawMesh != null)
                    {
                        pass.SetScissor(new Scissor { X = (int)halfWidth, Width = halfWidth, Height = RecordingSize });
                        pass.DrawMeshlets(drawMesh, oneGroup);
                    }
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (iteration < warmups)
                {
                    context.DiscardFrame();
                    continue;
                }

                check(allocated == 0, $"warmed compute and render scopes record without managed allocations (actual={allocated})");
                context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
                context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                GPUCommandBuffer commands = context.EndFrame();
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                context.MarkSubmitted(completion);
            }
            context.Wait();

            Vector4 expected = new Vector4(64, 128, 192, 255) / 255;
            check(PixelMatches(readback.Range, format, halfWidth / 2, halfWidth, expected),
                "graphics draw reads distinct direct, indirect-offset, and prepacked compute results after one scope barrier");
            if (drawMesh != null)
            {
                expected = new Vector4(expected.Z, expected.Y, expected.X, expected.W);
                check(PixelMatches(readback.Range, format, halfWidth + halfWidth / 2, halfWidth, expected),
                    "mesh scope draw reads the same compute buffer and produces its own output");
            }

            context.BeginFrame();
            GPURange packedWrite = context.AllocateRoot(new ScopeRoot { Values = values.View, Index = 0, Value = 32 });
            GPURange packedIndirectMaterial = context.AllocateRoot(new ScopeRoot { Values = values.View, Index = 1, Value = 48 });
            GPURange packedIndirectShader = context.AllocateRoot(new ScopeRoot { Values = values.View, Index = 2, Value = 80 });
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                compute.Dispatch(write, packedWrite, oneGroup);
                compute.DispatchIndirect(writeIndirect, packedIndirectMaterial, arguments, byteOffset: 4 * sizeof(uint));
                compute.DispatchIndirect(indirect, packedIndirectShader, arguments, byteOffset: 4 * sizeof(uint));
                compute.Barrier(Stage.Compute, Access.ShaderWrite, readers, Access.ShaderRead);
            }
            using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc { Colors = colors }, formats))
            {
                pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                pass.SetScissor(new Scissor { Width = halfWidth, Height = RecordingSize });
                pass.Draw(draw, 3);
            }
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();
            Vector4 expectedPacked = new Vector4(32, 96, 160, 255) / 255;
            check(PixelMatches(readback.Range, format, halfWidth / 2, halfWidth, expectedPacked),
                "ComputeScope prepacked root Dispatch and DispatchIndirect overloads execute on Material and Shader");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            completion.Semaphore?.Destroy();
            readback.Destroy();
            target?.Destroy();
            arguments?.Destroy();
            values?.Destroy();
            mesh?.Destroy();
            indirect?.Destroy();
            direct?.Destroy();
            graphics?.Destroy();
        }
    }

    private static unsafe void CheckParallelRecording(GPUDevice device, RenderingSystem system, Shader shader, Action<bool, string> check)
    {
        const int workerCount = 2;
        const int measuredDrawCount = 32;
        Buffer? vertices = null;
        Buffer? indices = null;
        GPUHeap readback = default;
        Material? material = null;
        GPUTimelineSemaphore? timeline = null;
        Texture[] targets = new Texture[workerCount];
        RenderContext[] contexts = new RenderContext[workerCount];
        RenderBundle[] bundles = new RenderBundle[workerCount];
        try
        {
            material = shader.CreateMaterial("parallel").CreateInstance().CreateInstance();
            readback = device.CreateGPUHeap(workerCount * RecordingSize * RecordingSize * 4, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            ReadOnlySpan<TestVertex> triangle =
            [
                new() { Position = new Vector4(-0.75f, -0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(-0.75f, -0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(0.75f, -0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(0, 0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
            ];
            vertices = system.CreateBuffer(triangle);
            ReadOnlySpan<ushort> triangleIndices = [0, 0, 0, 0, 1, 2];
            indices = system.CreateBuffer(triangleIndices);
            Matrix4x4 identity = Matrix4x4.Identity;
            Vector4 tint = new(0.25f, 0.5f, 0.75f, 1);
            uint bufferVersion = material.Version;
            material.SetBuffer("vertices", vertices, (ulong)sizeof(TestVertex));
            GPUBufferView stagedView = ReadRoot<GPUBufferView>(material, 0);
            check(sizeof(GPUBufferView) == 8 && stagedView.Index == vertices.View.Index &&
                stagedView.Offset == vertices.View.Offset + sizeof(TestVertex) && material.Version == bufferVersion + 1,
                "SetBuffer stages the 8-byte buffer index/offset view plus byte offset and increments the version");
            material.SetData("transform", ref identity);
            material.SetData("tint", ref tint);
            MaterialInstance sibling = material.CreateInstance();
            sibling.SetData("tint", ref tint);
            sibling.SetData("transform", ref identity);
            GPUPipeline rgba = material.GetPipeline([Format.Rgba8Unorm], Format.Undefined);
            ulong rootStride = (shader.Root.SizeBytes + 15UL) & ~15UL;
            uint frozenVersion = material.Version;
            Format[] formats = [Format.Rgba8Unorm, Format.Bgra8Unorm];
            GPURenderView[] views = new GPURenderView[workerCount];
            GPUAllocation<byte>[] pixels = new GPUAllocation<byte>[workerCount];
            for (int index = 0; index < workerCount; ++index)
            {
                targets[index] = system.CreateTexture(new TextureDesc
                {
                    Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                    Format = formats[index],
                    Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
                });
                views[index] = targets[index].RenderView;
                contexts[index] = new RenderContext(system, 16 * 1024);
                bundles[index] = new RenderBundle(system, [formats[index]]);
                pixels[index] = results.Allocate(RecordingSize * RecordingSize * 4);
            }
            using Barrier gate = new(workerCount);
            Thread[] threads = new Thread[workerCount];
            GPUPipeline?[] prepared = new GPUPipeline?[workerCount];
            bool[] recorded = new bool[workerCount];
            string?[] errors = new string?[workerCount];
            long[] allocated = new long[workerCount];
            bool[] freshRoots = new bool[workerCount];
            bool[] validSnapshots = new bool[workerCount];
            for (int index = 0; index < workerCount; ++index)
            {
                int worker = index;
                threads[index] = new Thread(() =>
                {
                    Gpu.SetErrorCallback((string message, bool fatal) => errors[worker] = message);
                    try
                    {
                        gate.SignalAndWait();
                        try
                        {
                            prepared[worker] = material.GetPipeline([Format.Bgra8Unorm], Format.Undefined);
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        {
                            errors[worker] = exception.ToString();
                        }
                        gate.SignalAndWait();
                        if (prepared[worker] == null || errors[worker] != null) return;

                        RenderBundle bundle = bundles[worker];
                        for (int draw = 0; draw < 16; ++draw)
                            bundle.Draw(draw % 2 == 0 ? material : sibling, 3);
                        bundle.EndRecording();
                        Span<byte> snapshot = stackalloc byte[(int)shader.Root.SizeBytes];
                        bool snapshotsMatch = true;
                        RenderContext context = contexts[worker];
                        context.BeginFrame();
                        BumpAllocator frameData = context.FrameData;
                        using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
                        {
                            Colors = [new() { RenderView = views[worker], Load = LoadOp.Clear }],
                        }, [formats[worker]]))
                        {
                            pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                            pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                            for (int draw = 0; draw < 8; ++draw)
                            {
                                Material drawMaterial = draw % 2 == 0 ? material : sibling;
                                drawMaterial.CopyRoot(snapshot);
                                if (worker == 0) pass.Draw(drawMaterial, 3);
                                else pass.DrawIndexed(drawMaterial, indices, IndexType.UInt16, 3, firstIndex: 1, byteOffset: 2 * sizeof(ushort));
                            }
                            GPUAllocation<byte> beforeRoots = frameData.Allocate(16);
                            long before = GC.GetAllocatedBytesForCurrentThread();
                            for (int draw = 0; draw < measuredDrawCount; ++draw)
                            {
                                Material drawMaterial = draw % 2 == 0 ? material : sibling;
                                drawMaterial.CopyRoot(snapshot);
                                GPUBufferView view = MemoryMarshal.Read<GPUBufferView>(snapshot[..8]);
                                snapshotsMatch &= view.Index == vertices.View.Index && view.Offset == vertices.View.Offset + sizeof(TestVertex) &&
                                    MemoryMarshal.Read<Vector4>(snapshot.Slice(16, 16)) == tint &&
                                    MemoryMarshal.Read<Matrix4x4>(snapshot.Slice(32, 64)) == identity;
                                if (worker == 0) pass.Draw(drawMaterial, 3);
                                else pass.DrawIndexed(drawMaterial, indices, IndexType.UInt16, 3, firstIndex: 1, byteOffset: 2 * sizeof(ushort));
                            }
                            allocated[worker] = GC.GetAllocatedBytesForCurrentThread() - before;
                            validSnapshots[worker] = snapshotsMatch;
                            GPUAllocation<byte> afterRoots = frameData.Allocate(16);
                            freshRoots[worker] = ReferenceEquals(afterRoots.Buffer, beforeRoots.Buffer) &&
                                afterRoots.Offset - beforeRoots.Offset == 16 + measuredDrawCount * rootStride;
                            pass.ExecuteBundle(bundle);
                        }
                        // The BGRA readback depends on the other context preceding this one in the batch.
                        if (worker == 0)
                        {
                            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                            for (int targetIndex = 0; targetIndex < workerCount; ++targetIndex)
                                context.CopyTextureToMemory(targets[targetIndex].NativeTexture, pixels[targetIndex].GPURange);
                            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                        }
                        recorded[worker] = true;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        errors[worker] = exception.ToString();
                    }
                    finally
                    {
                        Gpu.SetErrorCallback(null);
                    }
                });
                threads[index].Start();
            }
            for (int index = 0; index < workerCount; ++index) threads[index].Join();
            check(prepared[0] != null && prepared[0] == prepared[1] && prepared[0] != rgba,
                "concurrent preparation of one cold signature publishes a single shared pipeline");
            bool ready = true;
            for (int index = 0; index < workerCount; ++index)
            {
                bool succeeded = recorded[index] && errors[index] == null;
                ready &= succeeded;
                check(succeeded, $"parallel material recording worker {index}: {errors[index] ?? "no diagnostics"}");
                check(recorded[index] && allocated[index] == 0,
                    $"warmed context draws allocate zero managed bytes on worker {index} (actual={allocated[index]})");
                check(freshRoots[index], $"every context draw reserves a complete fresh root on worker {index}");
                check(validSnapshots[index], $"parallel nested-instance composition preserves every overridden field on worker {index}");
            }
            check(material.Version == frozenVersion, "parallel preparation and recording leave the shared material unchanged");
            if (!ready) return;
            RenderContext reused = contexts[0];
            RenderContext other = contexts[1];
            Span<ulong> timestamps = stackalloc ulong[workerCount];
            timestamps.Fill(ulong.MaxValue);
            reused.WriteTimestamp(timestamps[..1]);
            other.WriteTimestamp(timestamps[1..]);
            TimelinePoint batch = system.Submit([other, reused]);
            reused.Wait();
            other.Wait();
            bool retired = batch.Semaphore != null && batch.Value > 0 && batch.Semaphore.CompletedValue >= batch.Value &&
                timestamps[0] != ulong.MaxValue && timestamps[1] != ulong.MaxValue;
            check(retired, "one batch point covers both submitted slots and their timestamps");
            if (!retired) return;
            Vector4 clear = new(0, 0, 0, 1);
            for (int index = 0; index < workerCount; ++index)
            {
                check(PixelMatches(pixels[index], formats[index], 8, 8, tint) && PixelMatches(pixels[index], formats[index], 0, 0, clear),
                    $"parallel {(index == 0 ? "nonindexed RGBA" : "indexed BGRA with byteOffset and firstIndex")} draw honors buffer offsets and tint");
            }

            timestamps.Fill(ulong.MaxValue);
            other.BeginFrame();
            other.WriteTimestamp(timestamps[1..]);
            reused.BeginFrame();
            reused.WriteTimestamp(timestamps[..1]);
            TimelinePoint firstSubmission = reused.Submit();
            reused.BeginFrame();
            reused.DiscardFrame();
            TimelinePoint secondSubmission = other.Submit([firstSubmission]);
            reused.Wait();
            other.Wait();
            check(firstSubmission.Semaphore == batch.Semaphore && secondSubmission.Semaphore == batch.Semaphore &&
                firstSubmission.Value == batch.Value + 1 && secondSubmission.Value == firstSubmission.Value + 1,
                "reverse-recorded submissions signal the shared timeline in submit order without consuming a value on discard");
            check(timestamps[0] != ulong.MaxValue && timestamps[1] != ulong.MaxValue, "single submits retire both frames recorded in reverse order");

            timeline = device.CreateTimelineSemaphore();
            TimelinePoint completion = new() { Semaphore = timeline };
            material.WriteMask = 1;
            reused.BeginFrame();
            reused.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
            bool unprepared = false;
            system.PipelineMissPolicy = PipelineMissPolicy.Throw;
            try
            {
                using (RenderContext.RenderScope pass = reused.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = views[0], Load = LoadOp.Clear }],
                }, [Format.Rgba8Unorm]))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                    try
                    {
                        pass.Draw(material, 3);
                    }
                    catch (InvalidOperationException)
                    {
                        unprepared = true;
                    }
                }
            }
            finally
            {
                system.PipelineMissPolicy = PipelineMissPolicy.Create;
            }
            reused.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            reused.CopyTextureToMemory(targets[0].NativeTexture, pixels[0].GPURange);
            reused.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            GPUCommandBuffer commands = reused.EndFrame();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            reused.MarkSubmitted(completion);
            reused.Wait();
            check(unprepared && PixelMatches(pixels[0], Format.Rgba8Unorm, 8, 8, clear),
                "an unprepared state throws without drawing and its otherwise empty context pass can end");

            GPUPipeline redOnly = material.GetPipeline([Format.Rgba8Unorm], Format.Undefined);
            check(redOnly != rgba, "explicit preparation after a state change returns a different pipeline");
            material.WriteMask = 0xf;
            reused.BeginFrame();
            reused.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
            using (RenderContext.RenderScope pass = reused.BeginRenderPass(new RenderingDesc
            {
                Colors = [new() { RenderView = views[0], Load = LoadOp.Clear }],
            }, [Format.Rgba8Unorm]))
            {
                pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                pass.SetScissor(new Scissor { Width = RecordingSize / 2, Height = RecordingSize });
                pass.Draw(material, 3);
                material.WriteMask = 1;
                pass.SetScissor(new Scissor { X = (int)RecordingSize / 2, Width = RecordingSize / 2, Height = RecordingSize });
                pass.DrawIndexed(material, indices, IndexType.UInt16, 3, firstIndex: 1, byteOffset: 2 * sizeof(ushort));
            }
            reused.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            reused.CopyTextureToMemory(targets[0].NativeTexture, pixels[0].GPURange);
            reused.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
            Vector4 changedTint = new(0.75f, 0.25f, 0.5f, 1);
            material.SetData("tint", ref changedTint);
            using (RenderContext.RenderScope pass = reused.BeginRenderPass(new RenderingDesc
            {
                Colors = [new() { RenderView = views[0], Load = LoadOp.Clear }],
            }, [Format.Rgba8Unorm]))
            {
                pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                pass.Draw(material, 3);
            }
            reused.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            reused.CopyTextureToMemory(targets[0].NativeTexture, pixels[1].GPURange);
            reused.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands = reused.EndFrame();
            check(material.GetPipeline([Format.Rgba8Unorm], Format.Undefined) == redOnly,
                "parameter-only changes draw without preparation and reuse the prepared pipeline");
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            reused.MarkSubmitted(completion);
            reused.Wait();
            Vector4 originalRed = new(tint.X, 0, 0, 1);
            Vector4 changedRed = new(changedTint.X, 0, 0, 1);
            check(PixelMatches(pixels[0], Format.Rgba8Unorm, 7, 8, tint) && PixelMatches(pixels[0], Format.Rgba8Unorm, 8, 8, originalRed),
                "one context notices changes to the same material's prepared state within a pass");
            check(PixelMatches(pixels[1], Format.Rgba8Unorm, 8, 8, changedRed),
                "a reused context starts a second pass and copies parameter changes into its root");

            frozenVersion = material.Version;
            GPUCommandBuffer[] frameCommands = new GPUCommandBuffer[3];
            GPURange[] frameRanges = new GPURange[3];
            bool unchangedRoots = true;
            for (int frame = 0; frame < frameCommands.Length; ++frame)
            {
                reused.BeginFrame();
                GPUAllocation<byte> frameStart = reused.FrameData.Allocate(16);
                frameRanges[frame] = frameStart.GPURange;
                reused.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope pass = reused.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = views[0], Load = LoadOp.Clear }],
                }, [Format.Rgba8Unorm]))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                    pass.DrawIndexed(material, indices, IndexType.UInt16, 3, firstIndex: 1, byteOffset: 2 * sizeof(ushort));
                    GPUAllocation<byte> frameEnd = reused.FrameData.Allocate(16);
                    unchangedRoots &= ReferenceEquals(frameEnd.Buffer, frameStart.Buffer) && frameEnd.Offset - frameStart.Offset == 16 + rootStride;
                }
                reused.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                reused.CopyTextureToMemory(targets[0].NativeTexture, pixels[0].GPURange);
                reused.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands = reused.EndFrame();
                frameCommands[frame] = commands;
                completion.Value++;
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                reused.MarkSubmitted(completion);
                completion.Wait();
                unchangedRoots &= PixelMatches(pixels[0], Format.Rgba8Unorm, 8, 8, changedRed);
                // Poison only the completed draw's tint, before Wait or BeginFrame can recycle its slot.
                new Span<byte>(frameStart.Cpu + 32, 16).Clear();
                reused.Wait();
            }
            check(frameCommands[0] != frameCommands[1] && frameCommands[2] == frameCommands[0],
                "three submitted frames alternate the context's two command buffers and reuse the first wrapper");
            check(!frameRanges[0].Equals(frameRanges[1]) && frameRanges[2].Equals(frameRanges[0]) && frameRanges[0].Buffer != null &&
                frameRanges[1].Buffer != null && frameRanges[0].Offset % 16 == 0 && frameRanges[1].Offset % 16 == 0,
                "frame roots alternate between distinct aligned slots and reset to the first slot's base");
            check(material.Version == frozenVersion && unchangedRoots,
                "unchanged material versions still copy fresh root snapshots across frames and recycled slots");

            TimelinePoint[] preceding = new TimelinePoint[1];
            bool consecutive = true;
            long singleAllocations = 0;
            long batchAllocations = 0;
            for (int frame = 0; frame < 44; ++frame)
            {
                bool submitBatch = (frame & 1) != 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                reused.BeginFrame();
                if (submitBatch) other.BeginFrame();
                reused.Barrier(Stage.Transfer | Stage.ColorOutput, Access.TransferRead | Access.ColorWrite, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope pass = reused.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = views[0], Load = LoadOp.Clear }],
                }, [Format.Rgba8Unorm]))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                    pass.DrawIndexed(material, indices, IndexType.UInt16, 3, firstIndex: 1, byteOffset: 2 * sizeof(ushort));
                }
                TimelinePoint point = submitBatch ? system.Submit(contexts, preceding) : reused.Submit();
                long frameAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (frame >= 12)
                {
                    consecutive &= point.Semaphore == batch.Semaphore && point.Value == preceding[0].Value + 1;
                    if (submitBatch) batchAllocations += frameAllocated;
                    else singleAllocations += frameAllocated;
                }
                preceding[0] = point;
            }
            reused.Wait();
            other.Wait();
            check(consecutive, "single submissions and two-context batches each consume one value on the shared timeline");
            check(singleAllocations == 0 && batchAllocations == 0,
                $"32 warmed indexed frames with single and batch Submit allocate zero managed bytes (single={singleAllocations}, batch={batchAllocations})");

            GPUCommandBuffer[] submission = new GPUCommandBuffer[1];
            Material alternate = shader.CreateMaterial("shared_buffers");
            alternate.SetBuffer("vertices", vertices, (ulong)sizeof(TestVertex));
            alternate.SetData("transform", ref identity);
            alternate.SetData("tint", ref changedTint);
            alternate.WriteMask = material.WriteMask;
            alternate.GetPipeline([Format.Rgba8Unorm], Format.Undefined);
            long alternatingAllocations = 0;
            for (int frame = 0; frame < 4; ++frame)
            {
                long beforeAlternating = GC.GetAllocatedBytesForCurrentThread();
                reused.BeginFrame();
                reused.Barrier(Stage.Transfer | Stage.ColorOutput, Access.TransferRead | Access.ColorWrite, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope pass = reused.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = views[0], Load = LoadOp.Clear }],
                }, [Format.Rgba8Unorm]))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                    for (int draw = 0; draw < measuredDrawCount; ++draw)
                        pass.DrawIndexed(draw % 2 == 0 ? material : alternate, indices, IndexType.UInt16, 3, firstIndex: 1, byteOffset: 2 * sizeof(ushort));
                }
                reused.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                reused.CopyTextureToMemory(targets[0].NativeTexture, pixels[0].GPURange);
                reused.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                submission[0] = reused.EndFrame();
                completion.Value++;
                device.Submit(new SubmitDesc { Commands = submission, Completion = completion });
                reused.MarkSubmitted(completion);
                reused.Wait();
                long frameAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlternating;
                if (frame >= 2) alternatingAllocations += frameAllocated;
            }
            check(alternatingAllocations == 0 && PixelMatches(pixels[0], Format.Rgba8Unorm, 8, 8, changedRed),
                $"warmed alternating-material frames reuse retention capacity without allocations or rendering changes (actual={alternatingAllocations})");
        }
        finally
        {
            device.WaitIdle();
            for (int index = 0; index < workerCount; ++index)
            {
                contexts[index]?.DiscardFrame();
                contexts[index]?.Destroy();
                bundles[index]?.Destroy();
                targets[index]?.Destroy();
            }
            timeline?.Destroy();
            indices?.Destroy();
            vertices?.Destroy();
            readback.Destroy();
        }
    }

    private static unsafe void CheckRenderBundles(GPUDevice device, RenderingSystem system, Shader shader, Texture checker, Sampler nearest,
        Action<bool, string> check)
    {
        Buffer? vertices = null;
        Buffer? indices = null;
        Buffer? replacement = null;
        RenderBundle? bundle = null;
        RenderBundle? empty = null;
        RenderBundle? depthBundle = null;
        RenderBundle? grouped = null;
        GPUHeap readback = default;
        GPUTimelineSemaphore? timeline = null;
        Texture[] targets = new Texture[2];
        Texture[] depths = new Texture[2];
        RenderContext[] contexts = new RenderContext[2];
        try
        {
            ReadOnlySpan<TestVertex> triangle =
            [
                new() { Position = new Vector4(0, 0, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(0, 0, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(-0.75f, -0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(0.75f, -0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(0, 0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
            ];
            vertices = system.CreateBuffer(triangle);
            indices = system.CreateBuffer<ushort>([0, 0, 0, 0, 1, 2]);
            Span<TestVertex> degenerate = stackalloc TestVertex[triangle.Length];
            degenerate.Fill(triangle[0]);
            replacement = system.CreateBuffer<TestVertex>(degenerate);
            Material material = shader.CreateMaterial("bundle_snapshots");
            bundle = new RenderBundle(system, [Format.Rgba8Unorm], Format.D32Float);
            SetBundleSnapshot(material, vertices, checker, nearest, indexed: false);
            bundle.Draw(material, 3, instanceCount: 2, firstVertex: 1, firstInstance: 3);
            SetBundleSnapshot(material, vertices, system.TextureWhite, system.SamplerDefault, indexed: true);
            bundle.DrawIndexed(material, indices, IndexType.UInt16, 3, instanceCount: 2, firstIndex: 1,
                vertexOffset: 1, firstInstance: 3, byteOffset: 2 * sizeof(ushort));
            Vector4 zero = Vector4.Zero;
            Matrix4x4 identity = Matrix4x4.Identity;
            material.SetBuffer("vertices", replacement);
            material.SetData("tint", ref zero);
            material.SetData("transform", ref identity);
            material.WriteMask = 0;
            material.DepthStencilState = new DepthStencilState { DepthTest = true, DepthCompare = CompareOp.Never };
            bundle.EndRecording();
            empty = new RenderBundle(system, [Format.Rgba8Unorm], Format.D32Float);
            empty.EndRecording();
            check(bundle.DrawCount == 2 && empty.DrawCount == 0, "sealed mixed and empty bundles preserve their recorded draw counts");

            readback = device.CreateGPUHeap(6 * RecordingSize * RecordingSize * 4, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);
            GPUAllocation<byte>[] pixels = new GPUAllocation<byte>[6];
            for (int index = 0; index < pixels.Length; ++index) pixels[index] = results.Allocate(RecordingSize * RecordingSize * 4);
            for (int index = 0; index < contexts.Length; ++index)
            {
                contexts[index] = new RenderContext(system, 16 * 1024);
                targets[index] = system.CreateTexture(new TextureDesc
                {
                    Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                    Format = Format.Rgba8Unorm,
                    Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
                });
                depths[index] = system.CreateTexture(new TextureDesc
                {
                    Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                    Format = Format.D32Float,
                    Usage = TextureUsage.DepthStencilAttachment,
                });
            }
            timeline = device.CreateTimelineSemaphore();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            RenderContext context = contexts[0];
            context.BeginFrame();
            using (RenderContext.RenderScope pass = BeginBundlePass(context, targets[0], depths[0]))
            {
                SetBundleSnapshot(material, vertices, checker, nearest, indexed: false);
                pass.Draw(material, 3, instanceCount: 2, firstVertex: 1, firstInstance: 3);
                SetBundleSnapshot(material, vertices, system.TextureWhite, system.SamplerDefault, indexed: true);
                pass.DrawIndexed(material, indices, IndexType.UInt16, 3, instanceCount: 2, firstIndex: 1,
                    vertexOffset: 1, firstInstance: 3, byteOffset: 2 * sizeof(ushort));
            }
            CopyBundlePassOutput(context, targets[0], pixels[0]);
            GPUCommandBuffer commands = context.EndFrame();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            Vector4 leftTint = new(0.8f, 64 / 255f, 16 / 255f, 0);
            Vector4 rightTint = new(0.25f, 0, 0.5f, 1);
            Vector4 clear = new(0, 0, 0, 1);
            check(PixelMatches(pixels[0], Format.Rgba8Unorm, 4, 8, leftTint) && PixelMatches(pixels[0], Format.Rgba8Unorm, 12, 8, rightTint),
                "direct reference draws honor vertex/index offsets, transforms, texture bindings, and different color masks");
            material.SetBuffer("vertices", replacement);
            material.SetTexture("albedo", checker);
            material.SetSampler("sampler", nearest);
            material.SetData("tint", ref zero);
            material.SetData("transform", ref identity);
            material.WriteMask = 0;
            material.DepthStencilState = new DepthStencilState { DepthTest = true, DepthCompare = CompareOp.Never };
            uint frozenVersion = material.Version;
            long allocated = 0;
            bool noRoots = true;
            bool equivalent = true;
            for (int frame = 0; frame < 4; ++frame)
            {
                context.BeginFrame();
                using (RenderContext.RenderScope pass = BeginBundlePass(context, targets[0], depths[0]))
                {
                    pass.ExecuteBundle(empty);
                    for (int draw = 0; draw < 8; ++draw) pass.ExecuteBundle(bundle);
                    GPUAllocation<byte> beforeRoots = context.FrameData.Allocate(16);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int draw = 0; draw < 32; ++draw) pass.ExecuteBundle(bundle);
                    long frameAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    if (frame >= 2) allocated += frameAllocated;
                    GPUAllocation<byte> afterRoots = context.FrameData.Allocate(16);
                    noRoots &= ReferenceEquals(afterRoots.Buffer, beforeRoots.Buffer) && afterRoots.Offset == beforeRoots.Offset + 16;
                }
                CopyBundlePassOutput(context, targets[0], pixels[1]);
                commands = context.EndFrame();
                if (frame == 1)
                {
                    ulong completedBefore = timeline.CompletedValue;
                    context.DiscardFrame();
                    check(timeline.CompletedValue == completedBefore && bundle.DrawCount == 2,
                        "discarding bundle playback neither submits it nor invalidates the immutable bundle");
                    continue;
                }
                completion.Value++;
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                context.MarkSubmitted(completion);
                context.Wait();
                equivalent &= pixels[0].CpuSpan.SequenceEqual(pixels[1].CpuSpan);
            }
            check(equivalent && material.Version == frozenVersion,
                "three submitted bundle frames remain pixel-identical after root/state edits, buffer rebinding, slot reuse, and discard");
            check(noRoots, "repeated mixed bundle playback consumes no frame-root storage between allocator range brackets");
            check(allocated == 0, $"warmed mixed bundle replay allocates zero managed bytes (actual={allocated})");

            using Barrier gate = new(contexts.Length);
            Thread[] threads = new Thread[contexts.Length];
            GPUCommandBuffer?[] recorded = new GPUCommandBuffer?[contexts.Length];
            string?[] errors = new string?[contexts.Length];
            RenderBundle shared = bundle;
            for (int index = 0; index < contexts.Length; ++index)
            {
                int worker = index;
                threads[index] = new Thread(() =>
                {
                    Gpu.SetErrorCallback((string message, bool fatal) => errors[worker] = message);
                    try
                    {
                        gate.SignalAndWait();
                        RenderContext recording = contexts[worker];
                        recording.BeginFrame();
                        using (RenderContext.RenderScope pass = BeginBundlePass(recording, targets[worker], depths[worker]))
                        {
                            if (worker == 1)
                            {
                                pass.SetViewport(new Viewport { Width = RecordingSize / 2, Height = RecordingSize });
                                pass.SetScissor(new Scissor { X = (int)RecordingSize / 4, Width = RecordingSize / 4, Height = RecordingSize });
                            }
                            for (int draw = 0; draw < 32; ++draw) pass.ExecuteBundle(shared);
                        }
                        CopyBundlePassOutput(recording, targets[worker], pixels[worker + 1]);
                        recorded[worker] = recording.EndFrame();
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        errors[worker] = exception.ToString();
                    }
                    finally
                    {
                        Gpu.SetErrorCallback(null);
                    }
                });
                threads[index].Start();
            }
            for (int index = 0; index < contexts.Length; ++index) threads[index].Join();
            for (int index = 0; index < contexts.Length; ++index)
                check(errors[index] == null && recorded[index] != null, $"shared bundle recording worker {index}: {errors[index] ?? "no diagnostics"}");
            if (recorded[0] is not GPUCommandBuffer first || recorded[1] is not GPUCommandBuffer second) return;
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [first, second], Completion = completion });
            contexts[0].MarkSubmitted(completion);
            contexts[1].MarkSubmitted(completion);
            contexts[0].Wait();
            contexts[1].Wait();
            check(pixels[0].CpuSpan.SequenceEqual(pixels[1].CpuSpan) && PixelMatches(pixels[2], Format.Rgba8Unorm, 5, 8, rightTint) &&
                PixelMatches(pixels[2], Format.Rgba8Unorm, 2, 8, clear) && PixelMatches(pixels[2], Format.Rgba8Unorm, 12, 8, clear),
                "independent contexts concurrently replay one immutable bundle while preserving their own viewport and scissor");

            Material stateA = shader.CreateMaterial("bundle_depth_a");
            Material stateB = shader.CreateMaterial("bundle_depth_b");
            stateA.SetBuffer("vertices", vertices, 2UL * (ulong)sizeof(TestVertex));
            stateB.SetBuffer("vertices", vertices, 2UL * (ulong)sizeof(TestVertex));
            stateA.DepthStencilState = new DepthStencilState { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.Less };
            stateB.DepthStencilState = new DepthStencilState { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.Always };
            stateB.WriteMask = 2;
            Vector4 green = new(0, 1, 0, 1);
            stateB.SetData("tint", ref green);
            stateB.SetData("transform", ref identity);
            stateA.GetPipeline([Format.Rgba8Unorm], Format.D32Float);
            depthBundle = new RenderBundle(system, [Format.Rgba8Unorm], Format.D32Float);
            depthBundle.Draw(stateB, 3);
            depthBundle.EndRecording();
            context.BeginFrame();
            for (int passIndex = 0; passIndex < 2; ++passIndex)
            {
                using (RenderContext.RenderScope pass = BeginBundlePass(context, targets[0], depths[0]))
                    RecordBundleDepthSequence(pass, stateA, stateB, passIndex == 0 ? null : depthBundle);
                CopyBundlePassOutput(context, targets[0], pixels[passIndex + 2]);
                stateB.WriteMask = 0;
                stateB.DepthStencilState = new DepthStencilState { DepthTest = true, DepthCompare = CompareOp.Never };
                stateB.SetData("tint", ref zero);
            }
            bool emptyUsedNoRoots;
            using (RenderContext.RenderScope pass = BeginBundlePass(context, targets[0], depths[0]))
            {
                GPUAllocation<byte> beforeEmpty = context.FrameData.Allocate(16);
                pass.ExecuteBundle(empty);
                GPUAllocation<byte> afterEmpty = context.FrameData.Allocate(16);
                emptyUsedNoRoots = ReferenceEquals(afterEmpty.Buffer, beforeEmpty.Buffer) && afterEmpty.Offset == beforeEmpty.Offset + 16;
            }
            CopyBundlePassOutput(context, targets[0], pixels[4]);
            Vector4 red = new(1, 0, 0, 1);
            Vector4 blue = new(0, 0, 1, 1);
            using (RenderContext.RenderScope pass = BeginBundlePass(context, targets[0], depths[0]))
            {
                Matrix4x4 far = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, 0, 0.25f));
                stateA.SetData("tint", ref red);
                pass.Draw(stateA, 3);
                stateA.DepthStencilState = new DepthStencilState { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.Greater };
                stateA.SetData("tint", ref blue);
                stateA.SetData("transform", ref far);
                pass.SetScissor(new Scissor { X = (int)RecordingSize / 2, Width = RecordingSize / 2, Height = RecordingSize });
                pass.Draw(stateA, 3);
                stateA.DepthStencilState = new DepthStencilState { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.Less };
                stateA.SetData("tint", ref green);
                stateA.SetData("transform", ref identity);
                pass.SetScissor(new Scissor { X = (int)RecordingSize / 2, Width = 1, Height = RecordingSize });
                pass.Draw(stateA, 3);
            }
            CopyBundlePassOutput(context, targets[0], pixels[5]);
            commands = context.EndFrame();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            check(pixels[2].CpuSpan.SequenceEqual(pixels[3].CpuSpan) &&
                PixelMatches(pixels[3], Format.Rgba8Unorm, 7, 8, new Vector4(1, 1, 0, 1)) &&
                PixelMatches(pixels[3], Format.Rgba8Unorm, 8, 8, new Vector4(0, 0, 1, 1)),
                "ordinary A/A/B/A and ordinary A/A, bundle B, ordinary A preserve both pipeline and depth state after material edits");
            check(emptyUsedNoRoots && PixelMatches(pixels[4], Format.Rgba8Unorm, 8, 8, clear),
                "an empty bundle leaves the pass clear color and frame-root allocator unchanged");
            check(PixelMatches(pixels[5], Format.Rgba8Unorm, 7, 8, red) && PixelMatches(pixels[5], Format.Rgba8Unorm, 9, 8, blue) &&
                PixelMatches(pixels[5], Format.Rgba8Unorm, 8, 8, green),
                "a new managed pass resets bindings and applies depth Less/Greater/Less transitions without changing pipelines");

            ReadOnlySpan<Vector4> runTints =
            [
                new(0.25f, 0.5f, 0.75f, 1), new(0.75f, 0.25f, 0.5f, 1),
                new(0.5f, 0.75f, 0.25f, 1), new(0.75f, 0.5f, 0.25f, 1),
            ];
            Material runMaterial = shader.CreateMaterial("bundle_state_runs");
            runMaterial.SetBuffer("vertices", vertices, 2UL * (ulong)sizeof(TestVertex));
            grouped = new RenderBundle(system, [Format.Rgba8Unorm], Format.D32Float);
            context.BeginFrame();
            using (RenderContext.RenderScope pass = BeginBundlePass(context, targets[0], depths[0]))
            {
                for (int draw = 0; draw < runTints.Length; ++draw)
                {
                    Matrix4x4 transform = Matrix4x4.Transpose(Matrix4x4.CreateScale(0.5f, 0.5f, 1) *
                        Matrix4x4.CreateTranslation((draw & 1) == 0 ? -0.5f : 0.5f, draw < 2 ? 0.5f : -0.5f, 0));
                    Vector4 tint = runTints[draw];
                    runMaterial.SetData("transform", ref transform);
                    runMaterial.SetData("tint", ref tint);
                    runMaterial.WriteMask = draw == 2 ? (byte)5 : (byte)15;
                    if ((draw & 1) == 0)
                    {
                        grouped.Draw(runMaterial, 3, firstVertex: 1);
                        pass.Draw(runMaterial, 3, firstVertex: 1);
                    }
                    else
                    {
                        grouped.DrawIndexed(runMaterial, indices, IndexType.UInt16, 3, firstIndex: 1, vertexOffset: 1, byteOffset: 2 * sizeof(ushort));
                        pass.DrawIndexed(runMaterial, indices, IndexType.UInt16, 3, firstIndex: 1, vertexOffset: 1, byteOffset: 2 * sizeof(ushort));
                    }
                }
                grouped.EndRecording();
            }
            CopyBundlePassOutput(context, targets[0], pixels[2]);
            using (RenderContext.RenderScope pass = BeginBundlePass(context, targets[0], depths[0]))
                pass.ExecuteBundle(grouped);
            CopyBundlePassOutput(context, targets[0], pixels[3]);
            commands = context.EndFrame();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            bool runsMatch = grouped.DrawCount == 4 && pixels[2].CpuSpan.SequenceEqual(pixels[3].CpuSpan);
            for (int draw = 0; draw < runTints.Length; ++draw)
            {
                Vector4 expected = runTints[draw];
                if (draw == 2) expected.Y = 0;
                runsMatch &= PixelMatches(pixels[3], Format.Rgba8Unorm, (draw & 1) == 0 ? 4u : 12u, draw < 2 ? 4u : 12u, expected);
            }
            check(runsMatch, "one A/A/B/A bundle replays every direct/indexed root snapshot, including both draws in its first state run");
        }
        finally
        {
            device.WaitIdle();
            for (int index = 0; index < contexts.Length; ++index)
            {
                contexts[index]?.DiscardFrame();
                contexts[index]?.Destroy();
                depths[index]?.Destroy();
                targets[index]?.Destroy();
            }
            grouped?.Destroy();
            depthBundle?.Destroy();
            empty?.Destroy();
            bundle?.Destroy();
            timeline?.Destroy();
            replacement?.Destroy();
            indices?.Destroy();
            vertices?.Destroy();
            readback.Destroy();
        }
    }

    private static unsafe void CheckMaterialInstances(GPUDevice device, RenderingSystem system, Shader shader, Action<bool, string> check)
    {
        Buffer? vertices = null;
        Texture? spare = null;
        Texture? target = null;
        GPUHeap readback = default;
        byte* rootStorage = null;
        GPUTimelineSemaphore? timeline = null;
        RenderContext? context = null;
        RenderBundle? bundle = null;
        RenderBundle? updatedBundle = null;
        try
        {
            ReadOnlySpan<TestVertex> triangle =
            [
                new() { Position = new Vector4(-0.75f, -0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(0.75f, -0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(0, 0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
            ];
            vertices = system.CreateBuffer(triangle);
            spare = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(4, 4, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });

            Matrix4x4 left = Matrix4x4.Transpose(Matrix4x4.CreateScale(0.5f, 0.75f, 1) * Matrix4x4.CreateTranslation(-0.45f, 0, 0));
            Matrix4x4 right = Matrix4x4.Transpose(Matrix4x4.CreateScale(0.5f, 0.75f, 1) * Matrix4x4.CreateTranslation(0.45f, 0, 0));
            Vector4 parentTint = new(1, 0, 0, 1);
            Vector4 firstTint = new(0, 1, 0, 1);
            Vector4 secondTint = new(0, 0, 1, 1);

            Material parent = shader.CreateMaterial("instance_parent");
            parent.SetBuffer("vertices", vertices);
            parent.SetData("transform", ref left);
            parent.SetData("tint", ref parentTint);
            parent.SetTexture("albedo", spare);

            MaterialInstance first = parent.CreateInstance();
            MaterialInstance second = parent.CreateInstance();
            check(first.Parent == parent && first.Shader == shader && second.Parent == parent,
                "instances reference their parent and share its shader");
            check(first.GetPipeline([Format.Rgba8Unorm], Format.Undefined) == parent.GetPipeline([Format.Rgba8Unorm], Format.Undefined),
                "an instance with the inherited state reuses the parent's pipeline");

            GPUBufferView parentView = ReadRoot<GPUBufferView>(parent, 0);
            GPUBufferView instanceView = ReadRoot<GPUBufferView>(first, 0);
            check(instanceView.Index == parentView.Index && instanceView.Offset == parentView.Offset && ReadUInt(first, 8) == spare.SampledIndex &&
                ReadUInt(first, 12) == system.SamplerDefault.Index,
                "instances inherit the parent's buffer, texture, and sampler bindings");
            check(ReadRoot<Vector4>(first, 16) == parentTint && ReadRoot<Matrix4x4>(first, 32) == left,
                "instances inherit the parent's data fields");

            first.SetData("tint", ref firstTint);
            second.SetData("transform", ref left);
            second.SetData("tint", ref secondTint);
            check(ReadRoot<Vector4>(first, 16) == firstTint && ReadRoot<Vector4>(parent, 16) == parentTint,
                "an instance override changes only the instance's root field");

            parent.SetData("transform", ref right);
            check(ReadRoot<Matrix4x4>(first, 32) == right, "later parent edits propagate into unoverridden instance fields");
            check(ReadRoot<Matrix4x4>(second, 32) == left, "overridden fields keep the instance's own value");

            uint resourceVersion = first.ResourceVersion;
            parent.SetTexture("albedo", system.TextureWhite);
            check(first.ResourceVersion != resourceVersion && ReadUInt(first, 8) == system.TextureWhite.SampledIndex,
                "rebinding a parent texture propagates and changes the instance's resource version");
            CheckMaterialInstanceBindings(system, shader, vertices, spare, check);

            rootStorage = (byte*)NativeMemory.AlignedAlloc(4096, 16);
            BumpAllocator roots = new(new GPUAllocation<byte> { Cpu = rootStorage, SizeBytes = 4096 });
            MaterialInstance nested = first.CreateInstance().CreateInstance();
            uint nestedVersion = nested.Version;
            GPUAllocation<byte> beforeRoot = nested.AllocateRoot(roots);
            parent.SetData("transform", ref left);
            GPUAllocation<byte> afterRoot = nested.AllocateRoot(roots);
            Span<byte> expectedRoot = stackalloc byte[(int)shader.Root.SizeBytes];
            nested.CopyRoot(expectedRoot);
            check(beforeRoot.SizeBytes == shader.Root.SizeBytes && beforeRoot.Offset % 16 == 0 && afterRoot.Offset % 16 == 0 &&
                beforeRoot.Offset != afterRoot.Offset && afterRoot.CpuSpan.SequenceEqual(expectedRoot) && nested.Version == nestedVersion &&
                MemoryMarshal.Read<Matrix4x4>(beforeRoot.CpuSpan.Slice(32, 64)) == right &&
                MemoryMarshal.Read<Matrix4x4>(afterRoot.CpuSpan.Slice(32, 64)) == left && ReadRoot<Vector4>(nested, 16) == firstTint,
                "three-level instances follow ancestor edits while preserving distinct aligned CPU root snapshots and local overrides");
            parent.SetData("transform", ref right);

            readback = device.CreateGPUHeap(4096, MemoryType.Readback);
            BumpAllocator results = new(readback.Range);

            timeline = device.CreateTimelineSemaphore();
            TimelinePoint completion = new() { Semaphore = timeline };
            context = new RenderContext(system, 16 * 1024);
            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            GPUAllocation<byte>[] pixels =
            [
                results.Allocate(RecordingSize * RecordingSize * 4),
                results.Allocate(RecordingSize * RecordingSize * 4),
                results.Allocate(RecordingSize * RecordingSize * 4),
            ];

            bundle = new RenderBundle(system, [Format.Rgba8Unorm], Format.Undefined);
            bundle.Draw(nested, 3);
            bundle.EndRecording();
            parent.SetData("transform", ref left);
            updatedBundle = new RenderBundle(system, [Format.Rgba8Unorm], Format.Undefined);
            updatedBundle.Draw(nested, 3);
            updatedBundle.EndRecording();
            Vector4 liveTint = new(1, 1, 0, 1);
            first.SetData("tint", ref liveTint);

            parent.GetPipeline([Format.Rgba8Unorm], Format.Undefined);
            context.BeginFrame();
            context.Barrier(Stage.Transfer | Stage.ColorOutput, Access.TransferRead | Access.ColorWrite, Stage.ColorOutput, Access.ColorWrite);
            using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
            {
                Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
            }, [Format.Rgba8Unorm]))
            {
                pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                pass.Draw(parent, 3);
                parent.SetData("transform", ref right);
                pass.Draw(first, 3);
                pass.Draw(second, 3);
            }
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, pixels[0].GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);

            RenderBundle[] snapshots = [bundle, updatedBundle];
            for (int index = 0; index < snapshots.Length; ++index)
            {
                context.Barrier(Stage.Transfer | Stage.ColorOutput, Access.TransferRead | Access.ColorWrite, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                }, [Format.Rgba8Unorm]))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                    pass.ExecuteBundle(snapshots[index]);
                }
                context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                context.CopyTextureToMemory(target.NativeTexture, pixels[index + 1].GPURange);
                context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            }
            GPUCommandBuffer commands = context.EndFrame();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();

            Vector4 clear = new(0, 0, 0, 1);
            check(PixelMatches(pixels[0], Format.Rgba8Unorm, 4, 8, secondTint) && PixelMatches(pixels[0], Format.Rgba8Unorm, 12, 8, liveTint),
                "instance draws honor overridden transforms and tints while following the parent's live transform");
            check(PixelMatches(pixels[1], Format.Rgba8Unorm, 12, 8, firstTint) && PixelMatches(pixels[1], Format.Rgba8Unorm, 4, 8, clear),
                "sealed nested-instance bundles preserve their original roots after ancestor and local edits");
            check(PixelMatches(pixels[2], Format.Rgba8Unorm, 4, 8, firstTint) && PixelMatches(pixels[2], Format.Rgba8Unorm, 12, 8, clear),
                "a newly recorded nested-instance bundle captures ancestor edits without changing older snapshots");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            updatedBundle?.Destroy();
            bundle?.Destroy();
            timeline?.Destroy();
            target?.Destroy();
            spare?.Destroy();
            vertices?.Destroy();
            readback.Destroy();
            NativeMemory.AlignedFree(rootStorage);
        }
    }

    private static void CheckMaterialInstanceBindings(RenderingSystem system, Shader shader, Buffer vertices, Texture texture,
        Action<bool, string> check)
    {
        Buffer? replacement = null;
        Texture? storage = null;
        Sampler? parentSampler = null;
        Sampler? localSampler = null;
        try
        {
            replacement = system.CreateBuffer(256);
            storage = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(4, 4, 1),
                MipLevels = 3,
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            });
            parentSampler = system.CreateSampler(new SamplerDesc());
            localSampler = system.CreateSampler(new SamplerDesc { MinFilter = Filter.Nearest, MagFilter = Filter.Nearest });
            Material parent = shader.CreateMaterial("binding_parent");
            parent.SetBuffer("vertices", vertices);
            parent.SetTexture("albedo", texture);
            parent.SetSampler("sampler", parentSampler);
            Vector4 tint = new(0.25f, 0.5f, 0.75f, 1);
            Matrix4x4 transform = Matrix4x4.CreateScale(2);
            MaterialInstance child = parent.CreateInstance();
            MaterialInstance sibling = parent.CreateInstance();
            child.SetData("tint", ref tint);
            child.SetData("transform", ref transform);
            check(child.ResourceOwner == parent && sibling.ResourceOwner == parent,
                "data-only instances share their parent's resource retention identity");

            child.SetSampler("sampler", localSampler);
            child.SetBuffer("vertices", replacement, 16);
            child.SetTexture("albedo", storage);
            GPUBufferView localView = ReadRoot<GPUBufferView>(child, 0);
            check(child.ResourceOwner == child && localView.Index == replacement.View.Index && localView.Offset == replacement.View.Offset + 16 &&
                ReadUInt(child, 8) == storage.SampledIndex && ReadUInt(child, 12) == localSampler.Index &&
                ReadRoot<Vector4>(child, 16) == tint && ReadRoot<Matrix4x4>(child, 32) == transform,
                "out-of-order resource and data overrides compose adjacent fields without losing buffer offsets");

            MaterialInstance leaf = child.CreateInstance();
            leaf.SetBuffer("vertices", vertices);
            List<RenderingResource> retained = [];
            leaf.RetainResources(retained);
            check(retained.Count == 4 && retained.Contains(shader) && retained.Contains(vertices) && retained.Contains(storage) &&
                retained.Contains(localSampler) && !retained.Contains(replacement) && !retained.Contains(texture) && !retained.Contains(parentSampler),
                "nested snapshots retain one shader and only their effective buffer, texture, and sampler");

            child.SetTexture("albedo", null);
            check(ReadUInt(child, 8) == texture.SampledIndex && ReadUInt(child, 12) == localSampler.Index &&
                ReadRoot<Vector4>(child, 16) == tint && ReadRoot<Matrix4x4>(child, 32) == transform,
                "removing an interior coalesced texture override preserves neighboring resource and data overrides");
            child.SetTexture("albedo", null);
            child.SetSampler("sampler", null);
            child.SetBuffer("vertices", null);
            check(child.ResourceOwner == parent && ReadRoot<GPUBufferView>(child, 0).Equals(vertices.View) &&
                ReadUInt(child, 8) == texture.SampledIndex && ReadUInt(child, 12) == parentSampler.Index,
                "clearing all resource overrides restores inheritance and the shared resource retention identity");

            Span<byte> expected = stackalloc byte[(int)shader.Root.SizeBytes];
            Span<byte> partial = stackalloc byte[expected.Length];
            parent.CopyRoot(expected);
            MemoryMarshal.Write(expected.Slice(16, 16), in tint);
            MemoryMarshal.Write(expected.Slice(32, 64), in transform);
            bool subranges = true;
            for (int offset = 0; offset <= expected.Length; ++offset)
            {
                for (int length = 0; length <= expected.Length - offset; ++length)
                {
                    child.CopyRoot(partial[..length], offset);
                    subranges &= partial[..length].SequenceEqual(expected.Slice(offset, length));
                }
            }
            check(subranges, "root composition clips every parent/local subrange correctly, including empty and unaligned slices");

            uint resourceVersion = child.ResourceVersion;
            parent.SetBuffer("vertices", replacement, 32);
            parent.SetTexture("albedo", storage);
            parent.SetSampler("sampler", localSampler);
            GPUBufferView inheritedView = ReadRoot<GPUBufferView>(child, 0);
            check(child.ResourceVersion != resourceVersion && inheritedView.Index == replacement.View.Index &&
                inheritedView.Offset == replacement.View.Offset + 32 && ReadUInt(child, 8) == storage.SampledIndex &&
                ReadUInt(child, 12) == localSampler.Index,
                "restored bindings follow later parent buffer offsets, textures, and samplers");

            child.SetTexture("albedo", storage);
            parent.SetTexture("albedo", texture);
            check(ReadUInt(child, 8) == storage.SampledIndex, "an explicit binding equal to its parent remains a local override");
            child.SetStorageTexture("albedo", storage, 2);
            check(ReadUInt(child, 8) == storage.GetStorageIndex(2), "storage mip overrides preserve the selected descriptor");
            child.SetStorageTexture("albedo", null, uint.MaxValue);
            check(ReadUInt(child, 8) == texture.SampledIndex, "removing a storage override restores the parent's binding without using its mip argument");

            retained.Clear();
            child.RetainResources(retained);
            check(retained.Count == 4 && retained.Contains(replacement) && retained.Contains(texture) && retained.Contains(localSampler) &&
                !retained.Contains(storage) && !retained.Contains(vertices) && !retained.Contains(parentSampler),
                "removed local bindings are absent from subsequent resource snapshots");

            PipelineState initialState = child.State;
            DepthStencilState initialDepth = child.DepthStencilState;
            parent.Blend = new BlendState { Enabled = true };
            parent.WriteMask = 1;
            parent.Rasterization = new RasterizationState { Cull = CullMode.Clockwise };
            parent.Topology = PrimitiveTopology.LineList;
            parent.DepthStencilState = new DepthStencilState { DepthTest = true, DepthWrite = true };
            MaterialInstance changedState = parent.CreateInstance();
            check(child.State.Matches(initialState, ShaderKind.Graphics) &&
                child.DepthStencilState == initialDepth &&
                changedState.State.Matches(parent.State, ShaderKind.Graphics) &&
                changedState.DepthStencilState == parent.DepthStencilState,
                "instances keep independent creation-time pipeline and depth/stencil state snapshots");

            long allocated = 0;
            for (int iteration = 0; iteration < 80; ++iteration)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                child.SetBuffer("vertices", vertices);
                child.SetTexture("albedo", storage);
                child.SetSampler("sampler", parentSampler);
                child.SetTexture("albedo", null);
                child.SetBuffer("vertices", null);
                child.SetSampler("sampler", null);
                child.SetData("tint", ref tint);
                child.SetData("transform", ref transform);
                child.CopyRoot(partial);
                if (iteration >= 16)
                    allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            check(allocated == 0, $"warmed override insertion, splitting, removal, and root composition allocate no managed bytes (actual={allocated})");

            parent.SetBuffer("vertices", null);
            parent.SetTexture("albedo", null);
            parent.SetSampler("sampler", null);
            check(ReadRoot<GPUBufferView>(child, 0).Equals(default(GPUBufferView)) &&
                ReadUInt(child, 8) == system.TextureWhite.SampledIndex && ReadUInt(child, 12) == system.SamplerDefault.Index,
                "clearing a base material's bindings restores its zero buffer view and system defaults through descendants");
        }
        finally
        {
            localSampler?.Destroy();
            parentSampler?.Destroy();
            storage?.Destroy();
            replacement?.Destroy();
        }
    }

    private static unsafe void SetBundleSnapshot(Material material, Buffer vertices, Texture texture, Sampler sampler, bool indexed)
    {
        Matrix4x4 transform = Matrix4x4.Transpose(Matrix4x4.CreateScale(0.5f, 0.75f, 1) * Matrix4x4.CreateTranslation(indexed ? 0.45f : -0.45f, 0, 0));
        Vector4 tint = indexed ? new Vector4(0.25f, 0.75f, 0.5f, 1) : new Vector4(0.8f, 0.5f, 0.25f, 1);
        // Slang's SV_VertexID subtracts BaseVertex; firstVertex/vertexOffset do not advance this shader's buffer reads.
        material.SetBuffer("vertices", vertices, 2UL * (ulong)sizeof(TestVertex));
        material.SetTexture("albedo", texture);
        material.SetSampler("sampler", sampler);
        material.SetData("transform", ref transform);
        material.SetData("tint", ref tint);
        material.WriteMask = indexed ? (byte)5 : (byte)15;
        material.DepthStencilState = new DepthStencilState();
    }

    private static RenderContext.RenderScope BeginBundlePass(RenderContext context, Texture target, Texture depth)
    {
        context.Barrier(Stage.Transfer | Stage.ColorOutput, Access.TransferRead | Access.ColorWrite, Stage.ColorOutput, Access.ColorWrite);
        context.Barrier(Stage.DepthStencilTests, Access.DepthStencilWrite, Stage.DepthStencilTests, Access.DepthStencilRead | Access.DepthStencilWrite);
        RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
        {
            Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
            Depth = new DepthAttachment { RenderView = depth.RenderView, Load = LoadOp.Clear, Store = StoreOp.Discard },
        }, [Format.Rgba8Unorm], Format.D32Float);
        pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
        pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
        return pass;
    }

    private static void CopyBundlePassOutput(RenderContext context, Texture target, GPUAllocation<byte> pixels)
    {
        context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
        context.CopyTextureToMemory(target.NativeTexture, pixels.GPURange);
        context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
    }

    private static void RecordBundleDepthSequence(RenderContext.RenderScope pass, Material stateA, Material stateB, RenderBundle? bundle)
    {
        Matrix4x4 near = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, 0, -0.25f));
        Matrix4x4 far = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, 0, 0.25f));
        Vector4 red = new(1, 0, 0, 1);
        Vector4 blue = new(0, 0, 1, 1);
        stateA.SetData("transform", ref near);
        stateA.SetData("tint", ref red);
        pass.Draw(stateA, 3);
        stateA.SetData("transform", ref far);
        stateA.SetData("tint", ref blue);
        pass.Draw(stateA, 3);
        if (bundle == null) pass.Draw(stateB, 3);
        else pass.ExecuteBundle(bundle);
        pass.SetScissor(new Scissor { Width = RecordingSize / 2, Height = RecordingSize });
        pass.Draw(stateA, 3);
        stateA.SetData("transform", ref near);
        pass.SetScissor(new Scissor { X = (int)RecordingSize / 2, Width = RecordingSize / 2, Height = RecordingSize });
        pass.Draw(stateA, 3);
    }

    private static unsafe void CheckFrameLifecycle(GPUDevice device, RenderingSystem system, GPURenderView renderView, Action<bool, string> check)
    {
        RenderContext? context = null;
        GPUTimelineSemaphore? timeline = null;
        GPUHeap readback = default;
        Span<ulong> timestamps = stackalloc ulong[2];
        timestamps.Fill(ulong.MaxValue);
        try
        {
            context = new RenderContext(system, 256);
            timeline = device.CreateTimelineSemaphore();
            readback = device.CreateGPUHeap(16, MemoryType.Readback);
            TimelinePoint completion = new() { Semaphore = timeline };
            context.Wait();
            context.DiscardFrame();
            context.BeginFrame();
            GPUAllocation<byte> first = context.FrameData.Allocate(16);
            first.CpuSpan.Fill(0x5a);
            context.WriteTimestamp(timestamps[..1]);
            context.CopyMemory(first.GPURange, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            ulong completedBefore = timeline.CompletedValue;
            GPUCommandBuffer ended = context.EndFrame();
            check(timeline.CompletedValue == completedBefore && timestamps[0] == ulong.MaxValue,
                "EndFrame leaves recorded work unsubmitted");
            context.Wait();
            check(timeline.CompletedValue == completedBefore && timestamps[0] == ulong.MaxValue,
                "Wait leaves an ended frame pending for external submission");
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [ended], Completion = completion });
            context.MarkSubmitted(completion);

            context.BeginFrame();
            BumpAllocator activeData = context.FrameData;
            GPUAllocation<byte> second = activeData.Allocate(16);
            second.CpuSpan.Fill(0xa5);
            context.WriteTimestamp(timestamps[1..]);
            context.Wait();
            GPUAllocation<byte> afterWait = context.FrameData.Allocate(16);
            bool firstCopy = true;
            foreach (byte value in readback.Range.CpuSpan) firstCopy &= value == 0x5a;
            check(context.FrameData == activeData && ReferenceEquals(afterWait.Buffer, second.Buffer) && afterWait.Offset == second.Offset + 16 &&
                timestamps[0] != ulong.MaxValue && timestamps[1] == ulong.MaxValue && firstCopy,
                "Wait reads prior submitted timestamps and copies without ending, submitting, or resetting the active frame");
            context.CopyMemory(second.GPURange, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            GPUCommandBuffer commands = context.EndFrame();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            context.Wait();
            bool secondCopy = true;
            foreach (byte value in readback.Range.CpuSpan) secondCopy &= value == 0xa5;
            check(timestamps[1] != ulong.MaxValue && secondCopy, "a frame remains recordable after Wait and is read back only after external submission");

            context.BeginFrame();
            GPURange discardedRange = context.FrameData.Allocate(16).GPURange;
            GPUCommandBuffer pending = context.EndFrame();
            context.DiscardFrame();
            context.DiscardFrame();
            context.BeginFrame();
            check(context.FrameData.Allocate(16).GPURange.Equals(discardedRange),
                "discarding a pending frame and an idle context preserves the slot and resets its root allocator");
            context.DiscardFrame();
            context.BeginFrame();
            check(context.FrameData.Allocate(16).GPURange.Equals(discardedRange),
                "discarding an active frame allows recording the same slot from its root base");

            ReadOnlySpan<ColorAttachment> colors = [new() { RenderView = renderView, Load = LoadOp.Clear }];
            RenderingDesc desc = new() { Colors = colors };
            context.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
            using (RenderContext.RenderScope discardedPass = context.BeginRenderPass(desc, [Format.Rgba8Unorm]))
            {
                RenderContext.RenderScope stale = discardedPass;
                context.DiscardFrame();
                context.BeginFrame();
                GPURange afterDiscard = context.FrameData.Allocate(16).GPURange;
                context.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope pass = context.BeginRenderPass(desc, [Format.Rgba8Unorm]))
                {
                    stale.Dispose();
                    pass.SetViewport(new Viewport { Width = 64, Height = 64 });
                    pass.End();
                }
                check(context.EndFrame() == pending && afterDiscard.Equals(discardedRange),
                    "discarding an open render pass preserves the slot, and disposing its copied scope leaves the new pass active");
            }
            context.DiscardFrame();
            context.DiscardFrame();

            GPUCommandBuffer[] submission = new GPUCommandBuffer[1];
            long allocated = 0;
            for (int frame = 0; frame < 44; ++frame)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                context.BeginFrame();
                context.FrameData.Allocate(16).CpuSpan.Clear();
                submission[0] = context.EndFrame();
                completion.Value++;
                device.Submit(new SubmitDesc { Commands = submission, Completion = completion });
                context.MarkSubmitted(completion);
                long frameAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (frame >= 12) allocated += frameAllocated;
            }
            context.Wait();
            check(allocated == 0, $"32 warmed BeginFrame/EndFrame/Submit/MarkSubmitted frames allocate zero managed bytes (actual={allocated})");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
        }
    }

    private static unsafe void CheckSlotWait(GPUDevice device, RenderingSystem system, Action<bool, string> check)
    {
        RenderContext? context = null;
        GPUTimelineSemaphore? firstTimeline = null;
        GPUTimelineSemaphore? secondTimeline = null;
        using ManualResetEventSlim secondReady = new();
        using ManualResetEventSlim allowThird = new();
        using ManualResetEventSlim thirdEntered = new();
        using ManualResetEventSlim thirdReady = new();
        Thread? worker = null;
        GPUCommandBuffer? firstCommands = null;
        GPUCommandBuffer? secondCommands = null;
        GPUCommandBuffer? thirdCommands = null;
        GPURange firstRange = default;
        GPURange secondRange = default;
        GPURange thirdRange = default;
        string? error = null;
        bool probeThird = false;
        bool secondReturned = false;
        bool blocked = false;
        bool returnedBeforeOtherSlot = false;
        Span<ulong> timestamp = stackalloc ulong[1];
        timestamp[0] = ulong.MaxValue;
        try
        {
            context = new RenderContext(system, 256);
            RenderContext recording = context;
            firstTimeline = device.CreateTimelineSemaphore();
            secondTimeline = device.CreateTimelineSemaphore();
            TimelinePoint firstActual = new() { Semaphore = firstTimeline, Value = 1 };
            TimelinePoint firstRetired = new() { Semaphore = firstTimeline, Value = 2 };
            context.BeginFrame();
            firstRange = context.FrameData.Allocate(16).GPURange;
            context.WriteTimestamp(timestamp);
            firstCommands = context.EndFrame();
            device.Submit(new SubmitDesc { Commands = [firstCommands], Completion = firstActual });
            // Later empty submissions cover every prior use without making the retirement point complete yet.
            context.MarkSubmitted(firstRetired);
            firstActual.Wait();
            worker = new Thread(() =>
            {
                Gpu.SetErrorCallback((string message, bool fatal) => error = message);
                try
                {
                    recording.BeginFrame();
                    secondRange = recording.FrameData.Allocate(16).GPURange;
                    secondCommands = recording.EndFrame();
                    secondReady.Set();
                    allowThird.Wait();
                    if (!probeThird) return;
                    thirdEntered.Set();
                    recording.BeginFrame();
                    thirdRange = recording.FrameData.Allocate(16).GPURange;
                    thirdCommands = recording.EndFrame();
                    thirdReady.Set();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    error = exception.ToString();
                }
                finally
                {
                    secondReady.Set();
                    Gpu.SetErrorCallback(null);
                }
            });
            worker.Start();
            secondReturned = secondReady.Wait(TimeSpan.FromSeconds(2)) && secondCommands != null && error == null;
            if (secondReturned && secondCommands is GPUCommandBuffer second)
            {
                TimelinePoint secondActual = new() { Semaphore = secondTimeline, Value = 1 };
                TimelinePoint secondRetired = new() { Semaphore = secondTimeline, Value = 2 };
                device.Submit(new SubmitDesc { Commands = [second], Completion = secondActual });
                context.MarkSubmitted(secondRetired);
                secondActual.Wait();
                probeThird = true;
                allowThird.Set();
                blocked = thirdEntered.Wait(TimeSpan.FromSeconds(2)) && !thirdReady.Wait(TimeSpan.FromMilliseconds(100));
            }
        }
        finally
        {
            if (firstTimeline != null)
                device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = firstTimeline, Value = 2 } });
            if (probeThird)
                returnedBeforeOtherSlot = thirdReady.Wait(TimeSpan.FromSeconds(2)) && secondTimeline != null && secondTimeline.CompletedValue == 1;
            if (secondTimeline != null)
                device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = secondTimeline, Value = 2 } });
            allowThird.Set();
            worker?.Join();
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            firstTimeline?.Destroy();
            secondTimeline?.Destroy();
        }
        check(error == null && secondReturned && firstCommands != secondCommands && !firstRange.Equals(secondRange),
            $"the second frame uses a separate slot without waiting for the first slot's future retirement point ({error ?? "no diagnostics"})");
        check(blocked && returnedBeforeOtherSlot && thirdCommands == firstCommands && thirdRange.Equals(firstRange),
            "the third BeginFrame waits for its selected slot, not WaitIdle or the other slot's future retirement point");
        check(timestamp[0] != ulong.MaxValue, "slot reuse reads completed timestamps before resetting the selected command pool");
    }

    // Leaked wrappers must become unreachable before GC.Collect: once-executed methods stay tier-0-jitted,
    // where every local is a GC root until the method returns, so each leak is created inside a NoInlining
    // helper whose frame dies before the caller collects.
    private static Material? s_retainedMaterial;

    private static unsafe void CheckGcFallback(GPUDevice device, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        GPUTimelineSemaphore? timeline = null;
        RenderContext? context = null;
        Texture? replacement = null;
        Sampler? recycled = null;
        Texture? other = null;
        Texture? reclaimed = null;
        Buffer? reclaimedBuffer = null;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024);
            timeline = device.CreateTimelineSemaphore();
            context = new RenderContext(system, 256);
            TimelinePoint completion = new() { Semaphore = timeline };

            (uint TextureSlot, uint SamplerSlot) leaked = LeakSubmittedPair(system, context, device, ref completion);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            completion.Wait();
            system.Tick();
            replacement = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(2, 2, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });
            recycled = system.CreateSampler(new SamplerDesc());
            check(replacement.SampledIndex == leaked.TextureSlot && recycled.Index == leaked.SamplerSlot,
                "finalized texture and sampler wrappers release their slots behind their covering submission");
            replacement.Destroy();
            recycled.Destroy();
            replacement = null;
            recycled = null;

            using (ShaderCompiler compiler = new(device, new ShaderCompilerOptions
            {
                FileResolver = path => path.Replace('\\', '/').EndsWith("material.slang", StringComparison.Ordinal) ? MaterialShaderSource : null,
            }))
            {
                (uint TextureSlot, WeakReference Buffer, GPURange BufferRange) retained = CreateRetainedMaterial(system, compiler);
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                system.Tick();
                other = system.CreateTexture(new TextureDesc
                {
                    Extent = new Extent3D(2, 2, 1),
                    Format = Format.Rgba8Unorm,
                    Usage = TextureUsage.Sampled,
                });
                check(other.SampledIndex != retained.TextureSlot, "a material keeps its bound texture and descriptor slot alive");
                check(retained.Buffer.IsAlive, "a material keeps its bound buffer alive without a caller-owned reference");
                other.Destroy();
                other = null;

                s_retainedMaterial = null;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                device.WaitIdle();
                system.Tick();
                reclaimed = system.CreateTexture(new TextureDesc
                {
                    Extent = new Extent3D(2, 2, 1),
                    Format = Format.Rgba8Unorm,
                    Usage = TextureUsage.Sampled,
                });
                check(reclaimed.SampledIndex == retained.TextureSlot, "dropping a material and its shader releases the retained texture and cached pipelines");
                reclaimedBuffer = system.CreateBuffer(128);
                check(!retained.Buffer.IsAlive && reclaimedBuffer.Range.Equals(retained.BufferRange),
                    "dropping a material releases its buffer allocation for reuse");
                reclaimedBuffer.Destroy();
                reclaimedBuffer = null;
                reclaimed.Destroy();
                reclaimed = null;
            }

            LeakSubmittedContext(system, device, ref completion);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            system.Tick();
            context.BeginFrame();
            GPUCommandBuffer commands = context.EndFrame();
            completion.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
            context.MarkSubmitted(completion);
            completion.Wait();
            check(timeline.CompletedValue == completion.Value, "the system stays fully usable after a finalized render context reclaims itself");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            timeline?.Destroy();
            reclaimedBuffer?.Destroy();
            reclaimed?.Destroy();
            other?.Destroy();
            recycled?.Destroy();
            replacement?.Destroy();
            system?.Destroy();
        }
    }

    private static unsafe void CheckRecordedBufferLifetime(GPUDevice device, Action<bool, string> check, bool bundleDraw = false,
        bool restoreOverride = false)
    {
        RenderingSystem? system = null;
        Shader? shader = null;
        RenderContext? context = null;
        Texture? target = null;
        Buffer? replacement = null;
        Buffer? spare = null;
        Buffer? firstRecycled = null;
        Buffer? secondRecycled = null;
        Buffer? unexpected = null;
        GPUHeap readback = default;
        GPUTimelineSemaphore? timeline = null;
        TimelinePoint retirement = default;
        try
        {
            HeapPoolDesc bufferPool = new()
            {
                InitialHeapSize = 512,
                MaxHeapSize = 512,
                Budget = 512,
                InitialAllocationCapacity = 1,
            };
            HeapPoolDesc texturePool = new() { InitialHeapSize = 4 * 1024 * 1024 };
            system = new RenderingSystem(device, bufferPool, texturePool);
            using (ShaderCompiler compiler = new(device, new ShaderCompilerOptions
            {
                FileResolver = path => path.Replace('\\', '/').EndsWith("material.slang", StringComparison.Ordinal) ? MaterialShaderSource : null,
            }))
            {
                shader = new Shader(system, compiler, "material", name: "recorded_buffers");
            }
            Material material = shader.CreateMaterial("buffer_rebinding");
            Matrix4x4 identity = Matrix4x4.Identity;
            Vector4 tint = new(0.25f, 0.5f, 0.75f, 1);
            material.SetData("transform", ref identity);
            material.SetData("tint", ref tint);
            material.GetPipeline([Format.Rgba8Unorm], Format.Undefined);
            replacement = system.CreateBuffer(128);
            ReadOnlySpan<TestVertex> degenerate =
            [
                new() { Position = new Vector4(0, 0, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(0, 0, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(0, 0, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
            ];
            replacement.SetData(degenerate);
            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            readback = device.CreateGPUHeap(RecordingSize * RecordingSize * 4, MemoryType.Readback);
            context = new RenderContext(system, 256);
            timeline = device.CreateTimelineSemaphore();

            (WeakReference Vertices, WeakReference Indices, GPURange VertexRange, GPURange IndexRange, WeakReference? Bundle) recorded =
                RecordBufferPair(system, context, material, replacement, target.RenderView, bundleDraw, restoreOverride);
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            GPUCommandBuffer commands = context.EndFrame();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            system.Tick();
            bool retained = IsNotDestroyed(recorded.Vertices) && IsNotDestroyed(recorded.Indices) &&
                (!bundleDraw || IsNotDestroyed(recorded.Bundle));
            check(retained, bundleDraw
                ? "an ended frame protects its bundle and captured buffers after material rebinding and collection before MarkSubmitted"
                : "an ended frame protects recorded buffer A and its index buffer after the material is rebound to B and collected");
            // Never submit stale GPU ranges when the retention regression fails.
            if (!retained) return;
            spare = system.CreateBuffer(128);
            check(!spare.Range.Equals(recorded.VertexRange) && !spare.Range.Equals(recorded.IndexRange),
                "pre-MarkSubmitted collection cannot recycle recorded vertex or index storage");

            TimelinePoint actual = new() { Semaphore = timeline, Value = 2 };
            retirement = new TimelinePoint { Semaphore = timeline, Value = 3 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = actual });
            context.MarkSubmitted(retirement);
            actual.Wait();
            check(PixelMatches(readback.Range, Format.Rgba8Unorm, 8, 8, tint) &&
                PixelMatches(readback.Range, Format.Rgba8Unorm, 0, 0, new Vector4(0, 0, 0, 1)),
                "a draw recorded with A still renders its triangle after rebinding to degenerate buffer B");
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            system.Tick();
            bool released = bundleDraw
                ? IsNotDestroyed(recorded.Bundle)
                : IsNotDestroyed(recorded.Vertices) && IsNotDestroyed(recorded.Indices);
            check(released, bundleDraw
                ? "a finalized bundle remains protected after MarkSubmitted until its registered covering completion retires"
                : "finalized recorded buffers remain protected after MarkSubmitted until their registered covering completion retires");
            if (!released) return;
            bool exhausted = false;
            try
            {
                unexpected = system.CreateBuffer(128);
            }
            catch (InvalidOperationException)
            {
                exhausted = true;
            }
            check(exhausted && timeline.CompletedValue == actual.Value, bundleDraw
                ? "a finalized bundle keeps captured buffer storage unavailable until its registered covering completion retires"
                : "finalized recorded buffers keep their allocations until the registered covering completion retires");
            unexpected?.Destroy();
            unexpected = null;
            device.Submit(new SubmitDesc { Completion = retirement });
            context.Wait();
            system.Tick();
            if (bundleDraw)
            {
                // Reclaiming the finalized bundle makes its captured resource wrappers eligible for their own finalizers.
                for (int collection = 0; collection < 2; ++collection)
                {
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                    GC.WaitForPendingFinalizers();
                    system.Tick();
                }
                released = IsReclaimed(recorded.Bundle) && IsReclaimed(recorded.Vertices) && IsReclaimed(recorded.Indices);
                check(released, "completed bundle finalization releases the bundle and actual old vertex and index wrappers, not the rebound material's buffers");
                if (!released) return;
            }
            else
            {
                released = IsReclaimed(recorded.Vertices) && IsReclaimed(recorded.Indices);
                check(released, "Tick reclaims collected recorded buffers after the registered covering completion retires");
                if (!released) return;
            }
            firstRecycled = system.CreateBuffer(128);
            secondRecycled = system.CreateBuffer(128);
            check((firstRecycled.Range.Equals(recorded.VertexRange) && secondRecycled.Range.Equals(recorded.IndexRange)) ||
                (firstRecycled.Range.Equals(recorded.IndexRange) && secondRecycled.Range.Equals(recorded.VertexRange)),
                "registered completion retirement returns both recorded buffer allocations");
            firstRecycled.Destroy();
            firstRecycled = null;
            secondRecycled.Destroy();
            secondRecycled = null;

            for (int ended = 0; ended < 2; ++ended)
            {
                recorded = RecordBufferPair(system, context, material, replacement, target.RenderView, bundleDraw, restoreOverride);
                if (ended != 0) context.EndFrame();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                system.Tick();
                retained = IsNotDestroyed(recorded.Vertices) && IsNotDestroyed(recorded.Indices) &&
                    (!bundleDraw || IsNotDestroyed(recorded.Bundle));
                check(retained,
                    $"an {(ended == 0 ? "active" : "ended")} frame protects {(bundleDraw ? "its bundle and captured" : "rebound")} buffers until discard");
                if (!retained) return;
                context.DiscardFrame();
                for (int collection = 0; collection < (bundleDraw ? 3 : 1); ++collection)
                {
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                    GC.WaitForPendingFinalizers();
                    system.Tick();
                }
                released = IsReclaimed(recorded.Vertices) && IsReclaimed(recorded.Indices) &&
                    (!bundleDraw || IsReclaimed(recorded.Bundle));
                check(released,
                    $"discarding an {(ended == 0 ? "active" : "ended")} frame releases {(bundleDraw ? "bundle and buffers" : "buffers")} without a submit");
                if (!released) return;
                firstRecycled = system.CreateBuffer(128);
                secondRecycled = system.CreateBuffer(128);
                check((firstRecycled.Range.Equals(recorded.VertexRange) && secondRecycled.Range.Equals(recorded.IndexRange)) ||
                    (firstRecycled.Range.Equals(recorded.IndexRange) && secondRecycled.Range.Equals(recorded.VertexRange)),
                    "discarded buffer storage is immediately recyclable after collection and Tick");
                firstRecycled.Destroy();
                firstRecycled = null;
                secondRecycled.Destroy();
                secondRecycled = null;
            }
            GC.KeepAlive(material);
        }
        finally
        {
            if (retirement.Semaphore != null && retirement.Semaphore.CompletedValue < retirement.Value)
                device.Submit(new SubmitDesc { Completion = retirement });
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            shader?.Destroy();
            target?.Destroy();
            unexpected?.Destroy();
            secondRecycled?.Destroy();
            firstRecycled?.Destroy();
            spare?.Destroy();
            replacement?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            for (int collection = 0; collection < (bundleDraw ? 3 : 1); ++collection)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                system?.Tick();
            }
            system?.Destroy();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe (WeakReference Vertices, WeakReference Indices, GPURange VertexRange, GPURange IndexRange, WeakReference? Bundle) RecordBufferPair(
        RenderingSystem system, RenderContext context, Material material, Buffer replacement, GPURenderView renderView, bool bundleDraw,
        bool restoreOverride)
    {
        Buffer vertices = system.CreateBuffer(128);
        Buffer indices = system.CreateBuffer(128);
        ReadOnlySpan<TestVertex> triangle =
        [
            new() { Position = new Vector4(-0.75f, -0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
            new() { Position = new Vector4(0.75f, -0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
            new() { Position = new Vector4(0, 0.75f, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
        ];
        vertices.SetData(triangle);
        indices.SetData<ushort>([0, 1, 2]);
        material.SetBuffer("vertices", replacement);
        Material recordedMaterial = restoreOverride ? material.CreateInstance() : material;
        recordedMaterial.SetBuffer("vertices", vertices);
        RenderBundle? bundle = null;
        if (bundleDraw)
        {
            bundle = new RenderBundle(system, [Format.Rgba8Unorm]);
            bundle.DrawIndexed(recordedMaterial, indices, IndexType.UInt16, 3);
            recordedMaterial.SetBuffer("vertices", restoreOverride ? null : replacement);
            bundle.EndRecording();
        }
        context.BeginFrame();
        context.Barrier(Stage.Transfer | Stage.ColorOutput, Access.TransferRead | Access.ColorWrite, Stage.ColorOutput, Access.ColorWrite);
        using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
        {
            Colors = [new() { RenderView = renderView, Load = LoadOp.Clear }],
        }, [Format.Rgba8Unorm]))
        {
            pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
            pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
            if (bundle == null) pass.DrawIndexed(recordedMaterial, indices, IndexType.UInt16, 3);
            else pass.ExecuteBundle(bundle);
            recordedMaterial.SetBuffer("vertices", restoreOverride ? null : replacement);
        }
        return (new WeakReference(vertices, trackResurrection: true), new WeakReference(indices, trackResurrection: true),
            vertices.Range, indices.Range, bundle == null ? null : new WeakReference(bundle, trackResurrection: true));
    }

    private static bool IsNotDestroyed(WeakReference? reference) =>
        reference?.Target is RenderingResource { IsDestroyed: false };

    private static bool IsReclaimed(WeakReference? reference) =>
        reference?.Target is null or RenderingResource { IsDestroyed: true };

    private static void CheckReverseSubmissionRetirement(GPUDevice device, bool sharedTimeline, bool registerBeforeTick, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        RenderContext? first = null;
        RenderContext? second = null;
        RenderContext? later = null;
        GPUTimelineSemaphore? firstTimeline = null;
        GPUTimelineSemaphore? secondTimeline = null;
        GPUTimelineSemaphore? laterTimeline = null;
        Texture? blockedTexture = null;
        Sampler? blockedSampler = null;
        Texture? recycledTexture = null;
        Sampler? recycledSampler = null;
        TimelinePoint retirement = default;
        TimelinePoint laterRetirement = default;
        string label = (sharedTimeline ? "shared timeline" : "distinct timelines") + (registerBeforeTick ? ", before first Tick" : ", after first Tick");
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024);
            first = new RenderContext(system, 256);
            second = new RenderContext(system, 256);
            later = new RenderContext(system, 256);
            firstTimeline = device.CreateTimelineSemaphore();
            if (!sharedTimeline) secondTimeline = device.CreateTimelineSemaphore();
            GPUTimelineSemaphore secondSemaphore = secondTimeline ?? firstTimeline;
            laterTimeline = device.CreateTimelineSemaphore();
            TimelinePoint firstActual = new() { Semaphore = firstTimeline, Value = 1 };
            TimelinePoint secondActual = new() { Semaphore = secondSemaphore, Value = sharedTimeline ? 2UL : 1UL };
            retirement = new TimelinePoint { Semaphore = secondSemaphore, Value = secondActual.Value + 1 };
            // Record before collection: BeginFrame itself ticks, but registration must also be tested before the first collecting Tick.
            later.BeginFrame();
            GPUCommandBuffer laterCommands = later.EndFrame();
            TimelinePoint laterActual = new() { Semaphore = laterTimeline, Value = 1 };
            laterRetirement = new TimelinePoint { Semaphore = laterTimeline, Value = 2 };
            (uint TextureSlot, uint SamplerSlot) leaked = LeakReverseSubmittedPair(system, first, second, device,
                firstActual, secondActual, retirement);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            if (registerBeforeTick)
            {
                device.Submit(new SubmitDesc { Commands = [laterCommands], Completion = laterActual });
                later.MarkSubmitted(laterRetirement);
                laterActual.Wait();
            }
            system.Tick();
            blockedTexture = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(2, 2, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });
            blockedSampler = system.CreateSampler(new SamplerDesc());
            check(secondSemaphore.CompletedValue == secondActual.Value && blockedTexture.SampledIndex != leaked.TextureSlot &&
                blockedSampler.Index != leaked.SamplerSlot,
                $"reverse MarkSubmitted preserves texture and sampler slots until B's future retirement point ({label})");

            if (!registerBeforeTick)
            {
                device.Submit(new SubmitDesc { Commands = [laterCommands], Completion = laterActual });
                later.MarkSubmitted(laterRetirement);
                laterActual.Wait();
            }
            device.Submit(new SubmitDesc { Completion = retirement });
            first.Wait();
            second.Wait();
            system.Tick();
            recycledTexture = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(2, 2, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });
            recycledSampler = system.CreateSampler(new SamplerDesc());
            check(recycledTexture.SampledIndex == leaked.TextureSlot && recycledSampler.Index == leaked.SamplerSlot &&
                laterTimeline.CompletedValue == laterActual.Value,
                $"B retirement releases both slots without waiting for an unrelated post-snapshot submission ({label})");
        }
        finally
        {
            if (retirement.Semaphore != null && retirement.Semaphore.CompletedValue < retirement.Value)
                device.Submit(new SubmitDesc { Completion = retirement });
            if (laterRetirement.Semaphore != null && laterRetirement.Semaphore.CompletedValue < laterRetirement.Value)
                device.Submit(new SubmitDesc { Completion = laterRetirement });
            device.WaitIdle();
            first?.DiscardFrame();
            first?.Destroy();
            second?.DiscardFrame();
            second?.Destroy();
            later?.DiscardFrame();
            later?.Destroy();
            firstTimeline?.Destroy();
            secondTimeline?.Destroy();
            laterTimeline?.Destroy();
            recycledSampler?.Destroy();
            recycledTexture?.Destroy();
            blockedSampler?.Destroy();
            blockedTexture?.Destroy();
            system?.Destroy();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (uint TextureSlot, uint SamplerSlot) LeakReverseSubmittedPair(RenderingSystem system, RenderContext first, RenderContext second,
        GPUDevice device, TimelinePoint firstActual, TimelinePoint secondActual, TimelinePoint retirement)
    {
        Texture texture = system.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(4, 4, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled,
        });
        Sampler sampler = system.CreateSampler(new SamplerDesc());
        first.BeginFrame();
        GPUCommandBuffer firstCommands = first.EndFrame();
        second.BeginFrame();
        GPUCommandBuffer secondCommands = second.EndFrame();
        device.Submit(new SubmitDesc { Commands = [firstCommands], Completion = firstActual });
        device.Submit(new SubmitDesc { Commands = [secondCommands], Completion = secondActual });
        second.MarkSubmitted(retirement);
        first.MarkSubmitted(firstActual);
        // The deliberately conservative retirement must not hide genuinely executing initialization from a broken collector.
        secondActual.Wait();
        firstActual.Wait();
        return (texture.SampledIndex, sampler.Index);
    }

    private enum TimelineRetirement
    {
        Wait,
        SlotReuse,
        Destroy,
    }

    private static void CheckBorrowedTimelineLifetime(GPUDevice device, TimelineRetirement retirement, Action<bool, string> check)
    {
        RenderingSystem? system = null;
        RenderContext? context = null;
        GPUTimelineSemaphore? reuseTimeline = null;
        Texture? firstTexture = null;
        Texture? secondTexture = null;
        Sampler? firstSampler = null;
        Sampler? secondSampler = null;
        try
        {
            system = new RenderingSystem(device, 4 * 1024 * 1024);
            context = new RenderContext(system, 256);
            if (retirement == TimelineRetirement.SlotReuse) reuseTimeline = device.CreateTimelineSemaphore();
            (WeakReference Timeline, uint PendingTexture, uint PendingSampler, uint LaterTexture, uint LaterSampler) leaked =
                RetireBorrowedTimeline(system, context, device, reuseTimeline, retirement);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            bool timelineReleased = !leaked.Timeline.IsAlive;
            check(timelineReleased, $"{retirement} drops every borrowed reference to the destroyed timeline while the context stays reachable");
            // Old collectors may retain a destroyed native handle; never let their failure become a driver crash.
            if (!timelineReleased) return;
            system.Tick();
            firstTexture = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(2, 2, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });
            secondTexture = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(2, 2, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });
            firstSampler = system.CreateSampler(new SamplerDesc());
            secondSampler = system.CreateSampler(new SamplerDesc());
            check((firstTexture.SampledIndex == leaked.PendingTexture && secondTexture.SampledIndex == leaked.LaterTexture) ||
                (secondTexture.SampledIndex == leaked.PendingTexture && firstTexture.SampledIndex == leaked.LaterTexture),
                $"{retirement} permits Tick to reclaim pending and newly finalized textures after timeline destruction");
            check((firstSampler.Index == leaked.PendingSampler && secondSampler.Index == leaked.LaterSampler) ||
                (secondSampler.Index == leaked.PendingSampler && firstSampler.Index == leaked.LaterSampler),
                $"{retirement} permits Tick to reclaim pending and newly finalized samplers after timeline destruction");
            GC.KeepAlive(context);
        }
        finally
        {
            device.WaitIdle();
            if (context != null && !context.IsDestroyed)
            {
                context.DiscardFrame();
                context.Destroy();
            }
            reuseTimeline?.Destroy();
            firstSampler?.Destroy();
            secondSampler?.Destroy();
            firstTexture?.Destroy();
            secondTexture?.Destroy();
            system?.Destroy();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Timeline, uint PendingTexture, uint PendingSampler, uint LaterTexture, uint LaterSampler)
        RetireBorrowedTimeline(RenderingSystem system, RenderContext context, GPUDevice device, GPUTimelineSemaphore? reuseTimeline,
            TimelineRetirement retirement)
    {
        GPUTimelineSemaphore? timeline = null;
        RenderContext? marker = null;
        Texture? laterTexture = null;
        Sampler? laterSampler = null;
        bool leakLater = false;
        try
        {
            timeline = device.CreateTimelineSemaphore();
            if (retirement == TimelineRetirement.SlotReuse) marker = new RenderContext(system, 256);
            laterTexture = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(4, 4, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });
            laterSampler = system.CreateSampler(new SamplerDesc());
            TimelinePoint actual = new() { Semaphore = timeline, Value = 1 };
            TimelinePoint retired = new() { Semaphore = timeline, Value = 2 };
            (uint TextureSlot, uint SamplerSlot) pending = LeakBorrowedTimelinePair(system, context, marker, device,
                reuseTimeline, laterTexture, actual, retired);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            system.Tick();
            device.Submit(new SubmitDesc { Completion = retired });
            retired.Wait();
            marker?.Wait();
            marker?.Destroy();
            marker = null;
            switch (retirement)
            {
                case TimelineRetirement.Wait:
                    context.Wait();
                    break;
                case TimelineRetirement.SlotReuse:
                    context.BeginFrame();
                    context.DiscardFrame();
                    break;
                case TimelineRetirement.Destroy:
                    context.Destroy();
                    break;
            }
            WeakReference reference = new(timeline);
            timeline.Destroy();
            timeline = null;
            leakLater = true;
            return (reference, pending.TextureSlot, pending.SamplerSlot, laterTexture.SampledIndex, laterSampler.Index);
        }
        finally
        {
            if (timeline != null)
            {
                if (timeline.CompletedValue < 2)
                    device.Submit(new SubmitDesc { Completion = new TimelinePoint { Semaphore = timeline, Value = 2 } });
                device.WaitIdle();
                marker?.DiscardFrame();
                marker?.Destroy();
                if (!context.IsDestroyed)
                {
                    context.DiscardFrame();
                    context.Wait();
                }
                timeline.Destroy();
            }
            if (!leakLater)
            {
                laterSampler?.Destroy();
                laterTexture?.Destroy();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (uint TextureSlot, uint SamplerSlot) LeakBorrowedTimelinePair(RenderingSystem system, RenderContext context, RenderContext? marker,
        GPUDevice device, GPUTimelineSemaphore? reuseTimeline, Texture laterTexture, TimelinePoint actual, TimelinePoint retired)
    {
        Texture texture = system.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(4, 4, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled,
        });
        Sampler sampler = system.CreateSampler(new SamplerDesc());
        context.BeginFrame();
        GPUCommandBuffer commands = context.EndFrame();
        device.Submit(new SubmitDesc { Commands = [commands], Completion = actual });
        context.MarkSubmitted(retired);
        actual.Wait();
        if (marker != null && reuseTimeline != null)
        {
            context.BeginFrame();
            commands = context.EndFrame();
            TimelinePoint other = new() { Semaphore = reuseTimeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Completion = other });
            context.MarkSubmitted(other);
            // Keep the borrowed point last without leaving the other slot dependent on the timeline under test.
            marker.BeginFrame();
            commands = marker.EndFrame();
            other.Value++;
            device.Submit(new SubmitDesc { Commands = [commands], Completion = other });
            marker.MarkSubmitted(retired);
            other.Wait();
        }
        return (texture.SampledIndex, sampler.Index);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (uint TextureSlot, uint SamplerSlot) LeakSubmittedPair(RenderingSystem system, RenderContext context, GPUDevice device,
        ref TimelinePoint completion)
    {
        Texture leaked = system.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(4, 4, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        Sampler leakedSampler = system.CreateSampler(new SamplerDesc());
        context.BeginFrame();
        GPUCommandBuffer commands = context.EndFrame();
        completion.Value++;
        device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
        context.MarkSubmitted(completion);
        return (leaked.SampledIndex, leakedSampler.Index);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe (uint TextureSlot, WeakReference Buffer, GPURange BufferRange) CreateRetainedMaterial(RenderingSystem system,
        ShaderCompiler compiler)
    {
        Shader shader = new(system, compiler, "material", name: "gc_test");
        Material material = shader.CreateMaterial("gc_retention");
        Texture retained = system.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(4, 4, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        Buffer buffer = system.CreateBuffer(128);
        material.SetBuffer("vertices", buffer);
        material.SetTexture("albedo", retained);
        material.GetPipeline([Format.Rgba8Unorm], Format.Undefined);
        s_retainedMaterial = material;
        return (retained.SampledIndex, new WeakReference(buffer), buffer.Range);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LeakSubmittedContext(RenderingSystem system, GPUDevice device, ref TimelinePoint completion)
    {
        RenderContext extra = new RenderContext(system, 256);
        extra.BeginFrame();
        GPUCommandBuffer extraCommands = extra.EndFrame();
        completion.Value++;
        device.Submit(new SubmitDesc { Commands = [extraCommands], Completion = completion });
        extra.MarkSubmitted(completion);
    }

    private static void CheckDepthStencilEquality(Action<bool, string> check)
    {
        DepthStencilState original = new()
        {
            DepthTest = true,
            DepthWrite = true,
            DepthCompare = CompareOp.Less,
            StencilTest = true,
            StencilReadMask = 0x5a,
            StencilWriteMask = 0xa5,
            Front = new StencilFaceState
            {
                Compare = CompareOp.LessEqual,
                Fail = StencilOp.Replace,
                Pass = StencilOp.IncrementWrap,
                DepthFail = StencilOp.DecrementWrap,
                Reference = 23,
            },
            Back = new StencilFaceState
            {
                Compare = CompareOp.Equal,
                Fail = StencilOp.Invert,
                DepthFail = StencilOp.Zero,
                Reference = 47,
            },
        };
        DepthStencilState copy = original;
        check(original == copy && copy == original && original.Equals(copy) &&
            original.GetHashCode() == copy.GetHashCode(),
            "identical depth/stencil states compare equal in both directions");
        ReadOnlySpan<string> fields =
        [
            "DepthTest", "DepthWrite", "DepthCompare", "StencilTest", "StencilReadMask", "StencilWriteMask",
            "Front.Compare", "Front.Fail", "Front.Pass", "Front.DepthFail", "Front.Reference",
            "Back.Compare", "Back.Fail", "Back.Pass", "Back.DepthFail", "Back.Reference",
        ];
        for (int field = 0; field < fields.Length; ++field)
        {
            DepthStencilState changed = original;
            switch (field)
            {
                case 0: changed.DepthTest = false; break;
                case 1: changed.DepthWrite = false; break;
                case 2: changed.DepthCompare = CompareOp.Greater; break;
                case 3: changed.StencilTest = false; break;
                case 4: changed.StencilReadMask ^= 1; break;
                case 5: changed.StencilWriteMask ^= 1; break;
                case 6: changed.Front.Compare = CompareOp.Greater; break;
                case 7: changed.Front.Fail = StencilOp.Zero; break;
                case 8: changed.Front.Pass = StencilOp.IncrementClamp; break;
                case 9: changed.Front.DepthFail = StencilOp.DecrementClamp; break;
                case 10: changed.Front.Reference ^= 1; break;
                case 11: changed.Back.Compare = CompareOp.Never; break;
                case 12: changed.Back.Fail = StencilOp.Replace; break;
                case 13: changed.Back.Pass = StencilOp.Zero; break;
                case 14: changed.Back.DepthFail = StencilOp.Keep; break;
                case 15: changed.Back.Reference ^= 1; break;
            }
            check(original != changed && changed != original && !(original == changed) && !original.Equals(changed),
                $"depth/stencil equality independently distinguishes {fields[field]}");
        }
    }

    private static bool PixelMatches(GPUAllocation<byte> pixels, Format format, uint x, uint y, Vector4 expected)
    {
        ReadOnlySpan<byte> bytes = pixels.CpuSpan;
        int pixel = (int)((y * RecordingSize + x) * 4);
        int red = format == Format.Bgra8Unorm ? 2 : 0;
        int blue = format == Format.Bgra8Unorm ? 0 : 2;
        return Math.Abs(bytes[pixel + red] - expected.X * 255) <= 1 && Math.Abs(bytes[pixel + 1] - expected.Y * 255) <= 1 &&
            Math.Abs(bytes[pixel + blue] - expected.Z * 255) <= 1 && Math.Abs(bytes[pixel + 3] - expected.W * 255) <= 1;
    }

    private static void CheckPipelineCaching(Material material, GPUPipeline original, Action<bool, string> check)
    {
        ReadOnlySpan<Format> colorFormats = [Format.Rgba8Unorm];
        material.GetPipeline(colorFormats, Format.D32Float);

        BlendState blend = material.Blend;
        material.Blend = new BlendState { Enabled = true };
        check(material.GetPipeline(colorFormats, Format.D32Float) != original, "changing blend state replaces the pipeline for the same pass formats");
        material.Blend = blend;
        check(material.GetPipeline(colorFormats, Format.D32Float) == original, "restoring blend state reuses the original pipeline");

        byte writeMask = material.WriteMask;
        material.WriteMask = 0;
        check(material.GetPipeline(colorFormats, Format.D32Float) != original, "changing the write mask replaces the pipeline for the same pass formats");
        material.WriteMask = writeMask;
        check(material.GetPipeline(colorFormats, Format.D32Float) == original, "restoring the write mask reuses the original pipeline");

        RasterizationState rasterization = material.Rasterization;
        material.Rasterization = new RasterizationState { Cull = CullMode.Clockwise };
        check(material.GetPipeline(colorFormats, Format.D32Float) != original, "changing rasterization replaces the pipeline for the same pass formats");
        material.Rasterization = new RasterizationState { Fill = FillMode.Wireframe };
        check(material.GetPipeline(colorFormats, Format.D32Float) != original, "changing rasterization fill mode replaces the pipeline for the same pass formats");
        material.Rasterization = rasterization;
        check(material.GetPipeline(colorFormats, Format.D32Float) == original, "restoring rasterization reuses the original pipeline");
        Span<RasterizationState> paddedState = stackalloc RasterizationState[1];
        MemoryMarshal.AsBytes(paddedState).Fill(0xa5);
        ref RasterizationState paddedRasterization = ref paddedState[0];
        paddedRasterization.Cull = rasterization.Cull;
        paddedRasterization.Fill = rasterization.Fill;
        paddedRasterization.DepthBiasConstant = rasterization.DepthBiasConstant;
        paddedRasterization.DepthBiasClamp = rasterization.DepthBiasClamp;
        paddedRasterization.DepthBiasSlope = rasterization.DepthBiasSlope;
        material.Rasterization = paddedRasterization;
        check(material.GetPipeline(colorFormats, Format.D32Float) == original, "rasterization padding does not change the pipeline signature");
        material.Rasterization = rasterization;

        PrimitiveTopology topology = material.Topology;
        material.Topology = PrimitiveTopology.TriangleStrip;
        check(material.GetPipeline(colorFormats, Format.D32Float) != original, "changing topology replaces the pipeline for the same pass formats");
        material.Topology = topology;
        check(material.GetPipeline(colorFormats, Format.D32Float) == original, "restoring topology reuses the original pipeline");

        ReadOnlySpan<Format> alternateFormats = [Format.Rgba16Float];
        ReadOnlySpan<Format> multipleFormats = [Format.Rgba8Unorm, Format.Rgba8Unorm];
        GPUPipeline alternate = material.GetPipeline(alternateFormats, Format.Undefined);
        GPUPipeline multiple = material.GetPipeline(multipleFormats, Format.D32Float);
        bool reused = original != alternate && original != multiple && alternate != multiple;
        for (int index = 0; index < 128; ++index)
        {
            reused &= material.GetPipeline(colorFormats, Format.D32Float) == original;
            reused &= material.GetPipeline(alternateFormats, Format.Undefined) == alternate;
            reused &= material.GetPipeline(multipleFormats, Format.D32Float) == multiple;
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1024; ++index)
        {
            reused &= material.GetPipeline(colorFormats, Format.D32Float) == original;
            reused &= material.GetPipeline(alternateFormats, Format.Undefined) == alternate;
            reused &= material.GetPipeline(multipleFormats, Format.D32Float) == multiple;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        check(reused, "alternating pass formats and attachment counts reuse their cached pipelines");
        check(allocated == 0, $"warmed alternating material pipeline queries allocate zero managed bytes (actual={allocated})");
    }

    private static void CheckVariantLayoutsAndCaching(RenderingSystem system, Buffer buffer, Buffer overrideBuffer, Texture texture,
        Action<bool, string> check)
    {
        const string module = "material_variants_test";
        const string source = """
            module material_variants_test;
            import Core;

            public interface IShape { associatedtype Params; };
            public struct SmallParams
            {
                public uint Padding;
                public GPUBufferView Values;
                public GpuTextureId Albedo;
            };
            public struct LargeParams
            {
                public uint Padding[3];
                public GpuTextureId Detail;
                public GPUBufferView Values;
                public GpuTextureId Albedo;
                public GpuSamplerId Sampler;
            };
            public struct SmallShape : IShape { public typedef SmallParams Params; };
            public struct LargeShape : IShape { public typedef LargeParams Params; };

            [GPURoot]
            public struct VariantRoot<T : IShape>
            {
                public T.Params Shape;
                public uint Marker;
            };

            [shader("vertex")]
            float4 vertexMain<T : IShape, let N : int, let M : int>(uint vertex : SV_VertexID) : SV_Position
            {
                VariantRoot<T> root = gpu_load_root<VariantRoot<T>>();
                return float4(root.Marker, N, M, 1);
            }
            """;

        Shader shader = system.Shaders.GetShaderFromSource(module, module + ".slang", source);
        ShaderVariant first = shader.CreateMaterial("first_expression", "SmallShape", "1|2", "4").Variant;
        ShaderVariant second = shader.CreateMaterial("second_expression", "SmallShape", "1", "2|4").Variant;
        check(first != second && !first.Compiled.GetStage(ShaderStageKind.Vertex).Code.Span.SequenceEqual(
            second.Compiled.GetStage(ShaderStageKind.Vertex).Code.Span), "specialization arguments containing pipes can compile to distinct variants");
        check(first == shader.CreateMaterial("first_again", "SmallShape", "1|2", 4).Variant &&
            second == shader.CreateMaterial("second_again", "SmallShape", 1, "2|4").Variant,
            "materials with identical normalized specialization arguments share the same variant");

        Material parent = shader.CreateMaterial("variant_parent", "SmallShape", 1, 3);
        uint parentMarker = 99;
        uint localMarker = 77;
        parent.SetData("Marker", ref parentMarker);
        parent.SetBuffer("Shape.Values", buffer, 16);
        parent.SetTexture("Shape.Albedo", texture);
        int smallMarker = (int)parent.Root.Find("Marker")!.Offset;
        int smallValues = (int)parent.Root.Find("Shape.Values")!.Offset;
        int smallAlbedo = (int)parent.Root.Find("Shape.Albedo")!.Offset;

        MaterialInstance independent = parent.CreateInstance();
        independent.SetSpecializations("LargeShape", 1, 3);
        int largeMarker = (int)independent.Root.Find("Marker")!.Offset;
        RootField largeValues = independent.Root.Find("Shape.Values")!;
        RootField largeAlbedo = independent.Root.Find("Shape.Albedo")!;
        RootField detail = independent.Root.Find("Shape.Detail")!;
        check(largeMarker > smallMarker && detail.Offset == (uint)smallAlbedo, "the generic root specialization moves fields and reuses offsets");
        check(ReadUInt(independent, largeMarker) == parentMarker &&
            ReadRoot<GPUBufferView>(independent, (int)largeValues.Offset).Equals(buffer.View.OffsetBy(16)) &&
            ReadUInt(independent, (int)largeAlbedo.Offset) == texture.SampledIndex &&
            independent.GetBinding(largeValues) == buffer && independent.GetBinding(largeAlbedo) == texture,
            "an independent larger root inherits matching data and resource views by name, including the buffer byte offset");
        List<RenderingResource> retained = [];
        independent.ResourceOwner.RetainResources(retained);
        check(ReadUInt(independent, (int)detail.Offset) == system.TextureWhite.SampledIndex && independent.GetBinding(detail) == null &&
            ReadUInt(independent, (int)independent.Root.Find("Shape.Sampler")!.Offset) == system.SamplerDefault.Index &&
            retained.Contains(system.TextureWhite) && retained.Contains(texture) && retained.Contains(buffer),
            "unmatched resource fields use their own defaults even when their offsets match other parent fields");
        Span<byte> snapshot = stackalloc byte[(int)independent.Root.SizeBytes];
        Span<byte> partial = stackalloc byte[8];
        int partialOffset = (int)detail.Offset + 1;
        independent.CopyRoot(snapshot);
        independent.CopyRoot(partial, partialOffset);
        check(partial.SequenceEqual(snapshot.Slice(partialOffset, partial.Length)),
            "a partial cross-layout root copy preserves both unmatched defaults and the shifted inherited buffer view");
        independent.SetData("Marker", ref localMarker);

        MaterialInstance live = parent.CreateInstance();
        live.SetData("Marker", ref localMarker);
        live.SetBuffer("Shape.Values", overrideBuffer, 32);
        MaterialInstance rollback = live.CreateInstance();
        uint rollbackMarker = 88;
        rollback.SetData("Marker", ref rollbackMarker);
        rollback.WriteMask = 5;
        ShaderVariant initial = parent.Variant;
        parent.SetSpecializations("SmallShape", 3, 1);

        Span<byte> before = stackalloc byte[(int)rollback.Root.SizeBytes];
        Span<byte> after = stackalloc byte[before.Length];
        rollback.CopyRoot(before);
        uint version = rollback.Version;
        uint resourceVersion = rollback.ResourceVersion;
        bool rejected = false;
        try
        {
            rollback.SetSpecializations("SmallShape", "NoSuchValue", 1);
        }
        catch (Exception exception) when (exception is ShaderCompilationException or InvalidOperationException)
        {
            rejected = true;
        }
        rollback.CopyRoot(after);
        check(rejected && parent.Variant != initial && parent.Root.MatchesLayout(initial.Root) && rollback.Variant == parent.Variant &&
            rollback.Specializations[1] == "3" && rollback.Specializations[2] == "1" && rollback.Version == version &&
            rollback.ResourceVersion == resourceVersion && rollback.WriteMask == 5 && before.SequenceEqual(after),
            "a failed same-layout specialization preserves instance state and the currently inherited variant");

        parent.SetSpecializations("LargeShape", 1, 3);
        check(live.Variant == parent.Variant && ReadUInt(live, largeMarker) == localMarker &&
            ReadRoot<GPUBufferView>(live, (int)largeValues.Offset).Equals(overrideBuffer.View.OffsetBy(32)) &&
            live.GetBinding(largeValues) == overrideBuffer && ReadUInt(live, (int)largeAlbedo.Offset) == texture.SampledIndex,
            "existing instances keep their named data and resource overrides when the inherited layout grows");

        rollbackMarker = 123;
        rollback.SetData("Marker", ref rollbackMarker);
        live.SetBuffer("Shape.Values", null);
        check(rollback.Variant == parent.Variant && ReadUInt(rollback, largeMarker) == rollbackMarker && ReadUInt(live, largeMarker) == localMarker &&
            ReadRoot<GPUBufferView>(rollback, (int)largeValues.Offset).Equals(buffer.View.OffsetBy(16)),
            "instances can write new overrides after an inherited layout change and clear moved resource overrides");

        independent.SetSpecializations("SmallShape", 1, 3);
        check(independent.Root.SizeBytes < parent.Root.SizeBytes && ReadUInt(independent, smallMarker) == localMarker &&
            ReadRoot<GPUBufferView>(independent, smallValues).Equals(buffer.View.OffsetBy(16)) &&
            ReadUInt(independent, smallAlbedo) == texture.SampledIndex,
            "switching to a smaller local layout keeps named overrides and inherits matching fields from the larger parent");
    }

    private static unsafe void CheckShaderSystemCompositionAndSpecialization(RenderingSystem system, Action<bool, string> check)
    {
        const string contractSrc = """
            module mat_test_contract;
            import Core;
            public interface IMatTestSurface
            {
                associatedtype Params;
                float4 Eval(Params p);
            };
            """;
        const string computeTemplateSrc = """
            module mat_test_cs_template;
            import Core;
            import mat_test_contract;

            [GPURoot]
            public struct ComputePassRoot<T : IMatTestSurface>
            {
                public GPUBufferView Output;
                public T.Params Material;
            };

            [shader("compute")]
            [numthreads(1, 1, 1)]
            void computeMain<T : IMatTestSurface, let scale : int, let invert : bool>(uint3 tid : SV_DispatchThreadID)
            {
                ComputePassRoot<T> root = gpu_load_root<ComputePassRoot<T>>();
                T surface;
                float4 value = surface.Eval(root.Material) * float(scale);
                if (invert)
                    value = float4(1.0) - value;
                gpu_store<float4>(root.Output, tid.x, value);
            }
            """;
        const string graphicsTemplateSrc = """
            module mat_test_gfx_template;
            import Core;
            import mat_test_contract;

            [GPURoot]
            public struct GraphicsPassRoot<T : IMatTestSurface>
            {
                public T.Params Material;
            };

            struct VsOut
            {
                float4 Position : SV_Position;
            };

            [shader("vertex")]
            VsOut vertexMain<T : IMatTestSurface>(uint vertex : SV_VertexID)
            {
                VsOut o;
                o.Position = float4(float2((vertex << 1) & 2, vertex & 2) * 2.0 - 1.0, 0.5, 1.0);
                return o;
            }

            [shader("pixel")]
            float4 pixelMain<T : IMatTestSurface, let doubleOutput : bool>(VsOut input) : SV_Target0
            {
                GraphicsPassRoot<T> root = gpu_load_root<GraphicsPassRoot<T>>();
                T surface;
                float4 color = surface.Eval(root.Material);
                return doubleOutput ? color * 2.0 : color;
            }
            """;
        const string surfaceSrc = """
            module mat_test_surface;
            import Core;
            import mat_test_contract;

            public struct TestSurfaceParams
            {
                public GpuTextureId Albedo;
                public GpuSamplerId Sampler;
                public float4 Tint;
            };

            public struct TestSurface : IMatTestSurface
            {
                public typedef TestSurfaceParams Params;
                public float4 Eval(TestSurfaceParams p)
                {
                    float4 tex = gpu_texture<Texture2D>(p.Albedo.index).SampleLevel(gpu_sampler(p.Sampler.index), float2(0.5, 0.5), 0);
                    return tex * p.Tint;
                }
            };
            """;

        Dictionary<string, string> vfs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["mat_test_contract.slang"] = contractSrc,
            ["mat_test_cs_template.slang"] = computeTemplateSrc,
            ["mat_test_gfx_template.slang"] = graphicsTemplateSrc,
            ["mat_test_surface.slang"] = surfaceSrc,
        };

        string? ResolveVirtual(string path)
        {
            string fileName = Path.GetFileName(path);
            return vfs.TryGetValue(fileName, out string? src) ? src : null;
        }

        using ShaderSystem shaders = system.CreateShaderSystem(new ShaderCompilerOptions
        {
            SearchPaths = ["/virtual"],
            FileResolver = ResolveVirtual,
            PathExists = path => ResolveVirtual(path) != null,
        });

        Shader computeComposed = shaders.Compose("mat_test_cs_template", "mat_test_surface");
        Shader computeComposedSame = shaders.Compose("mat_test_cs_template", "mat_test_surface");
        Shader graphicsComposed = shaders.Compose("mat_test_gfx_template", "mat_test_surface");
        check(ReferenceEquals(computeComposed, computeComposedSame), "ShaderSystem interns composed shaders by (template, surface, kind)");
        check(computeComposed.Kind == ShaderKind.Compute && graphicsComposed.Kind == ShaderKind.Graphics,
            "ShaderSystem.Compose infers Compute and Graphics shader kinds automatically");

        Buffer? outputBuffer = null;
        Texture? target = null;
        GPUHeap readback = default;
        RenderContext? context = null;
        try
        {
            outputBuffer = system.CreateBuffer(64);
            Material computeMat = computeComposed.CreateMaterial("cs_spec", 2, false);
            check(computeMat.Specializations.Count == 2 && computeMat.Specializations[0] == "2" && computeMat.Specializations[1] == "false",
                "CreateMaterial normalizes C# specialization arguments into canonical Slang literals");
            check(ReadUInt(computeMat, (int)computeMat.Root.Find("Material.Albedo")!.Offset) == system.TextureWhite.SampledIndex &&
                ReadUInt(computeMat, (int)computeMat.Root.Find("Material.Sampler")!.Offset) == system.SamplerDefault.Index,
                "Composed material initializes nested texture and sampler fields inside Material.Params to system defaults");

            Vector4 tint = new(0.25f, 0.1f, 0.2f, 0.5f);
            computeMat.SetBuffer("Output", outputBuffer);
            computeMat.SetData("Material.Tint", ref tint);

            MaterialInstance instance = computeMat.CreateInstance();
            instance.SetSpecializations(3, true);
            instance.SetBuffer("Output", outputBuffer, 16);
            check(instance.Specializations[0] == "3" && instance.Specializations[1] == "true" &&
                computeMat.Specializations[0] == "2" && computeMat.Specializations[1] == "false",
                "MaterialInstance overrides specializations independently of its parent");

            context = new RenderContext(system);
            readback = system.Device.CreateGPUHeap(64, MemoryType.Readback);
            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                compute.Dispatch(computeMat, new Extent3D(1, 1, 1));
                compute.Dispatch(instance, new Extent3D(1, 1, 1));
            }
            context.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer, Access.TransferRead);
            context.CopyMemory(outputBuffer.Range, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit();
            context.Wait();

            Vector4 firstResult = MemoryMarshal.Read<Vector4>(readback.Range.CpuSpan);
            Vector4 instanceResult = MemoryMarshal.Read<Vector4>(readback.Range.CpuSpan[16..]);
            check(MathF.Abs(firstResult.X - 0.5f) < 1e-4f && MathF.Abs(firstResult.Y - 0.2f) < 1e-4f &&
                MathF.Abs(firstResult.Z - 0.4f) < 1e-4f && MathF.Abs(firstResult.W - 1.0f) < 1e-4f,
                $"Composed compute material (2, false) evaluates expected GPU output ({firstResult})");

            // Switch specialization on the same material without re-binding Output or Material.Tint (scale=3, invert=true): expected = (0.25, 0.7, 0.4, -0.5)
            computeMat.SetSpecializations(3, true);
            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
                compute.Dispatch(computeMat, new Extent3D(1, 1, 1));
            context.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer, Access.TransferRead);
            context.CopyMemory(outputBuffer.Range, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit();
            context.Wait();

            Vector4 secondResult = MemoryMarshal.Read<Vector4>(readback.Range.CpuSpan);
            check(MathF.Abs(secondResult.X - 0.25f) < 1e-4f && MathF.Abs(secondResult.Y - 0.7f) < 1e-4f &&
                MathF.Abs(secondResult.Z - 0.4f) < 1e-4f && MathF.Abs(secondResult.W - (-0.5f)) < 1e-4f,
                $"Material.SetSpecializations(3, true) preserves bound buffer/data and switches GPU variant ({secondResult})");
            check(instanceResult == secondResult, $"dispatching two variants in one compute scope binds the instance variant ({instanceResult})");

            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(4, 4, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });

            Material gfxMat = graphicsComposed.CreateMaterial("gfx_spec", false);
            Vector4 gfxTint = new(0.25f, 0.5f, 0.0f, 1.0f);
            gfxMat.SetData("Material.Tint", ref gfxTint);
            gfxMat.GetPipeline([Format.Rgba8Unorm], Format.Undefined);
            graphicsComposed.GetPipeline(gfxMat.State, [Format.Rgba8Unorm], Format.Undefined, Format.Undefined, true);

            context.BeginFrame();
            GPURange packedRoot = gfxMat.AllocateRoot(context.FrameData).GPURange;
            using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
            {
                Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
            }, [Format.Rgba8Unorm]))
            {
                pass.SetViewport(new Viewport { Width = 4, Height = 4 });
                pass.SetScissor(new Scissor { Width = 2, Height = 4 });
                pass.Draw(gfxMat, packedRoot, default, 3);
                gfxMat.SetSpecializations(true);
                pass.SetScissor(new Scissor { X = 2, Width = 2, Height = 4 });
                pass.Draw(gfxMat, 3);
            }
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit();
            context.Wait();

            ReadOnlySpan<byte> pixels = readback.Range.CpuSpan;
            byte r1 = pixels[0];
            byte g1 = pixels[1];
            byte r2 = pixels[8];
            byte g2 = pixels[9];
            check(Math.Abs(r1 - 64) <= 2 && Math.Abs(g1 - 128) <= 2 && Math.Abs(r2 - 128) <= 2 && Math.Abs(g2 - 255) <= 2,
                $"prepacked draw without a default variant ({r1},{g1}) and a same-scope specialization switch ({r2},{g2}) render expected colors");
        }
        finally
        {
            system.Device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            target?.Destroy();
            outputBuffer?.Destroy();
            readback.Destroy();
        }
    }

    private static unsafe void CheckAutomaticPipelinesAndWarmUp(GPUDevice device, Action<bool, string> check)
    {
        const string specializedSource = """
            import Core;

            [GPURoot]
            struct SpecRoot
            {
                float4 Tint;
            };

            struct VsOut
            {
                float4 Position : SV_Position;
            };

            [shader("vertex")]
            VsOut vertexMain(uint vertex : SV_VertexID)
            {
                float2 uv = float2((vertex << 1) & 2, vertex & 2);
                VsOut o;
                o.Position = float4(uv * 2.0 - 1.0, 0.5, 1.0);
                return o;
            }

            [shader("pixel")]
            float4 pixelMain<let Scale : int>(VsOut input) : SV_Target0
            {
                SpecRoot root = gpu_load_root<SpecRoot>();
                return float4(root.Tint.rgb * (1.0 / float(Scale)), root.Tint.a);
            }
            """;

        RenderPassSignature emptySig = default;
        RenderPassSignature explicitEmptySig = new([], Format.Undefined, Format.Undefined, 1);
        check(emptySig.ColorCount == 0 && emptySig.DepthFormat == Format.Undefined &&
            emptySig.StencilFormat == Format.Undefined && emptySig.SampleCount == 1 &&
            emptySig.Equals(explicitEmptySig) && emptySig.GetHashCode() == explicitEmptySig.GetHashCode(),
            "default RenderPassSignature matches an empty single-sample pass");

        RenderPassSignature msaaPassSig = new([Format.Rgba8Unorm, Format.Rgba16Float], Format.D32Float, Format.S8Uint, 4);
        RenderPassSignature msaaPassSigCopy = new([Format.Rgba8Unorm, Format.Rgba16Float], Format.D32Float, Format.S8Uint, 4);
        RenderPassSignature singleSamplePassSig = new([Format.Rgba8Unorm, Format.Rgba16Float], Format.D32Float, Format.S8Uint, 1);
        Span<Format> copied = stackalloc Format[2];
        msaaPassSig.CopyColorFormats(copied);
        check(msaaPassSig.ColorCount == 2 && msaaPassSig.GetColorFormat(0) == Format.Rgba8Unorm &&
            msaaPassSig.GetColorFormat(1) == Format.Rgba16Float && copied[0] == Format.Rgba8Unorm && copied[1] == Format.Rgba16Float &&
            msaaPassSig.DepthFormat == Format.D32Float && msaaPassSig.StencilFormat == Format.S8Uint && msaaPassSig.SampleCount == 4 &&
            msaaPassSig.Equals(msaaPassSigCopy) && msaaPassSig.GetHashCode() == msaaPassSigCopy.GetHashCode() &&
            !msaaPassSig.Equals(singleSamplePassSig),
            "RenderPassSignature packs multi-target formats, depth/stencil, and sample count accurately");

        RenderingSystem? system = null;
        Shader? shader = null;
        Shader? specializedShader = null;
        Texture? target = null;
        Texture? depth = null;
        Texture? msaaTarget = null;
        Buffer? vertices = null;
        RenderContext? context = null;
        GPUHeap readback = default;
        ErrorCallback? previousCallback = Gpu.ErrorCallback;
        try
        {
            system = new RenderingSystem(device, 8 * 1024 * 1024);
            using (ShaderCompiler compiler = new(device))
            {
                ShaderModule module = compiler.LoadModuleFromSource("auto_pso_mat", "auto_pso_mat.slang", MaterialShaderSource);
                shader = new Shader(system, module, name: "auto_pso_mat");
            }
            specializedShader = system.Shaders.GetShaderFromSource("auto_pso_spec", "auto_pso_spec.slang", specializedSource);

            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            depth = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                Format = Format.D32Float,
                Usage = TextureUsage.DepthStencilAttachment,
            });
            msaaTarget = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment,
                SampleCount = 4,
            });

            check(target.RenderView.Format == Format.Rgba8Unorm && target.RenderView.SampleCount == 1 &&
                depth.RenderView.Format == Format.D32Float && depth.RenderView.SampleCount == 1 &&
                msaaTarget.RenderView.Format == Format.Rgba8Unorm && msaaTarget.RenderView.SampleCount == 4,
                "GPURenderView exposes underlying attachment format and sample count");

            RenderingDesc autoPassDesc = new()
            {
                Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                Depth = new DepthAttachment { RenderView = depth.RenderView, Load = LoadOp.Clear, Store = StoreOp.Discard },
            };
            RenderPassSignature derivedSig = RenderPassSignature.From(in autoPassDesc);
            RenderingDesc msaaPassDesc = new()
            {
                Colors = [new() { RenderView = msaaTarget.RenderView, Load = LoadOp.Clear }],
            };
            RenderPassSignature msaaDerivedSig = RenderPassSignature.From(in msaaPassDesc);
            check(derivedSig.ColorCount == 1 && derivedSig.GetColorFormat(0) == Format.Rgba8Unorm &&
                derivedSig.DepthFormat == Format.D32Float && derivedSig.StencilFormat == Format.Undefined && derivedSig.SampleCount == 1 &&
                msaaDerivedSig.ColorCount == 1 && msaaDerivedSig.GetColorFormat(0) == Format.Rgba8Unorm && msaaDerivedSig.SampleCount == 4,
                "RenderPassSignature.From derives color, depth, stencil, and sample count from RenderingDesc");

            ReadOnlySpan<TestVertex> quadTriangle =
            [
                new() { Position = new Vector4(-1, -1, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(3, -1, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(-1, 3, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
            ];
            vertices = system.CreateBuffer(quadTriangle);
            Matrix4x4 identity = Matrix4x4.Identity;
            Vector4 tint = new(0.25f, 0.5f, 0.75f, 1.0f);

            Material material = shader.CreateMaterial("auto_pso");
            material.SetBuffer("vertices", vertices);
            material.SetData("transform", ref identity);
            material.SetData("tint", ref tint);
            material.Blend = new BlendState
            {
                Enabled = true,
                Color = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
            };

            readback = device.CreateGPUHeap(RecordingSize * RecordingSize * 4, MemoryType.Readback);
            context = new RenderContext(system, 4096);

            // 1. Automatic on-demand PSO creation during BeginRenderPass(desc) + Draw with zero manual GetPipeline.
            context.BeginFrame();
            context.Barrier(Stage.DepthStencilTests, Access.DepthStencilWrite, Stage.DepthStencilTests, Access.DepthStencilWrite);
            using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
            {
                Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                Depth = new DepthAttachment { RenderView = depth.RenderView, Load = LoadOp.Clear, Store = StoreOp.Discard },
            }))
            {
                pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                pass.Draw(material, 3);
            }
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();
            check(PixelMatches(readback.Range, Format.Rgba8Unorm, 8, 8, tint),
                "unprepared material draws automatically using BeginRenderPass(desc) without calling GetPipeline");

            // 2. Warmed automatic BeginRenderPass(desc) + Draw allocates zero managed bytes.
            long drawAllocations = 0;
            for (int frame = 0; frame < 6; ++frame)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                context.BeginFrame();
                context.Barrier(Stage.Transfer | Stage.ColorOutput, Access.TransferRead | Access.ColorWrite, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                    Depth = new DepthAttachment { RenderView = depth.RenderView, Load = LoadOp.Clear, Store = StoreOp.Discard },
                }))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                    pass.Draw(material, 3);
                }
                context.Submit();
                long frameAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (frame >= 2)
                    drawAllocations += frameAllocated;
            }
            context.Wait();
            check(drawAllocations == 0, $"warmed BeginRenderPass(desc) + Draw path allocates zero managed bytes (actual={drawAllocations})");

            // 3. PipelineMissPolicy.CreateAndWarn emits exactly one nonfatal diagnostic on miss and none on cache hit.
            int missWarnings = 0;
            bool nonfatalOnly = true;
            Gpu.SetErrorCallback((string message, bool fatal) =>
            {
                if (message.Contains("Pipeline miss", StringComparison.Ordinal))
                {
                    ++missWarnings;
                    nonfatalOnly &= !fatal;
                }
            });
            system.PipelineMissPolicy = PipelineMissPolicy.CreateAndWarn;
            material.WriteMask = 0x7;
            try
            {
                context.BeginFrame();
                context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                    Depth = new DepthAttachment { RenderView = depth.RenderView, Load = LoadOp.Clear, Store = StoreOp.Discard },
                }))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                    pass.Draw(material, 3);
                    pass.Draw(material, 3);
                }
                context.Submit().Wait();
            }
            finally
            {
                system.PipelineMissPolicy = PipelineMissPolicy.Create;
                Gpu.SetErrorCallback(previousCallback);
            }
            check(missWarnings == 1 && nonfatalOnly,
                $"PipelineMissPolicy.CreateAndWarn emits one nonfatal diagnostic on miss (count={missWarnings})");

            material.WriteMask = 0x3;
            material.Topology = PrimitiveTopology.TriangleStrip;
            material.WarmUpPipeline(derivedSig, default(DepthStencilState)).GetAwaiter().GetResult();
            DepthStencilState warmedDepth = new() { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.Less };
            material.DepthStencilState = warmedDepth;
            material.WarmUpPipeline(derivedSig).GetAwaiter().GetResult();
            D3D12Pso? d3d12Pipeline = material.Variant.TryGetCachedPipeline(in derivedSig, material.StateKey) as D3D12Pso;
            int warmedVariantCount = d3d12Pipeline?.CachedVariantCount ?? 0;
            if (device.ActiveBackend == GraphicsBackend.Dx12)
                check(warmedVariantCount == 4, "material warmup prepares depth-enabled D3D12 variants for both strip index types");

            Task warmHitTask = material.WarmUpPipeline(derivedSig);
            bool hitReturnedCompleted = ReferenceEquals(warmHitTask, Task.CompletedTask);
            long warmAllocBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 256; ++iteration)
            {
                _ = material.WarmUpPipeline(derivedSig);
                _ = material.WarmUpPipeline([Format.Rgba8Unorm], Format.D32Float);
            }
            long warmAllocBytes = GC.GetAllocatedBytesForCurrentThread() - warmAllocBefore;
            check(hitReturnedCompleted && warmAllocBytes == 0,
                $"warmed Material.WarmUpPipeline returns Task.CompletedTask with zero allocations (actual={warmAllocBytes})");

            system.PipelineMissPolicy = PipelineMissPolicy.Throw;
            try
            {
                context.BeginFrame();
                context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                    Depth = new DepthAttachment { RenderView = depth.RenderView, Load = LoadOp.Clear, Store = StoreOp.Discard },
                }))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                    pass.Draw(material, 3);
                }
                context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
                context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                context.Submit().Wait();
            }
            finally
            {
                system.PipelineMissPolicy = PipelineMissPolicy.Create;
            }
            Vector4 expectedRgMask = new(tint.X, tint.Y, 0.0f, 1.0f);
            check(PixelMatches(readback.Range, Format.Rgba8Unorm, 8, 8, expectedRgMask),
                "Material.WarmUpPipeline prepares the depth-tested draw for PipelineMissPolicy.Throw and write mask 0x3");
            if (d3d12Pipeline != null)
                check(d3d12Pipeline.CachedVariantCount == warmedVariantCount, "the first warmed draw creates no D3D12 native variants");

            PipelineState concurrentState = new() { WriteMask = 0x5 };
            DepthStencilState alternateDepth = warmedDepth;
            alternateDepth.DepthCompare = CompareOp.Always;
            Task[] concurrentTasks = new Task[4];
            Task alternateDepthTask;
            lock (SlangCompiler.NativeLock)
            {
                using Barrier gate = new(concurrentTasks.Length);
                Thread[] workers = new Thread[concurrentTasks.Length];
                for (int index = 0; index < workers.Length; ++index)
                {
                    int worker = index;
                    workers[worker] = new Thread(() =>
                    {
                        gate.SignalAndWait();
                        concurrentTasks[worker] = specializedShader.WarmUpPipeline(in concurrentState, derivedSig, warmedDepth, 2);
                    });
                    workers[worker].Start();
                }
                for (int index = 0; index < workers.Length; ++index)
                    workers[index].Join();

                check(!concurrentTasks[0].IsCompleted && Array.TrueForAll(concurrentTasks, task => ReferenceEquals(task, concurrentTasks[0])),
                    "concurrent requests share one task before specialization compilation finishes");
                alternateDepthTask = specializedShader.WarmUpPipeline(in concurrentState, derivedSig, alternateDepth, 2);
                bool sharesDepthTask = ReferenceEquals(concurrentTasks[0], alternateDepthTask);
                check(sharesDepthTask == (device.ActiveBackend != GraphicsBackend.Dx12),
                    "warmup distinguishes depth states only when the backend needs separate native pipelines");
            }
            Task.WhenAll(concurrentTasks).GetAwaiter().GetResult();
            alternateDepthTask.GetAwaiter().GetResult();

            Material specMat = specializedShader.CreateMaterial("warmed_spec", 2);
            specMat.WriteMask = 0x5;
            specMat.DepthStencilState = warmedDepth;
            Vector4 specTint = new(0.8f, 0.4f, 0.6f, 1.0f);
            specMat.SetData("Tint", ref specTint);
            system.PipelineMissPolicy = PipelineMissPolicy.Throw;
            try
            {
                context.BeginFrame();
                context.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
                using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
                    Depth = new DepthAttachment { RenderView = depth.RenderView, Load = LoadOp.Clear, Store = StoreOp.Discard },
                }))
                {
                    pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                    pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                    pass.Draw(specMat, 3);
                }
                context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
                context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                context.Submit().Wait();
            }
            finally
            {
                system.PipelineMissPolicy = PipelineMissPolicy.Create;
            }
            Vector4 expectedSpec = new(0.4f, 0.0f, 0.3f, 1.0f);
            check(PixelMatches(readback.Range, Format.Rgba8Unorm, 8, 8, expectedSpec),
                "concurrent specialized Shader.WarmUpPipeline satisfies PipelineMissPolicy.Throw and renders expected output");

            // 6. Lifecycle safety: destroying RenderingSystem (with ShaderSystem) and standalone Shader while warmups are in-flight drains cleanly.
            RenderPassSignature rgbaPass = new(Format.Rgba8Unorm);
            RenderingSystem? shutdownSystem = new(device, 2 * 1024 * 1024);
            List<Task> shutdownTasks = new(8);
            try
            {
                Shader systemOwnedShader = shutdownSystem.Shaders.GetShaderFromSource("shutdown_warm_sys", "shutdown_warm_sys.slang", specializedSource);
                for (int index = 0; index < 4; ++index)
                {
                    PipelineState state = new() { WriteMask = (byte)(index + 1) };
                    shutdownTasks.Add(systemOwnedShader.WarmUpPipeline(in state, rgbaPass, index + 1));
                }
                shutdownSystem.Destroy();
                shutdownSystem = null;
            }
            finally
            {
                shutdownSystem?.Destroy();
            }

            RenderingSystem? standaloneSystem = new(device, 2 * 1024 * 1024);
            Shader? standaloneShader = null;
            using (ShaderCompiler standaloneCompiler = new(device))
            {
                try
                {
                    ShaderModule specModule = standaloneCompiler.LoadModuleFromSource("shutdown_warm_standalone",
                        "shutdown_warm_standalone.slang", specializedSource);
                    standaloneShader = new Shader(standaloneSystem, specModule, name: "shutdown_warm_standalone");
                    for (int index = 0; index < 4; ++index)
                    {
                        PipelineState state = new() { WriteMask = (byte)(index + 5) };
                        shutdownTasks.Add(standaloneShader.WarmUpPipeline(in state, rgbaPass, index + 1));
                    }
                    standaloneShader.Destroy();
                    standaloneShader = null;
                    standaloneSystem.Destroy();
                    standaloneSystem = null;
                }
                finally
                {
                    standaloneShader?.Destroy();
                    standaloneSystem?.Destroy();
                }
            }
            check(shutdownTasks.TrueForAll(task => task.IsCompleted), "destroying the rendering systems completes all queued warmup tasks");
            Task.WhenAll(shutdownTasks).GetAwaiter().GetResult();
        }
        finally
        {
            Gpu.SetErrorCallback(previousCallback);
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            shader?.Destroy();
            vertices?.Destroy();
            msaaTarget?.Destroy();
            depth?.Destroy();
            target?.Destroy();
            system?.Destroy();
            readback.Destroy();
        }
    }

    private static unsafe void CheckPrepackedDrawIndexed(GPUDevice device, RenderingSystem system, Shader shader, Action<bool, string> check)
    {
        Buffer? vertices = null;
        Buffer? indices = null;
        Texture? target = null;
        RenderContext? context = null;
        GPUHeap readback = default;
        try
        {
            ReadOnlySpan<TestVertex> vertexData =
            [
                // Leading sentinel vertex skipped via Buffer.View.OffsetBy.
                new() { Position = new Vector4(0, 0, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(-1, -1, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(3, -1, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
                new() { Position = new Vector4(-1, 3, 0.5f, 1), Uv = new Vector2(0.5f, 0.5f) },
            ];
            vertices = system.CreateBuffer(vertexData);
            indices = system.CreateBuffer<ushort>([99, 99, 0, 1, 2]);
            check(vertices.View.Index != 0, "Buffer.View exposes a nonzero shader-visible descriptor index");

            target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(RecordingSize, RecordingSize, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            readback = device.CreateGPUHeap(RecordingSize * RecordingSize * 4, MemoryType.Readback);
            context = new RenderContext(system, 4096);
            Material material = shader.CreateMaterial("prepacked_indexed");
            Vector4 expectedTint = new(0.25f, 0.75f, 0.5f, 1.0f);

            context.BeginFrame();
            GPUAllocation<PrepackedMaterialRoot> root = context.FrameData.Allocate<PrepackedMaterialRoot>();
            *root.Cpu = new PrepackedMaterialRoot
            {
                Vertices = vertices.View.OffsetBy((uint)sizeof(TestVertex)),
                Albedo = system.TextureWhite.SampledIndex,
                Sampler = system.SamplerDefault.Index,
                Tint = expectedTint,
                Transform = Matrix4x4.Identity,
            };

            using (RenderContext.RenderScope pass = context.BeginRenderPass(new RenderingDesc
            {
                Colors = [new() { RenderView = target.RenderView, Load = LoadOp.Clear }],
            }))
            {
                pass.SetViewport(new Viewport { Width = RecordingSize, Height = RecordingSize });
                pass.SetScissor(new Scissor { Width = RecordingSize, Height = RecordingSize });
                pass.DrawIndexed(material, root.GPURange, default, indices, IndexType.UInt16, 3, firstIndex: 1, byteOffset: sizeof(ushort));
            }
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();

            check(PixelMatches(readback.Range, Format.Rgba8Unorm, 8, 8, expectedTint),
                "RenderScope.DrawIndexed with prepacked GPURange root and Buffer.View.OffsetBy renders expected pixels");
        }
        finally
        {
            device.WaitIdle();
            context?.DiscardFrame();
            context?.Destroy();
            target?.Destroy();
            indices?.Destroy();
            vertices?.Destroy();
            readback.Destroy();
        }
    }

    private static T ReadRoot<T>(Material material, int offset) where T : unmanaged
    {
        Span<byte> root = stackalloc byte[(int)material.Root.SizeBytes];
        material.CopyRoot(root);
        return MemoryMarshal.Read<T>(root.Slice(offset, Unsafe.SizeOf<T>()));
    }

    private static uint ReadUInt(Material material, int offset) => ReadRoot<uint>(material, offset);

    [StructLayout(LayoutKind.Sequential)]
    private struct ScopeRoot
    {
        public GPUBufferView Values;
        public uint Index;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrepackedMaterialRoot
    {
        public GPUBufferView Vertices;
        public uint Albedo;
        public uint Sampler;
        public Vector4 Tint;
        public Matrix4x4 Transform;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestVertex
    {
        public Vector4 Position;
        public Vector2 Uv;
    }
}
