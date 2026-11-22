using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.AgentControlProtocol.Test;

/// <summary>
/// End-to-end tests verifying HTTP agent control script execution and live swapchain screenshots.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
[Platform("Win")]
[Apartment(ApartmentState.STA)]
public sealed class ScreenshotToolTests
{
    private sealed class ApiCaptureHarness : GameEngine
    {
        private readonly AgentControlHost _host;
        private readonly RenderContext _renderContext;
        private readonly int _port;
        private volatile bool _done;

        public HttpStatusCode Status { get; private set; }
        public string? ContentType { get; private set; }
        public string? HeaderWidth { get; private set; }
        public string? HeaderHeight { get; private set; }
        public byte[]? PngBytes { get; private set; }
        public string? ScriptResult { get; private set; }
        public Exception? Failure { get; private set; }

        public ApiCaptureHarness(GameEngineSetting setting, int port) : base(setting)
        {
            _port = port;
            _renderContext = new RenderContext(RenderingSystem);
            _host = new AgentControlHost(this, new AgentControlOptions());
            AddSystem(_host);
        }

        protected override void OnStart()
        {
            base.OnStart();
            _host.StartServer(_port);
            _ = Task.Run(ProbeAsync);
        }

        protected override void OnUpdate(float delta)
        {
            base.OnUpdate(delta);
            if (_done)
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
                    Clear = new Vector4(0.1f, 0.5f, 0.8f, 0.0f),
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

        private async Task ProbeAsync()
        {
            using HttpClient client = new() { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    try
                    {
                        using HttpResponseMessage ready = await client.GetAsync("/status");
                        if (ready.IsSuccessStatusCode)
                        {
                            break;
                        }
                    }
                    catch (HttpRequestException)
                    {
                    }

                    await Task.Delay(50);
                }

                using HttpResponseMessage scriptResponse = await client.PostAsJsonAsync(
                    "/tools/ExecuteScript",
                    new { code = "return Engine.MainView.Size.X.ToString();" });
                scriptResponse.EnsureSuccessStatusCode();
                ScriptResult = await scriptResponse.Content.ReadAsStringAsync();

                using HttpResponseMessage shotResponse = await client.GetAsync("/tools/CaptureScreenshot");
                Status = shotResponse.StatusCode;
                ContentType = shotResponse.Content.Headers.ContentType?.MediaType;
                HeaderWidth = shotResponse.Headers.TryGetValues("X-Screenshot-Width", out var widths)
                    ? string.Join(",", widths)
                    : null;
                HeaderHeight = shotResponse.Headers.TryGetValues("X-Screenshot-Height", out var heights)
                    ? string.Join(",", heights)
                    : null;
                PngBytes = await shotResponse.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                Failure = ex;
            }
            finally
            {
                _done = true;
            }
        }
    }

    /// <summary>
    /// Verifies that the HTTP API executes scripts and returns a valid PNG screenshot of the presented window.
    /// </summary>
    /// <param name="backend">The graphics backend used to present the window.</param>
    [TestCase(GraphicsBackend.Auto)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    public unsafe void HttpApi_CapturesPresentedScreenshotAndExecutesScript(GraphicsBackend backend)
    {
        const int width = 160;
        const int height = 120;
        int port = ReserveLoopbackPort();

        GameEngineSetting setting = new()
        {
            StopWhenError = true,
            View = new ViewSetting(width, height, "Agent Control Screenshot Test"),
            Graphics = GraphicsSetting.Default with { Backend = backend },
        };

        using ApiCaptureHarness harness = new(setting, port);
        harness.Run();

        Assert.That(harness.Failure, Is.Null, harness.Failure?.ToString());
        Assert.That(harness.ScriptResult, Does.Contain("\"success\":true").And.Contain("160"));
        Assert.That(harness.Status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(harness.ContentType, Is.EqualTo("image/png"));
        Assert.That(harness.HeaderWidth, Is.EqualTo("160"));
        Assert.That(harness.HeaderHeight, Is.EqualTo("120"));
        Assert.That(harness.PngBytes, Is.Not.Null.And.Not.Empty);

        fixed (byte* png = harness.PngBytes!)
        {
            byte* decoded = ImageDecodeUtility.DecodePng(new ReadOnlySpan<byte>(png, harness.PngBytes!.Length), out int w, out int h);
            try
            {
                Assert.That(w, Is.EqualTo(width));
                Assert.That(h, Is.EqualTo(height));

                int center = ((h / 2) * w + (w / 2)) * 4;
                Assert.That(decoded[center + 3], Is.EqualTo(255));
                Assert.That(decoded[center + 2], Is.GreaterThan(decoded[center + 0]), "blue clear channel exceeds red");
            }
            finally
            {
                NativeMemory.Free(decoded);
            }
        }
    }

    private static int ReserveLoopbackPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
