using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;

namespace Alco.GUI.Test;

/// <summary>Headless GPU pixel-readback verification across VulkanCompact and Dx12 for bindless Alco.GUI batching.</summary>
[TestFixture]
public class CanvasBatchingTests
{
    private const string TintOverlaySurfaceSource = """
        #language slang 2025
        module TintOverlaySurface;

        import Core;
        import SharedSampler;
        import AlcoGUI_Surface;

        public struct TintOverlayParams
        {
            public GpuTextureId Overlay;
            public uint _Pad0;
            public uint _Pad1;
            public uint _Pad2;
            public float4 Accent;
            public float Blend;
            public float3 _Pad3;
        };

        public struct TintOverlaySurface : IUISurface
        {
            public typedef TintOverlayParams Params;
            public float4 Shade(UISurfaceInput input, Params p)
            {
                float4 overlay = gpu_texture<Texture2D<float4>>(p.Overlay.index)
                    .SampleLevel(shared_sampler_linear_clamp(), input.LocalUv, 0.0);
                float4 overlayPremul = float4(overlay.rgb * overlay.a, overlay.a);
                float4 mixed = lerp(input.BaseColor, overlayPremul, p.Blend);
                return float4(mixed.rgb * (p.Accent.rgb * p.Accent.a), mixed.a * p.Accent.a);
            }
        };
        """;

    private sealed class MutatingImage(Action onBeforeDraw) : UIImage
    {
        protected override void OnUpdate(Canvas canvas, float delta)
        {
            onBeforeDraw();
            base.OnUpdate(canvas, delta);
        }
    }

    private sealed class MeshDrawNode : UINode
    {
        public Texture? Texture { get; set; }

        protected override void OnUpdate(Canvas canvas, float delta)
        {
            base.OnUpdate(canvas, delta);
            Span<UIMeshVertex> verts =
            [
                new UIMeshVertex { Position = new Vector2(-24, -24), Uv = new Vector2(0, 1), Color = new ColorFloat(1, 0, 0, 1) },
                new UIMeshVertex { Position = new Vector2(24, -24), Uv = new Vector2(1, 1), Color = new ColorFloat(0, 1, 0, 1) },
                new UIMeshVertex { Position = new Vector2(0, 24), Uv = new Vector2(0.5f, 0), Color = new ColorFloat(0, 0, 1, 1) },
            ];
            Span<ushort> indices = [0, 1, 2];
            canvas.DrawMesh(verts, indices, Texture);
        }
    }

    /// <summary>Verifies single-draw bindless batching across all quad types matches SplitDraws=true pixel-for-pixel.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    public void SingleDrawBatching_MatchesSplitDrawsWithinOneLsb(GraphicsBackend backend)
    {
        const int Size = 128;
        using UITestHost host = new(backend);
        Font font = Font.Create(UITestHost.LoadDefaultFontBytes());
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(Size, Size));

        // 2x2 pixel-art texture: TL=red, TR=green, BL=blue, BR=white
        Texture crispTex = host.CreateRgba8Texture(2, 2, [
            255, 0, 0, 255,     0, 255, 0, 255,
            0, 0, 255, 255,     255, 255, 255, 255,
        ]);

        // Premultiplied half-alpha blue square (premul RGB=(0,0,128), A=128)
        Texture premulTex = host.CreateRgba8Texture(2, 2, [
            0, 0, 128, 128,     0, 0, 128, 128,
            0, 0, 128, 128,     0, 0, 128, 128,
        ]);

