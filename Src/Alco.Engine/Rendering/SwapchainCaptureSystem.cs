using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Engine-wide frame screenshot service: captures the main view's swapchain surface —
/// the exact pixels about to be presented, <b>including the ImGui overlay</b> — as PNG
/// bytes, delivered through a <see cref="Task{RenderCaptureResult}"/>. This complements
/// <see cref="RenderCaptureSystem"/>, whose render-graph captures stop before the ImGui
/// pass; screenshots meant for debugging editor/ImGui UI must come from here.
/// <br/>In headless/offscreen mode (no swapchain), requests are routed through
/// <see cref="OffscreenFallback"/> — typically the render-graph chain tail, which is
/// the frame that would be presented.
/// <br/>Requests are serialized (one capture in flight). A dispatched request arms a
/// one-shot conversion in the post-ImGui / pre-present window
/// (<see cref="IGPUDeviceHost.OnEndFrame"/>): the surface is blitted into an RGBA8
/// staging texture with a full-screen GPU draw (any surface format is converted by the
/// blit — no CPU-side pixel processing), then the shared <see cref="PngReadbackPipeline"/>
/// performs the asynchronous GPU readback and a thread-pool PNG encode, completing the
/// request a couple of frames later. When another capture system holds the shared
/// pipeline, the staged capture waits and begins its readback on a later update.
/// </summary>
public sealed class SwapchainCaptureSystem : BaseEngineSystem
{
    private readonly GameEngine _engine;
    private readonly PngReadbackPipeline _readback;
    private readonly RenderContext? _renderContext;
    private readonly GraphicsMaterial? _blitMaterial;
    private readonly List<PendingCaptureRequest> _pendingRequests = new();
    private PendingCaptureRequest? _activeRequest;
    private RenderTexture? _staging;
    private bool _readArmed;
    private bool _readStaged;
    private Task<RenderCaptureResult>? _fallbackTask;

    /// <summary>
    /// Services capture requests when the main view has no presenter surface —
    /// headless/offscreen mode, where no swapchain exists to hook. Typically wired to
    /// <see cref="RenderCaptureSystem.RequestCaptureAsync()"/> so the pipeline's chain
    /// tail (the frame that would be presented; no ImGui overlay exists headless)
    /// serves as the screenshot. Requests fail with a clear error when null.
    /// </summary>
    public Func<Task<RenderCaptureResult>>? OffscreenFallback { get; set; }

    /// <summary>
    /// Runs after <see cref="RenderCaptureSystem"/> (order 9000) so a render-graph
    /// capture issued in the same frame reads back first when both contend for the
    /// shared readback pipeline.
    /// </summary>
    public override int Order => 9500;

    /// <summary>
    /// Creates the capture system and hooks the post-ImGui / pre-present window.
    /// </summary>
    /// <param name="engine">The owning engine, for the presenter, graphics device and main-thread checks.</param>
    /// <param name="readback">The shared PNG readback pipeline; pumped by the engine, not disposed here.</param>
    public SwapchainCaptureSystem(GameEngine engine, PngReadbackPipeline readback)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(readback);
        _engine = engine;
        _readback = readback;

