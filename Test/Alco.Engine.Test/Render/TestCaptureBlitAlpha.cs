#nullable enable

using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.AlcoGpu;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>
/// Pixel-level GPU tests of the capture blits' alpha handling on the real alco-gpu
/// backend, drawn the way the capture systems draw them (a full-screen blit into an
/// RGBA8 target): the plain <c>Blit</c> passes the source alpha through — the
/// transparent-capture path — while <c>BlitOpaque</c> forces every alpha to 1, so
/// opaque screenshots need no CPU-side alpha pass.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
public class TestCaptureBlitAlpha
{
    /// <summary>The square test image's side in pixels.</summary>
    private const int TestImageSize = 32;

    /// <summary>
    /// The region blit's push constant, byte-for-byte: float2 uvOffset then float2
    /// uvScale. The identity window samples the whole source.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RegionCrop
    {
        public float UvOffsetX;
        public float UvOffsetY;
        public float UvScaleX;
        public float UvScaleY;
    }

    /// <summary>The plain blit keeps the source alpha (transparent pixels stay transparent).</summary>
    [Test]
    public unsafe void TestBlitPassesSourceAlphaThrough()
    {
        AssertBlitAlpha("Blit", expectedAlpha255: false);
    }

    /// <summary>The opaque blit forces every alpha to 255 without touching RGB.</summary>
    [Test]
    public unsafe void TestBlitOpaqueForcesAlphaTo255()
    {
        AssertBlitAlpha("BlitOpaque", expectedAlpha255: true);
    }

    /// <summary>The cropped opaque blit (map snapshots) forces every alpha to 255 too.</summary>
    [Test]
    public unsafe void TestBlitRegionOpaqueForcesAlphaTo255()
    {
        AssertBlitAlpha("BlitRegionOpaque", expectedAlpha255: true, useIdentityCropConstant: true);
    }

    /// <summary>
    /// Blits an alpha-banded source through the given module's full-screen shader and
    /// asserts the destination's alpha channel: forced to 255, or passed through.
    /// </summary>
    /// <param name="moduleName">The slang module to blit with ("Blit" or "BlitOpaque").</param>
    /// <param name="expectedAlpha255">True when the module must force every alpha to 255;
    /// false when it must reproduce the source alphas exactly.</param>
    /// <param name="useIdentityCropConstant">True to draw with the region blit's crop push
    /// constant (identity window: the whole source), as the map snapshot blit does.</param>
    private static unsafe void AssertBlitAlpha(string moduleName, bool expectedAlpha255, bool useIdentityCropConstant = false)
    {
        byte[] source = CreateBandedPixels();
        using TestDeviceHost deviceHost = new();
        using BlitRenderingHost renderingHost = new();
        AlcoGpuDevice device = new(new DeviceDescriptor(deviceHost, GraphicsBackend.Auto, debug: false));
        RenderingSystem rendering = new(
            renderingHost,
            device,
            PixelFormat.RGBA16Float,
            PixelFormat.Depth24PlusStencil8,
            CreateShaderResolver());

        Texture2D sourceTexture = rendering.CreateTexture2D(TestImageSize, TestImageSize, ImageLoadOption.Default with
        {
            Format = PixelFormat.RGBA8Unorm,
            Usage = TextureUsage.Write | TextureUsage.TextureBinding,
            Name = "blit_alpha_source",
        });
        RenderTexture target = rendering.CreateRenderTexture(
            rendering.PreferredRGBATexturePass, TestImageSize, TestImageSize, "blit_alpha_target");
        GraphicsMaterial material = rendering.CreateGraphicsMaterial(rendering.ShaderSystem.GetShader(moduleName));
        RenderContext context = rendering.CreateRenderContext("blit_alpha_context");
        byte[] readback = new byte[source.Length];
        GPUTextureReadbackRequest request = new();
        try
        {
            fixed (byte* pointer = source)
            {
                device.WriteTexture(sourceTexture.NativeTexture, pointer, (uint)source.Length);
            }

            material.SetTexture(ShaderResourceId.Texture, sourceTexture);
            using (RenderFrameScope frame = context.BeginFrame())
            using (RenderPassScope pass = context.BeginPass(target.FrameBuffer))
            {
                if (useIdentityCropConstant)
                {
                    RegionCrop identity = new() { UvOffsetX = 0f, UvOffsetY = 0f, UvScaleX = 1f, UvScaleY = 1f };
                    pass.DrawWithConstant(rendering.MeshFullScreen, material, identity);
                }
                else
                {
                    pass.Draw(rendering.MeshFullScreen, material);
                }
            }

            fixed (byte* pointer = readback)
            {
                device.BeginReadTexture(target.ColorTextures[0].NativeTexture, pointer, (uint)readback.Length, request);
                for (int frame = 0; frame < 100 && request.IsPending; frame++)
                {
                    device.ProcessPendingReadbacks();
                    if (request.IsPending)
                    {
                        System.Threading.Thread.Sleep(15);
                    }
                }
            }

            request.ThrowIfFailed();
            Assert.That(request.IsPending, Is.False, "the blit readback did not complete in 100 polls");
        }
        finally
        {
            request.Reset();
            context.Dispose();
            material.Dispose();
            target.Dispose();
            sourceTexture.Dispose();
        }

        Assert.That(readback.Length, Is.EqualTo(source.Length));
        for (int i = 0; i < readback.Length; i += 4)
        {
            byte expectedAlpha = expectedAlpha255 ? (byte)255 : source[i + 3];
            Assert.That(readback[i + 3], Is.EqualTo(expectedAlpha), $"alpha at pixel {i / 4}");
            Assert.That(readback[i], Is.EqualTo(source[i]), $"red at pixel {i / 4}");
            Assert.That(readback[i + 1], Is.EqualTo(source[i + 1]), $"green at pixel {i / 4}");
            Assert.That(readback[i + 2], Is.EqualTo(source[i + 2]), $"blue at pixel {i / 4}");
        }
    }

