using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Cross-target shader compilation/ABI tests, CPU batching coalescing tests,
/// and multi-backend GPU pixel readback tests for <see cref="SpriteBatch2D"/>.
/// </summary>
[TestFixture]
public unsafe class TestSpriteBatch2D
{
    private const string HighlightSurfaceModule = "TestSprite2DHighlightSurface";
    private const string SilhouetteSurfaceModule = "TestSprite2DSilhouetteSurface";

    private const string HighlightSurfaceSource = """
        #language slang 2025
        module TestSprite2DHighlightSurface;

        import Core;
        import AlcoRendering_Sprite2D;

        public struct HighlightParams
        {
            public float4 HighlightTint;
        };

        public struct Surface : ISprite2DSurface
        {
            public typedef HighlightParams Params;

            public float4 Shade(Sprite2DSurfaceInput input, Params p)
            {
                return float4(p.HighlightTint.rgb * p.HighlightTint.a, p.HighlightTint.a);
            }
        }
        """;

    private const string SilhouetteSurfaceSource = """
        #language slang 2025
        module TestSprite2DSilhouetteSurface;

        import Core;
        import AlcoRendering_Sprite2D;

        public struct Surface : ISprite2DSurface
        {
            public typedef EmptySprite2DParams Params;

            public float4 Shade(Sprite2DSurfaceInput input, Params p)
            {
                float4 texel = sprite2DSampleTexture(input.TextureIndex, input.Flags, input.Uv);
                float luma = dot(texel.rgb, float3(0.299, 0.587, 0.114));
                float a = texel.a * input.Color.a;
                return float4(luma * input.Color.rgb * a, a);
            }
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct HighlightParams
    {
        public Vector4 HighlightTint;
    }

    /// <summary>Verifies built-in and composed 2D sprite shaders compile and match C# ABI layouts across all targets.</summary>
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.MetalSource)]
    [TestCase(ShaderTarget.Dxil)]
    public void Sprite2DShaders_CompileAndMatchLayouts_AcrossAllTargets(ShaderTarget target)
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

        compiler.LoadModuleFromSource(
            AlcoSprite2DShaders.Sprite2DSurfaceContractModule,
            AlcoSprite2DShaders.Sprite2DSurfaceContractModule + ".slang",
            AlcoSprite2DShaders.LoadEmbeddedSource("AlcoRendering_Sprite2D.slang"));

        CompiledShader defaultCompiled = compiler.CompileSource(
            AlcoSprite2DShaders.Sprite2DDefaultModule,
            AlcoSprite2DShaders.Sprite2DDefaultModule + ".slang",
            AlcoSprite2DShaders.LoadEmbeddedSource("Sprite2DDefault.slang"),
            ShaderPipelineKind.Graphics);

        Assert.Multiple(() =>
        {
            Assert.That(defaultCompiled.Reflection.Root, Is.Not.Null);
            Assert.That(defaultCompiled.Reflection.Root!.SizeBytes, Is.EqualTo((uint)sizeof(Sprite2DPassHeader)));
            Assert.That(defaultCompiled.Reflection.MatchesStructLayout<Sprite2DPassHeader>("Sprite2DDefaultRoot"), Is.True);
            Assert.That(defaultCompiled.Reflection.Root.Find("Pass.Quads")?.OffsetBytes, Is.EqualTo(64u));
            Assert.That(defaultCompiled.Reflection.Root.Find("Pass.Time")?.OffsetBytes, Is.EqualTo(72u));
        });

        _ = compiler.LoadModuleFromSource(
            AlcoSprite2DShaders.Sprite2DPassTemplateModule,
            AlcoSprite2DShaders.Sprite2DPassTemplateModule + ".slang",
            AlcoSprite2DShaders.LoadEmbeddedSource("Sprite2DPass.slang"));
        _ = compiler.LoadModuleFromSource(
            HighlightSurfaceModule,
            HighlightSurfaceModule + ".slang",
            HighlightSurfaceSource);
        _ = compiler.LoadModuleFromSource(
            SilhouetteSurfaceModule,
            SilhouetteSurfaceModule + ".slang",
            SilhouetteSurfaceSource);

        CompiledShader composed = compiler.CompileComposed(
            AlcoSprite2DShaders.Sprite2DPassTemplateModule,
            HighlightSurfaceModule,
            ShaderPipelineKind.Graphics);

        CompiledShader composedSilhouette = compiler.CompileComposed(
            AlcoSprite2DShaders.Sprite2DPassTemplateModule,
            SilhouetteSurfaceModule,
            ShaderPipelineKind.Graphics);

        Assert.Multiple(() =>
        {
            Assert.That(composed.Reflection.Root, Is.Not.Null);
            Assert.That(composed.Reflection.Root!.SizeBytes, Is.EqualTo((uint)(sizeof(Sprite2DPassHeader) + sizeof(HighlightParams))));
            Assert.That(composed.Reflection.Root.Find("Material.HighlightTint")?.OffsetBytes, Is.EqualTo(80u));
            Assert.That(composedSilhouette.Reflection.Root, Is.Not.Null);
            Assert.That(composedSilhouette.Reflection.Root!.SizeBytes, Is.GreaterThanOrEqualTo((uint)sizeof(Sprite2DPassHeader)));
        });
    }

    /// <summary>Verifies mixed draw primitives coalesce into one instanced call and custom material state snapshots split cleanly.</summary>
    [Test]
    public void SpriteBatch2D_CoalescesMixedDraws_AndSplitsOnCustomMaterial()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        RenderingSystem rendering = host.RenderingSystem;

        Texture texA = CreateRgbaTexture(rendering, 4, 4, (255, 0, 0, 255));
        Texture texB = CreateRgbaTexture(rendering, 4, 4, (0, 255, 0, 255));
        Texture texC = CreateRgbaTexture(rendering, 4, 4, (0, 0, 255, 255));
        Texture texD = CreateRgbaTexture(rendering, 4, 4, (255, 255, 0, 255));
        Texture target = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(128u, 128u, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment,
        });

        Font font = Font.Create(LoadDefaultFontBytes());
        using var glyphCache = new GlyphCache(rendering);
        using var batch = new SpriteBatch2D(rendering, glyphCache);
        Shader customShader = AlcoSprite2DShaders.ComposeSprite2DSurfaceFromSource(
            rendering.Shaders,
            HighlightSurfaceModule,
            HighlightSurfaceModule + ".slang",
            HighlightSurfaceSource,
            "TestHighlight2D");
        Material customMaterial = customShader.CreateMaterial("TestHighlight2DMat");
        customMaterial.Blend = BlendState.PremultipliedAlpha;

        var context = new RenderContext(rendering);
        var camera = new Camera2D { Size = new Vector2(128f, 128f), Near = -10f, Far = 10f };
        var passSig = new RenderPassSignature([Format.Rgba8Unorm]);

        try
        {
            // Frame 1: mixed textures, sampler/shading modes, rects, lines, 9-slice, and SDF glyphs coalesce to 1 draw call.
            batch.Begin();
            batch.DrawSprite(texA, new Transform2D(new Vector2(-40f, 30f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White, 0f, Sprite2DSamplerMode.LinearClamp, Sprite2DShadingKind.Straight);
            batch.DrawSprite(texB, new Transform2D(new Vector2(-20f, 30f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White, 0f, Sprite2DSamplerMode.NearestClamp, Sprite2DShadingKind.Straight);
            batch.DrawSprite(texC, new Transform2D(new Vector2(0f, 30f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White, 0f, Sprite2DSamplerMode.LinearRepeat, Sprite2DShadingKind.Premultiplied);
            batch.DrawSprite(texD, new Transform2D(new Vector2(20f, 30f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, new ColorFloat(0f, 1f, 0f, 1f), 0f, Sprite2DSamplerMode.LinearClamp, Sprite2DShadingKind.Straight);
            batch.DrawRect(new Vector2(40f, 30f), new Vector2(12f, 12f), new ColorFloat(1f, 0f, 0f, 1f));
            batch.DrawLine(new Vector2(-40f, 0f), new Vector2(40f, 0f), 2f, ColorFloat.White);
            batch.DrawNineSlice(texA, Rect.One, new Padding(1f), new Vector2(0f, -20f), new Vector2(24f, 16f), ColorFloat.White);
            batch.DrawText(font, "HI", new Vector2(0f, -45f), 16f, ColorFloat.White);

            context.BeginFrame();
            batch.Upload(context);
            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            SpriteBatch2DStats stats1;
            using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
            {
                render.SetViewport(new Viewport { Width = 128, Height = 128 });
                render.SetScissor(new Scissor { Width = 128, Height = 128 });
                stats1 = batch.Record(context, in render, camera);
            }
            context.Submit().Wait();

            Assert.Multiple(() =>
            {
                Assert.That(stats1.DrawCalls, Is.EqualTo(1));
                Assert.That(stats1.CustomMaterialDraws, Is.Zero);
                Assert.That(stats1.TextGlyphCount, Is.EqualTo(2));
                Assert.That(stats1.QuadCount, Is.EqualTo(17)); // 4 sprites + 1 rect + 1 line + 9 patches + 2 glyphs
            });

            // Frame 2: default -> custom(tintA) -> custom(tintB) -> default produces 4 draw runs via root snapshot change detection.
            batch.Begin();
            batch.DrawRect(new Vector2(-30f, 0f), new Vector2(16f, 16f), ColorFloat.White);

            HighlightParams tintA = new() { HighlightTint = new Vector4(1f, 0f, 0f, 1f) };
            customMaterial.SetData("Material", ref tintA);
            batch.DrawSprite(texA, new Transform2D(new Vector2(-10f, 0f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White, material: customMaterial);

            HighlightParams tintB = new() { HighlightTint = new Vector4(0f, 1f, 1f, 1f) };
            customMaterial.SetData("Material", ref tintB);
            batch.DrawSprite(texB, new Transform2D(new Vector2(10f, 0f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White, material: customMaterial);

            batch.DrawRect(new Vector2(30f, 0f), new Vector2(16f, 16f), new ColorFloat(0f, 0f, 1f, 1f));

            context.BeginFrame();
            batch.Upload(context);
            SpriteBatch2DStats stats2;
            using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
            {
                render.SetViewport(new Viewport { Width = 128, Height = 128 });
                render.SetScissor(new Scissor { Width = 128, Height = 128 });
                stats2 = batch.Record(context, in render, camera, time: 0.5f);
            }
            context.Submit().Wait();

            Assert.Multiple(() =>
            {
                Assert.That(stats2.DrawCalls, Is.EqualTo(4));
                Assert.That(stats2.CustomMaterialDraws, Is.EqualTo(2));
                Assert.That(stats2.QuadCount, Is.EqualTo(4));
            });
        }
        finally
        {
            context.Destroy();
            if (!target.IsDestroyed) target.Destroy();
            if (!texA.IsDestroyed) texA.Destroy();
            if (!texB.IsDestroyed) texB.Destroy();
            if (!texC.IsDestroyed) texC.Destroy();
            if (!texD.IsDestroyed) texD.Destroy();
        }
    }

    /// <summary>Verifies real GPU rendering of multi-texture quads, Nearest vs Linear filtering, Premultiplied, GrayscaleSilhouette, 9-slice, SDF text, and custom surfaces.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    public void SpriteBatch2D_GpuReadback_VerifiesSamplingModesNineSliceTextAndCustomSurface(GraphicsBackend backend)
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

            Texture target = rendering.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(width, height, 1u),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });
            Texture texRed = CreateRgbaTexture(rendering, 4, 4, (255, 0, 0, 255));
            Texture texGreen = CreateRgbaTexture(rendering, 4, 4, (0, 255, 0, 255));
            Texture texBlue = CreateRgbaTexture(rendering, 4, 4, (0, 0, 255, 255));
            Texture texYellow = CreateRgbaTexture(rendering, 4, 4, (255, 255, 0, 255));
            Texture texPremul = CreateRgbaTexture(rendering, 4, 4, (128, 0, 0, 128));
            Texture texChecker = CreateChecker2x2Texture(rendering);
            Texture texNineSlice = CreateNineSliceTestTexture(rendering);

            Font font = Font.Create(LoadDefaultFontBytes());
            using var glyphCache = new GlyphCache(rendering);
            using var batch = new SpriteBatch2D(rendering, glyphCache);
            Shader customShader = AlcoSprite2DShaders.ComposeSprite2DSurfaceFromSource(
                rendering.Shaders,
                HighlightSurfaceModule,
                HighlightSurfaceModule + ".slang",
                HighlightSurfaceSource,
                "TestHighlightGpu");
            Material customMaterial = customShader.CreateMaterial("TestHighlightGpuMat");
            customMaterial.Blend = BlendState.PremultipliedAlpha;

            Shader silhouetteShader = AlcoSprite2DShaders.ComposeSprite2DSurfaceFromSource(
                rendering.Shaders,
                SilhouetteSurfaceModule,
                SilhouetteSurfaceModule + ".slang",
                SilhouetteSurfaceSource,
                "TestSilhouetteGpu");
            Material silhouetteMaterial = silhouetteShader.CreateMaterial("TestSilhouetteGpuMat");
            silhouetteMaterial.Blend = BlendState.PremultipliedAlpha;

            HighlightParams customTint = new() { HighlightTint = new Vector4(0.25f, 0.50f, 0.75f, 1.0f) };
            customMaterial.SetData("Material", ref customTint);

            var context = new RenderContext(rendering);
            var camera = new Camera2D { Size = new Vector2(width, height), Near = -10f, Far = 10f };
            var passSig = new RenderPassSignature([Format.Rgba8Unorm]);

            try
            {
                batch.Begin();

                // Top band (y = +32): 4 distinct textures, Nearest vs Linear checkerboard, Premultiplied, custom ISprite2DSurface silhouette.
                batch.DrawSprite(texRed, new Transform2D(new Vector2(-112f, 32f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White);
                batch.DrawSprite(texGreen, new Transform2D(new Vector2(-88f, 32f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White);
                batch.DrawSprite(texBlue, new Transform2D(new Vector2(-64f, 32f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White);
                batch.DrawSprite(texYellow, new Transform2D(new Vector2(-40f, 32f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White);

                batch.DrawSprite(texChecker, new Transform2D(new Vector2(-8f, 32f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White, 0f, Sprite2DSamplerMode.NearestClamp, Sprite2DShadingKind.Straight);
                batch.DrawSprite(texChecker, new Transform2D(new Vector2(24f, 32f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White, 0f, Sprite2DSamplerMode.LinearClamp, Sprite2DShadingKind.Straight);

                batch.DrawSprite(texPremul, new Transform2D(new Vector2(56f, 32f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White, 0f, Sprite2DSamplerMode.NearestClamp, Sprite2DShadingKind.Premultiplied);
                batch.DrawSprite(texRed, new Transform2D(new Vector2(88f, 32f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, new ColorFloat(0f, 1f, 0f, 1f), 0f, Sprite2DSamplerMode.NearestClamp, Sprite2DShadingKind.Straight, material: silhouetteMaterial);

                // Bottom band (y = -32): 9-slice, SDF text, and custom ISprite2DSurface highlight material.
                batch.DrawNineSlice(
                    texNineSlice,
                    Rect.One,
                    new Padding(2f),
                    new Vector2(-80f, -32f),
                    new Vector2(48f, 24f),
                    ColorFloat.White,
                    borderScale: 1f,
                    sampler: Sprite2DSamplerMode.NearestClamp);

                batch.DrawText(font, "A", new Vector2(8f, -32f), 36f, ColorFloat.White);
                batch.DrawSprite(texRed, new Transform2D(new Vector2(88f, -32f), Rotation2D.Identity, new Vector2(16f, 16f)), Rect.One, ColorFloat.White, material: customMaterial);

                context.BeginFrame();
                batch.Upload(context);

                var passDesc = new RenderingDesc
                {
                    Colors =
                    [
                        new ColorAttachment
                        {
                            RenderView = target.RenderView,
                            Load = LoadOp.Clear,
                            Store = StoreOp.Store,
                            Clear = new Vector4(0f, 0f, 0f, 1f),
                        },
                    ],
                };
                SpriteBatch2DStats stats;
                using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = width, Height = height });
                    render.SetScissor(new Scissor { Width = width, Height = height });
                    stats = batch.Record(context, in render, camera);
                }

                byte[] pixels = ReadBackColorRgba8AndSubmit(host.Device, context, target, width, height);

                Assert.Multiple(() =>
                {
                    Assert.That(stats.DrawCalls, Is.EqualTo(4));
                    Assert.That(stats.CustomMaterialDraws, Is.EqualTo(2));

                    AssertPixel(pixels, width, height, -112f, 32f, 255, 0, 0, 255, 5, "Red tile");
                    AssertPixel(pixels, width, height, -88f, 32f, 0, 255, 0, 255, 5, "Green tile");
                    AssertPixel(pixels, width, height, -64f, 32f, 0, 0, 255, 255, 5, "Blue tile");
                    AssertPixel(pixels, width, height, -40f, 32f, 255, 255, 0, 255, 5, "Yellow tile");

                    // Nearest checkerboard: crisp black top-left quadrant and white top-right quadrant.
                    AssertPixel(pixels, width, height, -12f, 36f, 0, 0, 0, 255, 5, "Nearest checker TL");
                    AssertPixel(pixels, width, height, -4f, 36f, 255, 255, 255, 255, 5, "Nearest checker TR");

                    // Linear checkerboard near center seam averages black & white into mid gray.
                    (byte lr, byte lg, byte lb, byte la) = SampleWorldPixel(pixels, width, height, 24f, 32f);
                    Assert.That(lr, Is.InRange((byte)80, (byte)175), $"Linear checker center should blend, got R={lr}");
                    Assert.That(lg, Is.EqualTo(lr).Within(2));
                    Assert.That(lb, Is.EqualTo(lr).Within(2));
                    Assert.That(la, Is.EqualTo((byte)255));

                    // Premultiplied (128, 0, 0, 128) over black keeps ~128 without double premultiplication.
                    (byte pr, byte pg, byte pb, _) = SampleWorldPixel(pixels, width, height, 56f, 32f);
                    Assert.That(pr, Is.InRange((byte)118, (byte)138), $"Premultiplied red expected ~128, got {pr}");
                    Assert.That(pg, Is.Zero);
                    Assert.That(pb, Is.Zero);

                    // GrayscaleSilhouette: BT.601 luma of (255,0,0) = 0.299 * green tint => ~76 green.
                    (byte gr, byte gg, byte gb, _) = SampleWorldPixel(pixels, width, height, 88f, 32f);
                    Assert.That(gr, Is.LessThanOrEqualTo((byte)5));
                    Assert.That(gg, Is.InRange((byte)68, (byte)84), $"Grayscale green expected ~76, got {gg}");
                    Assert.That(gb, Is.LessThanOrEqualTo((byte)5));

                    // 9-slice corners remain cyan while center stays magenta.
                    AssertPixel(pixels, width, height, -103f, -21f, 0, 255, 255, 255, 5, "9-slice TL corner");
                    AssertPixel(pixels, width, height, -57f, -43f, 0, 255, 255, 255, 5, "9-slice BR corner");
                    AssertPixel(pixels, width, height, -80f, -32f, 255, 0, 255, 255, 5, "9-slice center");

                    // SDF text "A" produces crisp bright pixels inside its bounding region while outside stays black.
                    int brightTextPixels = CountBrightPixelsInWorldBox(pixels, width, height, -12f, -12f, 28f, -52f, minChannel: 200);
                    Assert.That(brightTextPixels, Is.GreaterThan(10), "SDF glyph 'A' must render lit pixels.");
                    AssertPixel(pixels, width, height, 36f, -32f, 0, 0, 0, 255, 5, "Outside SDF text remains clear black");

                    // Custom ISprite2DSurface material overwrites output with HighlightTint (0.25, 0.50, 0.75, 1.0).
                    AssertPixel(pixels, width, height, 88f, -32f, 64, 128, 191, 255, 6, "Custom Sprite2D surface");
                });
            }
            finally
            {
                context.Destroy();
                if (!target.IsDestroyed) target.Destroy();
                if (!texRed.IsDestroyed) texRed.Destroy();
                if (!texGreen.IsDestroyed) texGreen.Destroy();
                if (!texBlue.IsDestroyed) texBlue.Destroy();
                if (!texYellow.IsDestroyed) texYellow.Destroy();
                if (!texPremul.IsDestroyed) texPremul.Destroy();
                if (!texChecker.IsDestroyed) texChecker.Destroy();
                if (!texNineSlice.IsDestroyed) texNineSlice.Destroy();
            }
        }
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

    private static byte[] LoadDefaultFontBytes()
    {
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Fonts", "Default.ttf");
        return File.ReadAllBytes(path);
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

    private static Texture CreateChecker2x2Texture(RenderingSystem rendering)
    {
        byte[] bytes =
        [
            0, 0, 0, 255,       255, 255, 255, 255,
            255, 255, 255, 255, 0, 0, 0, 255,
        ];
        Texture texture = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(2u, 2u, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        rendering.UploadTexture(texture, bytes);
        return texture;
    }

    private static Texture CreateNineSliceTestTexture(RenderingSystem rendering)
    {
        const int size = 8;
        byte[] bytes = new byte[size * size * 4];
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                bool isBorder = x < 2 || x >= size - 2 || y < 2 || y >= size - 2;
                int offset = (y * size + x) * 4;
                if (isBorder)
                {
                    bytes[offset + 0] = 0;
                    bytes[offset + 1] = 255;
                    bytes[offset + 2] = 255;
                    bytes[offset + 3] = 255;
                }
                else
                {
                    bytes[offset + 0] = 255;
                    bytes[offset + 1] = 0;
                    bytes[offset + 2] = 255;
                    bytes[offset + 3] = 255;
                }
            }
        }

        Texture texture = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(size, size, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        rendering.UploadTexture(texture, bytes);
        return texture;
    }

    private static byte[] ReadBackColorRgba8AndSubmit(GPUDevice device, RenderContext context, Texture target, int width, int height)
    {
        ulong byteCount = (ulong)width * (ulong)height * 4UL;
        GPUHeap readback = device.AllocateGPUHeap(byteCount, MemoryType.Readback);
        try
        {
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();

            byte[] pixels = new byte[(int)byteCount];
            new ReadOnlySpan<byte>(readback.Range.Cpu, (int)byteCount).CopyTo(pixels);
            return pixels;
        }
        finally
        {
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

    private static int CountBrightPixelsInWorldBox(
        byte[] pixels,
        int width,
        int height,
        float minX,
        float maxY,
        float maxX,
        float minY,
        byte minChannel)
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
                int offset = (y * width + x) * 4;
                if (pixels[offset + 0] >= minChannel &&
                    pixels[offset + 1] >= minChannel &&
                    pixels[offset + 2] >= minChannel)
                {
                    ++count;
                }
            }
        }

        return count;
    }
}
