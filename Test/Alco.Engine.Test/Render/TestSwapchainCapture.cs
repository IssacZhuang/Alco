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
        private int _frames;
        private Task<RenderCaptureResult>? _captureTask;

        public RenderCaptureResult? CaptureResult { get; private set; }

        public ClearWindowGame(GameEngineSetting setting, Vector4 clearColor) : base(setting)
        {
            _clearColor = clearColor;
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

            ColorAttachment color = new()
            {
                RenderView = MainPresenter.CurrentFrame.RenderView,
                Load = LoadOp.Clear,
                Store = StoreOp.Store,
                Clear = _clearColor,
            };
            RenderingDesc pass = new() { Colors = [color] };
            using (MainPresenter.FrameContext.BeginRenderPass(pass, [MainPresenter.SurfaceFormat]))
            {
            }
        }
    }

    /// <summary>
    /// Verifies that capturing a presented swapchain frame preserves RGB output and forces alpha to 255.
    /// </summary>
    /// <param name="backend">The graphics backend to test.</param>
    [TestCase(GraphicsBackend.Auto)]
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    public unsafe void CapturesPresentedFrameWithOpaqueAlphaAndExpectedColor(GraphicsBackend backend)
    {
        const uint width = 128;
        const uint height = 96;
        Vector4 linearClear = new(0.2f, 0.2f, 0.4f, 0.0f);

        GameEngineSetting setting = new()
        {
            StopWhenError = true,
            View = new ViewSetting((int)width, (int)height, "Swapchain Capture Test"),
            Graphics = GraphicsSetting.Default with { Backend = backend },
        };

        RenderCaptureResult? result;
        using (ClearWindowGame game = new(setting, linearClear))
        {
            game.Run();
            result = game.CaptureResult;
        }

        Assert.That(result, Is.Not.Null, "Swapchain capture did not complete within 240 frames.");
        Assert.That(result!.Success, Is.True, result.Error);
        Assert.That(result.Width, Is.EqualTo((int)width));
        Assert.That(result.Height, Is.EqualTo((int)height));
        Assert.That(result.PngBytes, Is.Not.Null.And.Not.Empty);

        fixed (byte* png = result.PngBytes!)
        {
            byte* decoded = ImageDecodeUtility.DecodePng(new ReadOnlySpan<byte>(png, result.PngBytes!.Length), out int w, out int h);
            try
            {
                Assert.That(w, Is.EqualTo((int)width));
                Assert.That(h, Is.EqualTo((int)height));

                byte expectedR = LinearToSrgbByte(linearClear.X);
                byte expectedG = LinearToSrgbByte(linearClear.Y);
                byte expectedB = LinearToSrgbByte(linearClear.Z);

                int centerOffset = ((h / 2) * w + (w / 2)) * 4;
                Assert.That(decoded[centerOffset + 0], Is.InRange((byte)(expectedR - 2), (byte)(expectedR + 2)), "red");
                Assert.That(decoded[centerOffset + 1], Is.InRange((byte)(expectedG - 2), (byte)(expectedG + 2)), "green");
                Assert.That(decoded[centerOffset + 2], Is.InRange((byte)(expectedB - 2), (byte)(expectedB + 2)), "blue");
                Assert.That(decoded[centerOffset + 3], Is.EqualTo(255), "opaque capture must force alpha to 255");
            }
            finally
            {
                NativeMemory.Free(decoded);
            }
        }
    }

    private static byte LinearToSrgbByte(float linear)
    {
        float srgb = linear <= 0.0031308f
            ? linear * 12.92f
            : 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
        return (byte)Math.Clamp(MathF.Round(srgb * 255.0f), 0.0f, 255.0f);
    }
}
