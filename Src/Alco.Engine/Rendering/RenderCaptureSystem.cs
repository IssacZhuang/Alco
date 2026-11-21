using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// Engine system that captures an active render-graph target texture as PNG bytes and
/// delivers it asynchronously. Use <see cref="SwapchainCaptureSystem"/> for presented frames.
/// </summary>
public sealed class RenderCaptureSystem : BaseEngineSystem
{
    private readonly GameEngine _engine;
    private readonly PngReadbackPipeline _readback;
    private readonly List<PendingCaptureRequest> _pendingRequests = new();
    private PendingCaptureRequest? _activeRequest;
    private Texture? _stagedTarget;

    /// <summary>
    /// Runs late in the update so frame rendering has staged its capture target.
    /// </summary>
    public override int Order => 9000;

    /// <summary>
    /// Gets or sets the callback supplying the completed RGBA8 offscreen render texture to capture.
    /// Requests fail cleanly when null until a render pipeline sets it.
    /// </summary>
    public Func<Texture?>? ActiveTargetProvider { get; set; }

    /// <summary>
    /// Creates the capture system.
    /// </summary>
    /// <param name="engine">The owning engine, for main-thread checks and graphics settings.</param>
    /// <param name="readback">The shared PNG readback pipeline; pumped by the engine, not disposed here.</param>
    public RenderCaptureSystem(GameEngine engine, PngReadbackPipeline readback)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(readback);
        _engine = engine;
        _readback = readback;
    }

    /// <summary>
    /// Requests a capture of the active render pipeline's output texture.
    /// </summary>
    /// <returns>A task completing with the PNG-encoded capture.</returns>
    /// <exception cref="InvalidOperationException">Called off the game main thread.</exception>
    public Task<RenderCaptureResult> RequestCaptureAsync()
    {
        if (!_engine.IsMainThread)
        {
            throw new InvalidOperationException("Render capture requests must be registered on the game main thread.");
        }

        PendingCaptureRequest request = new()
        {
            Completion = new TaskCompletionSource<RenderCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        _pendingRequests.Add(request);
        return request.Completion.Task;
    }

    /// <summary>Advances pending captures and starts readbacks when the shared pipeline is free.</summary>
    /// <param name="deltaTime">The elapsed time since the previous update, in seconds.</param>
    public override void OnUpdate(float deltaTime)
    {
        BeginReadIfStaged();
        DispatchPendingRequests();
    }

    private void DispatchPendingRequests()
    {
        if (_activeRequest != null || _pendingRequests.Count == 0)
        {
            return;
        }

        PendingCaptureRequest request = _pendingRequests[0];
        _pendingRequests.RemoveAt(0);

        if (!_engine.Setting.HasGPU)
        {
            request.Completion.TrySetResult(
                RenderCaptureResult.CreateFailure("Render capture requires a GPU backend.", nameof(GraphicsBackend.NoGPU)));
            return;
        }

        Texture? target = ActiveTargetProvider?.Invoke();
        if (target == null)
        {
            request.Completion.TrySetResult(
                RenderCaptureResult.CreateFailure("No active render target provider is configured.", nameof(InvalidOperationException)));
            return;
        }

        _activeRequest = request;
        _stagedTarget = target;
        BeginReadIfStaged();
    }

    private void BeginReadIfStaged()
    {
        if (_activeRequest == null || _stagedTarget == null || _readback.IsBusy)
        {
            return;
        }

        Texture target = _stagedTarget;
        _stagedTarget = null;
        if (_readback.TryBeginRead(target, CompleteActiveRequest, out RenderCaptureResult? failure))
        {
            return;
        }

        CompleteActiveRequest(failure);
    }

    private void CompleteActiveRequest(RenderCaptureResult result)
    {
        PendingCaptureRequest? request = _activeRequest;
        _activeRequest = null;
        _stagedTarget = null;
        request?.Completion.TrySetResult(result);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        RenderCaptureResult disposedResult = RenderCaptureResult.CreateFailure(
            "Render capture system is disposed.",
            nameof(ObjectDisposedException));

        for (int i = 0; i < _pendingRequests.Count; i++)
        {
            _pendingRequests[i].Completion.TrySetResult(disposedResult);
        }

        _pendingRequests.Clear();
        CompleteActiveRequest(disposedResult);
    }

    private sealed class PendingCaptureRequest
    {
        public required TaskCompletionSource<RenderCaptureResult> Completion { get; init; }
    }
}

/// <summary>The outcome of a render capture request: PNG bytes on success, an
/// error description on failure.</summary>
public sealed record RenderCaptureResult
{
    /// <summary>The MIME type of <see cref="PngBytes"/>.</summary>
    public const string PngMimeType = "image/png";

    /// <summary>Gets whether the capture succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the PNG-encoded image bytes on success.</summary>
    public byte[]? PngBytes { get; init; }
    /// <summary>Gets the capture width in pixels.</summary>
    public int? Width { get; init; }
    /// <summary>Gets the capture height in pixels.</summary>
    public int? Height { get; init; }
    /// <summary>Gets the number of bytes in the encoded image.</summary>
    public int? ByteLength { get; init; }
    /// <summary>Gets the time the capture was taken in UTC.</summary>
    public DateTimeOffset? CapturedAtUtc { get; init; }
    /// <summary>Gets the GPU readback time in milliseconds.</summary>
    public double? ReadbackTimeMs { get; init; }
    /// <summary>Gets the PNG encode time in milliseconds.</summary>
    public double? EncodeTimeMs { get; init; }
    /// <summary>Gets the failure description.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the failure type name.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Builds a success result.</summary>
    public static RenderCaptureResult CreateSuccess(
        byte[] pngBytes,
        int width,
        int height,
        DateTimeOffset capturedAtUtc,
        double? readbackTimeMs = null,
        double? encodeTimeMs = null)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);

        return new RenderCaptureResult
        {
            Success = true,
            PngBytes = pngBytes,
            Width = width,
            Height = height,
            ByteLength = pngBytes.Length,
            CapturedAtUtc = capturedAtUtc,
            ReadbackTimeMs = readbackTimeMs,
            EncodeTimeMs = encodeTimeMs,
        };
    }

    /// <summary>Builds a failure result.</summary>
    public static RenderCaptureResult CreateFailure(string error, string errorType)
    {
        return new RenderCaptureResult
        {
            Success = false,
            Error = error,
            ErrorType = errorType,
        };
    }
}
