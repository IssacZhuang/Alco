using System.ComponentModel;
using System.Globalization;
using Alco.Engine;

namespace Alco.AgentControlProtocol;

/// <summary>
/// Built-in agent tool: captures the final presented frame through the engine's
/// <see cref="SwapchainCaptureSystem"/> — the exact pixels about to be presented,
/// including any ImGui overlay. Works in windowed and headless (offscreen) modes.
/// </summary>
public sealed class ScreenshotTool
{
    private readonly GameEngine _engine;

    /// <summary>Creates the tool bound to an engine.</summary>
    /// <param name="engine">The engine whose presented frame is captured.</param>
    public ScreenshotTool(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
    }

    /// <summary>
    /// Captures a screenshot of the presented frame. Runs on the agent thread: it hops
    /// to the main thread only to register the capture, then awaits the asynchronous
    /// GPU readback and PNG encode.
    /// </summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Capture the current presented frame and return it as an HTTP image/png response through the agent control API. Windowed mode captures the exact presented pixels including any ImGui overlay; headless mode captures the rendered frame at the render-graph chain tail.")]
    public async Task<BinaryToolResult> CaptureScreenshot()
    {
        if (_engine.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(GameEngine), "Engine is disposed.");
        }

        try
        {
            Task<RenderCaptureResult> captureTask = await _engine
                .PostToMainThreadAsync(() => _engine.SwapchainCapture.RequestCaptureAsync())
                .ConfigureAwait(false);

            RenderCaptureResult result = await captureTask.ConfigureAwait(false);
            if (!result.Success || result.PngBytes == null)
            {
                string error = result.Error ?? "Screenshot failed.";
                throw new InvalidOperationException(error);
            }

            return new BinaryToolResult(
                result.PngBytes,
                RenderCaptureResult.PngMimeType,
                "screenshot.png",
                BuildHeaders(result));
        }
        catch (Exception ex) when (ex is not ObjectDisposedException and not InvalidOperationException)
        {
            throw new InvalidOperationException($"Screenshot failed: {ex.Message}", ex);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildHeaders(RenderCaptureResult result)
    {
        var headers = new Dictionary<string, string>();

        if (result.Width.HasValue)
        {
            headers["X-Screenshot-Width"] = result.Width.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (result.Height.HasValue)
        {
            headers["X-Screenshot-Height"] = result.Height.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (result.ByteLength.HasValue)
        {
            headers["X-Screenshot-Byte-Length"] = result.ByteLength.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (result.CapturedAtUtc.HasValue)
        {
            headers["X-Screenshot-Captured-At-Utc"] = result.CapturedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (result.ReadbackTimeMs.HasValue)
        {
            headers["X-Screenshot-Readback-Time-Ms"] = result.ReadbackTimeMs.Value.ToString("F3", CultureInfo.InvariantCulture);
        }

        if (result.EncodeTimeMs.HasValue)
        {
            headers["X-Screenshot-Encode-Time-Ms"] = result.EncodeTimeMs.Value.ToString("F3", CultureInfo.InvariantCulture);
        }

        return headers;
    }
}
