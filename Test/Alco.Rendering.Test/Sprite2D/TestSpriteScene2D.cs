using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Cross-target shader ABI tests, CPU concurrency/bulk-growth/ref-counting tests,
/// and multi-backend GPU culling/tier/surface readback tests for <see cref="GpuInstancePool{TInstance}"/> and <see cref="SpriteScene2D"/>.
/// </summary>
[TestFixture]
public unsafe class TestSpriteScene2D
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedInstanceScatterRoot
    {
        public GPUBufferView Patches;
        public GPUBufferView PatchSlots;
        public GPUBufferView Pool;
        public uint PatchCount;
        public uint WordsPerInstance;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedSpriteSceneResetArgsRoot
    {
        public GPUBufferView BucketMeta;
        public GPUBufferView DrawArgs;
        public uint BucketCount;
        public uint Pad0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedSpriteSceneCullRoot
    {
        public Vector4 Viewport;
        public GPUBufferView Pool;
        public GPUBufferView SlotBuckets;
        public GPUBufferView BucketMeta;
        public GPUBufferView DrawArgs;
        public GPUBufferView VisibleSlots;
        public uint SlotCount;
        public uint BucketCount;
        public float CullMargin;
        public uint Pad0;
        public Vector2 OriginOffset;
    }

    private const string CustomSurfaceModule = "TestCustomSpriteSceneSurface";

    private const string CustomSurfaceSource = """
        #language slang 2025
        module TestCustomSpriteSceneSurface;

        import Core;
        import SharedSampler;
        import AlcoRendering_SpriteScene;

        public struct CustomSurfaceParams
        {
            public float DisplaceStrength;
            public float AlphaCutoff;
            public float Pad0;
            public float Pad1;
        };

        public struct Surface : ISpriteSceneSurface
        {
            public typedef CustomSurfaceParams Params;

            public override SpriteSceneLocalVertex ExpandLocalVertex(uint vertexId, SpriteSceneVertexInput input, Params p)
            {
                SpriteSceneLocalVertex v = spriteSceneExpandUnitQuad(vertexId);
                if (input.BucketTag == 2u)
                {
                    uint quadIdx = vertexId / 6u;
                    v.Position.x = v.Position.x * 0.4 + (quadIdx == 0u ? -0.35 : 0.35);
                }

                if (input.HasMaterialParams != 0u)
                {
                    CustomSurfaceParams mp = gpu_load<CustomSurfaceParams>(input.MaterialParams, input.MaterialParamId);
                    v.Position.x += (1.0 - v.Uv.y) * mp.DisplaceStrength;
                }
                return v;
            }

            public override float3 TransformToWorld(SpriteSceneLocalVertex vertex, SpriteSceneVertexInput input, Params p)
            {
                float2 localScaled = vertex.Position.xy * input.Scale;
                float2 rotated = spriteSceneRotate(localScaled, input.Rotation);
                float z = vertex.Position.z + input.Depth;
                if ((input.Flags & (1u << 3)) != 0u)
                {
                    z -= rotated.y * input.CustomData.x;
                }
                return float3(rotated + input.Position, z);
            }

            public float4 GetColor(SpriteSceneSurfaceInput input, Params p)
            {
                float4 baseCol = spriteSceneSampleTexture(input.TextureId, input.Flags, input.Uv, input.DdxUv, input.DdyUv);
                if (input.AuxTextureId != 0u)
                {
                    float mask = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(input.AuxTextureId))
                        .SampleGrad(shared_sampler_linear_clamp(), input.Uv, input.DdxUv, input.DdyUv).r;
                    return baseCol * lerp(float4(1.0, 1.0, 1.0, 1.0), input.Color, mask);
                }
                return baseCol * input.Color;
            }

            public override float GetAlphaCutoff(Params p)
            {
                return p.AlphaCutoff > 0.0 ? p.AlphaCutoff : 0.5;
            }
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct CustomSurfaceParams
    {
        public float DisplaceStrength;
        public float AlphaCutoff;
        public float Pad0;
        public float Pad1;
    }

    /// <summary>Verifies generic compute and composed SpriteScene2D surface shaders compile and match C# ABI layouts across all targets.</summary>
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.MetalSource)]
    [TestCase(ShaderTarget.Dxil)]
    public void SpriteSceneShaders_CompileAndMatchLayouts_AcrossAllTargets(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
        {
            Assert.Ignore($"Shader target {target} is unsupported on this host.");
            return;
        }

        ShaderCompilerOptions options = new()
        {
            FileResolver = AlcoSprite2DShaders.CreateFileResolver(),
        };
        using ShaderCompiler compiler = new(target, options);

        foreach (string moduleName in AlcoSprite2DShaders.BuiltInModuleOrder)
        {
            _ = compiler.LoadModuleFromSource(
                moduleName,
                moduleName + ".slang",
                AlcoSprite2DShaders.LoadEmbeddedSource(moduleName + ".slang"));
        }

        _ = compiler.LoadModuleFromSource(
            CustomSurfaceModule,
            CustomSurfaceModule + ".slang",
            CustomSurfaceSource);

        CompiledShader scatter = compiler.CompileSource(
            AlcoSprite2DShaders.GpuInstanceScatterModule,
            AlcoSprite2DShaders.GpuInstanceScatterModule + ".slang",
            AlcoSprite2DShaders.LoadEmbeddedSource("GpuInstanceScatter.slang"),
            ShaderPipelineKind.Compute);

        CompiledShader resetArgs = compiler.CompileSource(
            AlcoSprite2DShaders.SpriteSceneResetArgsModule,
            AlcoSprite2DShaders.SpriteSceneResetArgsModule + ".slang",
            AlcoSprite2DShaders.LoadEmbeddedSource("SpriteSceneResetArgs.slang"),
            ShaderPipelineKind.Compute);

        CompiledShader cull = compiler.CompileSource(
            AlcoSprite2DShaders.SpriteSceneCullModule,
            AlcoSprite2DShaders.SpriteSceneCullModule + ".slang",
            AlcoSprite2DShaders.LoadEmbeddedSource("SpriteSceneCull.slang"),
            ShaderPipelineKind.Compute);

        Assert.Multiple(() =>
        {
            Assert.That(scatter.Reflection.MatchesStructLayout<ExpectedInstanceScatterRoot>("InstanceScatterRoot"), Is.True);
            Assert.That(resetArgs.Reflection.MatchesStructLayout<ExpectedSpriteSceneResetArgsRoot>("SpriteSceneResetArgsRoot"), Is.True);
            Assert.That(cull.Reflection.MatchesStructLayout<ExpectedSpriteSceneCullRoot>("SpriteSceneCullRoot"), Is.True);
            Assert.That(cull.Reflection.Root?.Find("CullMargin")?.OffsetBytes, Is.EqualTo(64u));
        });

        (string Surface, uint ExpectedMinTailBytes)[] surfaces =
        [
            (AlcoSprite2DShaders.SpriteSceneSurfaceStandardModule, 4u),
            (CustomSurfaceModule, (uint)sizeof(CustomSurfaceParams)),
        ];

        string[] alphaSpecs = ["false", "true"];
        foreach ((string surface, uint expectedTail) in surfaces)
        {
            foreach (string alphaSpec in alphaSpecs)
            {
                CompiledShader composed = compiler.CompileComposed(
                    AlcoSprite2DShaders.SpriteScenePassTemplateModule,
                    surface,
                    ShaderPipelineKind.Graphics,
                    new ShaderCompileOptions { SpecializationArgs = [alphaSpec] });

                Assert.Multiple(() =>
                {
                    Assert.That(composed.Reflection.Root, Is.Not.Null, $"{surface} alphaTest={alphaSpec}");
                    Assert.That(composed.Reflection.Root!.Find("Pass")?.Type.SizeBytes, Is.EqualTo((uint)sizeof(SpriteScenePassHeader)), $"{surface} header");
                    Assert.That(composed.Reflection.Root.Find("Pass.VisibleSlots")?.OffsetBytes, Is.EqualTo(72u), $"{surface} Pass.VisibleSlots offset");
                    Assert.That(composed.Reflection.Root.Find("Pass.BucketTag")?.OffsetBytes, Is.EqualTo(92u), $"{surface} Pass.BucketTag offset");
                    Assert.That(composed.Reflection.Root.Find("Pass.HasMaterialParams")?.OffsetBytes, Is.EqualTo(112u), $"{surface} Pass.HasMaterialParams offset");
                    Assert.That(composed.Reflection.Root.SizeBytes, Is.GreaterThanOrEqualTo((uint)sizeof(SpriteScenePassHeader) + expectedTail),
                        $"{surface} alphaTest={alphaSpec} root size");
                    if (expectedTail == 16u)
                    {
                        Assert.That(composed.Reflection.Root.SizeBytes, Is.EqualTo((uint)(sizeof(SpriteScenePassHeader) + 16)),
                            $"{surface} alphaTest={alphaSpec} exact root size");
                    }
                });
            }
        }
    }

    /// <summary>Verifies bulk capacity growth, parallel worker slot writes, same-frame detach/reuse, and bindless texture ref-counting.</summary>
    [Test]
    public void GpuInstancePool_ParallelWrite_BulkGrowth_SameFrameReuse_AndTextureRefCount()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        RenderingSystem rendering = host.RenderingSystem;

        Texture tex0 = CreateRgbaTexture(rendering, 2, 2, (255, 0, 0, 255));
        Texture tex1 = CreateRgbaTexture(rendering, 2, 2, (0, 255, 0, 255));
        Texture tex2 = CreateRgbaTexture(rendering, 2, 2, (0, 0, 255, 255));

        using var pool = new GpuInstancePool<SpriteSceneInstance>(rendering);
        var context = new RenderContext(rendering);
        var handles = new List<GpuInstancePool<SpriteSceneInstance>.Registration>(5000);

        try
        {
            const int initialCount = 5000;
            for (int i = 0; i < initialCount; ++i)
            {
                uint bucket = (uint)(i & 1);
                Texture primary = (i % 3) switch
                {
                    0 => tex0,
                    1 => tex1,
                    _ => tex2,
                };
                SpriteSceneInstance inst = new()
                {
                    Position = new Vector2(i, -i),
                    Rotation = Rotation2D.Identity,
                    Scale = Vector2.One,
                    Color = Vector4.One,
                    UvRect = Rect.One,
                    TextureId = primary.SampledIndex,
                    Flags = SpriteSceneInstance.PackFlags(),
                };
                handles.Add(pool.RegisterDeferred(bucket, in inst, primary));
            }

            // Bulk grow from 1024 -> >= 5000 in a single SyncToGpu without pre-calling EnsureSlotCapacity.
            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                bool membershipChanged = pool.SyncToGpu(context, compute);
                Assert.That(membershipChanged, Is.True);
            }
            context.Submit().Wait();

            Assert.Multiple(() =>
            {
                Assert.That(pool.TotalSlots, Is.EqualTo(initialCount));
                Assert.That(pool.LiveCount, Is.EqualTo(initialCount));
                Assert.That(pool.SlotCapacity, Is.GreaterThanOrEqualTo(initialCount));
                Assert.That(pool.GetBucketCount(0) + pool.GetBucketCount(1), Is.EqualTo(initialCount));
                Assert.That(pool.ActiveTextureCount, Is.EqualTo(3));
            });

            // Concurrent Parallel.For updates across all 5000 slots.
            Parallel.For(0, initialCount, i =>
            {
                SpriteSceneInstance updated = new()
                {
                    Position = new Vector2(i * 2f, i * 0.5f),
                    Rotation = Rotation2D.Identity,
                    Scale = new Vector2(2f, 2f),
                    Color = Vector4.One,
                    UvRect = Rect.One,
                    TextureId = tex0.SampledIndex,
                    Flags = SpriteSceneInstance.PackFlags(),
                };
                Assert.That(handles[i].Slot.Write(in updated), Is.True);
            });

            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                _ = pool.SyncToGpu(context, compute);
            }
            context.Submit().Wait();
            Assert.That(pool.LastDirtyPatchCount, Is.EqualTo(initialCount));

            // Same-frame detach 100 handles + register 50 replacements reuses freed slots without advancing TotalSlots.
            for (int i = 0; i < 100; ++i)
            {
                handles[i].Dispose();
            }

            for (int i = 0; i < 50; ++i)
            {
                SpriteSceneInstance replacement = new()
                {
                    Position = new Vector2(-10f, i),
                    Rotation = Rotation2D.Identity,
                    Scale = Vector2.One,
                    Color = Vector4.One,
                    UvRect = Rect.One,
                    TextureId = tex1.SampledIndex,
                    Flags = SpriteSceneInstance.PackFlags(),
                };
                handles.Add(pool.RegisterDeferred(0u, in replacement, tex1));
            }

            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                _ = pool.SyncToGpu(context, compute);
            }
            context.Submit().Wait();

            Assert.Multiple(() =>
            {
                Assert.That(pool.LiveCount, Is.EqualTo(4950));
                Assert.That(pool.TotalSlots, Is.EqualTo(initialCount));
            });

            // Dispose every remaining handle using tex2 and sync across a completed frame so retirement releases tex2.
            for (int i = 100; i < initialCount; ++i)
            {
                if (i % 3 == 2)
                {
                    handles[i].Dispose();
                }
            }

            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                _ = pool.SyncToGpu(context, compute);
            }
            context.Submit().Wait();

            // Run one more Flush/SyncToGpu after frame completion to reclaim retired texture entry.
            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                _ = pool.SyncToGpu(context, compute);
            }
            context.Submit().Wait();

            Assert.That(pool.ActiveTextureCount, Is.EqualTo(2));
        }
        finally
        {
            for (int i = 0; i < handles.Count; ++i)
            {
                handles[i].Dispose();
            }

            context.Destroy();
            if (!tex0.IsDestroyed) tex0.Destroy();
            if (!tex1.IsDestroyed) tex1.Destroy();
            if (!tex2.IsDestroyed) tex2.Destroy();
        }
    }

    /// <summary>Verifies single-draw GPU culling of 8,192 sprites across 64 textures and Frame 2 hot-patching/detachment/texture swap.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    public void SpriteScene2D_GpuCulling_8192SpritesAcross64Textures_AndHotPatching(GraphicsBackend backend)
    {
        if (!TryCreateGpuHost(backend, out RenderingTestHost? host))
        {
            return;
        }

        using (host!)
        {
            RenderingSystem rendering = host.RenderingSystem;
            const int width = 128;
            const int height = 128;

            Texture colorTarget = CreateColorTarget(rendering, width, height);
            Texture depthTarget = CreateDepthTarget(rendering, width, height);

            Texture[] textures = new Texture[64];
            (byte R, byte G, byte B)[] palette = new (byte, byte, byte)[64];
            for (int i = 0; i < 64; ++i)
            {
                byte r = (byte)(35 + (i & 3) * 60);
                byte g = (byte)(35 + ((i >> 2) & 3) * 60);
                byte b = (byte)(35 + ((i >> 4) & 3) * 60);
                palette[i] = (r, g, b);
                textures[i] = CreateRgbaTexture(rendering, 4, 4, (r, g, b, 255));
            }
            Texture swapTexture = CreateRgbaTexture(rendering, 4, 4, (255, 255, 255, 255));

            Shader stdShader = AlcoSprite2DShaders.ComposeSpriteSceneSurface(
                rendering.Shaders, AlcoSprite2DShaders.SpriteSceneSurfaceStandardModule, "TestScene8192");
            Material stdMat = stdShader.CreateMaterial("TestScene8192Mat", false);

            using var scene = new SpriteScene2D(rendering);
            var context = new RenderContext(rendering);
            var camera = new Camera2D { Size = new Vector2(64f, 64f), Near = -10f, Far = 10f };
            var passSig = new RenderPassSignature([Format.Rgba8Unorm], Format.D32Float);
            var handles = new List<GpuInstancePool<SpriteSceneInstance>.Registration>(8192);

            try
            {
                uint bucketId = scene.RegisterBucket(new SpriteSceneBucketDesc(0, stdMat, DepthStencilState.Write));

                // 4,096 sprites on a 64x64 grid strictly inside [-32, 32] x [-32, 32] + 4,096 culled outside viewport.
                for (int gy = 0; gy < 64; ++gy)
                {
                    for (int gx = 0; gx < 64; ++gx)
                    {
                        int texIdx = (gx + gy * 7) & 63;
                        SpriteSceneInstance vis = new()
                        {
                            Position = new Vector2(-31.5f + gx, 31.5f - gy),
                            Rotation = Rotation2D.Identity,
                            Scale = new Vector2(0.9f, 0.9f),
                            Depth = 0f,
                            Color = Vector4.One,
                            UvRect = Rect.One,
                            TextureId = textures[texIdx].SampledIndex,
                            Flags = SpriteSceneInstance.PackFlags(SpriteSceneSamplerMode.NearestClamp),
                        };
                        handles.Add(scene.RegisterDeferred(bucketId, in vis, textures[texIdx]));
                    }
                }

                for (int i = 0; i < 4096; ++i)
                {
                    SpriteSceneInstance offscreen = new()
                    {
                        Position = new Vector2(250f + (i & 63), 250f + (i >> 6)),
                        Rotation = Rotation2D.Identity,
                        Scale = Vector2.One,
                        Depth = 0f,
                        Color = Vector4.One,
                        UvRect = Rect.One,
                        TextureId = textures[i & 63].SampledIndex,
                        Flags = SpriteSceneInstance.PackFlags(SpriteSceneSamplerMode.NearestClamp),
                    };
                    handles.Add(scene.RegisterDeferred(bucketId, in offscreen, textures[i & 63]));
                }

                // Frame 1: GPU frustum culling & single indirect draw of 4,096 visible sprites.
                context.BeginFrame();
                scene.PrepareAndCull(context, camera.GetViewport(), cullMargin: 0f);
                var passDesc = new RenderingDesc
                {
                    Colors = [new ColorAttachment { RenderView = colorTarget.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = new Vector4(0f, 0f, 0f, 1f) }],
                    Depth = new DepthAttachment { RenderView = depthTarget.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 1f },
                };
                int drawCalls1;
                using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = width, Height = height });
                    render.SetScissor(new Scissor { Width = width, Height = height });
                    drawCalls1 = scene.RenderTier(context, in render, tier: 0, camera);
                }

                GpuDrawIndirectArgs[] args1 = ReadBackDrawArgsAndSubmit(host.Device, context, scene.DrawArgsBuffer, 1);
                byte[] pixels1 = ReadBackRgba8FreshFrame(host.Device, rendering, colorTarget, width, height);

                Assert.Multiple(() =>
                {
                    Assert.That(drawCalls1, Is.EqualTo(1));
                    Assert.That(args1[0].VertexCount, Is.EqualTo(6u));
                    Assert.That(args1[0].InstanceCount, Is.EqualTo(4096u));
                    Assert.That(scene.Stats.ActiveTextureCount, Is.EqualTo(64));

                    // Each cell (gx, gy) spans 2x2 pixels centered at px = gx*2 + 1, py = gy*2 + 1.
                    (int gx, int gy)[] checkCells = [(0, 0), (17, 5), (42, 31), (63, 63)];
                    foreach ((int gx, int gy) in checkCells)
                    {
                        int texIdx = (gx + gy * 7) & 63;
                        (byte er, byte eg, byte eb) = palette[texIdx];
                        int px = gx * 2 + 1;
                        int py = gy * 2 + 1;
                        int offset = (py * width + px) * 4;
                        Assert.That(pixels1[offset + 0], Is.EqualTo(er).Within(3), $"Cell ({gx},{gy}) R");
                        Assert.That(pixels1[offset + 1], Is.EqualTo(eg).Within(3), $"Cell ({gx},{gy}) G");
                        Assert.That(pixels1[offset + 2], Is.EqualTo(eb).Within(3), $"Cell ({gx},{gy}) B");
                    }
                });

                // Frame 2: hot-move 10 sprites offscreen, swap textures on 5 sprites, dispose 10 sprites.
                for (int k = 0; k < 10; ++k)
                {
                    SpriteSceneInstance movedOut = new()
                    {
                        Position = new Vector2(300f + k, 300f),
                        Rotation = Rotation2D.Identity,
                        Scale = new Vector2(0.9f, 0.9f),
                        Color = Vector4.One,
                        UvRect = Rect.One,
                        TextureId = textures[k].SampledIndex,
                        Flags = SpriteSceneInstance.PackFlags(SpriteSceneSamplerMode.NearestClamp),
                    };
                    handles[k].Slot.Write(in movedOut);
                }

                for (int k = 10; k < 15; ++k)
                {
                    SpriteSceneInstance swapped = new()
                    {
                        Position = new Vector2(-31.5f + k, 31.5f),
                        Rotation = Rotation2D.Identity,
                        Scale = new Vector2(0.9f, 0.9f),
                        Color = Vector4.One,
                        UvRect = Rect.One,
                        TextureId = swapTexture.SampledIndex,
                        Flags = SpriteSceneInstance.PackFlags(SpriteSceneSamplerMode.NearestClamp),
                    };
                    handles[k].Slot.Write(in swapped);
                    handles[k].SetTextures(swapTexture);
                }

                for (int k = 15; k < 25; ++k)
                {
                    handles[k].Dispose();
                }

                context.BeginFrame();
                scene.PrepareAndCull(context, camera.GetViewport(), cullMargin: 0f);
                using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = width, Height = height });
                    render.SetScissor(new Scissor { Width = width, Height = height });
                    _ = scene.RenderTier(context, in render, tier: 0, camera);
                }

                GpuDrawIndirectArgs[] args2 = ReadBackDrawArgsAndSubmit(host.Device, context, scene.DrawArgsBuffer, 1);
                byte[] pixels2 = ReadBackRgba8FreshFrame(host.Device, rendering, colorTarget, width, height);

                Assert.Multiple(() =>
                {
                    Assert.That(args2[0].InstanceCount, Is.EqualTo(4076u));
                    Assert.That(scene.Stats.DirtyPatchesUploaded, Is.GreaterThanOrEqualTo(15));

                    for (int k = 10; k < 15; ++k)
                    {
                        int px = k * 2 + 1;
                        int py = 1;
                        int offset = (py * width + px) * 4;
                        Assert.That(pixels2[offset + 0], Is.GreaterThan((byte)245), $"Swapped cell ({k},0) R");
                        Assert.That(pixels2[offset + 1], Is.GreaterThan((byte)245), $"Swapped cell ({k},0) G");
                        Assert.That(pixels2[offset + 2], Is.GreaterThan((byte)245), $"Swapped cell ({k},0) B");
                    }
                });
            }
            finally
            {
                for (int i = 0; i < handles.Count; ++i)
                {
                    handles[i].Dispose();
                }

                context.Destroy();
                if (!colorTarget.IsDestroyed) colorTarget.Destroy();
                if (!depthTarget.IsDestroyed) depthTarget.Destroy();
                if (!swapTexture.IsDestroyed) swapTexture.Destroy();
                for (int i = 0; i < textures.Length; ++i)
                {
                    if (!textures[i].IsDestroyed) textures[i].Destroy();
                }
            }
        }
    }

    /// <summary>Verifies multi-tier ordering, custom depth-slope occlusion, MaterialParamsBuffer lookup, procedural 12-vertex expansion, and AuxTextureId sampling.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    public void SpriteScene2D_MultiTier_CustomSurface_ProceduralVertices_MaterialParams_AndDepthOcclusion(GraphicsBackend backend)
    {
        if (!TryCreateGpuHost(backend, out RenderingTestHost? host))
        {
            return;
        }

        using (host!)
        {
            RenderingSystem rendering = host.RenderingSystem;
            const int width = 256;
            const int height = 128;

            Texture colorTarget = CreateColorTarget(rendering, width, height);
            Texture depthTarget = CreateDepthTarget(rendering, width, height);

            Texture texWhite = CreateRgbaTexture(rendering, 4, 4, (255, 255, 255, 255));
            Texture texRed = CreateRgbaTexture(rendering, 4, 4, (255, 0, 0, 255));
            Texture texGreen = CreateRgbaTexture(rendering, 4, 4, (0, 255, 0, 255));
            Texture texBlue = CreateRgbaTexture(rendering, 4, 4, (0, 0, 255, 255));
            Texture texYellow = CreateRgbaTexture(rendering, 4, 4, (255, 255, 0, 255));
            Texture texSplitMask = CreateSplitMaskTexture(rendering, 16);

            Shader stdShader = AlcoSprite2DShaders.ComposeSpriteSceneSurface(
                rendering.Shaders, AlcoSprite2DShaders.SpriteSceneSurfaceStandardModule, "MultiStd");
            Shader customShader = AlcoSprite2DShaders.ComposeSpriteSceneSurfaceFromSource(
                rendering.Shaders,
                CustomSurfaceModule,
                CustomSurfaceModule + ".slang",
                CustomSurfaceSource,
                "MultiCustom");

            Material layerMat = stdShader.CreateMaterial("MultiLayerMat", false);
            layerMat.Blend = BlendState.AlphaBlend;

            Material customOpaqueMat = customShader.CreateMaterial("MultiCustomOpaqueMat", true);
            customOpaqueMat.Blend = BlendState.Opaque;
            CustomSurfaceParams defaultParams = new() { DisplaceStrength = 0f, AlphaCutoff = 0.5f };
            customOpaqueMat.SetData("Material", ref defaultParams);

            Material customMaskMat = customShader.CreateMaterial("MultiCustomMaskMat", false);
            customMaskMat.Blend = BlendState.AlphaBlend;
            customMaskMat.SetData("Material", ref defaultParams);

            Alco.Graphics.Buffer customParamsBuffer = rendering.CreateBuffer((ulong)(2 * sizeof(CustomSurfaceParams)));
            customParamsBuffer.SetData<CustomSurfaceParams>(
            [
                new CustomSurfaceParams { DisplaceStrength = 0f, AlphaCutoff = 0.5f },
                new CustomSurfaceParams { DisplaceStrength = 1.5f, AlphaCutoff = 0.5f },
            ]);

            using var scene = new SpriteScene2D(rendering);
            var context = new RenderContext(rendering);
            var camera = new Camera2D { Size = new Vector2(width, height), Near = -50f, Far = 50f };
            var passSig = new RenderPassSignature([Format.Rgba8Unorm], Format.D32Float);

            try
            {
                uint bucketTier0 = scene.RegisterBucket(new SpriteSceneBucketDesc(0, layerMat, DepthStencilState.None));
                uint bucketTier1A = scene.RegisterBucket(new SpriteSceneBucketDesc(1, layerMat, DepthStencilState.None));
                uint bucketTier1B = scene.RegisterBucket(new SpriteSceneBucketDesc(1, layerMat, DepthStencilState.None));
                uint bucketTier1C = scene.RegisterBucket(new SpriteSceneBucketDesc(1, layerMat, DepthStencilState.None));
                uint bucketDepthSlope = scene.RegisterBucket(new SpriteSceneBucketDesc(2, customOpaqueMat, DepthStencilState.Write));
                uint bucketParamLookup = scene.RegisterBucket(new SpriteSceneBucketDesc(
                    2, customOpaqueMat, DepthStencilState.Write, VertexCount: 6u, LocalExtentScale: 0.5f, MaterialParamsBuffer: customParamsBuffer));
                uint bucketProcedural12 = scene.RegisterBucket(new SpriteSceneBucketDesc(
                    2, customOpaqueMat, DepthStencilState.Write, VertexCount: 12u, LocalExtentScale: 1.5f, BucketTag: 2u));
                uint bucketAuxMask = scene.RegisterBucket(new SpriteSceneBucketDesc(3, customMaskMat, DepthStencilState.Read));

                uint nearestFlags = SpriteSceneInstance.PackFlags(SpriteSceneSamplerMode.NearestClamp);
                uint slopeFlags = SpriteSceneInstance.PackFlags(SpriteSceneSamplerMode.NearestClamp, userFlags: 1u << 3);

                // Zone A (-90, 0): Tier 0 < Tier 1 ordered buckets A < B < C.
                _ = scene.AddDirect(bucketTier0, MakeQuad(new Vector2(-90f, 0f), new Vector2(24f, 24f), 0f, texRed, nearestFlags), texRed);
                _ = scene.AddDirect(bucketTier1A, MakeQuad(new Vector2(-90f, 0f), new Vector2(16f, 16f), 0f, texGreen, nearestFlags), texGreen);
                _ = scene.AddDirect(bucketTier1B, MakeQuad(new Vector2(-90f, 0f), new Vector2(10f, 10f), 0f, texBlue, nearestFlags), texBlue);
                _ = scene.AddDirect(bucketTier1C, MakeQuad(new Vector2(-90f, 0f), new Vector2(4f, 4f), 0f, texYellow, nearestFlags), texYellow);

                // Zone B (-40, 0): Custom TransformToWorld oblique depth slope via CustomData.X and user flag bit 3.
                SpriteSceneInstance wall = MakeQuad(new Vector2(-40f, 0f), new Vector2(20f, 20f), 0f, texRed, slopeFlags);
                wall.CustomData = new Vector4(0.5f, 0f, 0f, 0f); // Top (+6Y) has z = -3; bottom (-6Y) has z = +3.
                _ = scene.AddDirect(bucketDepthSlope, in wall, texRed);
                // Green sprite behind upper wall (z=0 > -3) is occluded -> stays Red.
                _ = scene.AddDirect(bucketDepthSlope, MakeQuad(new Vector2(-40f, 6f), new Vector2(8f, 8f), depth: 0f, texGreen, nearestFlags), texGreen);
                // Blue sprite in front of lower wall (z=-3 < +3) occludes wall -> becomes Blue.
                _ = scene.AddDirect(bucketDepthSlope, MakeQuad(new Vector2(-40f, -6f), new Vector2(8f, 8f), depth: -3f, texBlue, nearestFlags), texBlue);

                // Zone C (+16, +24 / -20): MaterialParamsBuffer entry [0] static vs [1] shifted top (+9 world units).
                SpriteSceneInstance paramStatic = MakeQuad(new Vector2(16f, 24f), new Vector2(6f, 8f), 0f, texWhite, nearestFlags);
                paramStatic.MaterialParamId = 0u;
                _ = scene.AddDirect(bucketParamLookup, in paramStatic, texWhite);

                SpriteSceneInstance paramDisplaced = MakeQuad(new Vector2(16f, -20f), new Vector2(6f, 8f), 0f, texWhite, nearestFlags);
                paramDisplaced.MaterialParamId = 1u;
                _ = scene.AddDirect(bucketParamLookup, in paramDisplaced, texWhite);

                // Zone D (+60, 0): Procedural 12-vertex expansion (two separated quads at x=54.4 and x=65.6 with center gap at x=60).
                _ = scene.AddDirect(bucketProcedural12, MakeQuad(new Vector2(60f, 0f), new Vector2(16f, 16f), 0f, texGreen, nearestFlags), texGreen);

                // Zone E (+96, +24): AuxTextureId mask sampling (left red, right white).
                SpriteSceneInstance auxMasked = MakeQuad(new Vector2(96f, 24f), new Vector2(16f, 16f), 0f, texWhite, nearestFlags);
                auxMasked.Color = new Vector4(1f, 0f, 0f, 1f);
                auxMasked.AuxTextureId = texSplitMask.SampledIndex;
                _ = scene.AddDirect(bucketAuxMask, in auxMasked, texWhite, texSplitMask);

                context.BeginFrame();
                scene.PrepareAndCull(context, camera.GetViewport(), cullMargin: 2f);

                var passDesc = new RenderingDesc
                {
                    Colors = [new ColorAttachment { RenderView = colorTarget.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = new Vector4(0f, 0f, 0f, 1f) }],
                    Depth = new DepthAttachment { RenderView = depthTarget.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 1f },
                };
                using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = width, Height = height });
                    render.SetScissor(new Scissor { Width = width, Height = height });
                    for (int tier = 0; tier <= 3; ++tier)
                    {
                        _ = scene.RenderTier(context, in render, tier, camera, time: 1.0f);
                    }
                }

                GpuDrawIndirectArgs[] args = ReadBackDrawArgsAndSubmit(host.Device, context, scene.DrawArgsBuffer, 8);
                byte[] pixels = ReadBackRgba8FreshFrame(host.Device, rendering, colorTarget, width, height);

                Assert.Multiple(() =>
                {
                    // Tier 0 < Tier 1A < 1B < 1C draw order.
                    AssertPixel(pixels, width, height, -100f, 0f, 255, 0, 0, 255, 5, "Zone A Tier 0");
                    AssertPixel(pixels, width, height, -96f, 0f, 0, 255, 0, 255, 5, "Zone A Tier 1A");
                    AssertPixel(pixels, width, height, -93f, 0f, 0, 0, 255, 255, 5, "Zone A Tier 1B");
                    AssertPixel(pixels, width, height, -90f, 0f, 255, 255, 0, 255, 5, "Zone A Tier 1C");

                    // Custom depth slope occlusion.
                    AssertPixel(pixels, width, height, -40f, 6f, 255, 0, 0, 255, 5, "Zone B Upper sloped surface occludes back sprite");
                    AssertPixel(pixels, width, height, -40f, -6f, 0, 0, 255, 255, 5, "Zone B Front sprite occludes lower sloped surface");

                    // MaterialParamsBuffer: static entry [0] stays in [13,19], displaced entry [1] shifts top right into [22,28].
                    int staticOutside = CountLitPixelsAroundBand(pixels, width, height, 6f, 32f, 30f, 16f, excludeMinX: 12f, excludeMaxX: 20f);
                    int dynamicOutside = CountLitPixelsAroundBand(pixels, width, height, 6f, -12f, 30f, -28f, excludeMinX: 12f, excludeMaxX: 20f);
                    Assert.That(staticOutside, Is.Zero, "Param entry [0] must remain unshifted.");
                    Assert.That(dynamicOutside, Is.GreaterThan(10), "Param entry [1] must shift top vertices horizontally.");

                    // Procedural 12-vertex expansion draws two split green quads leaving center gap black.
                    Assert.That(args[(int)bucketProcedural12].VertexCount, Is.EqualTo(12u));
                    Assert.That(args[(int)bucketProcedural12].InstanceCount, Is.EqualTo(1u));
                    AssertPixel(pixels, width, height, 54f, 0f, 0, 255, 0, 255, 5, "Procedural 12-vert left subquad");
                    AssertPixel(pixels, width, height, 66f, 0f, 0, 255, 0, 255, 5, "Procedural 12-vert right subquad");
                    AssertPixel(pixels, width, height, 60f, 0f, 0, 0, 0, 255, 5, "Procedural 12-vert center gap");

                    // AuxTextureId mask splits left (red tinted) vs right (untinted white).
                    AssertPixel(pixels, width, height, 91f, 24f, 255, 0, 0, 255, 5, "AuxTexture masked half");
                    AssertPixel(pixels, width, height, 101f, 24f, 255, 255, 255, 255, 5, "AuxTexture unmasked half");
                });
            }
            finally
            {
                context.Destroy();
                customParamsBuffer.Destroy();
                if (!colorTarget.IsDestroyed) colorTarget.Destroy();
                if (!depthTarget.IsDestroyed) depthTarget.Destroy();
                if (!texWhite.IsDestroyed) texWhite.Destroy();
                if (!texRed.IsDestroyed) texRed.Destroy();
                if (!texGreen.IsDestroyed) texGreen.Destroy();
                if (!texBlue.IsDestroyed) texBlue.Destroy();
                if (!texYellow.IsDestroyed) texYellow.Destroy();
                if (!texSplitMask.IsDestroyed) texSplitMask.Destroy();
            }
        }
    }

    /// <summary>Verifies dynamic bucket growth above 64, scatter/cull paths for bucket >= 64, and non-zero OriginOffset parity.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    public void SpriteScene2D_Over96Buckets_And_OriginOffset(GraphicsBackend backend)
    {
        if (!TryCreateGpuHost(backend, out RenderingTestHost? host))
        {
            return;
        }

        using (host!)
        {
            RenderingSystem rendering = host.RenderingSystem;
            const int width = 128;
            const int height = 128;
            const int bucketCount = 96;
            Vector2 originOffset = new(100_000f, 100_000f);

            Texture colorTarget = CreateColorTarget(rendering, width, height);
            Texture depthTarget = CreateDepthTarget(rendering, width, height);
            Texture texRed = CreateRgbaTexture(rendering, 2, 2, (255, 0, 0, 255));
            Texture texGreen = CreateRgbaTexture(rendering, 2, 2, (0, 255, 0, 255));

            Shader stdShader = AlcoSprite2DShaders.ComposeSpriteSceneSurface(
                rendering.Shaders, AlcoSprite2DShaders.SpriteSceneSurfaceStandardModule, "TestOver96Std");
            Material stdMat = stdShader.CreateMaterial("TestOver96StdMat", false);

            using var scene = new SpriteScene2D(rendering);
            var context = new RenderContext(rendering);
            var camera = new Camera2D { Size = new Vector2(width, height), Near = -10f, Far = 10f };
            var passSig = new RenderPassSignature([Format.Rgba8Unorm], Format.D32Float);
            var handles = new List<GpuInstancePool<SpriteSceneInstance>.Registration>();

            try
            {
                for (int b = 0; b < bucketCount; ++b)
                {
                    _ = scene.RegisterBucket(new SpriteSceneBucketDesc(0, stdMat, DepthStencilState.Write, BucketTag: (uint)b));
                }

                Assert.That(scene.BucketCount, Is.EqualTo(bucketCount));

                uint nearestFlags = SpriteSceneInstance.PackFlags(SpriteSceneSamplerMode.NearestClamp);
                // Bucket 1 (< 64): visible sprite near (-32, 0) in origin-relative space.
                handles.Add(scene.RegisterDeferred(1u, MakeQuad(originOffset + new Vector2(-32f, 0f), new Vector2(16f, 16f), 0f, texRed, nearestFlags), texRed));
                // Bucket 80 (>= 64): visible sprite near (+32, 0) in origin-relative space.
                handles.Add(scene.RegisterDeferred(80u, MakeQuad(originOffset + new Vector2(32f, 0f), new Vector2(16f, 16f), 0f, texGreen, nearestFlags), texGreen));
                // Bucket 95 (>= 64): culled sprite far from origin-relative viewport.
                handles.Add(scene.RegisterDeferred(95u, MakeQuad(originOffset + new Vector2(500f, 500f), new Vector2(16f, 16f), 0f, texRed, nearestFlags), texRed));

                context.BeginFrame();
                scene.PrepareAndCull(context, camera.GetViewport(), cullMargin: 0f, originOffset: originOffset);

                var passDesc = new RenderingDesc
                {
                    Colors = [new ColorAttachment { RenderView = colorTarget.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = new Vector4(0f, 0f, 0f, 1f) }],
                    Depth = new DepthAttachment { RenderView = depthTarget.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 1f },
                };
                int drawCalls;
                using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = width, Height = height });
                    render.SetScissor(new Scissor { Width = width, Height = height });
                    drawCalls = scene.RenderTier(context, in render, tier: 0, camera, originOffset: originOffset);
                }

                GpuDrawIndirectArgs[] args = ReadBackDrawArgsAndSubmit(host.Device, context, scene.DrawArgsBuffer, bucketCount);
                byte[] pixels = ReadBackRgba8FreshFrame(host.Device, rendering, colorTarget, width, height);

                Assert.Multiple(() =>
                {
                    Assert.That(drawCalls, Is.EqualTo(3));
                    Assert.That(args[1].InstanceCount, Is.EqualTo(1u));
                    Assert.That(args[80].InstanceCount, Is.EqualTo(1u));
                    Assert.That(args[95].InstanceCount, Is.EqualTo(0u));
                    AssertPixel(pixels, width, height, -32f, 0f, 255, 0, 0, 255, 5, "Bucket < 64 with OriginOffset");
                    AssertPixel(pixels, width, height, 32f, 0f, 0, 255, 0, 255, 5, "Bucket >= 64 with OriginOffset");
                    AssertPixel(pixels, width, height, 0f, 0f, 0, 0, 0, 255, 5, "Background stays unlit");
                });
            }
            finally
            {
                for (int i = 0; i < handles.Count; ++i)
                {
                    handles[i].Dispose();
                }

                context.Destroy();
                if (!colorTarget.IsDestroyed) colorTarget.Destroy();
                if (!depthTarget.IsDestroyed) depthTarget.Destroy();
                if (!texRed.IsDestroyed) texRed.Destroy();
                if (!texGreen.IsDestroyed) texGreen.Destroy();
            }
        }
    }

    private static SpriteSceneInstance MakeQuad(Vector2 pos, Vector2 scale, float depth, Texture texture, uint flags)
    {
        return new SpriteSceneInstance
        {
            Position = pos,
            Rotation = Rotation2D.Identity,
            Scale = scale,
            Depth = depth,
            Color = Vector4.One,
            UvRect = Rect.One,
            TextureId = texture.SampledIndex,
            Flags = flags,
        };
    }

    private static bool TryCreateGpuHost(GraphicsBackend backend, [NotNullWhen(true)] out RenderingTestHost? host)
    {
        if (backend == GraphicsBackend.Vulkan && !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Ignore("Full Vulkan backend is tested on Windows/Linux.");
            host = null;
            return false;
        }

        try
        {
            host = Utility.CreateRenderingSystem(backend);
            return true;
        }
        catch (GPUException ex) when (ex.Error == Error.Unsupported)
        {
            Assert.Ignore($"Backend {backend} is unsupported on this host: {ex.Message}");
            host = null;
            return false;
        }
    }

    private static Texture CreateColorTarget(RenderingSystem rendering, int width, int height)
    {
        return rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)width, (uint)height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
        });
    }

    private static Texture CreateDepthTarget(RenderingSystem rendering, int width, int height)
    {
        return rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)width, (uint)height, 1u),
            Format = Format.D32Float,
            Usage = TextureUsage.DepthStencilAttachment,
        });
    }

    private static Texture CreateRgbaTexture(RenderingSystem rendering, int width, int height, (byte R, byte G, byte B, byte A) rgba)
    {
        byte[] bytes = new byte[width * height * 4];
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i + 0] = rgba.R;
            bytes[i + 1] = rgba.G;
            bytes[i + 2] = rgba.B;
            bytes[i + 3] = rgba.A;
        }

        Texture texture = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)width, (uint)height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        rendering.UploadTexture(texture, bytes);
        return texture;
    }

    private static Texture CreateSplitMaskTexture(RenderingSystem rendering, int size)
    {
        byte[] bytes = new byte[size * size * 4];
        int half = size / 2;
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                int o = (y * size + x) * 4;
                byte mask = x < half ? (byte)255 : (byte)0;
                bytes[o + 0] = mask;
                bytes[o + 1] = mask;
                bytes[o + 2] = mask;
                bytes[o + 3] = 255;
            }
        }

        Texture texture = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)size, (uint)size, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        rendering.UploadTexture(texture, bytes);
        return texture;
    }

    private static GpuDrawIndirectArgs[] ReadBackDrawArgsAndSubmit(
        GPUDevice device,
        RenderContext context,
        Alco.Graphics.Buffer drawArgsBuffer,
        int bucketCount)
    {
        GPUHeap readback = device.AllocateGPUHeap(drawArgsBuffer.SizeBytes, MemoryType.Readback);
        try
        {
            context.Barrier(Stage.Compute | Stage.Indirect, Access.ShaderWrite | Access.IndirectRead, Stage.Transfer, Access.TransferRead);
            context.CopyMemory(drawArgsBuffer.Range, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();

            GpuDrawIndirectArgs[] result = new GpuDrawIndirectArgs[bucketCount];
            var src = new ReadOnlySpan<GpuDrawIndirectArgs>(readback.Range.Cpu, bucketCount);
            src.CopyTo(result);
            return result;
        }
        finally
        {
            readback.Destroy();
        }
    }

    private static byte[] ReadBackRgba8FreshFrame(GPUDevice device, RenderingSystem rendering, Texture target, int width, int height)
    {
        ulong byteCount = (ulong)width * (ulong)height * 4UL;
        GPUHeap readback = device.AllocateGPUHeap(byteCount, MemoryType.Readback);
        var context = new RenderContext(rendering);
        try
        {
            context.BeginFrame();
            context.Barrier(Stage.AllCommands, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();

            byte[] pixels = new byte[(int)byteCount];
            new ReadOnlySpan<byte>(readback.Range.Cpu, (int)byteCount).CopyTo(pixels);
            return pixels;
        }
        finally
        {
            context.Destroy();
            readback.Destroy();
        }
    }

    private static (byte R, byte G, byte B, byte A) SampleWorldPixel(byte[] pixels, int width, int height, float worldX, float worldY)
    {
        int px = Math.Clamp((int)MathF.Floor(width * 0.5f + worldX), 0, width - 1);
        int py = Math.Clamp((int)MathF.Floor(height * 0.5f - worldY), 0, height - 1);
        int offset = (py * width + px) * 4;
        return (pixels[offset + 0], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    private static void AssertPixel(
        byte[] pixels,
        int width,
        int height,
        float worldX,
        float worldY,
        byte r,
        byte g,
        byte b,
        byte a,
        int tolerance,
        string label)
    {
        (byte ar, byte ag, byte ab, byte aa) = SampleWorldPixel(pixels, width, height, worldX, worldY);
        Assert.Multiple(() =>
        {
            Assert.That(ar, Is.EqualTo(r).Within(tolerance), $"{label} R at ({worldX}, {worldY})");
            Assert.That(ag, Is.EqualTo(g).Within(tolerance), $"{label} G at ({worldX}, {worldY})");
            Assert.That(ab, Is.EqualTo(b).Within(tolerance), $"{label} B at ({worldX}, {worldY})");
            Assert.That(aa, Is.EqualTo(a).Within(tolerance), $"{label} A at ({worldX}, {worldY})");
        });
    }

    private static int CountLitPixelsAroundBand(
        byte[] pixels,
        int width,
        int height,
        float minX,
        float maxY,
        float maxX,
        float minY,
        float excludeMinX,
        float excludeMaxX)
    {
        int x0 = Math.Clamp((int)MathF.Floor(width * 0.5f + minX), 0, width - 1);
        int x1 = Math.Clamp((int)MathF.Ceiling(width * 0.5f + maxX), 0, width - 1);
        int ex0 = Math.Clamp((int)MathF.Floor(width * 0.5f + excludeMinX), 0, width - 1);
        int ex1 = Math.Clamp((int)MathF.Ceiling(width * 0.5f + excludeMaxX), 0, width - 1);
        int y0 = Math.Clamp((int)MathF.Floor(height * 0.5f - maxY), 0, height - 1);
        int y1 = Math.Clamp((int)MathF.Ceiling(height * 0.5f - minY), 0, height - 1);

        int count = 0;
        for (int y = y0; y <= y1; ++y)
        {
            for (int x = x0; x <= x1; ++x)
            {
                if (x >= ex0 && x <= ex1)
                {
                    continue;
                }

                int o = (y * width + x) * 4;
                if (pixels[o + 0] > 128 && pixels[o + 1] > 128 && pixels[o + 2] > 128)
                {
                    ++count;
                }
            }
        }

        return count;
    }

    private static int CountGreenPixels(byte[] pixels, int width, int height, float minX, float maxY, float maxX, float minY)
    {
        int x0 = Math.Clamp((int)MathF.Floor(width * 0.5f + minX), 0, width - 1);
        int x1 = Math.Clamp((int)MathF.Ceiling(width * 0.5f + maxX), 0, width - 1);
        int y0 = Math.Clamp((int)MathF.Floor(height * 0.5f - maxY), 0, height - 1);
        int y1 = Math.Clamp((int)MathF.Ceiling(height * 0.5f - minY), 0, height - 1);

        int count = 0;
        for (int y = y0; y <= y1; ++y)
        {
            for (int x = x0; x <= x1; ++x)
            {
                int o = (y * width + x) * 4;
                if (pixels[o + 1] > 180 && pixels[o + 0] < 40 && pixels[o + 2] < 40)
                {
                    ++count;
                }
            }
        }

        return count;
    }
}
