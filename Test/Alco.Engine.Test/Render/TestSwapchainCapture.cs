using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>
/// End-to-end GPU tests verifying presented swapchain capture, sRGB fidelity, and opaque alpha normalization.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
[Platform("Win")]
[Apartment(ApartmentState.STA)]
public sealed class TestSwapchainCapture
{
    private sealed class ClearWindowGame : GameEngine
    {
        private readonly Vector4 _clearColor;
        private readonly RenderContext _renderContext;
        private int _frames;
        private Task<RenderCaptureResult>? _captureTask;

        public RenderCaptureResult? CaptureResult { get; private set; }
        public Format CapturedSurfaceFormat { get; }

        public ClearWindowGame(GameEngineSetting setting, Vector4 clearColor) : base(setting)
        {
            _clearColor = clearColor;
            _renderContext = new RenderContext(RenderingSystem);
            CapturedSurfaceFormat = MainPresenter.SurfaceFormat;
        }

        protected override void OnUpdate(float delta)
        {
            _frames++;
            if (_frames == 2 && _captureTask == null)
            {
                _captureTask = SwapchainCapture.RequestCaptureAsync();
            }

            if (_captureTask != null && _captureTask.IsCompleted)
            {
                CaptureResult = _captureTask.GetAwaiter().GetResult();
                Stop();
                return;
            }

            if (_frames > 240)
            {
                Stop();
                return;
            }

            if (!MainPresenter.HasValidFrame)
            {
                return;
            }

            bool begun = false;
            try
            {
                _renderContext.BeginFrame();
                begun = true;

                ColorAttachment color = new()
                {
                    RenderView = MainPresenter.CurrentFrame.RenderView,
                    Load = LoadOp.Clear,
                    Store = StoreOp.Store,
                    Clear = _clearColor,
                };
                RenderingDesc pass = new() { Colors = [color] };
                using (_renderContext.BeginRenderPass(pass, [MainPresenter.SurfaceFormat]))
                {
                }

                MainPresenter.EnqueueFrameContext(_renderContext);
            }
            catch
            {
                if (begun)
                {
                    _renderContext.DiscardFrame();
                }
                throw;
            }
        }

        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            GraphicsDevice.WaitIdle();
            _renderContext.Destroy();
            base.Dispose();
        }
    }

    /// <summary>
    /// Verifies that the default swapchain selects Bgra8Unorm and captures presented RGB without implicit hardware sRGB encoding, forcing alpha to 255.
    /// </summary>
    /// <param name="backend">The graphics backend to test.</param>
    [TestCase(GraphicsBackend.Auto)]
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    public void CapturesPresentedFrameWithOpaqueAlphaAndExpectedColor(GraphicsBackend backend)
    {
        Vector4 clearColor = new(0.2f, 0.2f, 0.4f, 0.0f);
        GameEngineSetting setting = new()
        {
            StopWhenError = true,
            View = new ViewSetting(128, 96, "Swapchain Capture Test"),
            Graphics = GraphicsSetting.Default with { Backend = backend },
        };

        RunAndVerifyCapture(
            setting,
            clearColor,
            Format.Bgra8Unorm,
            ToUnormByte(clearColor.X),
            ToUnormByte(clearColor.Y),
            ToUnormByte(clearColor.Z));
    }

    /// <summary>
    /// Verifies that explicitly requesting Bgra8Srgb applies hardware linear-to-sRGB encoding on presentation capture.
    /// </summary>
    /// <param name="backend">The graphics backend to test.</param>
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    public void CapturesPresentedSrgbFrameWhenExplicitlyRequested(GraphicsBackend backend)
    {
        Vector4 linearClear = new(0.2f, 0.2f, 0.4f, 0.0f);
        GameEngineSetting setting = new()
        {
            StopWhenError = true,
            View = new ViewSetting(128, 96, "Swapchain sRGB Capture Test"),
            Graphics = GraphicsSetting.Default with
            {
                Backend = backend,
                PreferredSurfaceFormat = Format.Bgra8Srgb,
            },
        };

        RunAndVerifyCapture(
            setting,
            linearClear,
            Format.Bgra8Srgb,
            LinearToSrgbByte(linearClear.X),
            LinearToSrgbByte(linearClear.Y),
            LinearToSrgbByte(linearClear.Z));
    }

    private static unsafe void RunAndVerifyCapture(
        GameEngineSetting setting,
        Vector4 clearColor,
        Format expectedFormat,
        byte expectedR,
        byte expectedG,
        byte expectedB)
    {
        int width = setting.View.Width;
        int height = setting.View.Height;

        RenderCaptureResult? result;
        Format actualFormat;
        using (ClearWindowGame game = new(setting, clearColor))
        {
            game.Run();
            result = game.CaptureResult;
            actualFormat = game.CapturedSurfaceFormat;
        }

        Assert.That(actualFormat, Is.EqualTo(expectedFormat));
        Assert.That(result, Is.Not.Null, "Swapchain capture did not complete within 240 frames.");
        Assert.That(result!.Success, Is.True, result.Error);
        Assert.That(result.Width, Is.EqualTo(width));
        Assert.That(result.Height, Is.EqualTo(height));
        Assert.That(result.PngBytes, Is.Not.Null.And.Not.Empty);

        fixed (byte* png = result.PngBytes!)
        {
            byte* decoded = ImageDecodeUtility.DecodePng(new ReadOnlySpan<byte>(png, result.PngBytes!.Length), out int w, out int h);
            try
            {
                Assert.That(w, Is.EqualTo(width));
                Assert.That(h, Is.EqualTo(height));

                int centerOffset = ((h / 2) * w + (w / 2)) * 4;
                Assert.That(decoded[centerOffset + 0], Is.InRange((byte)Math.Max(0, expectedR - 2), (byte)Math.Min(255, expectedR + 2)), "red");
                Assert.That(decoded[centerOffset + 1], Is.InRange((byte)Math.Max(0, expectedG - 2), (byte)Math.Min(255, expectedG + 2)), "green");
                Assert.That(decoded[centerOffset + 2], Is.InRange((byte)Math.Max(0, expectedB - 2), (byte)Math.Min(255, expectedB + 2)), "blue");
                Assert.That(decoded[centerOffset + 3], Is.EqualTo(255), "opaque capture must force alpha to 255");
            }
            finally
            {
                NativeMemory.Free(decoded);
            }
        }
    }

    private static byte ToUnormByte(float value)
    {
        return (byte)Math.Clamp(MathF.Round(value * 255.0f), 0.0f, 255.0f);
    }

    private static byte LinearToSrgbByte(float linear)
    {
        float srgb = linear <= 0.0031308f
            ? linear * 12.92f
            : 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
        return (byte)Math.Clamp(MathF.Round(srgb * 255.0f), 0.0f, 255.0f);
    }
}