        if (engine.Setting.Graphics.Backend != GraphicsBackend.None)
        {
            _renderContext = engine.RenderingSystem.CreateRenderContext("swapchain_capture_context");
            _blitMaterial = engine.RenderingSystem.CreateGraphicsMaterial(engine.BuiltInAssets.Shader_Blit, "swapchain_capture_blit");
        }

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
        // The shared readback pipeline is pumped by the engine; completed readbacks are
        // delivered to the callback registered when the read began.
        CompleteFallbackIfFinished();
        BeginReadIfStaged();
        DispatchPendingRequests();
    }

    /// <summary>
    /// Resolves the active request from the finished offscreen-fallback capture (the
    /// fallback's task completes on the render-graph system's own update, which runs
    /// just before this system in update order).
    /// </summary>
    private void CompleteFallbackIfFinished()
    {
        if (_fallbackTask == null || !_fallbackTask.IsCompleted)
        {
            return;
        }

        Task<RenderCaptureResult> task = _fallbackTask;
        _fallbackTask = null;
        CompleteActiveRequest(task.GetAwaiter().GetResult());
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

        // Headless/offscreen mode: no swapchain to hook, so the OnEndFrame arm below
        // would never fire. Route the request through the offscreen fallback instead
        // (typically the render-graph chain tail).
        if (_engine.MainView.Swapchain == null)
        {
            if (OffscreenFallback == null)
            {
                request.Completion.TrySetResult(RenderCaptureResult.CreateFailure(
                    "No presenter surface (headless mode) and no offscreen fallback configured.",
                    nameof(InvalidOperationException)));
                return;
            }

            _activeRequest = request;
            _fallbackTask = OffscreenFallback();
            return;
        }

        // Arm the one-shot conversion: it fires in this frame's post-ImGui /
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
            EnsureStaging(frameBuffer.Width, frameBuffer.Height);
            BlitSurface(frameBuffer);

            // Another capture system holds the shared readback pipeline: keep the staged
            // pixels (the staging texture persists) and begin the read on a later update.
            if (_readback.IsBusy)
            {
                _readStaged = true;
            }
            else if (_readback.TryBeginRead(_staging!, CompleteActiveRequest, out RenderCaptureResult? failure))
            {
                return;
            }
            else
            {
                CompleteActiveRequest(failure!);
            }
        }
        catch (Exception ex)
        {
            CompleteActiveRequest(RenderCaptureResult.CreateFailure(
                $"Swapchain capture failed: {ex.Message}",
                ex.GetType().Name));
        }
    }

    /// <summary>Begins the staged readback once the shared pipeline is free.</summary>
    private void BeginReadIfStaged()
    {
        if (!_readStaged || _activeRequest == null || _readback.IsBusy)
        {
            return;
        }

        _readStaged = false;
        if (_readback.TryBeginRead(_staging!, CompleteActiveRequest, out RenderCaptureResult? failure))
        {
            return;
        }

        CompleteActiveRequest(failure!);
    }

    /// <summary>Keeps the RGBA8 staging texture at the surface's current size.</summary>
    private void EnsureStaging(uint width, uint height)
    {
        if (_staging == null)
        {
            _staging = _engine.RenderingSystem.CreateRenderTexture(
                _engine.RenderingSystem.PreferredRGBATexturePass, width, height, "swapchain_capture_staging");
        }
        else
        {
            _staging.Resize(width, height);
        }
    }

    /// <summary>
    /// Converts the surface's first color attachment into the RGBA8 staging texture
    /// with a full-screen blit and submits it, ordered before the frame's present.
    /// </summary>
    private void BlitSurface(GPUFrameBuffer frameBuffer)
    {
        Texture2D source = _engine.RenderingSystem.CreateTexture2D(frameBuffer.Colors[0], frameBuffer.ColorViews[0]);
        try
        {
            _blitMaterial!.SetTexture(ShaderResourceId.Texture, source);
            using (RenderFrameScope frame = _renderContext!.BeginFrame())
            {
                using (RenderPassScope pass = _renderContext.BeginPass(_staging!.FrameBuffer))
                {
                    pass.Draw(_engine.RenderingSystem.MeshFullScreen, _blitMaterial);
                }
            }
        }
        finally
        {
            source.Dispose();
        }
    }

    private void CompleteActiveRequest(RenderCaptureResult result)
    {
        PendingCaptureRequest? request = _activeRequest;
        _activeRequest = null;
        _readArmed = false;
        _readStaged = false;
        _fallbackTask = null;
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

        // The readback pipeline is engine-owned and shared; not disposed here.
        _staging?.Dispose();
        _blitMaterial?.Dispose();
        _renderContext?.Dispose();
    }

    /// <summary>A queued capture request and how its result is delivered.</summary>
    private sealed class PendingCaptureRequest
    {
        /// <summary>Completed with the capture result (or a failure).</summary>
        public required TaskCompletionSource<RenderCaptureResult> Completion { get; init; }
    }
}
