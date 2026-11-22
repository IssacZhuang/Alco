using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Effects.Test;

/// <summary>Checks shared trail materials, continuous noise, and vertex/fragment contracts on the GPU.</summary>
[TestFixture]
[NonParallelizable]
public sealed class TestSmokeTrailNoise
{
    private readonly record struct Snapshot(
        Vector3 Anchor,
        Vector3 End,
        float Age = 0.1f,
        float HalfWidth = 0.2f,
        Vector4? Color0 = null,
        Vector4? Color1 = null,
        Vector4? UserData = null);

    private sealed class GpuCapture : IDisposable
    {
        private readonly EffectsTestHost _host;

        public GpuCapture()
        {
            _host = EffectsTestHost.CreateGpu();
        }

        public Vector3 CameraPosition { get; set; } = new(0f, 0f, -5f);
        public Matrix4x4 ViewProjection { get; set; } = Matrix4x4.Identity;

        public byte[] Capture(MaterialAsset material, bool is3D, float seed, in Snapshot snapshot, int width, int height)
        {
            using var system2D = is3D ? null : new GpuTrailSystem2D(_host.RenderingSystem, _host.Shaders, 128, 1);
            using var system3D = is3D ? new GpuTrailSystem3D(_host.RenderingSystem, _host.Shaders, 128, 1) : null;
            using var trail2D = system2D != null ? CreateTrail(system2D, material, seed, snapshot) : null;
            using var trail3D = system3D != null ? CreateTrail(system3D, material, seed, snapshot) : null;

            Texture target = _host.CreateColorTarget(width, height);
            var context = new RenderContext(_host.RenderingSystem);
            try
            {
                context.BeginFrame();
                system2D?.PrepareRender(context);
                system3D?.PrepareRender(context);

                var passDesc = new RenderingDesc
                {
                    Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
                };
                using (var render = context.BeginRenderPass(passDesc, new RenderPassSignature([Format.Rgba8Unorm])))
                {
                    render.SetViewport(new Viewport { Width = width, Height = height });
                    render.SetScissor(new Scissor { Width = (uint)width, Height = (uint)height });
                    Matrix4x4 vp = ViewProjection;
                    if (system2D != null)
                        system2D.Render(context, render, in vp);
                    else
                        system3D!.Render(context, render, in vp, CameraPosition);
                }

                return _host.ReadBackRgba8AndSubmit(context, target, width, height);
            }
            finally
            {
                context.Destroy();
                target.Destroy();
            }
        }

        public void Dispose() => _host.Dispose();
    }

    /// <summary>The same smoke material renders continuous noise through both dimensional templates.</summary>
    [TestCase(0.37f)]
    [TestCase(81.37f)]
    [Category("AlcoGpu")]
    public void NoiseRemainsContinuousAcrossCellBoundaries(float seed)
    {
        const int width = 4096, height = 65;
        using var capture = new GpuCapture { CameraPosition = new Vector3(4f, 0f, -5f) };
        capture.ViewProjection = Matrix4x4.CreateLookAtLeftHanded(capture.CameraPosition, new Vector3(4f, 0f, 0f), Vector3.UnitY)
            * Matrix4x4.CreateOrthographicLeftHanded(8f, 1f, 0.1f, 10f);
        var material = new MaterialAsset
        {
            Surface = "TrailSurfaceSmoke",
            Parameters = new Dictionary<string, ShaderValue>
            {
                ["smokeColor"] = Vector4.One,
                ["shape"] = new Vector4(1f, 0f, 0f, 0f),
                ["width"] = Vector4.Zero,
                ["edge"] = new Vector4(0.25f, 1f, 0f, 0f),
            },
        };
        var snapshot = new Snapshot(Vector3.Zero, new Vector3(8f, 0f, 0f));
        byte[] pixels2D = capture.Capture(material, false, seed, snapshot, width, height);
        byte[] pixels3D = capture.Capture(material, true, seed, snapshot, width, height);
        AssertContinuousNoise(pixels2D, width, height, $"Smoke, 2D, seed {seed}");
        AssertContinuousNoise(pixels3D, width, height, $"Smoke, 3D, seed {seed}");
        int maximumDifference = 0;
        for (int i = 0; i < pixels2D.Length; i++)
            maximumDifference = Math.Max(maximumDifference, Math.Abs(pixels2D[i] - pixels3D[i]));
        Assert.That(maximumDifference, Is.LessThanOrEqualTo(1),
            "One shared material must preserve planar smoke appearance across dimensional templates.");
    }

