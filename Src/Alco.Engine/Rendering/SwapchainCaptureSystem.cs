using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// Engine system that captures the main view's presented swapchain surface as PNG bytes and
/// delivers it through a task. In headless mode requests route through <see cref="OffscreenFallback"/>.
/// </summary>
public sealed class SwapchainCaptureSystem : BaseEngineSystem
{
    private readonly GameEngine _engine;
    private readonly PngReadbackPipeline _readback;
    private readonly RenderContext? _renderContext;
    private readonly List<PendingCaptureRequest> _pendingRequests = new();
    private Material? _blitMaterial;
    private PendingCaptureRequest? _activeRequest;
    private Texture? _surfaceCopy;
    private Texture? _staging;
    private bool _readArmed;
    private bool _readStaged;
    private Task<RenderCaptureResult>? _fallbackTask;

    /// <summary>
    /// Gets or sets the fallback used when no swapchain exists; requests fail when null.
    /// </summary>
    public Func<Task<RenderCaptureResult>>? OffscreenFallback { get; set; }

    /// <summary>
    /// Runs after <see cref="RenderCaptureSystem"/> so offscreen captures read back first on contention.
    /// </summary>
    public override int Order => 9500;

    /// <summary>
    /// Creates the capture system and hooks the pre-present presenter window.
    /// </summary>
    /// <param name="engine">The owning engine.</param>
    /// <param name="readback">The shared PNG readback pipeline; pumped by the engine, not disposed here.</param>
    public SwapchainCaptureSystem(GameEngine engine, PngReadbackPipeline readback)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(readback);
        _engine = engine;
        _readback = readback;

        if (engine.Setting.HasGPU)
        {
            _renderContext = new RenderContext(engine.RenderingSystem);
        }

