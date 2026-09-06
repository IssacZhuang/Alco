using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Engine-wide screenshot capture service: captures the active render pipeline's
/// output as PNG bytes, delivered through a <see cref="Task{RenderCaptureResult}"/>.
/// <br/>Captures are render-graph captures: a per-pipeline <c>RGNode_Capture</c> node
/// is inserted at a position in the pipeline's graph and copies the content chain's
/// state at that position into a private texture — before the final blit is the
/// complete frame (canvas UI included, ImGui overlay excluded), right after the
/// content node is the raw scene without post processing, and any anchor in between
/// yields the chain as of that point. <see cref="RequestCaptureAsync()"/> captures the
/// chain tail; <see cref="RequestCaptureAsync(Alco.Rendering.IRenderGraphNode, bool)"/>
/// captures at an explicit anchor.
/// <br/>Requests are serialized (one capture in flight) and complete a couple of
/// frames after the request: one frame for the node's blit, then an asynchronous GPU
/// readback and a thread-pool PNG encode. The system runs late in the update
/// (order 9000), before the frame's pipeline execution, so requests armed here take
/// effect in the same frame and readbacks land between two frames' submissions.
/// <br/>The PNG readback is performed on a shared <see cref="PngReadbackPipeline"/>
/// (pumped by the engine); when another capture system holds it, the completed
/// capture waits and begins its readback on a later update.
/// </summary>
public sealed class RenderCaptureSystem : BaseEngineSystem
{
    private readonly GameEngine _engine;
    private readonly PngReadbackPipeline _readback;
    private readonly Dictionary<RenderPipeline, RGNode_Capture> _captureNodes = new();
    private readonly List<PendingCaptureRequest> _pendingRequests = new();
    private PendingCaptureRequest? _activeRequest;
    private RGNode_Capture? _activeNode;

    /// <summary>
    /// Runs late in the update, after the frame's content is staged and before the
    /// pipeline executes, so armed captures take effect in the same frame.
    /// </summary>
    public override int Order => 9000;

    /// <summary>
    /// The pipeline captures are taken from. The owner keeps this current when the
    /// active pipeline switches; requests dispatched while it is null fail with a
    /// "no active render pipeline" result.
    /// </summary>
    public RenderPipeline? ActivePipeline { get; set; }