    /// <summary>Both templates preserve color interpolation and inherited fragment shading premultiplies alpha.</summary>
    [TestCase("TrailSurfaceDefault")]
    [TestCase("TrailInheritedSurface")]
    [Category("AlcoGpu")]
    public void SharedMaterialPreservesPremultipliedColorGradient(string surfaceName)
    {
        using var capture = new GpuCapture();
        var material = new MaterialAsset { Surface = surfaceName };
        var snapshot = new Snapshot(new Vector3(-0.5f, 0f, 0.1f), new Vector3(0.5f, 0f, 0.1f),
            Age: 0.25f, Color0: new Vector4(0.8f, 0.2f, 0.1f, 0.4f), Color1: new Vector4(0f, 0.6f, 0.9f, 0.8f));
        foreach (bool is3D in new[] { false, true })
        {
            byte[] pixels = capture.Capture(material, is3D, 0.37f, snapshot, 65, 65);
            int center = (32 * 65 + 32) * 4;
            int[] expected = [77, 38, 38, 128];
            for (int channel = 0; channel < 4; channel++)
                Assert.That((int)pixels[center + channel], Is.EqualTo(expected[channel]).Within(1),
                    $"{surfaceName}, 3D={is3D}, channel={channel}.");
        }
    }

    /// <summary>Offsets use world up and fragments receive expanded positions while 2D preserves sorting depth.</summary>
    [Test]
    [Category("AlcoGpu")]
    public void SharedVertexHookPreservesDimensionalPositionContract()
    {
        const int width = 512, height = 512;
        using var capture = new GpuCapture();
        var material = new MaterialAsset { Surface = "TrailContractProbe" };
        var snapshot = new Snapshot(new Vector3(0f, 0f, 0.125f), new Vector3(0.5f, 0f, 0.125f),
            HalfWidth: 0.1f, UserData: new Vector4(0.125f, 0.25f, 0f, 0f));
        byte[] pixels2D = capture.Capture(material, false, 0.37f, snapshot, width, height);
        byte[] pixels3D = capture.Capture(material, true, 0.37f, snapshot, width, height);
        const int column = 368, row = 204;
        int offset = (row * width + column) * 4;
        int expectedX = (int)MathF.Round(((column + 0.5f) * 2f / width - 1f) * 255f);
        int expectedY = (int)MathF.Round((1f - (row + 0.5f) * 2f / height) * 255f);
        foreach (byte[] pixels in new[] { pixels2D, pixels3D })
        {
            Assert.That((int)pixels[offset], Is.EqualTo(expectedX).Within(1), "Fragment X must include the tangent offset.");
            Assert.That((int)pixels[offset + 1], Is.EqualTo(expectedY).Within(1), "Fragment Y must include expansion and displacement.");
            Assert.That(pixels[offset + 3], Is.EqualTo(255));
        }
        Assert.That((int)pixels2D[offset + 2], Is.EqualTo(32).Within(1), "2D must retain the original .125 depth despite the hook's Z offset.");
        Assert.That((int)pixels3D[offset + 2], Is.EqualTo(128).Within(1), "3D must apply .25 Z offset and .125 up displacement.");
        Assert.That(pixels2D[(153 * width + column) * 4 + 3], Is.EqualTo(255), "2D up moves the doubled ribbon width toward +Y.");
        Assert.That(pixels3D[(153 * width + column) * 4 + 3], Is.Zero, "3D up leaves the ribbon's Y bounds unchanged.");
        Assert.That(pixels2D[(268 * width + column) * 4 + 3], Is.Zero);
        Assert.That(pixels3D[(268 * width + column) * 4 + 3], Is.EqualTo(255), "3D retains the doubled width below the displaced center.");

        var beyondFarPlane = snapshot with { UserData = new Vector4(0.125f, 2f, 0f, 0f) };
        byte[] protected2D = capture.Capture(material, false, 0.37f, beyondFarPlane, width, height);
        byte[] displaced3D = capture.Capture(material, true, 0.37f, beyondFarPlane, width, height);
        Assert.That(protected2D[offset + 3], Is.EqualTo(255), "The 2D ribbon must stay in the clip volume.");
        Assert.That((int)protected2D[offset + 2], Is.EqualTo(32).Within(1));
        Assert.That(displaced3D[offset + 3], Is.Zero, "The 3D ribbon must move beyond the far plane.");
    }

