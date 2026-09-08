using System.Numerics;
using System.Reflection;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>Regresses 3D trail pool reuse, material-cache allocations and rendered ring continuity.</summary>
[TestFixture]
[NonParallelizable]
public sealed class TestGpuTrailSystem3D
{
    private sealed class DeviceHost : IGPUDeviceHost, IDisposable
    {
        /// <inheritdoc />
        public event Action? OnEndFrame;

        /// <inheritdoc />
        public event Action? OnDispose;

        /// <inheritdoc />
        public void Dispose()
        {
            OnEndFrame?.Invoke();
            OnDispose?.Invoke();
        }

        /// <inheritdoc />
        public void LogInfo(ReadOnlySpan<char> message) { }

        /// <inheritdoc />
        public void LogWarning(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());

        /// <inheritdoc />
        public void LogError(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());

        /// <inheritdoc />
        public void LogSuccess(ReadOnlySpan<char> message) { }
    }

    /// <summary>A freed large slice can serve smaller requests, including a non-power-of-two total budget.</summary>
    [TestCase(64)]
    [TestCase(95)]
    public void FreedLargeSliceSplits(int pointCapacity)
    {
        using var host = CreateHost();
        using var system = new GpuTrailSystem3D(host.RenderingSystem, pointCapacity, 3);
        using var large = CreateTrail(system, 64);
        large.Dispose();

        using var first = CreateTrail(system, 32);
        using var second = CreateTrail(system, 32);
        Assert.That(system.TryCreateInstance(new TrailEffect3D { ExpectedPoints = 32 }, Vector3.Zero, out _), Is.False);
        Assert.That(first.IsAlive && second.IsAlive, Is.True);
    }

    /// <summary>Splitting a free slice does not require every other trail to be retired.</summary>
    [Test]
    public void FreedLargeSliceSplitsWhileAnotherTrailIsAlive()
    {
        using var host = CreateHost();
        using var system = new GpuTrailSystem3D(host.RenderingSystem, 96, 3);
        using var large = CreateTrail(system, 64);
        using var retained = CreateTrail(system, 32);
        large.Dispose();

        using var first = CreateTrail(system, 32);
        using var second = CreateTrail(system, 32);
        retained.ExtendTo(Vector3.UnitX);
        Assert.That(retained.IsEmitting, Is.True);
        Assert.That(first.IsAlive && second.IsAlive, Is.True);
    }

    /// <summary>Freeing the middle slice joins both neighbors without crossing a live allocation.</summary>
    [Test]
    public void AdjacentFreeSlicesMergeWhileAnotherTrailIsAlive()
    {
        using var host = CreateHost();
        using var system = new GpuTrailSystem3D(host.RenderingSystem, 128, 4);
        using var first = CreateTrail(system, 32);
        using var middle = CreateTrail(system, 32);
        using var third = CreateTrail(system, 32);
        using var retained = CreateTrail(system, 32);
        first.Dispose();
        third.Dispose();
        Assert.That(system.TryCreateInstance(new TrailEffect3D { ExpectedPoints = 64 }, Vector3.Zero, out _), Is.False,
            "Non-adjacent free ranges must not be joined through the live middle trail.");

        middle.Dispose();
        using var large = CreateTrail(system, 64);
        using var remainder = CreateTrail(system, 32);
        Assert.That(retained.IsEmitting, Is.True);
        Assert.That(system.TryCreateInstance(new TrailEffect3D { ExpectedPoints = 32 }, Vector3.Zero, out _), Is.False);
    }

    /// <summary>Repeated size changes and natural expiry do not strand the fixed point budget.</summary>
    [Test]
    public void MixedCapacitiesRecycleAfterExpiry()
    {
        using var host = CreateHost();
        using var system = new GpuTrailSystem3D(host.RenderingSystem, 256, 2);
        int[] sizes = [256, 32, 128, 64];
        for (int iteration = 0; iteration < 32; iteration++)
        {
            foreach (int size in sizes)
            {
                using var trail = CreateTrail(system, size);
                trail.ExtendTo(Vector3.UnitX);
                trail.Stop();
                system.Update(1f);
                using var replacement = CreateTrail(system, 256);
                trail.Dispose();
                Assert.That(replacement.IsEmitting, Is.True, "A stale handle affected the recycled slot.");
            }
        }
    }