    /// <summary>
    /// Serves <c>Src/Alco.Rendering/Assets/Shaders</c> by dashed filename, applying the
    /// engine resolver's underscore→dashed convention, so the built-in blit modules and
    /// their <c>AlcoRendering_Core</c> import compile from the repository.
    /// </summary>
    private static SlangFileResolver CreateShaderResolver()
    {
        Dictionary<string, string> shaderFiles = new(StringComparer.OrdinalIgnoreCase);
        string shaderRoot = Path.Combine(RepoRoot(), "Src", "Alco.Rendering", "Assets", "Shaders");
        foreach (string file in Directory.EnumerateFiles(shaderRoot, "*.slang", SearchOption.AllDirectories))
        {
            shaderFiles[Path.GetFileName(file).Replace('_', '-')] = File.ReadAllText(file);
        }

        return path => shaderFiles.TryGetValue(Path.GetFileName(path).Replace('_', '-'), out string? content)
            ? content
            : null;
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Alco.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>
    /// Creates the RGBA8 test image: three horizontal bands over a constant RGB with
    /// alpha 0, 128 and 255, so alpha handling is observable independently of RGB.
    /// </summary>
    private static byte[] CreateBandedPixels()
    {
        byte[] alphas = [0, 128, 255];
        byte[] pixels = new byte[TestImageSize * TestImageSize * 4];
        int bandHeight = TestImageSize / alphas.Length;
        for (int y = 0; y < TestImageSize; y++)
        {
            byte alpha = alphas[Math.Min(y / bandHeight, alphas.Length - 1)];
            for (int x = 0; x < TestImageSize; x++)
            {
                int offset = (y * TestImageSize + x) * 4;
                pixels[offset] = 10;
                pixels[offset + 1] = 20;
                pixels[offset + 2] = 30;
                pixels[offset + 3] = alpha;
            }
        }

        return pixels;
    }

    /// <summary>
    /// The minimal <see cref="IRenderingSystemHost"/> a standalone
    /// <see cref="RenderingSystem"/> needs: per-frame updates are ignored; disposal
    /// fans out through the dispose event the rendering system subscribes to.
    /// </summary>
    private sealed class BlitRenderingHost : IRenderingSystemHost, IDisposable
    {
        /// <inheritdoc />
        public event Action<float>? OnUpdate
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public event Action? OnDispose;

        /// <inheritdoc />
        public void Dispose()
        {
            OnDispose?.Invoke();
        }
    }

    /// <summary>
    /// The minimal <see cref="IGPUDeviceHost"/> a bare device needs: deferred
    /// destroys and readback processing hang off its end-of-frame event.
    /// </summary>
    private sealed class TestDeviceHost : IGPUDeviceHost, IDisposable
    {
        /// <inheritdoc />
        public event Action? OnEndFrame;

        /// <inheritdoc />
        public event Action? OnDispose;

        /// <summary>Runs deferred resource disposal and readback processing.</summary>
        public void EndFrame() => OnEndFrame?.Invoke();

        /// <inheritdoc />
        public void Dispose() => OnDispose?.Invoke();

        /// <inheritdoc />
        public void LogInfo(ReadOnlySpan<char> message) { }

        /// <inheritdoc />
        public void LogWarning(ReadOnlySpan<char> message) { }

        /// <inheritdoc />
        public void LogError(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());

        /// <inheritdoc />
        public void LogSuccess(ReadOnlySpan<char> message) { }
    }
}
