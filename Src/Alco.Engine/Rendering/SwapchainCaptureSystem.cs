using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// Engine-wide frame screenshot service: captures the main view's swapchain surface —
/// the exact pixels about to be presented, <b>including the ImGui overlay</b> — as PNG
/// bytes, delivered through a <see cref="Task{RenderCaptureResult}"/>. This complements
/// <see cref="RenderCaptureSystem"/>, whose render-graph captures stop before the ImGui
/// pass; screenshots meant for debugging editor/ImGui UI must come from here.
/// <br/>Requests are serialized (one capture in flight). A dispatched request arms a
/// one-shot read of the presenter's current surface in the post-ImGui / pre-present
/// window (<see cref="IGPUDeviceHost.OnEndFrame"/>), then the shared
/// <see cref="PngReadbackPipeline"/> performs the asynchronous GPU readback and a
/// thread-pool PNG encode, completing the request a couple of frames later.
/// </summary>
public sealed class SwapchainCaptureSystem : BaseEngineSystem
{
    private readonly GameEngine _engine;
    private readonly PngReadbackPipeline _readback;
    private readonly List<PendingCaptureRequest> _pendingRequests = new();
    private PendingCaptureRequest? _activeRequest;
    private bool _readArmed;

    /// <summary>
    /// Creates the capture system and hooks the post-ImGui / pre-present window.
    /// </summary>
    /// <param name="engine">The owning engine, for the presenter, graphics device and main-thread checks.</param>
    public SwapchainCaptureSystem(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _readback = new PngReadbackPipeline(engine.GraphicsDevice);
        ((IGPUDeviceHost)engine).OnEndFrame += OnEndFrame;
    }

    /// <summary>
    /// Requests a screenshot of the presented frame (ImGui overlay included).
    /// </summary>
    /// <returns>A task completing with the PNG-encoded screenshot a couple of frames later.</returns>
    /// <exception cref="InvalidOperationException">Called off the game main thread.</exception>
    public Task<RenderCaptureResult> RequestCaptureAsync()
    {
        if (!_engine.IsMainThread)
        {
            throw new InvalidOperationException("Swapchain capture requests must be registered on the game main thread.");
        }

        PendingCaptureRequest request = new()
        {
            Completion = new TaskCompletionSource<RenderCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        _pendingRequests.Add(request);
        return request.Completion.Task;
    }

    public override void OnUpdate(float deltaTime)
    {
        RenderCaptureResult? result = _readback.Poll();
        if (result != null)
        {
            CompleteActiveRequest(result);
        }

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

        if (_engine.Setting.Graphics.Backend == GraphicsBackend.None)
        {
            request.Completion.TrySetResult(
                RenderCaptureResult.CreateFailure("Swapchain capture requires a GPU backend.", nameof(GraphicsBackend.None)));
            return;
        }

        // Arm the one-shot surface read: it fires in this frame's post-ImGui /
        // pre-present window, when the swapchain texture holds the final pixels.
        _activeRequest = request;
        _readArmed = true;
    }

    private void OnEndFrame()
    {
        if (!_readArmed)
        {
            return;
        }

        GPUFrameBuffer? frameBuffer = _engine.MainPresenter.FrameBuffer;
        if (frameBuffer == null || frameBuffer.Colors.Length == 0)
        {
            // No surface this frame (e.g. minimized window): stay armed and retry on
            // the next presented frame instead of failing the request.
            return;
        }

        _readArmed = false;
        try
        {
            if (_readback.TryBeginRead(frameBuffer.Colors[0], out RenderCaptureResult? failure))
            {
                return;
            }

            CompleteActiveRequest(failure!);
        }
        catch (Exception ex)
        {
            CompleteActiveRequest(RenderCaptureResult.CreateFailure(
                $"Swapchain capture failed: {ex.Message}",
                ex.GetType().Name));
        }
    }

    private void CompleteActiveRequest(RenderCaptureResult result)
    {
        PendingCaptureRequest? request = _activeRequest;
        _activeRequest = null;
        _readArmed = false;
        request?.Completion.TrySetResult(result);
    }

    public override void Dispose()
    {
        ((IGPUDeviceHost)_engine).OnEndFrame -= OnEndFrame;

        RenderCaptureResult disposedResult = RenderCaptureResult.CreateFailure(
            "Swapchain capture system is disposed.",
            nameof(ObjectDisposedException));

        for (int i = 0; i < _pendingRequests.Count; i++)
        {
            _pendingRequests[i].Completion.TrySetResult(disposedResult);
        }

        _pendingRequests.Clear();
        CompleteActiveRequest(disposedResult);

        _readback.Dispose();
    }

    /// <summary>A queued capture request and how its result is delivered.</summary>
    private sealed class PendingCaptureRequest
    {
        /// <summary>Completed with the capture result (or a failure).</summary>
        public required TaskCompletionSource<RenderCaptureResult> Completion { get; init; }
    }
}