    /// <summary>Very large requests clamp to the documented maximum before power-of-two rounding.</summary>
    [Test]
    public void ExpectedPointLimitDoesNotOverflow()
    {
        using var host = CreateHost();
        using var system = new GpuTrailSystem3D(host.RenderingSystem, 1024, 2);
        using var trail = CreateTrail(system, int.MaxValue);
        Assert.That(system.TryCreateInstance(new TrailEffect3D { ExpectedPoints = 32 }, Vector3.Zero, out _), Is.False);
    }

    /// <summary>Both built-in surfaces compose with the corrected trail pass and resource layout.</summary>
    [TestCase("TrailSurfaceDefault")]
    [TestCase("TrailSurfaceSmoke")]
    public void BuiltInSurfaceComposes(string surfaceName)
    {
        using var host = CreateHost();
        using var system = new GpuTrailSystem3D(host.RenderingSystem, 32, 1);
        var effect = new TrailEffect3D
        {
            ExpectedPoints = 32,
            Material = new MaterialAsset { Surface = host.RenderingSystem.ShaderSystem.GetLibrary(surfaceName) },
        };
        Assert.That(system.TryCreateInstance(effect, Vector3.Zero, out TrailEffectInstance3D trail), Is.True);
        using (trail)
        {
            trail.ExtendTo(Vector3.UnitX);
            Assert.That(trail.IsAlive, Is.True);
        }
    }