        // 8x8 9-slice border frame: outer 2px yellow border, inner 4x4 dark cyan
        byte[] slicePixels = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; ++y)
        {
            for (int x = 0; x < 8; ++x)
            {
                bool border = x < 2 || x >= 6 || y < 2 || y >= 6;
                int p = (y * 8 + x) * 4;
                slicePixels[p + 0] = border ? (byte)255 : (byte)0;
                slicePixels[p + 1] = (byte)200;
                slicePixels[p + 2] = border ? (byte)0 : (byte)200;
                slicePixels[p + 3] = 255;
            }
        }
        Texture sliceTex = host.CreateRgba8Texture(8, 8, slicePixels);

        // Top row: Solid, Straight crisp nearest, Premul overlay, Tiled 2x2
        canvas.Root.Add(new UIImage { Position = new Vector2(-48, 44), Size = new Vector2(20, 20), Color = new ColorFloat(0.8f, 0.2f, 0.1f, 1f) });
        canvas.Root.Add(new UIImage { Position = new Vector2(-16, 44), Size = new Vector2(20, 20), Texture = crispTex, NearestFilter = true });
        canvas.Root.Add(new UIImage { Position = new Vector2(16, 44), Size = new Vector2(20, 20), Color = new ColorFloat(1, 0, 0, 1) });
        canvas.Root.Add(new UIImage { Position = new Vector2(16, 44), Size = new Vector2(16, 16), Texture = premulTex, Premultiplied = true });
        canvas.Root.Add(new UIImage { Position = new Vector2(48, 44), Size = new Vector2(20, 20), Texture = crispTex, ImageType = ImageType.Tiled, TilingScale = new Vector2(0.2f, 0.2f) });

        // Middle row: 9-slice + compressed 9-slice
        canvas.Root.Add(new UIImage { Position = new Vector2(-32, 8), Size = new Vector2(40, 24), Texture = sliceTex, ImageType = ImageType.Sliced, SliceBorders = new Vector4(2, 2, 2, 2) });
        canvas.Root.Add(new UIImage { Position = new Vector2(32, 8), Size = new Vector2(3, 3), Texture = sliceTex, ImageType = ImageType.Sliced, SliceBorders = new Vector4(2, 2, 2, 2) });

        // Bottom row: multi-size SDF text + rich text <color>
        canvas.Root.Add(new UIText { Font = font, FontSize = 18, Position = new Vector2(0, -24), Size = new Vector2(120, 24), IsRichText = true, Text = "Hi <color=#00ff00>OK</color>" });
        canvas.Root.Add(new UIText { Font = font, FontSize = 10, Position = new Vector2(0, -48), Size = new Vector2(120, 16), Text = "Small" });

        Texture batchedTex = host.CreateColorTarget(Size, Size);
        Texture splitTex = host.CreateColorTarget(Size, Size);
        Texture stencil = host.CreateStencilTarget(Size, Size);
        try
        {
            canvas.SplitDraws = false;
            canvas.Tick(0f);
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats batchedStats = canvas.Draw(host.Context, batchedTex.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), stencil.RenderView, Format.S8Uint, LoadOp.Clear, Vector4.Zero);
            host.Context.Submit().Wait();

            canvas.SplitDraws = true;
            canvas.Tick(0f);
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats splitStats = canvas.Draw(host.Context, splitTex.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), stencil.RenderView, Format.S8Uint, LoadOp.Clear, Vector4.Zero);
            host.Context.Submit().Wait();

            Assert.That(batchedStats.ContentDraws, Is.EqualTo(1), "All default-material images and SDF text must merge into 1 draw call.");
            Assert.That(batchedStats.MaskDraws, Is.EqualTo(0));
            Assert.That(splitStats.ContentDraws, Is.EqualTo(splitStats.QuadCount));
            Assert.That(splitStats.QuadCount, Is.GreaterThan(15));

            byte[] batchedPixels = host.ReadbackRgba8(batchedTex, Size, Size);
            byte[] splitPixels = host.ReadbackRgba8(splitTex, Size, Size);
            for (int i = 0; i < batchedPixels.Length; ++i)
            {
                Assert.That(Math.Abs(batchedPixels[i] - splitPixels[i]), Is.LessThanOrEqualTo(1),
                    $"Pixel byte {i} diverged between single-draw batch and SplitDraws.");
            }

            // Spot-check Nearest TL quadrant (-16, 44 -> pixel x=42, y=14) is crisp Red
            int nearestTl = PixelIndex(Size, 43, 15);
            Assert.That(batchedPixels[nearestTl + 0], Is.EqualTo((byte)255));
            Assert.That(batchedPixels[nearestTl + 1], Is.EqualTo((byte)0));
            Assert.That(batchedPixels[nearestTl + 2], Is.EqualTo((byte)0));

            // Spot-check Premultiplied overlay over Red: (0,0,0.5) + (1,0,0)*(1-0.5) => ~(128, 0, 128, 255)
            int premulIdx = PixelIndex(Size, 80, 20);
            Assert.That(batchedPixels[premulIdx + 0], Is.InRange((byte)125, (byte)131));
            Assert.That(batchedPixels[premulIdx + 2], Is.InRange((byte)125, (byte)131));
            Assert.That(batchedPixels[premulIdx + 3], Is.EqualTo((byte)255));

            // Spot-check rich text bottom half has bright green pixels from <color=#00ff00>OK</color>
            bool foundGreenText = false;
            for (int y = 70; y < 105; ++y)
            {
                for (int x = 60; x < 120; ++x)
                {
                    int p = PixelIndex(Size, x, y);
                    if (batchedPixels[p + 1] > 180 && batchedPixels[p + 0] < 30 && batchedPixels[p + 2] < 30)
                        foundGreenText = true;
                }
            }
            Assert.That(foundGreenText, Is.True, "Rich text green slice must render crisp green glyph pixels.");
        }
        finally
        {
            stencil.Destroy();
            splitTex.Destroy();
            batchedTex.Destroy();
            sliceTex.Destroy();
            premulTex.Destroy();
            crispTex.Destroy();
        }
    }

    /// <summary>Verifies scissor fast-path, nested rotated stencil masks, textured alpha-cutout mask, empty mask elision, and no-stencil fallback.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    public void Masks_ScissorRotatedNestedTexturedAndFallbackRenderCorrectly(GraphicsBackend backend)
    {
        const int Size = 128;
        using UITestHost host = new(backend);
        Font font = Font.CreateEmpty();
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(Size, Size));

        // Left column (pixel x=0) opaque white, right column (pixel x=1) transparent zero.
        Texture halfMask = host.CreateRgba8Texture(2, 1, [255, 255, 255, 255, 0, 0, 0, 0]);

        Texture target = host.CreateColorTarget(Size, Size);
        Texture targetNoStencil = host.CreateColorTarget(Size, Size);
        Texture stencil = host.CreateStencilTarget(Size, Size);
        try
        {
            // Top-left (-32, 32): Scissor 20x20 around large 40x40 green quad -> MaskDraws == 0
            UIMask scissor = new() { Position = new Vector2(-32, 32), Size = new Vector2(20, 20) };
            scissor.Add(new UIImage { Size = new Vector2(40, 40), Color = new ColorFloat(0, 1, 0, 1) });

            // Top-right (32, 32): Nested rotated stencil masks -> Level 1 40x40 rot 45 deg, Level 2 20x20 -> cyan quad
            UIMask rot1 = new()
            {
                Position = new Vector2(32, 32),
                Size = new Vector2(40, 40),
                LocalTransform = new Transform2D(new Vector2(32, 32), new Rotation2D(45f)),
            };
            UIMask rot2 = new() { Size = new Vector2(24, 24) };
            rot2.Add(new UIImage { Size = new Vector2(50, 50), Color = new ColorFloat(0, 1, 1, 1) });
            rot1.Add(rot2);

            // Bottom-left (-32, -32): Textured alpha cutout mask (left half white passes, right half discarded)
            UIMask texMask = new() { Position = new Vector2(-32, -32), Size = new Vector2(40, 40), Texture = halfMask };
            texMask.Add(new UIImage { Size = new Vector2(40, 40), Color = new ColorFloat(1, 1, 1, 1) });

            // Empty rotated stencil mask (must elide!)
            UIMask emptyMask = new() { Position = new Vector2(32, -32), Size = new Vector2(30, 30), LocalTransform = new Transform2D(new Vector2(32, -32), new Rotation2D(30f)) };

            canvas.Root.Add(scissor);
            canvas.Root.Add(rot1);
            canvas.Root.Add(texMask);
            canvas.Root.Add(emptyMask);

            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats stats = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), stencil.RenderView, Format.S8Uint, LoadOp.Clear, Vector4.Zero);
            host.Context.Submit().Wait();

            // Nested rot1 + rot2 + texMask = 3 active stencil masks -> 6 MaskDraws; emptyMask elided!
            Assert.That(stats.ScissorMaskCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(stats.ElidedMaskCount, Is.EqualTo(1));
            Assert.That(stats.StencilMaskCount, Is.EqualTo(3));
            Assert.That(stats.MaskDraws, Is.EqualTo(6));

            byte[] pixels = host.ReadbackRgba8(target, Size, Size);
            // Scissor center (32, 32) green; just outside scissor (16, 32) zero
            int scIn = PixelIndex(Size, 32, 32);
            int scOut = PixelIndex(Size, 16, 32);
            Assert.That(pixels[scIn + 1], Is.EqualTo((byte)255));
            Assert.That(pixels[scOut + 3], Is.EqualTo((byte)0));

            // Nested rotated center (96, 32) cyan; corner (115, 32) clipped
            int rotIn = PixelIndex(Size, 96, 32);
            int rotOut = PixelIndex(Size, 115, 32);
            Assert.That(pixels[rotIn + 1], Is.EqualTo((byte)255));
            Assert.That(pixels[rotIn + 2], Is.EqualTo((byte)255));
            Assert.That(pixels[rotOut + 3], Is.EqualTo((byte)0));

            // Textured mask: center-left (22, 96) white; center-right (42, 96) clipped zero
            int texIn = PixelIndex(Size, 22, 96);
            int texOut = PixelIndex(Size, 42, 96);
            Assert.That(pixels[texIn + 0], Is.EqualTo((byte)255));
            Assert.That(pixels[texOut + 3], Is.EqualTo((byte)0));

            // Render same tree WITHOUT stencil attachment -> AABB scissor fallback must execute cleanly with 0 mask draws
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats fallbackStats = canvas.Draw(host.Context, targetNoStencil.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), null, Format.Undefined, LoadOp.Clear, Vector4.Zero);
            host.Context.Submit().Wait();
            Assert.That(fallbackStats.MaskDraws, Is.EqualTo(0));
            byte[] fbPixels = host.ReadbackRgba8(targetNoStencil, Size, Size);
            Assert.That(fbPixels[PixelIndex(Size, 96, 32) + 2], Is.EqualTo((byte)255), "No-stencil fallback keeps center visible.");
        }
        finally
        {
            stencil.Destroy();
            targetNoStencil.Destroy();
            target.Destroy();
            halfMask.Destroy();
        }
    }

    /// <summary>Verifies discarding an active frame preserves uploaded SDF glyphs so the next submitted frame displays them.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    public void DiscardFrame_PreservesDirtyGlyphsForNextSubmittedFrame(GraphicsBackend backend)
    {
        const int Size = 96;
        using UITestHost host = new(backend);
        Font font = Font.Create(UITestHost.LoadDefaultFontBytes());
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(Size, Size));
        canvas.Root.Add(new UIText { Font = font, FontSize = 28, Size = new Vector2(Size, Size), Color = new ColorFloat(1, 1, 1, 1), Text = "RETRY" });

        Texture target = host.CreateColorTarget(Size, Size);
        try
        {
            // Frame 1: Prepare + Upload, then DISCARD!
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            canvas.Upload(host.Context);
            host.Context.DiscardFrame();

            // Frame 2: Render & Submit -> glyphs must remain valid and display cleanly.
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats stats = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), colorLoadOp: LoadOp.Clear, clearColor: Vector4.Zero);
            host.Context.Submit().Wait();

            Assert.That(canvas.GlyphCache.PageCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(canvas.GlyphCache.GeneratedGlyphCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(stats.FontPageCount, Is.GreaterThanOrEqualTo(1));
            byte[] pixels = host.ReadbackRgba8(target, Size, Size);
            int whitePixels = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 200 && pixels[i + 3] > 200)
                    ++whitePixels;
            }
            Assert.That(whitePixels, Is.GreaterThan(20), "Glyphs uploaded on Frame 1 after DiscardFrame must be visible on Frame 2.");
        }
        finally
        {
            target.Destroy();
        }
    }

    /// <summary>Verifies an externally injected GlyphCache is reused across Canvases and survives individual Canvas disposal.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    public void SharedGlyphCache_ReusesGlyphsAcrossCanvasesAndPreservesLifetime(GraphicsBackend backend)
    {
        const int Size = 96;
        using UITestHost host = new(backend);
        using GlyphCache sharedCache = new(host.RenderingSystem);
        Font font = Font.Create(UITestHost.LoadDefaultFontBytes());
        Texture target = host.CreateColorTarget(Size, Size);
        try
        {
            using (Canvas canvasA = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(Size, Size), sharedCache))
            {
                canvasA.Root.Add(new UIText { Font = font, FontSize = 24, Size = new Vector2(Size, Size), Color = new ColorFloat(1, 1, 1, 1), Text = "SHARED" });
                canvasA.PrepareRender(0f);
                host.Context.BeginFrame();
                UIFrameStats statsA = canvasA.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), colorLoadOp: LoadOp.Clear, clearColor: Vector4.Zero);
                host.Context.Submit().Wait();

                Assert.That(statsA.FontGlyphsUploaded, Is.GreaterThan(0));
            }

            Assert.That(sharedCache.IsDisposed, Is.False);
            int cachedGlyphs = sharedCache.GeneratedGlyphCount;
            Assert.That(cachedGlyphs, Is.GreaterThanOrEqualTo(5));

            using (Canvas canvasB = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(Size, Size), sharedCache))
            {
                canvasB.Root.Add(new UIText { Font = font, FontSize = 24, Size = new Vector2(Size, Size), Color = new ColorFloat(1, 1, 1, 1), Text = "SHARED" });
                canvasB.PrepareRender(0f);
                host.Context.BeginFrame();
                UIFrameStats statsB = canvasB.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), colorLoadOp: LoadOp.Clear, clearColor: Vector4.Zero);
                host.Context.Submit().Wait();

                Assert.That(statsB.FontGlyphsUploaded, Is.EqualTo(0));
                Assert.That(sharedCache.GeneratedGlyphCount, Is.EqualTo(cachedGlyphs));

                byte[] pixels = host.ReadbackRgba8(target, Size, Size);
                int whitePixels = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i] > 200 && pixels[i + 3] > 200)
                        ++whitePixels;
                }
                Assert.That(whitePixels, Is.GreaterThan(20));
            }

            Assert.That(sharedCache.IsDisposed, Is.False);
            Assert.That(sharedCache.PageCount, Is.GreaterThanOrEqualTo(1));
        }
        finally
        {
            target.Destroy();
        }
    }

    /// <summary>Verifies composed IUISurface with dynamic uniforms + secondary texture applied to UIImage and UIText with per-draw mutation snapshots.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    public void CustomSurface_SupportsDynamicUniformsSecondTextureAndMutationSnapshots(GraphicsBackend backend)
    {
        const int Size = 128;
        using UITestHost host = new(backend);
        Font font = Font.Create(UITestHost.LoadDefaultFontBytes());
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(Size, Size));
        Texture overlayTex = host.CreateRgba8Texture(2, 2, [
            255, 255, 255, 255,  255, 255, 255, 255,
            255, 255, 255, 255,  255, 255, 255, 255,
        ]);

        Material sharedMat = canvas.CreateCustomSurfaceMaterialFromSource(
            "TintOverlaySurface", "TintOverlaySurface.slang", TintOverlaySurfaceSource, "TintOverlay");
        sharedMat.SetTexture("Material.Overlay", overlayTex);
        Vector4 initialAccent = new(1, 0, 0, 1);
        float blend = 1.0f;
        sharedMat.SetData("Material.Accent", ref initialAccent);
        sharedMat.SetData("Material.Blend", ref blend);

        MaterialInstance textMat = sharedMat.CreateInstance();
        Vector4 greenAccent = new(0, 1, 0, 1);
        float textBlend = 0.0f;
        textMat.SetData("Material.Accent", ref greenAccent);
        textMat.SetData("Material.Blend", ref textBlend);

        Vector4 red = new(1, 0, 0, 1);
        Vector4 blue = new(0, 0, 1, 1);
        canvas.Root.Add(new MutatingImage(() => sharedMat.SetData("Material.Accent", ref red))
        {
            Position = new Vector2(-32, 32),
            Size = new Vector2(32, 32),
            Material = sharedMat,
        });
        canvas.Root.Add(new MutatingImage(() => sharedMat.SetData("Material.Accent", ref blue))
        {
            Position = new Vector2(32, 32),
            Size = new Vector2(32, 32),
            Material = sharedMat,
        });
        canvas.Root.Add(new UIText
        {
            Font = font,
            FontSize = 24,
            Position = new Vector2(0, -28),
            Size = new Vector2(100, 32),
            Color = new ColorFloat(1, 1, 1, 1),
            Text = "HI",
            Material = textMat,
        });

        Texture target = host.CreateColorTarget(Size, Size);
        try
        {
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats stats = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), colorLoadOp: LoadOp.Clear, clearColor: Vector4.Zero);
            host.Context.Submit().Wait();

            Assert.That(stats.ContentDraws, Is.EqualTo(3));

            byte[] pixels = host.ReadbackRgba8(target, Size, Size);
            // Left image (-32, 32 -> px 32, 32): RED (snapshot preserved before blue mutation!)
            int leftPx = PixelIndex(Size, 32, 32);
            Assert.That(pixels[leftPx + 0], Is.EqualTo((byte)255));
            Assert.That(pixels[leftPx + 2], Is.EqualTo((byte)0));

            // Right image (32, 32 -> px 96, 32): BLUE
            int rightPx = PixelIndex(Size, 96, 32);
            Assert.That(pixels[rightPx + 0], Is.EqualTo((byte)0));
            Assert.That(pixels[rightPx + 2], Is.EqualTo((byte)255));

            // Green SDF glyphs in bottom half
            bool foundGreenText = false;
            for (int y = 76; y < 112; ++y)
            {
                for (int x = 36; x < 92; ++x)
                {
                    int p = PixelIndex(Size, x, y);
                    if (pixels[p + 1] > 180 && pixels[p + 0] < 20)
                        foundGreenText = true;
                }
            }
            Assert.That(foundGreenText, Is.True, "Custom IUISurface applied to UIText must tint SDF glyphs green.");
        }
        finally
        {
            target.Destroy();
            overlayTex.Destroy();
        }
    }

    /// <summary>Verifies DrawMesh arbitrary triangle, UIGeometryIcon 0-frame SVG rasterization, and CanvasRenderGraphNode execution.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    public void DrawMeshAndGeometryIcon_RenderThroughRenderGraph(GraphicsBackend backend)
    {
        const int Size = 128;
        using UITestHost host = new(backend);
        Font font = Font.CreateEmpty();
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(Size, Size));

        canvas.Root.Add(new MeshDrawNode());

        const string GreenRectSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\"><rect x=\"4\" y=\"4\" width=\"56\" height=\"56\" fill=\"#00ff66\"/></svg>";
        UIGeometryIcon icon = new()
        {
            Position = new Vector2(36, 36),
            Size = new Vector2(32, 32),
            Svg = GreenRectSvg,
        };
        canvas.Root.Add(icon);

        // Kick off CPU SVG parse, await task completion, then run CanvasRenderGraphNode in RenderGraph.
        canvas.PrepareRender(0f);
        icon.PendingParseTask?.Wait();

        Texture target = host.CreateColorTarget(Size, Size);
        RenderGraph graph = new(host.RenderingSystem, new Extent2D(Size, Size), "UITestGraph");
        try
        {
            graph.Use(new CanvasRenderGraphNode(canvas, colorLoadOp: LoadOp.Clear, clearColor: Vector4.Zero));

            host.Context.BeginFrame();
            graph.Execute(host.Context, RenderGraphTarget.FromTexture(target));
            host.Context.Submit().Wait();

            UIFrameStats stats = canvas.LastFrameStats;
            Assert.That(stats.MeshDraws, Is.EqualTo(1));
            Assert.That(stats.ContentDraws, Is.GreaterThanOrEqualTo(1));

            byte[] pixels = host.ReadbackRgba8(target, Size, Size);
            // Center (64, 64) lies inside the DrawMesh vertex-colored triangle -> non-zero alpha & interpolated color
            int triPx = PixelIndex(Size, 64, 64);
            Assert.That(pixels[triPx + 3], Is.EqualTo((byte)255));
            Assert.That(pixels[triPx + 0] + pixels[triPx + 1] + pixels[triPx + 2], Is.GreaterThan(150));

            // Top-right (100, 28) lies inside SVG green rect icon -> green > 220
            int svgPx = PixelIndex(Size, 100, 28);
            Assert.That(pixels[svgPx + 1], Is.EqualTo((byte)255));
            Assert.That(pixels[svgPx + 3], Is.EqualTo((byte)255));
        }
        finally
        {
            graph.Destroy();
            target.Destroy();
        }
    }

    /// <summary>Verifies transient buffer growth beyond 2000 quads and zero managed GC allocations on warm frames.</summary>
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    public void LargePoolGrowthAndWarmZeroAllocations(GraphicsBackend backend)
    {
        const int Size = 128;
        using UITestHost host = new(backend);
        Font font = Font.Create(UITestHost.LoadDefaultFontBytes());
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(Size, Size));
        Texture target = host.CreateColorTarget(Size, Size);
        Texture stencil = host.CreateStencilTarget(Size, Size);
        try
        {
            // 1. Growth test: 2500 quads (~160 KB > 64 KB initial pool)
            for (int i = 0; i < 2500; ++i)
            {
                canvas.Root.Add(new UIImage
                {
                    Position = new Vector2((i % 50) * 2 - 48, (i / 50) * 2 - 48),
                    Size = new Vector2(2, 2),
                    Color = new ColorFloat(0, 0.5f, 1f, 1f),
                });
            }

            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats bigStats = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), stencil.RenderView, Format.S8Uint, LoadOp.Clear, Vector4.Zero);
            host.Context.Submit().Wait();

            Assert.That(bigStats.QuadCount, Is.EqualTo(2500));
            Assert.That(bigStats.ContentDraws, Is.EqualTo(1));

            // 2. Warm steady-state 0-byte allocation test
            canvas.Root.RemoveAllChildren();
            canvas.Root.Add(new UIImage { Size = new Vector2(40, 40), Color = new ColorFloat(1, 1, 1, 1) });
            canvas.Root.Add(new UIText { Font = font, FontSize = 16, Size = new Vector2(100, 32), Text = "Warm" });
            UIMask mask = new() { Size = new Vector2(40, 40) };
            mask.Add(new UIImage { Size = new Vector2(20, 20), Color = new ColorFloat(1, 0, 0, 1) });
            canvas.Root.Add(mask);

            for (int i = 0; i < 10; ++i)
            {
                canvas.Tick(0.016f);
                canvas.PrepareRender(0.016f);
                host.Context.BeginFrame();
                canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), stencil.RenderView, Format.S8Uint, LoadOp.Clear, Vector4.Zero);
                host.Context.Submit().Wait();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; ++i)
            {
                canvas.PrepareRender(0.016f);
                host.Context.BeginFrame();
                canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(Size, Size), stencil.RenderView, Format.S8Uint, LoadOp.Clear, Vector4.Zero);
                host.Context.Submit().Wait();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L), $"Warm batched Canvas frames must allocate zero managed bytes (actual={allocated}).");
        }
        finally
        {
            stencil.Destroy();
            target.Destroy();
        }
    }

    private static int PixelIndex(int width, int x, int y) => (y * width + x) * 4;
}