    /// <summary>
    /// Creates the capture system.
    /// </summary>
    /// <param name="engine">The owning engine, for the graphics device and main-thread checks.</param>
    /// <param name="readback">The shared PNG readback pipeline; pumped by the engine, not disposed here.</param>
    public RenderCaptureSystem(GameEngine engine, PngReadbackPipeline readback)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(readback);
        _engine = engine;
        _readback = readback;
    }

    /// <summary>
    /// Requests a capture of the active pipeline's chain tail (the complete frame:
    /// world, canvas UI and post processing, without the ImGui overlay).
    /// </summary>
    /// <returns>A task completing with the PNG-encoded capture a couple of frames later.</returns>
    /// <exception cref="InvalidOperationException">Called off the game main thread.</exception>
    public Task<RenderCaptureResult> RequestCaptureAsync()
    {
        return RequestCaptureAsyncCore(anchor: null, after: true);
    }

    /// <summary>
    /// Requests a capture at an explicit graph position: the capture node is moved to
    /// sit after (or before) <paramref name="anchor"/>, capturing the content chain as
    /// of that point.
    /// </summary>
    /// <param name="anchor">The graph node to anchor the capture at. Must be registered
    /// in the active pipeline's graph when the request is dispatched.</param>
    /// <param name="after">True to capture after the anchor's work, false to capture
    /// before it.</param>
    /// <returns>A task completing with the PNG-encoded capture a couple of frames later.</returns>
    /// <exception cref="InvalidOperationException">Called off the game main thread.</exception>
    public Task<RenderCaptureResult> RequestCaptureAsync(IRenderGraphNode anchor, bool after = true)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        return RequestCaptureAsyncCore(anchor, after);
    }

    private Task<RenderCaptureResult> RequestCaptureAsyncCore(IRenderGraphNode? anchor, bool after)
    {
        if (!_engine.IsMainThread)
        {
            throw new InvalidOperationException("Render capture requests must be registered on the game main thread.");
        }

        PendingCaptureRequest request = new()
        {
            Completion = new TaskCompletionSource<RenderCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously),
            Anchor = anchor,
            After = after,
        };
        _pendingRequests.Add(request);
        return request.Completion.Task;
    }

    public override void OnUpdate(float deltaTime)
    {
        // The shared readback pipeline is pumped by the engine; completed captures are
        // delivered to the callback registered in StartReadbackIfCaptureCompleted.
        StartReadbackIfCaptureCompleted();
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

        RenderCaptureResult? failure = TryDispatch(request);
        if (failure != null)
        {
            request.Completion.TrySetResult(failure);
            return;
        }

        _activeRequest = request;
    }

    private RenderCaptureResult? TryDispatch(PendingCaptureRequest request)
    {
        if (_engine.Setting.Graphics.Backend == GraphicsBackend.None)
        {
            return RenderCaptureResult.CreateFailure("Render capture requires a GPU backend.", nameof(GraphicsBackend.None));
        }

        RenderPipeline? pipeline = ActivePipeline;
        if (pipeline == null)
        {
            return RenderCaptureResult.CreateFailure("No active render pipeline to capture from.", nameof(InvalidOperationException));
        }

        RGNode_Capture node;
        try
        {
            node = GetOrCreateCaptureNode(pipeline);
            RepositionCaptureNode(pipeline, node, request);
            node.Submit();
        }
        catch (Exception ex)
        {
            return RenderCaptureResult.CreateFailure($"Screenshot capture failed: {ex.Message}", ex.GetType().Name);
        }

        _activeNode = node;
        return null;
    }

    private RGNode_Capture GetOrCreateCaptureNode(RenderPipeline pipeline)
    {
        if (_captureNodes.TryGetValue(pipeline, out RGNode_Capture? node))
        {
            return node;
        }

        node = new RGNode_Capture(_engine.RenderingSystem, pipeline.Graph, pipeline.Chain, pipeline.BlitShader);
        pipeline.Use(node);
        _captureNodes.Add(pipeline, node);
        return node;
    }

    /// <summary>
    /// Places the capture node for this request: after/before the anchor when one is
    /// given, otherwise at the chain tail (before the final blit). The node is always
    /// removed and re-inserted, so a previous request's position never leaks into a
    /// default request. Calls outside a frame; throws when the anchor is not registered
    /// in the pipeline's graph.
    /// </summary>
    private static void RepositionCaptureNode(RenderPipeline pipeline, RGNode_Capture node, PendingCaptureRequest request)
    {
        RenderGraph graph = pipeline.Graph;
        graph.Remove(node);
        if (request.Anchor != null)
        {
            if (request.After)
            {
                graph.InsertAfter(request.Anchor, node);
            }
            else
            {
                graph.InsertBefore(request.Anchor, node);
            }
        }
        else
        {
            graph.InsertBefore(pipeline.FinalBlit, node);
        }
    }

    private void StartReadbackIfCaptureCompleted()
    {
        if (_activeRequest == null || _activeNode == null)
        {
            return;
        }

        // Another capture system holds the shared pipeline: leave the node's completion
        // flag set and retry on a later update — the capture texture persists.
        if (_readback.IsBusy)
        {
            return;
        }

        if (!_activeNode.TryTakeCompleted())
        {
            return;
        }

        if (_readback.TryBeginRead(_activeNode.CaptureTexture, CompleteActiveRequest, out RenderCaptureResult? failure))
        {
            return;
        }

        CompleteActiveRequest(failure!);
    }

    private void CompleteActiveRequest(RenderCaptureResult result)
    {
        PendingCaptureRequest? request = _activeRequest;
        _activeRequest = null;
        _activeNode = null;
        request?.Completion.TrySetResult(result);
    }

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

        // The readback pipeline is engine-owned and shared; not disposed here.
        _captureNodes.Clear();
    }

    /// <summary>A queued capture request and how its result is delivered.</summary>
    private sealed class PendingCaptureRequest
    {
        /// <summary>Completed with the capture result (or a failure).</summary>
        public required TaskCompletionSource<RenderCaptureResult> Completion { get; init; }

        /// <summary>The graph node to capture after/before, or null for the chain tail.</summary>
        public IRenderGraphNode? Anchor { get; init; }

        /// <summary>Whether the capture sits after (true) or before (false) the anchor.</summary>
        public bool After { get; init; }
    }
}

/// <summary>The outcome of a render capture request: PNG bytes on success, an
/// error description on failure.</summary>
public sealed record RenderCaptureResult
{
    /// <summary>The MIME type of <see cref="PngBytes"/>.</summary>
    public const string PngMimeType = "image/png";

    public bool Success { get; init; }
    public byte[]? PngBytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? ByteLength { get; init; }
    public DateTimeOffset? CapturedAtUtc { get; init; }
    public double? ReadbackTimeMs { get; init; }
    public double? EncodeTimeMs { get; init; }
    public string? Error { get; init; }
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