    /// <summary>A warm cache lookup creates neither a closure nor boxed graphics states.</summary>
    [Test]
    public void MaterialCacheHitDoesNotAllocate()
    {
        using var host = CreateHost();
        using var system = new GpuTrailSystem3D(host.RenderingSystem, 32, 1);
        Func<TrailEffect3D, GraphicsMaterial> lookup = GetMaterialLookup(system);
        var effect = new TrailEffect3D();
        for (int i = 0; i < 1000; i++) lookup(effect);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) lookup(effect);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Material cache hits allocated {allocated} B over 2000 calls.");
    }

    /// <summary>The allocation-free comparer preserves material identity and all render-state distinctions.</summary>
    [Test]
    public void MaterialCacheSeparatesAssetsAndRenderStates()
    {
        using var host = CreateHost();
        using var system = new GpuTrailSystem3D(host.RenderingSystem, 32, 1);
        Func<TrailEffect3D, GraphicsMaterial> lookup = GetMaterialLookup(system);
        var asset = new MaterialAsset();
        var baseline = new TrailEffect3D { Material = asset };
        GraphicsMaterial material = lookup(baseline);
        Assert.That(lookup(new TrailEffect3D { Material = asset }), Is.SameAs(material));

        TrailEffect3D[] different =
        [
            new() { Material = new MaterialAsset() },
            new() { Material = asset, Blend = BlendState.Additive },
            new() { Material = asset, Depth = DepthStencilState.None },
            new() { Material = asset, Depth = DepthStencilState.Read with { StencilReadMask = 1 } },
            new() { Material = asset, Depth = DepthStencilState.Read with { StencilWriteMask = 1 } },
        ];
        foreach (TrailEffect3D effect in different)
        {
            GraphicsMaterial differentMaterial = lookup(effect);
            Assert.That(differentMaterial, Is.Not.SameAs(material));
            Assert.That(lookup(effect), Is.SameAs(differentMaterial));
        }
    }

    /// <summary>Emission, uploads, draw planning and the reusable frame/pass scopes stay allocation-free.</summary>
    [Test]
    public void SteadyFrameDoesNotAllocate()
    {
        using var host = CreateHost();
        RenderingSystem rendering = host.RenderingSystem;
        using var system = new GpuTrailSystem3D(rendering, 32, 1) { CameraPosition = new Vector3(0f, 0f, -5f) };
        using var trail = CreateTrail(system, 32);
        using var layout = rendering.GraphicsDevice.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "trail3d_test"));
        using var target = rendering.CreateRenderTexture(layout, 32, 32, "trail3d_test");
        using var context = rendering.CreateRenderContext("trail3d_test");
        int tick = 0;
        void Frame()
        {
            system.Update(1f / 60f);
            trail.ExtendTo(new Vector3(++tick * 0.25f, 0f, 0f));
            using (context.BeginFrame())
            using (RenderPassScope pass = context.BeginPass(target.FrameBuffer))
                system.Render(pass);
        }
        for (int i = 0; i < 1000; i++) Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) Frame();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Steady rendering allocated {allocated} B over 2000 frames.");
    }

    /// <summary>Real GPU pixels cover every live segment before and after one or multiple ring wraps.</summary>
    [TestCase(2, false)]
    [TestCase(31, false)]
    [TestCase(32, false)]
    [TestCase(34, false)]
    [TestCase(64, false)]
    [TestCase(66, true)]
    [Category("WebGPU")]
    public unsafe void RingRendersContinuousLiveWindow(int writtenPoints, bool offsetSlice)
    {
        using var deviceHost = new DeviceHost();
        GPUDevice device = GraphicsDeviceFactory.CreateWebGPUDevice(new DeviceDescriptor(deviceHost, GraphicsBackend.WGPUVulkan));
        using var host = CreateHost(device);
        RenderingSystem rendering = host.RenderingSystem;

        // The camera looks down +Z at the origin with +Y up: the ribbon runs
        // along world X, so the camera-facing expansion (side = viewDir x
        // tangent) is world Y — screen rows. The orthographic projection maps
        // world x in [-1.1, 1.1] to the target's 256 columns and world y in
        // [-0.6, 0.6] to its 64 rows (the 0.15 half width covers row 32).
        Matrix4x4 view = Matrix4x4.CreateLookAtLeftHanded(new Vector3(0f, 0f, -5f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreateOrthographicLeftHanded(2.2f, 1.2f, 0.1f, 10f);
        using var camera = rendering.CreateGraphicsValueBuffer(view * projection, "trail3d_test_camera");
        using var system = new GpuTrailSystem3D(rendering, 64, 2)
        {
            Camera = camera,
            CameraPosition = new Vector3(0f, 0f, -5f),
        };
        using var reserved = offsetSlice ? CreateTrail(system, 32) : null;
        const float spacing = 0.03125f;
        const float last = 0.3125f;
        var effect = new TrailEffect3D
        {
            ExpectedPoints = 32, Spacing = spacing, Life = 10f,
            Width0 = 0.15f, Width1 = 0.15f, Opacity = 1f,
            FadeIn = 0f, FadeOut = 0f, Depth = DepthStencilState.None,
        };
        Vector3 anchor = new(last - writtenPoints * spacing, 0f, 0f);
        Assert.That(system.TryCreateInstance(effect, anchor, out var trail), Is.True);
        using (trail)
        using (var layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "trail3d_test")))
        using (var target = rendering.CreateRenderTexture(layout, 256, 64, "trail3d_test"))
        using (var context = rendering.CreateRenderContext("trail3d_test"))
        {
            trail.ExtendTo(new Vector3(last, 0f, 0f));
            system.Update(0.1f);
            using (context.BeginFrame())
            using (RenderPassScope pass = context.BeginPass(target.FrameBuffer, [new ClearColorData(0, Vector4.Zero)]))
                system.Render(pass);

            byte[] pixels = new byte[256 * 64 * 4];
            fixed (byte* pointer = pixels)
                device.ReadTexture(target.FrameBuffer.Colors[0], pointer, (uint)pixels.Length);
            float first = last - (Math.Min(writtenPoints, 32) - 1) * spacing;
            for (int x = 0; x < 256; x++)
            {
                float worldX = ((x + 0.5f) / 256f * 2f - 1f) * 1.1f;
                byte alpha = pixels[(32 * 256 + x) * 4 + 3];
                if (worldX > first && worldX < last)
                    Assert.That(alpha, Is.GreaterThan(200), $"Missing live segment at x={worldX}, written={writtenPoints}.");
                else
                    Assert.That(alpha, Is.Zero, $"Stale geometry outside the live window at x={worldX}.");
            }
        }
    }

    /// <summary>
    /// The rendered envelope eases into and out of visibility like its 2D
    /// counterpart, while zero-duration ramps remain fully visible.
    /// </summary>
    [TestCase(0.05f, 0.2f, 0.5f, 40)]
    [TestCase(0.15f, 0.2f, 0.5f, 215)]
    [TestCase(0.625f, 0.2f, 0.5f, 215)]
    [TestCase(0.875f, 0.2f, 0.5f, 40)]
    [TestCase(0f, 0f, 0f, 255)]
    [TestCase(1f, 0f, 0f, 255)]
    [Category("WebGPU")]
    public unsafe void FadeEnvelopeRendersSmoothEndpoints(float age, float fadeIn, float fadeOut, int expectedAlpha)
    {
        using var deviceHost = new DeviceHost();
        GPUDevice device = GraphicsDeviceFactory.CreateWebGPUDevice(new DeviceDescriptor(deviceHost, GraphicsBackend.WGPUVulkan));
        using var host = CreateHost(device);
        RenderingSystem rendering = host.RenderingSystem;
        Vector3 cameraPosition = new(0f, 0f, -5f);
        Matrix4x4 view = Matrix4x4.CreateLookAtLeftHanded(cameraPosition, Vector3.Zero, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreateOrthographicLeftHanded(2f, 2f, 0.1f, 10f);
        using var camera = rendering.CreateGraphicsValueBuffer(view * projection, "trail3d_fade_camera");
        using var system = new GpuTrailSystem3D(rendering, 32, 1)
        {
            Camera = camera, CameraPosition = cameraPosition,
        };
        var effect = new TrailEffect3D
        {
            ExpectedPoints = 32, Spacing = 0.25f, Life = 1f,
            Width0 = 0.15f, Width1 = 0.15f, Opacity = 1f,
            FadeIn = fadeIn, FadeOut = fadeOut, Depth = DepthStencilState.None,
        };
        Assert.That(system.TryCreateInstance(effect, new Vector3(-0.75f, 0f, 0f), out var trail), Is.True);
        using (trail)
        using (var layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "trail3d_fade")))
        using (var target = rendering.CreateRenderTexture(layout, 64, 65, "trail3d_fade"))
        using (var context = rendering.CreateRenderContext("trail3d_fade"))
        {
            trail.ExtendTo(new Vector3(0.5f, 0f, 0f));
            system.Update(age);
            using (context.BeginFrame())
            using (RenderPassScope pass = context.BeginPass(target.FrameBuffer, [new ClearColorData(0, Vector4.Zero)]))
                system.Render(pass);

            byte[] pixels = new byte[64 * 65 * 4];
            fixed (byte* pointer = pixels)
                device.ReadTexture(target.FrameBuffer.Colors[0], pointer, (uint)pixels.Length);
            // The center pixel lies exactly on the camera-facing ribbon's axis,
            // isolating the age envelope from the surface's edge falloff.
            Assert.That(pixels[(32 * 64 + 32) * 4 + 3], Is.EqualTo(expectedAlpha).Within(1));
        }
    }

    private static TrailEffectInstance3D CreateTrail(GpuTrailSystem3D system, int points)
    {
        Assert.That(system.TryCreateInstance(new TrailEffect3D { ExpectedPoints = points, Depth = DepthStencilState.None },
            Vector3.Zero, out TrailEffectInstance3D trail), Is.True, $"Could not allocate {points} points.");
        return trail;
    }

    private static Func<TrailEffect3D, GraphicsMaterial> GetMaterialLookup(GpuTrailSystem3D system)
    {
        return (Func<TrailEffect3D, GraphicsMaterial>)typeof(GpuTrailSystem3D)
            .GetMethod("GetOrCreateMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Func<TrailEffect3D, GraphicsMaterial>), system);
    }

    private static DummyRenderingSystemHost CreateHost(GPUDevice? device = null)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Alco.slnx")))
            directory = directory.Parent;
        string root = Path.Combine(directory!.FullName, "Src", "Alco.Rendering", "Assets", "Shaders");
        string[] names = Directory.GetFiles(root, "*.slang", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).ToArray();
        return Utility.CreateRenderingSystem(ShaderModuleResolver.Create(
            path =>
            {
                string file = Path.Combine(root, path);
                return File.Exists(file) ? File.OpenRead(file) : null;
            }, () => names), device);
    }
}