        _engine.MainPresenter.OnBeforePresent += OnBeforePresent;
    }

    /// <summary>
    /// Requests a screenshot of the presented frame.
    /// </summary>
    /// <returns>A task completing with the PNG-encoded screenshot.</returns>
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

    /// <summary>Advances queued captures and starts staged readbacks when the shared pipeline is free.</summary>
    /// <param name="deltaTime">The elapsed time since the previous update, in seconds.</param>
    public override void OnUpdate(float deltaTime)
    {
        CompleteFallbackIfFinished();
        BeginReadIfStaged();
        DispatchPendingRequests();
    }

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

        if (!_engine.Setting.HasGPU)
        {
            request.Completion.TrySetResult(
                RenderCaptureResult.CreateFailure("Swapchain capture requires a GPU backend.", nameof(GraphicsBackend.NoGPU)));
            return;
        }

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

        _activeRequest = request;
        _readArmed = true;
    }

    private void OnBeforePresent()
    {
        if (!_readArmed || !_engine.MainPresenter.HasValidFrame)
        {
            return;
        }

        SwapchainFrame frame = _engine.MainPresenter.CurrentFrame;
        Format surfaceFormat = _engine.MainPresenter.SurfaceFormat;
        if (frame.Texture == null || surfaceFormat == Format.Undefined || _renderContext == null)
        {
            return;
        }

        _readArmed = false;
        Format stagingFormat = SelectStagingFormat(surfaceFormat);
        bool begun = false;
        try
        {
            Material blit = EnsureBlitMaterial();
            EnsureSurfaceCopy(frame.Extent, surfaceFormat);
            EnsureStaging(frame.Extent, stagingFormat);

            blit.SetTexture("Texture", _surfaceCopy);

            _renderContext.BeginFrame();
            begun = true;

            _renderContext.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            _renderContext.CopyTexture(frame.Texture, _surfaceCopy!.NativeTexture);
            _renderContext.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Pixel, Access.ShaderRead);

            RenderingDesc pass = new()
            {
                Colors =
                [
                    new ColorAttachment
                    {
                        RenderView = _staging!.RenderView,
                        Load = LoadOp.Discard,
                        Store = StoreOp.Store,
                    },
                ],
            };

            using (RenderContext.RenderScope render = _renderContext.BeginRenderPass(pass))
            {
                render.SetViewport(new Viewport { Width = frame.Extent.X, Height = frame.Extent.Y });
                render.SetScissor(new Scissor { Width = frame.Extent.X, Height = frame.Extent.Y });
                render.Draw(blit, 3);
            }

            _renderContext.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            _engine.MainPresenter.EnqueueFrameContext(_renderContext);
            _readStaged = true;
        }
        catch (Exception ex)
        {
            if (begun)
            {
                _renderContext.DiscardFrame();
            }

            CompleteActiveRequest(RenderCaptureResult.CreateFailure($"Swapchain capture failed: {ex.Message}", ex.GetType().Name));
        }
    }

    private Material EnsureBlitMaterial()
    {
        if (_blitMaterial == null)
        {
            _blitMaterial = _engine.BuiltInAssets.Shader_BlitOpaque.CreateMaterial("swapchain_capture_blit");
            _blitMaterial.Rasterization = new RasterizationState { Cull = CullMode.None };
        }

        return _blitMaterial;
    }

    private static Format SelectStagingFormat(Format surfaceFormat)
    {
        return surfaceFormat is Format.Bgra8Srgb or Format.Rgba8Srgb or Format.Rgba16Float
            ? Format.Rgba8Srgb
            : Format.Rgba8Unorm;
    }

    private void BeginReadIfStaged()
    {
        if (!_readStaged || _activeRequest == null || _staging == null || _readback.IsBusy)
        {
            return;
        }

        _readStaged = false;
        if (_readback.TryBeginRead(_staging, CompleteActiveRequest, out RenderCaptureResult? failure))
        {
            return;
        }

        CompleteActiveRequest(failure);
    }

    private void EnsureSurfaceCopy(Extent2D extent, Format format)
    {
        if (_surfaceCopy != null &&
            _surfaceCopy.Extent.X == extent.X &&
            _surfaceCopy.Extent.Y == extent.Y &&
            _surfaceCopy.Format == format)
        {
            return;
        }

        if (_surfaceCopy != null)
        {
            _engine.GraphicsDevice.WaitIdle();
            _surfaceCopy.Destroy();
        }

        _surfaceCopy = _engine.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(extent.X, extent.Y, 1),
            Format = format,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
    }

    private void EnsureStaging(Extent2D extent, Format format)
    {
        if (_staging != null &&
            _staging.Extent.X == extent.X &&
            _staging.Extent.Y == extent.Y &&
            _staging.Format == format)
        {
            return;
        }

        if (_staging != null)
        {
            _engine.GraphicsDevice.WaitIdle();
            _staging.Destroy();
        }

        _staging = _engine.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(extent.X, extent.Y, 1),
            Format = format,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
        });
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

    /// <inheritdoc />
    public override void Dispose()
    {
        _engine.MainPresenter.OnBeforePresent -= OnBeforePresent;

        RenderCaptureResult disposedResult = RenderCaptureResult.CreateFailure(
            "Swapchain capture system is disposed.",
            nameof(ObjectDisposedException));

        for (int i = 0; i < _pendingRequests.Count; i++)
        {
            _pendingRequests[i].Completion.TrySetResult(disposedResult);
        }

        _pendingRequests.Clear();
        CompleteActiveRequest(disposedResult);

        if (_renderContext != null || _staging != null || _surfaceCopy != null)
        {
            _engine.GraphicsDevice.WaitIdle();
        }

        _staging?.Destroy();
        _staging = null;
        _surfaceCopy?.Destroy();
        _surfaceCopy = null;
        _renderContext?.Destroy();
    }

    private sealed class PendingCaptureRequest
    {
        public required TaskCompletionSource<RenderCaptureResult> Completion { get; init; }
    }
}