    private static void AssertContinuousNoise(byte[] pixels, int width, int height, string label)
    {
        int minimum = 255, maximum = 0, maximumStep = 0, worstColumn = 0;
        for (int x = 129; x < width - 128; x++)
        {
            int offset = ((height / 2) * width + x) * 4;
            int value = pixels[offset];
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
            int step = Math.Abs(value - pixels[offset - 4]);
            if (step <= maximumStep) continue;
            maximumStep = step;
            worstColumn = x;
        }
        TestContext.Progress.WriteLine($"{label}: brightness {minimum}..{maximum}; largest adjacent step {maximumStep} at u={(worstColumn + 0.5f) * 8f / width}.");
        Assert.That(minimum, Is.GreaterThan(0), "The sampled ribbon must remain visible.");
        Assert.That(maximum - minimum, Is.GreaterThan(20), "The surface must retain spatial noise variation.");
        Assert.That(maximumStep, Is.LessThanOrEqualTo(4), $"{label}: brightness jumps by {maximumStep}/255 at column {worstColumn}.");
    }

    private static TrailEffectInstance2D CreateTrail(GpuTrailSystem2D system, MaterialAsset material, float seed, in Snapshot snapshot)
    {
        var effect = new TrailEffect2D
        {
            ExpectedPoints = 128, Spacing = 0.125f, Life = 1f,
            Width0 = snapshot.HalfWidth, Width1 = snapshot.HalfWidth, Opacity = 1f,
            FadeIn = 0f, FadeOut = 0f, Depth = DepthStencilState.None, Material = material,
            Color0 = snapshot.Color0 ?? Vector4.One, Color1 = snapshot.Color1 ?? Vector4.One,
            UserData = snapshot.UserData ?? Vector4.Zero,
        };
        Assert.That(system.TryCreateInstance(effect, new Vector2(snapshot.Anchor.X, snapshot.Anchor.Y), out var trail), Is.True);
        system.SetSeed(0, seed);
        trail.ExtendTo(new Vector2(snapshot.End.X, snapshot.End.Y), snapshot.End.Z);
        system.Update(snapshot.Age);
        return trail;
    }

    private static TrailEffectInstance3D CreateTrail(GpuTrailSystem3D system, MaterialAsset material, float seed, in Snapshot snapshot)
    {
        var effect = new TrailEffect3D
        {
            ExpectedPoints = 128, Spacing = 0.125f, Life = 1f,
            Width0 = snapshot.HalfWidth, Width1 = snapshot.HalfWidth, Opacity = 1f,
            FadeIn = 0f, FadeOut = 0f, Depth = DepthStencilState.None, Material = material,
            Color0 = snapshot.Color0 ?? Vector4.One, Color1 = snapshot.Color1 ?? Vector4.One,
            UserData = snapshot.UserData ?? Vector4.Zero,
        };
        Assert.That(system.TryCreateInstance(effect, snapshot.Anchor, out var trail), Is.True);
        system.SetSeed(0, seed);
        trail.ExtendTo(snapshot.End);
        system.Update(snapshot.Age);
        return trail;
    }
}
