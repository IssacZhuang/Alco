using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Cross-target shader ABI tests, dynamic bucket reassignment tests, math transform equivalence tests,
/// and multi-backend GPU culling/rendering/multi-view readback tests for <see cref="GpuInstancePool{TInstance}"/>
/// and <see cref="MeshScene3D"/>.
/// </summary>
[TestFixture]
public unsafe class TestMeshScene3D
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedResetArgsRoot
    {
        public GPUBufferView BucketMeta;
        public GPUBufferView DrawArgs;
        public uint BucketCount;
        public uint Pad0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedCullRoot
    {
        public Vector4 Plane0;
        public Vector4 Plane1;
        public Vector4 Plane2;
        public Vector4 Plane3;
        public Vector4 Plane4;
        public Vector4 Plane5;
        public GPUBufferView Pool;
        public GPUBufferView SlotBuckets;
        public GPUBufferView BucketMeta;
        public GPUBufferView DrawArgs;
        public GPUBufferView VisibleSlots;
        public uint SlotCount;
        public uint BucketCount;
        public float CullMargin;
        public uint RequiredCullMask;
        public uint PlaneCount;
        public uint Pad0;
        public Vector3 OriginOffset;
        public uint Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CustomMeshParams
    {
        public Vector4 Tint;
        public float DisplaceZ;
        public float AlphaCutoff;
        public float Pad0;
        public float Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LayoutProbeRoot
    {
        public VertexPBR Vertex;
        public MeshScene3DInstance Instance;
        public MeshScene3DBucketMeta BucketMeta;
        public MeshScene3DPassHeader PassHeader;
    }

    private const string CustomSurfaceModule = "TestCustomMeshScene3DSurface";

    private const string CustomSurfaceSource = """
        #language slang 2025
        module TestCustomMeshScene3DSurface;

        import Core;
        import SharedSampler;
        import AlcoRendering_MeshScene3D;

        public struct CustomMeshParams
        {
            public float4 Tint;
            public float DisplaceZ;
            public float AlphaCutoff;
            public float Pad0;
            public float Pad1;
        };

        public struct Surface : IMeshScene3DSurface
        {
            public typedef CustomMeshParams Params;

            public override MeshScene3DTransformedVertex TransformVertex(MeshScene3DVertexInput input, Params p)
            {
                MeshScene3DTransformedVertex v;
                float3 localPos = input.Vertex.Position + float3(0.0, 0.0, p.DisplaceZ);
                v.WorldPos = meshScene3DTransformPosition(input.Instance, localPos);
                v.WorldNormal = meshScene3DTransformNormal(input.Instance, input.Vertex.Normal);
                v.Uv = input.Vertex.Uv;
                return v;
            }

            public float4 GetColor(MeshScene3DSurfaceInput input, Params p)
            {
                float4 baseCol = input.TextureId != 0u
                    ? meshScene3DSampleTexture(input.TextureId, input.Flags, input.Uv, input.DdxUv, input.DdyUv)
                    : float4(1.0, 1.0, 1.0, 1.0);
                return float4(baseCol.rgb * input.Color.rgb * p.Tint.rgb, baseCol.a * input.Color.a * p.Tint.a);
            }

            public override float GetAlphaCutoff(Params p)
            {
                return p.AlphaCutoff > 0.0 ? p.AlphaCutoff : 0.5;
            }
        }
        """;

    private const string LayoutProbeShaderSource = """
        #language slang 2025
        module TestMeshScene3DLayoutProbe;

        import Core;
        import AlcoRendering_MeshScene3D;

        [GPURoot]
        public struct LayoutProbeRoot
        {
            public MeshScene3DVertex Vertex;
            public MeshScene3DInstance Instance;
            public MeshScene3DBucketMeta BucketMeta;
            public MeshScene3DPassHeader PassHeader;
        };

        [shader("compute")]
        [numthreads(1, 1, 1)]
        public void mainCS()
        {
            LayoutProbeRoot root = gpu_load_root<LayoutProbeRoot>();
            if (root.BucketMeta.IndexCount == 999999u)
            {
                return;
            }
        }
        """;

    /// <summary>Verifies MeshScene3D compute, vertex-pulling, and composed surface shaders compile and match C# layouts across all targets.</summary>
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.MetalSource)]
    [TestCase(ShaderTarget.Dxil)]
    public void MeshScene3DShaders_CompileAndMatchLayouts_AcrossAllTargets(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
        {
            Assert.Ignore($"Shader target {target} is unsupported on this host.");
            return;
        }

        ShaderCompilerOptions options = new()
        {
            FileResolver = AlcoMesh3DShaders.CreateFileResolver(),
        };
        using ShaderCompiler compiler = new(target, options);

        AlcoSprite2DShaders.BuiltInModuleOrder.ToList().ForEach(moduleName =>
            compiler.LoadModuleFromSource(moduleName, moduleName + ".slang", AlcoSprite2DShaders.LoadEmbeddedSource(moduleName + ".slang")));
        foreach (string moduleName in AlcoMesh3DShaders.BuiltInModuleOrder)
        {
            _ = compiler.LoadModuleFromSource(
                moduleName,
                moduleName + ".slang",
                AlcoMesh3DShaders.LoadEmbeddedSource(moduleName + ".slang"));
        }

        _ = compiler.LoadModuleFromSource(
            CustomSurfaceModule,
            CustomSurfaceModule + ".slang",
            CustomSurfaceSource);

        CompiledShader resetArgs = compiler.CompileSource(
            AlcoMesh3DShaders.MeshScene3DResetArgsModule,
            AlcoMesh3DShaders.MeshScene3DResetArgsModule + ".slang",
            AlcoMesh3DShaders.LoadEmbeddedSource("MeshScene3DResetArgs.slang"),
            ShaderPipelineKind.Compute);

        CompiledShader cull = compiler.CompileSource(
            AlcoMesh3DShaders.MeshScene3DCullModule,
            AlcoMesh3DShaders.MeshScene3DCullModule + ".slang",
            AlcoMesh3DShaders.LoadEmbeddedSource("MeshScene3DCull.slang"),
            ShaderPipelineKind.Compute);

        CompiledShader probe = compiler.CompileSource(
            "TestMeshScene3DLayoutProbe",
            "TestMeshScene3DLayoutProbe.slang",
            LayoutProbeShaderSource,
            ShaderPipelineKind.Compute);

        Assert.Multiple(() =>
        {
            Assert.That(sizeof(MeshScene3DInstance), Is.EqualTo(128));
            Assert.That(sizeof(MeshScene3DBucketMeta), Is.EqualTo(48));
            Assert.That(sizeof(MeshScene3DPassHeader), Is.EqualTo(144));
            Assert.That(sizeof(VertexPBR), Is.EqualTo(48));
            Assert.That(resetArgs.Reflection.MatchesStructLayout<ExpectedResetArgsRoot>("MeshScene3DResetArgsRoot"), Is.True);
            Assert.That(cull.Reflection.MatchesStructLayout<ExpectedCullRoot>("MeshScene3DCullRoot"), Is.True);
            Assert.That(probe.Reflection.MatchesStructLayout<LayoutProbeRoot>("LayoutProbeRoot"), Is.True);
        });

        (string Surface, uint ExpectedMinTailBytes)[] surfaces =
        [
            (AlcoMesh3DShaders.MeshScene3DSurfaceStandardModule, 4u),
            (CustomSurfaceModule, (uint)sizeof(CustomMeshParams)),
        ];

        string[] alphaSpecs = ["false", "true"];
        foreach ((string surface, uint expectedTail) in surfaces)
        {
            foreach (string alphaSpec in alphaSpecs)
            {
                CompiledShader composed = compiler.CompileComposed(
                    AlcoMesh3DShaders.MeshScene3DPassTemplateModule,
                    surface,
                    ShaderPipelineKind.Graphics,
                    new ShaderCompileOptions { SpecializationArgs = [alphaSpec] });

                Assert.Multiple(() =>
                {
                    Assert.That(composed.Reflection.Root, Is.Not.Null, $"{surface} alphaTest={alphaSpec}");
                    Assert.That(composed.Reflection.Root!.Find("Pass")?.Type.SizeBytes, Is.EqualTo((uint)sizeof(MeshScene3DPassHeader)), $"{surface} header");
                    Assert.That(composed.Reflection.Root.Find("Pass.Vertices")?.OffsetBytes, Is.EqualTo(64u), $"{surface} Pass.Vertices");
                    Assert.That(composed.Reflection.Root.Find("Pass.VisibleSlots")?.OffsetBytes, Is.EqualTo(80u), $"{surface} Pass.VisibleSlots");
                    Assert.That(composed.Reflection.Root.Find("Pass.PassFlags")?.OffsetBytes, Is.EqualTo(124u), $"{surface} Pass.PassFlags");
                    Assert.That(composed.Reflection.Root.SizeBytes, Is.GreaterThanOrEqualTo((uint)sizeof(MeshScene3DPassHeader) + expectedTail));
                    if (expectedTail == (uint)sizeof(CustomMeshParams))
                    {
                        Assert.That(composed.Reflection.Root.SizeBytes, Is.EqualTo((uint)(sizeof(MeshScene3DPassHeader) + sizeof(CustomMeshParams))));
                    }
                });
            }
        }
    }

    /// <summary>Verifies deferred and direct dynamic bucket switching on <see cref="GpuInstancePool{TInstance}"/> without GPU execution.</summary>
    [Test]
    public void GpuInstancePool_SetBucket_And_UpdateBucketDirect_NoGpu()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        RenderingSystem rendering = host.RenderingSystem;

        using var pool = new GpuInstancePool<MeshScene3DInstance>(rendering);
        var handles = new List<GpuInstancePool<MeshScene3DInstance>.Registration>();
        try
        {
            for (int i = 0; i < 40; ++i)
            {
                uint bucket = (uint)(i % 4);
                handles.Add(pool.RegisterDeferred(bucket, MeshScene3DInstance.Hidden));
            }

            // Same-frame SetBucket before Flush: handle 0 moves from bucket 0 -> bucket 5.
            handles[0].SetBucket(5u);

            // Direct slots spanning >32 buckets to also exercise _bucketCounts expansion.
            int directSlotA = pool.AddDirect(2u, MeshScene3DInstance.Hidden);
            int directSlotB = pool.AddDirect(70u, MeshScene3DInstance.Hidden);

            bool changed1 = pool.Flush();
            Assert.Multiple(() =>
            {
                Assert.That(changed1, Is.True);
                Assert.That(pool.LiveCount, Is.EqualTo(42));
                Assert.That(handles[0].BucketId, Is.EqualTo(5u));
                Assert.That(pool.GetBucketCount(0u), Is.EqualTo(9));
                Assert.That(pool.GetBucketCount(1u), Is.EqualTo(10));
                Assert.That(pool.GetBucketCount(2u), Is.EqualTo(11));
                Assert.That(pool.GetBucketCount(3u), Is.EqualTo(10));
                Assert.That(pool.GetBucketCount(5u), Is.EqualTo(1));
                Assert.That(pool.GetBucketCount(70u), Is.EqualTo(1));
            });

            // Mutate buckets across live handles (multiple queued updates before flush) and direct slots.
            handles[1].SetBucket(80u);
            handles[1].SetBucket(95u); // last one wins
            handles[2].SetBucket(5u);
            pool.UpdateBucketDirect(directSlotA, 95u);
            pool.UpdateBucketDirect(directSlotB, 5u);

            bool changed2 = pool.Flush();
            bool changedNoop = pool.Flush();
            Assert.Multiple(() =>
            {
                Assert.That(changed2, Is.True);
                Assert.That(changedNoop, Is.False);
                Assert.That(handles[1].BucketId, Is.EqualTo(95u));
                Assert.That(pool.GetBucketCount(1u), Is.EqualTo(9));
                Assert.That(pool.GetBucketCount(2u), Is.EqualTo(9));
                Assert.That(pool.GetBucketCount(5u), Is.EqualTo(3));
                Assert.That(pool.GetBucketCount(70u), Is.EqualTo(0));
                Assert.That(pool.GetBucketCount(95u), Is.EqualTo(2));
                Assert.That(pool.LiveCount, Is.EqualTo(42));
            });

            // Dispose swapped handle and remove direct slot, verify counts drop cleanly.
            handles[0].Dispose();
            pool.RemoveDirect(directSlotB);
            bool changed3 = pool.Flush();
            Assert.Multiple(() =>
            {
                Assert.That(changed3, Is.True);
                Assert.That(pool.GetBucketCount(5u), Is.EqualTo(1));
                Assert.That(pool.LiveCount, Is.EqualTo(40));
            });
        }
        finally
        {
            for (int i = 0; i < handles.Count; ++i)
            {
                handles[i].Dispose();
            }
        }
    }

    /// <summary>Verifies <see cref="MeshScene3DInstance.FromTransform2DMatrix"/> matches <see cref="Alco.math.rotate(Vector2, Rotation2D)"/> and hierarchical 3D matrices match <see cref="Transform3D"/>.</summary>
    [Test]
    public void MeshScene3D_TransformHelpers_Match2DRotationAnd3DHierarchy()
    {
        float[] anglesDeg = [0f, 37f, 90f, 180f, -45f, 132.5f];
        Vector3 localPoint = new(1.75f, -0.6f, 2.4f);

        foreach (float deg in anglesDeg)
        {
            Rotation2D rot2D = new(deg);
            Transform2D t2D = new(new Vector2(4.2f, -3.1f), rot2D, new Vector2(1.5f, 0.8f));
            Matrix4x4 m2D = MeshScene3DInstance.FromTransform2DMatrix(in t2D, z: 2.5f, zScale: 1.25f);

            Vector2 scaledXY = new(localPoint.X * t2D.Scale.X, localPoint.Y * t2D.Scale.Y);
            Vector2 expectedXY = math.rotate(scaledXY, rot2D) + t2D.Position;
            Vector3 expected = new(expectedXY.X, expectedXY.Y, localPoint.Z * 1.25f + 2.5f);

            MeshScene3DInstance inst = default;
            inst.SetWorldMatrix(in m2D);
            Vector3 cpuTransformed = TransformPositionCpu(in inst, localPoint);
            Matrix4x4 roundTrip = inst.GetWorldMatrix();

            Assert.Multiple(() =>
            {
                Assert.That(cpuTransformed.X, Is.EqualTo(expected.X).Within(1e-5f), $"X at {deg} deg");
                Assert.That(cpuTransformed.Y, Is.EqualTo(expected.Y).Within(1e-5f), $"Y at {deg} deg");
                Assert.That(cpuTransformed.Z, Is.EqualTo(expected.Z).Within(1e-5f), $"Z at {deg} deg");
                Assert.That(roundTrip.M41, Is.EqualTo(4.2f).Within(1e-5f));
                Assert.That(roundTrip.M42, Is.EqualTo(-3.1f).Within(1e-5f));
                Assert.That(roundTrip.M43, Is.EqualTo(2.5f).Within(1e-5f));
                Assert.That(roundTrip.M44, Is.EqualTo(1.0f).Within(1e-5f));
            });
        }

        // Hierarchical 3D transform with pitch/roll/yaw.
        Transform3D hull = new(
            new Vector3(10f, -4f, 0.5f),
            Quaternion.CreateFromYawPitchRoll(0.4f, -0.25f, 0.35f),
            new Vector3(1.2f, 1.0f, 1.1f));
        Transform3D turret = new(
            new Vector3(0f, 0.375f, 1.1f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.9f),
            Vector3.One);

        Matrix4x4 composed = MeshScene3DInstance.ComposeTransform(in turret, in hull);
        Vector3 expectedHierarchy = Vector3.Transform(Vector3.Transform(localPoint, turret.Matrix), hull.Matrix);
        Vector3 actualHierarchy = Vector3.Transform(localPoint, composed);
        Assert.Multiple(() =>
        {
            Assert.That(actualHierarchy.X, Is.EqualTo(expectedHierarchy.X).Within(1e-5f));
            Assert.That(actualHierarchy.Y, Is.EqualTo(expectedHierarchy.Y).Within(1e-5f));
            Assert.That(actualHierarchy.Z, Is.EqualTo(expectedHierarchy.Z).Within(1e-5f));
        });

        // Degenerate flat-Z shadow projection cull view must yield finite planes without NaN/Inf.
        CameraData2D cam = new() { Size = new Vector2(40f, 20f), Near = -50f, Far = 50f };
        Matrix4x4 shadowShear = new(
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0.3f, 0.5f, 0f, 0f,
            0f, 0f, 0f, 1f);
        MeshScene3DCullView shadowCull = MeshScene3DCullView.FromViewProjection(shadowShear * cam.ViewProjectionMatrix, cullMargin: 1.0f, requiredCullMask: 2u);
        Assert.Multiple(() =>
        {
            Assert.That(IsFinite(shadowCull.Plane0), Is.True);
            Assert.That(IsFinite(shadowCull.Plane1), Is.True);
            Assert.That(IsFinite(shadowCull.Plane2), Is.True);
            Assert.That(IsFinite(shadowCull.Plane3), Is.True);
            Assert.That(IsFinite(shadowCull.Plane4), Is.True);
            Assert.That(IsFinite(shadowCull.Plane5), Is.True);
        });
    }

    /// <summary>
    /// Verifies >64 buckets (100 independent geometries, zero mesh merging), 3D pitch/roll transforms,
    /// frustum culling, live bucket reassignment via SetBucket/UpdateBucketDirect, and secondary CullOutput + materialOverride.
    /// </summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.Metal)]
    public void MeshScene3D_GpuCulling_Over100Buckets_3DTransforms_AndSetBucket(GraphicsBackend backend)
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
            const int bucketCount = 100;

            Texture colorTarget = CreateColorTarget(rendering, width, height);
            Texture depthTarget = CreateDepthTarget(rendering, width, height);
            Texture texRed = CreateRgbaTexture(rendering, 2, 2, (255, 40, 40, 255));
            Texture texGreen = CreateRgbaTexture(rendering, 2, 2, (40, 255, 40, 255));
            Texture texBlue = CreateRgbaTexture(rendering, 2, 2, (40, 120, 255, 255));

            Shader stdShader = AlcoMesh3DShaders.ComposeMeshScene3DSurface(
                rendering.Shaders, AlcoMesh3DShaders.MeshScene3DSurfaceStandardModule, "TestMeshStd");
            Shader overrideShader = AlcoMesh3DShaders.ComposeMeshScene3DSurfaceFromSource(
                rendering.Shaders,
                CustomSurfaceModule,
                CustomSurfaceModule + ".slang",
                CustomSurfaceSource,
                "TestMeshOverride");

            Material mainMat = stdShader.CreateMaterial("TestMeshStdMat", false);
            mainMat.Blend = BlendState.Opaque;
            mainMat.Rasterization = RasterizationState.CullNone;
            mainMat.DepthStencilState = DepthStencilState.Write;

            Material overrideMat = overrideShader.CreateMaterial("TestMeshOverrideMat", false);
            overrideMat.Blend = BlendState.Opaque;
            overrideMat.Rasterization = RasterizationState.CullNone;
            overrideMat.DepthStencilState = DepthStencilState.Write;
            CustomMeshParams overrideParams = new() { Tint = new Vector4(0.2f, 1.0f, 1.0f, 1.0f), DisplaceZ = 0f, AlphaCutoff = 0.5f };
            overrideMat.SetData("Material", ref overrideParams);

            var geometries = new List<ModelGeometry>(bucketCount);
            using var scene = new MeshScene3D(rendering);
            using MeshScene3DCullOutput secondaryCullOutput = scene.CreateCullOutput();
            var handles = new List<GpuInstancePool<MeshScene3DInstance>.Registration>();
            var context = new RenderContext(rendering);

            try
            {
                // Register 100 independent ModelGeometry buckets (proving >64 bucket limit does not exist).
                for (int b = 0; b < bucketCount; ++b)
                {
                    float halfSize = b == 99 ? 10.0f : 6.0f;
                    ModelGeometry geom = CreateQuadGeometry(rendering, halfSize, $"Quad_{b}");
                    geometries.Add(geom);
                    uint registered = scene.RegisterBucket(new MeshScene3DBucketDesc(
                        Tier: 0,
                        Geometry: geom,
                        Material: mainMat,
                        DepthStencil: DepthStencilState.Write,
                        BucketTag: (uint)b));
                    Assert.That(registered, Is.EqualTo((uint)b));
                }

                // Populate buckets 0..97 across an offscreen ring (culled out) + a few visible ones in center.
                for (int b = 0; b < 98; ++b)
                {
                    float x = b < 4 ? (-90f + b * 24f) : (500f + b * 2f);
                    MeshScene3DInstance inst = MakeInstance(
                        new Vector3(x, -30f, 0f),
                        Quaternion.Identity,
                        Vector3.One,
                        texBlue,
                        cullMask: b % 2 == 0 ? 1u : 2u);
                    handles.Add(scene.RegisterDeferred((uint)b, in inst, texBlue));
                }

                // Bucket 98: one visible tilted 3D mesh at (-40, 20, 0) + one hidden (zero-scale) mesh.
                MeshScene3DInstance tiltedRed = MakeInstance(
                    new Vector3(-40f, 20f, 0f),
                    Quaternion.CreateFromYawPitchRoll(0f, 0.35f, -0.25f),
                    new Vector3(1.5f, 1.5f, 1.5f),
                    texRed,
                    cullMask: 0x3u);
                GpuInstancePool<MeshScene3DInstance>.Registration swappable = scene.RegisterDeferred(98u, in tiltedRed, texRed);
                handles.Add(swappable);

                MeshScene3DInstance hiddenInst = MeshScene3DInstance.Hidden;
                handles.Add(scene.RegisterDeferred(98u, in hiddenInst));

                // Bucket 99 (larger halfSize=10): visible green mesh at (+40, 20, 0), mask=2 only.
                MeshScene3DInstance visibleGreen = MakeInstance(
                    new Vector3(40f, 20f, 0f),
                    Quaternion.Identity,
                    Vector3.One,
                    texGreen,
                    cullMask: 2u);
                handles.Add(scene.RegisterDeferred(99u, in visibleGreen, texGreen));

                var camera = new Camera2D { Size = new Vector2(width, height), Near = -50f, Far = 50f };
                var passSig = new RenderPassSignature([Format.Rgba8Unorm], Format.D32Float);
                var passDesc = new RenderingDesc
                {
                    Colors = [new ColorAttachment { Target = colorTarget, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = new Vector4(0f, 0f, 0f, 1f) }],
                    Depth = new DepthAttachment { Target = depthTarget, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 1f },
                };

                // Frame 1: PrepareAndCull + RenderTier.
                context.BeginFrame();
                scene.PrepareAndCull(context, MeshScene3DCullView.FromCamera(camera, cullMargin: 0f));
                int drawCalls1;
                using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = width, Height = height });
                    render.SetScissor(new Scissor { Width = width, Height = height });
                    drawCalls1 = scene.RenderTier(context, in render, tier: 0, camera);
                }

                GpuDrawIndexedIndirectArgs[] args1 = ReadBackIndexedDrawArgsAndSubmit(host.Device, context, scene.DrawArgsBuffer, bucketCount);
                byte[] pixels1 = ReadBackRgba8FreshFrame(host.Device, rendering, colorTarget, width, height);

                Assert.Multiple(() =>
                {
                    Assert.That(drawCalls1, Is.EqualTo(bucketCount)); // Each bucket has at least 1 live slot.
                    Assert.That(args1[0].InstanceCount, Is.EqualTo(1u));
                    Assert.That(args1[4].InstanceCount, Is.EqualTo(0u)); // Offscreen bucket 4 culled out.
                    Assert.That(args1[98].InstanceCount, Is.EqualTo(1u)); // Hidden slot excluded.
                    Assert.That(args1[99].InstanceCount, Is.EqualTo(1u));
                    Assert.That(args1[99].IndexCount, Is.EqualTo(6u));

                    // Tilted red at (-40, 20) & green at (+40, 20)
                    (byte rRed, byte gRed, byte bRed, _) = SampleWorldPixel(pixels1, width, height, -40f, 20f);
                    Assert.That(rRed, Is.GreaterThan((byte)150));
                    Assert.That(gRed, Is.LessThan((byte)50));

                    (byte rGreen, byte gGreen, byte bGreen, _) = SampleWorldPixel(pixels1, width, height, 40f, 20f);
                    Assert.That(gGreen, Is.GreaterThan((byte)150));
                    Assert.That(rGreen, Is.LessThan((byte)50));

                    // Offscreen area around (0, 0) stayed background black.
                    (byte rBg, byte gBg, byte bBg, _) = SampleWorldPixel(pixels1, width, height, 0f, 0f);
                    Assert.That(rBg, Is.Zero);
                    Assert.That(gBg, Is.Zero);
                    Assert.That(bBg, Is.Zero);
                });

                // Frame 2: Switch swappable instance from bucket 98 (halfSize=6) to bucket 99 (halfSize=10) via SetBucket,
                // move green out, and test secondary CullOutput with RequiredCullMask=2 + materialOverride.
                swappable.SetBucket(99u);
                MeshScene3DInstance offscreenGreen = MakeInstance(new Vector3(400f, 400f, 0f), Quaternion.Identity, Vector3.One, texGreen, cullMask: 2u);
                handles[^1].Slot.Write(in offscreenGreen);

                context.BeginFrame();
                scene.SyncToGpu(context);
                scene.Cull(context, MeshScene3DCullView.FromCamera(camera, cullMargin: 0f));
                scene.Cull(context, MeshScene3DCullView.FromCamera(camera, cullMargin: 0f, requiredCullMask: 2u), secondaryCullOutput);

                using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = width, Height = height });
                    render.SetScissor(new Scissor { Width = width, Height = height });
                    _ = scene.RenderTier(
                        context,
                        in render,
                        tier: 0,
                        camera,
                        cullOutput: secondaryCullOutput,
                        materialOverride: overrideMat);
                }

                GpuDrawIndexedIndirectArgs[] mainArgs2 = ReadBackIndexedDrawArgsAndSubmit(host.Device, context, scene.DrawArgsBuffer, bucketCount);
                GpuDrawIndexedIndirectArgs[] secArgs2 = ReadBackIndexedDrawArgsFreshFrame(host.Device, rendering, secondaryCullOutput.DrawArgsBuffer, bucketCount);
                byte[] pixels2 = ReadBackRgba8FreshFrame(host.Device, rendering, colorTarget, width, height);

                Assert.Multiple(() =>
                {
                    Assert.That(scene.GetBucketLiveCount(98u), Is.EqualTo(1)); // only hidden slot left
                    Assert.That(scene.GetBucketLiveCount(99u), Is.EqualTo(2)); // swappable + moved green
                    Assert.That(mainArgs2[98].InstanceCount, Is.EqualTo(0u));
                    Assert.That(mainArgs2[99].InstanceCount, Is.EqualTo(1u));
                    Assert.That(secArgs2[0].InstanceCount, Is.EqualTo(0u)); // bucket 0 has mask=1 -> filtered out by RequiredCullMask=2
                    Assert.That(secArgs2[1].InstanceCount, Is.EqualTo(1u)); // bucket 1 has mask=2 -> visible
                    Assert.That(secArgs2[99].InstanceCount, Is.EqualTo(1u)); // swappable has mask=3 -> visible

                    // Bucket 99 now draws swappable's halfSize=10 rotated quad scaled 1.5x -> reaches (-40, 10) too,
                    // while overrideMat tinted red down (0.2x R).
                    (byte r2, byte g2, byte b2, _) = SampleWorldPixel(pixels2, width, height, -40f, 12f);
                    Assert.That(r2, Is.GreaterThan((byte)20).And.LessThan((byte)80));
                    Assert.That(g2, Is.LessThan((byte)50));
                });
            }
            finally
            {
                for (int i = 0; i < handles.Count; ++i)
                {
                    handles[i].Dispose();
                }

                context.Dispose();
                for (int i = 0; i < geometries.Count; ++i)
                {
                    geometries[i].Dispose();
                }

                if (!colorTarget.IsDisposed) colorTarget.Dispose();
                if (!depthTarget.IsDisposed) depthTarget.Dispose();
                if (!texRed.IsDisposed) texRed.Dispose();
                if (!texGreen.IsDisposed) texGreen.Dispose();
                if (!texBlue.IsDisposed) texBlue.Dispose();
            }
        }
    }

    /// <summary>Verifies non-zero OriginOffset preserves float32 culling and vertex precision at large world coordinates.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.Metal)]
    public void MeshScene3D_OriginOffset_PreservesLargeCoordinatePrecision(GraphicsBackend backend)
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
            Vector3 originOffset = new(100_000f, 100_000f, 0f);

            Texture colorTarget = CreateColorTarget(rendering, width, height);
            Texture depthTarget = CreateDepthTarget(rendering, width, height);
            Texture texGreen = CreateRgbaTexture(rendering, 2, 2, (40, 255, 40, 255));
            Texture texRed = CreateRgbaTexture(rendering, 2, 2, (255, 40, 40, 255));

            Shader stdShader = AlcoMesh3DShaders.ComposeMeshScene3DSurface(
                rendering.Shaders, AlcoMesh3DShaders.MeshScene3DSurfaceStandardModule, "TestMeshOriginStd");
            Material mainMat = stdShader.CreateMaterial("TestMeshOriginStdMat", false);
            mainMat.Blend = BlendState.Opaque;
            mainMat.Rasterization = RasterizationState.CullNone;
            mainMat.DepthStencilState = DepthStencilState.Write;

            using ModelGeometry geom = CreateQuadGeometry(rendering, 8f, "OriginQuad");
            using var scene = new MeshScene3D(rendering);
            var context = new RenderContext(rendering);
            var camera = new Camera2D { Size = new Vector2(width, height), Near = -50f, Far = 50f };
            var passSig = new RenderPassSignature([Format.Rgba8Unorm], Format.D32Float);
            var handles = new List<GpuInstancePool<MeshScene3DInstance>.Registration>();

            try
            {
                uint bucket = scene.RegisterBucket(new MeshScene3DBucketDesc(0, geom, mainMat, DepthStencilState.Write));
                handles.Add(scene.RegisterDeferred(bucket, MakeInstance(originOffset + new Vector3(24f, 0f, 0f), Quaternion.Identity, Vector3.One, texGreen, 1u), texGreen));
                handles.Add(scene.RegisterDeferred(bucket, MakeInstance(originOffset + new Vector3(400f, 400f, 0f), Quaternion.Identity, Vector3.One, texRed, 1u), texRed));

                context.BeginFrame();
                scene.PrepareAndCull(context, MeshScene3DCullView.FromCamera(camera, cullMargin: 0f, originOffset: originOffset));
                var passDesc = new RenderingDesc
                {
                    Colors = [new ColorAttachment { Target = colorTarget, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = new Vector4(0f, 0f, 0f, 1f) }],
                    Depth = new DepthAttachment { Target = depthTarget, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 1f },
                };
                using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = width, Height = height });
                    render.SetScissor(new Scissor { Width = width, Height = height });
                    _ = scene.RenderTier(context, in render, tier: 0, camera, originOffset: originOffset);
                }

                GpuDrawIndexedIndirectArgs[] args = ReadBackIndexedDrawArgsAndSubmit(host.Device, context, scene.DrawArgsBuffer, 1);
                byte[] pixels = ReadBackRgba8FreshFrame(host.Device, rendering, colorTarget, width, height);

                Assert.Multiple(() =>
                {
                    Assert.That(args[0].InstanceCount, Is.EqualTo(1u));
                    (byte rVis, byte gVis, _, _) = SampleWorldPixel(pixels, width, height, 24f, 0f);
                    Assert.That(gVis, Is.GreaterThan((byte)150));
                    Assert.That(rVis, Is.LessThan((byte)50));
                    (byte rBg, byte gBg, byte bBg, _) = SampleWorldPixel(pixels, width, height, 0f, 0f);
                    Assert.That(rBg, Is.Zero);
                    Assert.That(gBg, Is.Zero);
                    Assert.That(bBg, Is.Zero);
                });
            }
            finally
            {
                for (int i = 0; i < handles.Count; ++i)
                {
                    handles[i].Dispose();
                }

                context.Dispose();
                if (!colorTarget.IsDisposed) colorTarget.Dispose();
                if (!depthTarget.IsDisposed) depthTarget.Dispose();
                if (!texGreen.IsDisposed) texGreen.Dispose();
                if (!texRed.IsDisposed) texRed.Dispose();
            }
        }
    }

    private static MeshScene3DInstance MakeInstance(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        Texture texture,
        uint cullMask)
    {
        MeshScene3DInstance inst = new()
        {
            Color = Vector4.One,
            TextureId = texture.SampledIndex,
            Flags = MeshScene3DInstance.PackFlags(MeshScene3DSamplerMode.NearestClamp),
            CullMask = cullMask,
        };
        inst.SetTransform(new Transform3D(position, rotation, scale));
        return inst;
    }

    private static ModelGeometry CreateQuadGeometry(RenderingSystem rendering, float halfExtent, string name)
    {
        VertexPBR[] vertices =
        [
            new VertexPBR(new Vector3(-halfExtent, -halfExtent, 0f), Vector3.UnitZ, new Vector2(0f, 1f)),
            new VertexPBR(new Vector3(halfExtent, -halfExtent, 0f), Vector3.UnitZ, new Vector2(1f, 1f)),
            new VertexPBR(new Vector3(halfExtent, halfExtent, 0f), Vector3.UnitZ, new Vector2(1f, 0f)),
            new VertexPBR(new Vector3(-halfExtent, halfExtent, 0f), Vector3.UnitZ, new Vector2(0f, 0f)),
        ];
        uint[] indices = [0u, 1u, 2u, 0u, 2u, 3u];
        return ModelGeometry.Create<VertexPBR>(
            rendering,
            vertices,
            indices,
            new Vector3(-halfExtent, -halfExtent, 0f),
            new Vector3(halfExtent, halfExtent, 0f),
            name);
    }

    private static Vector3 TransformPositionCpu(in MeshScene3DInstance inst, Vector3 localPos)
    {
        Vector4 p = new(localPos, 1f);
        return new Vector3(
            Vector4.Dot(p, inst.World0),
            Vector4.Dot(p, inst.World1),
            Vector4.Dot(p, inst.World2));
    }

    private static bool IsFinite(Vector4 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);

    private static bool TryCreateGpuHost(GraphicsBackend backend, [NotNullWhen(true)] out RenderingTestHost? host)
    {
        if (backend == GraphicsBackend.Vulkan && !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Ignore("Full Vulkan backend is tested on Windows/Linux.");
            host = null;
            return false;
        }

        if (backend == GraphicsBackend.Dx12 && !OperatingSystem.IsWindows())
        {
            Assert.Ignore("D3D12 backend is tested on Windows.");
            host = null;
            return false;
        }

        if (backend == GraphicsBackend.Metal && !OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Metal backend is tested on macOS.");
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

    private static GpuDrawIndexedIndirectArgs[] ReadBackIndexedDrawArgsAndSubmit(
        GPUDevice device,
        RenderContext context,
        Alco.Graphics.Buffer drawArgsBuffer,
        int bucketCount)
    {
        GPUHeap readback = device.AllocateGPUHeap(drawArgsBuffer.SizeBytes, MemoryType.Readback);
        try
        {
            context.Barrier(Stage.Compute | Stage.Indirect, Access.ShaderWrite | Access.IndirectRead, Stage.Transfer, Access.TransferRead);
            context.CopyMemory(drawArgsBuffer.GetRange(), readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit();
            device.WaitIdle();

            GpuDrawIndexedIndirectArgs[] result = new GpuDrawIndexedIndirectArgs[bucketCount];
            var src = new ReadOnlySpan<GpuDrawIndexedIndirectArgs>(readback.Range.Cpu, bucketCount);
            src.CopyTo(result);
            return result;
        }
        finally
        {
            readback.Destroy();
        }
    }

    private static GpuDrawIndexedIndirectArgs[] ReadBackIndexedDrawArgsFreshFrame(
        GPUDevice device,
        RenderingSystem rendering,
        Alco.Graphics.Buffer drawArgsBuffer,
        int bucketCount)
    {
        GPUHeap readback = device.AllocateGPUHeap(drawArgsBuffer.SizeBytes, MemoryType.Readback);
        var context = new RenderContext(rendering);
        try
        {
            context.BeginFrame();
            context.Barrier(Stage.AllCommands, Access.ShaderWrite | Access.IndirectRead, Stage.Transfer, Access.TransferRead);
            context.CopyMemory(drawArgsBuffer.GetRange(), readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit();
            device.WaitIdle();

            GpuDrawIndexedIndirectArgs[] result = new GpuDrawIndexedIndirectArgs[bucketCount];
            var src = new ReadOnlySpan<GpuDrawIndexedIndirectArgs>(readback.Range.Cpu, bucketCount);
            src.CopyTo(result);
            return result;
        }
        finally
        {
            context.Dispose();
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
            context.CopyTextureToMemory(target.GetNativeTexture(), readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit();
            device.WaitIdle();

            byte[] pixels = new byte[(int)byteCount];
            new ReadOnlySpan<byte>(readback.Range.Cpu, (int)byteCount).CopyTo(pixels);
            return pixels;
        }
        finally
        {
            context.Dispose();
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
}
