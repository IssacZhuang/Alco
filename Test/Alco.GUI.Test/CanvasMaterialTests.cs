using System.Numerics;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;

namespace Alco.GUI.Test;

/// <summary>NoGPU unit tests covering Canvas batching, custom IUISurface material snapshots, mask modes, and control layout.</summary>
[TestFixture]
public class CanvasMaterialTests
{
    private const string CustomSurfaceSource = """
        #language slang 2025
        module NoGpuTestSurface;

        import Core;
        import SharedSampler;
        import AlcoGUI_Surface;

        public struct NoGpuTestParams
        {
            public float4 Accent;
            public float MixAmount;
            public float3 _Pad;
        };

        public struct NoGpuTestSurface : IUISurface
        {
            public typedef NoGpuTestParams Params;
            public float4 Shade(UISurfaceInput input, Params p)
            {
                return lerp(input.BaseColor, float4(p.Accent.rgb * p.Accent.a, p.Accent.a), p.MixAmount);
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

    /// <summary>Verifies custom surface composition, merging of identical parameter bytes, and splitting upon mid-frame mutation.</summary>
    [Test]
    public void CustomMaterial_MergesIdenticalAndSplitsOnMidFrameMutation()
    {
        using UITestHost host = new(GraphicsBackend.NoGPU);
        Font font = Font.Create(UITestHost.LoadDefaultFontBytes());
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(128, 128));
        Texture target = host.CreateColorTarget(128, 128);
        Texture stencil = host.CreateStencilTarget(128, 128);
        try
        {
            Material mat = canvas.CreateCustomSurfaceMaterialFromSource(
                "NoGpuTestSurface", "NoGpuTestSurface.slang", CustomSurfaceSource, "NoGpuCustom");
            Vector4 red = new(1, 0, 0, 1);
            float mix = 0.5f;
            mat.SetData("Material.Accent", ref red);
            mat.SetData("Material.MixAmount", ref mix);

            UIImage a = new() { Size = new Vector2(32, 32), Position = new Vector2(-20, 0), Material = mat };
            UIImage b = new() { Size = new Vector2(32, 32), Position = new Vector2(20, 0), Material = mat };
            canvas.Root.Add(a);
            canvas.Root.Add(b);

            canvas.Tick(0f);
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats merged = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(128, 128), stencil.RenderView, Format.S8Uint);
            host.Context.Submit().Wait();
            Assert.That(merged.QuadCount, Is.EqualTo(2));
            Assert.That(merged.ContentDraws, Is.EqualTo(1), "Consecutive identical custom material bytes must merge.");

            // Replace b with a node that mutates mat mid-frame -> must split into 2 draws!
            canvas.Root.Remove(b);
            Vector4 blue = new(0, 0, 1, 1);
            MutatingImage mutator = new(() => mat.SetData("Material.Accent", ref blue))
            {
                Size = new Vector2(32, 32),
                Position = new Vector2(20, 0),
                Material = mat,
            };
            canvas.Root.Add(mutator);

            canvas.Tick(0f);
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats split = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(128, 128), stencil.RenderView, Format.S8Uint);
            host.Context.Submit().Wait();
            Assert.That(split.QuadCount, Is.EqualTo(2));
            Assert.That(split.ContentDraws, Is.EqualTo(2), "Mid-frame mutation on shared Material must snapshot per run and split draw.");
        }
        finally
        {
            stencil.Destroy();
            target.Destroy();
        }
    }

    /// <summary>Verifies scissor fast-path, rotated/textured stencil push-pop, empty mask elision, and no-stencil fallback.</summary>
    [Test]
    public void Masks_ClassifyScissorVsStencilElideEmptyAndFallbackWithoutStencil()
    {
        using UITestHost host = new(GraphicsBackend.NoGPU);
        Font font = Font.Create(UITestHost.LoadDefaultFontBytes());
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(128, 128));
        Texture maskTex = host.CreateRgba8Texture(2, 2, [255, 255, 255, 255, 0, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0]);
        Texture target = host.CreateColorTarget(128, 128);
        Texture stencil = host.CreateStencilTarget(128, 128);
        try
        {
            // 1. Axis-aligned untextured mask -> Scissor only, zero mask draws.
            UIMask scissorMask = new() { Size = new Vector2(40, 40) };
            scissorMask.Add(new UIImage { Size = new Vector2(30, 30) });
            canvas.Root.Add(scissorMask);

            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats s1 = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(128, 128), stencil.RenderView, Format.S8Uint);
            host.Context.Submit().Wait();
            Assert.That(s1.ScissorMaskCount, Is.EqualTo(1));
            Assert.That(s1.StencilMaskCount, Is.EqualTo(0));
            Assert.That(s1.MaskDraws, Is.EqualTo(0));
            Assert.That(s1.ContentDraws, Is.EqualTo(1));

            // 2. Empty stencil mask (rotated, no children) -> Elided completely.
            canvas.Root.RemoveAllChildren();
            UIMask emptyRotated = new() { Size = new Vector2(40, 40), LocalTransform = new Transform2D(Vector2.Zero, new Rotation2D(45f)) };
            canvas.Root.Add(emptyRotated);

            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats s2 = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(128, 128), stencil.RenderView, Format.S8Uint);
            host.Context.Submit().Wait();
            Assert.That(s2.ElidedMaskCount, Is.EqualTo(1));
            Assert.That(s2.StencilMaskCount, Is.EqualTo(0));
            Assert.That(s2.MaskDraws, Is.EqualTo(0));

            // 3. Textured mask with visible child -> Push + Pop stencil draws (2).
            canvas.Root.RemoveAllChildren();
            UIMask textured = new() { Size = new Vector2(40, 40), Texture = maskTex };
            textured.Add(new UIImage { Size = new Vector2(30, 30) });
            canvas.Root.Add(textured);

            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats s3 = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(128, 128), stencil.RenderView, Format.S8Uint);
            host.Context.Submit().Wait();
            Assert.That(s3.StencilMaskCount, Is.EqualTo(1));
            Assert.That(s3.MaskDraws, Is.EqualTo(2));
            Assert.That(s3.ContentDraws, Is.EqualTo(1));

            // 4. Same textured/rotated tree without stencil attachment -> graceful fallback with 0 mask draws.
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats s4 = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(128, 128));
            host.Context.Submit().Wait();
            Assert.That(s4.MaskDraws, Is.EqualTo(0));
            Assert.That(s4.ContentDraws, Is.EqualTo(1));
        }
        finally
        {
            stencil.Destroy();
            target.Destroy();
            maskTex.Destroy();
        }
    }

    /// <summary>Verifies compressed 9-slice clamping, rich text + wrapping with Rune, UIInputBox selection, and control Material properties.</summary>
    [Test]
    public void Controls_SupportMaterialNineSliceClampingRichTextAndInputBox()
    {
        using UITestHost host = new(GraphicsBackend.NoGPU);
        Font font = Font.Create(UITestHost.LoadDefaultFontBytes());
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(200, 200));
        Texture tex = host.CreateRgba8Texture(32, 32, new byte[32 * 32 * 4]);
        Texture target = host.CreateColorTarget(200, 200);
        try
        {
            Material custom = canvas.CreateCustomSurfaceMaterialFromSource(
                "NoGpuTestSurface", "NoGpuTestSurface.slang", CustomSurfaceSource, "ControlCustom");

            // Compressed 9-slice: Size=10x10 smaller than borders (16+16) -> 4 positive patches (single draw!)
            UIImage sliced = new()
            {
                Texture = tex,
                ImageType = ImageType.Sliced,
                SliceBorders = new Vector4(16, 16, 16, 16),
                Size = new Vector2(10, 10),
                Material = custom,
            };

            UIText rich = new()
            {
                Font = font,
                FontSize = 14,
                Size = new Vector2(60, 80),
                OverflowHorizontal = OverflowModeHorizontal.NextLine,
                IsRichText = true,
                Text = "Hello <color=#ff0000>World</color> 😊",
                Material = custom,
            };

            UIInputBox input = new()
            {
                Font = font,
                FontSize = 14,
                Size = new Vector2(80, 30),
                Text = "Editable",
            };
            input.SelectAll();

            UIGeometryIcon icon = new() { Size = new Vector2(32, 32), Material = custom };
            Assert.That(icon.Material, Is.SameAs(custom));

            canvas.Root.Add(sliced);
            canvas.Root.Add(rich);
            canvas.Root.Add(input);
            canvas.Root.Add(icon);

            canvas.Tick(0f);
            canvas.PrepareRender(0f);
            host.Context.BeginFrame();
            UIFrameStats stats = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(200, 200));
            host.Context.Submit().Wait();

            Assert.That(stats.QuadCount, Is.GreaterThanOrEqualTo(10));
            Assert.That(rich.ContentHeight, Is.GreaterThan(14f), "Wrapped rich text + emoji should span multiple lines cleanly.");
        }
        finally
        {
            target.Destroy();
            tex.Destroy();
        }
    }

    /// <summary>Verifies UIImage(Image), SetImage, and Canvas Image/Sprite drawing overloads propagate import settings cleanly.</summary>
    [Test]
    public void UIImage_And_CanvasOverloads_ApplyImageAndSpriteMetadata()
    {
        using UITestHost host = new(GraphicsBackend.NoGPU);
        Font font = Font.Create(UITestHost.LoadDefaultFontBytes());
        using Canvas canvas = host.RenderingSystem.CreateCanvas(new TestInputTracker(), font, new Vector2(128, 128));
        Texture gpuTex = host.CreateRgba8Texture(16, 16, new byte[16 * 16 * 4]);
        Texture target = host.CreateColorTarget(128, 128);
        try
        {
            ImageLoadOption option = ImageLoadOption.Default with
            {
                Name = "UiSheet.png",
                FilterMode = Filter.Nearest,
                PremultiplyAlpha = true,
                SlicePadding = new Padding(3, 4, 5, 6),
            };
            var sprites = new Dictionary<string, RectInt> { ["icon"] = new RectInt(4, 4, 8, 8) };
            using Image sheet = ImageUtility.CreateFromTexture(gpuTex, option, sprites, ownsResource: false);

            UIImage fromCtor = new(sheet);
            Assert.That(fromCtor.Texture, Is.SameAs(gpuTex));
            Assert.That(fromCtor.NearestFilter, Is.True);
            Assert.That(fromCtor.Premultiplied, Is.True);
            Assert.That(fromCtor.SliceBorders, Is.EqualTo(new Vector4(3, 4, 5, 6)));

            fromCtor.SetImage(sheet, "icon");
            fromCtor.SetNativeSize();
            Assert.That(fromCtor.UvRect, Is.EqualTo(new Rect(0.25f, 0.25f, 0.5f, 0.5f)));
            Assert.That(fromCtor.Size, Is.EqualTo(new Vector2(8, 8)));

            fromCtor.ImageType = ImageType.Sliced;
            canvas.Root.Add(fromCtor);

            canvas.PrepareRender(0f);
            Span<UIMeshVertex> verts =
            [
                new UIMeshVertex { Position = new Vector2(0, 0), Uv = new Vector2(0, 0), Color = ColorFloat.White },
                new UIMeshVertex { Position = new Vector2(16, 0), Uv = new Vector2(1, 0), Color = ColorFloat.White },
                new UIMeshVertex { Position = new Vector2(8, 16), Uv = new Vector2(0.5f, 1), Color = ColorFloat.White },
            ];
            Span<ushort> indices = [0, 1, 2];

            canvas.DrawSprite(sheet, Matrix4x4.Identity, ColorFloat.White);
            canvas.DrawSprite(sheet.GetSprite("icon"), Matrix4x4.Identity, ColorFloat.White);
            canvas.DrawNineSlice(sheet, Matrix4x4.CreateScale(32, 32, 1), ColorFloat.White);
            canvas.DrawTiledSprite(sheet, Matrix4x4.CreateScale(32, 32, 1), Vector2.One, ColorFloat.White);
            canvas.DrawMesh(verts, indices, sheet);

            host.Context.BeginFrame();
            UIFrameStats stats = canvas.Draw(host.Context, target.RenderView, Format.Rgba8Unorm, new Extent2D(128, 128));
            host.Context.Submit().Wait();

            Assert.That(stats.MeshDraws, Is.EqualTo(1));
            Assert.That(stats.ContentDraws, Is.GreaterThanOrEqualTo(1));
        }
        finally
        {
            target.Destroy();
            gpuTex.Destroy();
        }
    }
}
