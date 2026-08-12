using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Post-lighting screen-space reflections inspired by Complementary Unbound's
/// composite reflection path. The node copies the completed HDR scene, traces
/// against the deferred depth buffer, temporally/spatially resolves the result,
/// and composites it before bloom and tone mapping.
/// </summary>
/// <remarks>
/// Register this node after forward scene content and before content processors.
/// It intentionally runs after deferred lighting so a hit samples the real shaded
/// scene color instead of attempting to reconstruct lighting from the G-buffer.
/// </remarks>
public sealed class RGNode_SSR : AutoDisposable, IRenderGraphNode
{
    private struct SsrData
    {
        public Matrix4x4 InvViewProjection;
        public Matrix4x4 ViewProjection;
        public Matrix4x4 PreviousViewProjection;
        public Vector4 CameraPosition;
        public Vector4 PreviousCameraPosition;
        public Vector4 RenderSize;
        public Vector4 Params;
        public Vector4 RayParams;
    }

    private readonly RenderingSystem _rendering;
    private readonly PBRDeferredPipeline _pipeline;
    private readonly RGNode_VoxelGI _voxelGi;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _copyMaterial;
    private readonly Material _traceMaterial;
    private readonly Material _resolveMaterial;
    private readonly Material _compositeMaterial;
    private readonly GraphicsValueBuffer<SsrData> _dataBuffer;
    private readonly GPUAttachmentLayout _historyLayout;

    private RenderTexture _sceneCopy;
    private RenderTexture _reflectionRaw;
    private readonly RenderTexture[] _reflectionHistory = new RenderTexture[2];
    private int _historyReadIndex;
    private bool _historyValid;
    private bool _lastSsrOnly;
    private bool _isEnabled = true;
    private float _traceResolutionScale;
    private uint _frameIndex;
    private Matrix4x4 _previousViewProjection = Matrix4x4.Identity;
    private Vector3 _previousCameraPosition;

    // Graph attachment state (AttachGraph). While attached, _sceneCopy/_reflectionRaw
    // are graph-owned facades of the transient resources below (not disposed here and
    // resized by the graph), and the node executes directly in the graph. The
    // reflection history stays persistent (cross-frame feedback never enters the graph).
    private RenderGraph? _graph;
    private RenderGraphTexture? _sceneCopyResource;
    private RenderGraphTexture? _rawResource;
    private bool _graphAttached;

    // The resource this node composites into, captured during Setup (the post chain
    // tail at this node's position — the scene color target in the usual case).
    private RenderGraphTexture? _input;

    /// <inheritdoc />
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }
            _isEnabled = value;
            _historyValid = false;
        }
    }

    /// <summary>Maximum world-space distance of the post-lighting SSR ray.</summary>
    public float MaxTraceDistance { get; set; } = 200.0f;

    /// <summary>Surfaces at or above this roughness use only the voxel fallback.</summary>
    public float RoughnessCutoff { get; set; } = 0.85f;

    /// <summary>
    /// Screen-space reflection trace resolution relative to the G-buffer.
    /// This is intentionally independent from the voxel GI trace resolution.
    /// </summary>
    public float TraceResolutionScale
    {
        get => _traceResolutionScale;
        set
        {
            ValidateTraceResolutionScale(value);
            if (MathF.Abs(value - _traceResolutionScale) < 0.0001f)
            {
                return;
            }

            _traceResolutionScale = value;
            _historyValid = false;
            if (_graphAttached && _graph != null && _rawResource != null)
            {
                // The raw trace target is a graph transient: recreate it at the new
                // scale and rebind (the facade object identity changes here). The
                // persistent history follows on the next frame's EnsureHistoryTextures.
                _graph.DestroyTransient(_rawResource);
                _rawResource = _graph.CreateTransient(new RenderGraphTextureDescriptor(
                    _rendering.PreferredLightMapPass, resolutionScale: value, name: "ssr_raw"));
                _reflectionRaw = _rawResource.Texture;
                _resolveMaterial.SetRenderTexture("_reflectionRaw", _reflectionRaw);
            }
        }
    }

    /// <summary>Current SSR trace width in pixels.</summary>
    public uint TraceWidth => _reflectionRaw.Width;

    /// <summary>Current SSR trace height in pixels.</summary>
    public uint TraceHeight => _reflectionRaw.Height;

    /// <summary>
    /// Creates the post-lighting SSR node. Shader objects and the pipeline remain
    /// owned by their callers; the node owns its materials and intermediate textures.
    /// </summary>
    public RGNode_SSR(
        RenderingSystem rendering,
        PBRDeferredPipeline pipeline,
        RGNode_VoxelGI voxelGi,
        CameraPerspectiveBuffer camera,
        Shader traceShader,
        Shader resolveShader,
        Shader compositeShader,
        Shader blitShader,
        uint width,
        uint height,
        float traceResolutionScale = 0.5f)
    {
        ValidateTraceResolutionScale(traceResolutionScale);
        _rendering = rendering;
        _pipeline = pipeline;
        _voxelGi = voxelGi;
        _camera = camera;
        _renderContext = rendering.CreateRenderContext("post_lighting_ssr");
        _fullScreenMesh = rendering.MeshFullScreen;
        _copyMaterial = rendering.CreateMaterial(blitShader, "ssr_scene_copy");
        _traceMaterial = rendering.CreateMaterial(traceShader, "ssr_trace");
        _resolveMaterial = rendering.CreateMaterial(resolveShader, "ssr_resolve");
        _compositeMaterial = rendering.CreateMaterial(compositeShader, "ssr_composite");
        _dataBuffer = rendering.CreateGraphicsValueBuffer<SsrData>("ssr_post_data");
        _traceResolutionScale = traceResolutionScale;
        _historyLayout = rendering.GraphicsDevice.CreateAttachmentLayout(
            new AttachmentLayoutDescriptor(
                [
                    new ColorAttachment(PixelFormat.RGBA16Float),
                    new ColorAttachment(PixelFormat.RGBA16Float),
                ],
                null,
                "ssr_history_pass"));

        _sceneCopy = CreateTexture(Math.Max(width, 1), Math.Max(height, 1), "ssr_scene_copy");
        uint traceWidth = TraceDimension(width);
        uint traceHeight = TraceDimension(height);
        _reflectionRaw = CreateTexture(traceWidth, traceHeight, "ssr_raw");
        _reflectionHistory[0] = CreateHistoryTexture(traceWidth, traceHeight, "ssr_history_a");
        _reflectionHistory[1] = CreateHistoryTexture(traceWidth, traceHeight, "ssr_history_b");

        _lastSsrOnly = voxelGi.SsrOnly;
        BindPersistentResources();
    }

    private uint TraceDimension(uint fullDimension)
    {
        return Math.Max((uint)MathF.Ceiling(fullDimension * _traceResolutionScale), 1u);
    }

    private static void ValidateTraceResolutionScale(float scale)
    {
        if (!float.IsFinite(scale) || scale < 0.25f || scale > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale,
                "The SSR trace-resolution scale must be between 0.25 and 1.0.");
        }
    }

    private RenderTexture CreateTexture(uint width, uint height, string name)
    {
        return _rendering.CreateRenderTexture(
            _rendering.PreferredLightMapPass, width, height, name);
    }

    private RenderTexture CreateHistoryTexture(uint width, uint height, string name)
    {
        return _rendering.CreateRenderTexture(_historyLayout, width, height, name);
    }

    private void BindPersistentResources()
    {
        _copyMaterial.SetRenderTexture(ShaderResourceId.Texture, _pipeline.ForwardRenderTexture);

        _traceMaterial.SetBuffer("_ssrData", _dataBuffer);
        _traceMaterial.SetRenderTexture("_sceneColor", _sceneCopy);
        _traceMaterial.SetRenderTexture("_albedo", _pipeline.GBuffer, 0);
        _traceMaterial.SetRenderTexture("_normal", _pipeline.GBuffer, 1);
        _traceMaterial.SetRenderTexture("_mrAO", _pipeline.GBuffer, 2);
        _traceMaterial.SetRenderTextureDepth("_gbufferDepth", _pipeline.GBuffer);

        _resolveMaterial.SetBuffer("_ssrData", _dataBuffer);
        _resolveMaterial.SetRenderTexture("_reflectionRaw", _reflectionRaw);
        _resolveMaterial.SetRenderTexture("_reflectionHistory", _reflectionHistory[0], 0);
        _resolveMaterial.SetRenderTexture("_historyMetadata", _reflectionHistory[0], 1);
        _resolveMaterial.SetRenderTexture("_normal", _pipeline.GBuffer, 1);
        _resolveMaterial.SetRenderTextureDepth("_gbufferDepth", _pipeline.GBuffer);

        _compositeMaterial.SetBuffer("_ssrData", _dataBuffer);
        _compositeMaterial.SetRenderTexture("_sceneColor", _sceneCopy);
        _compositeMaterial.SetRenderTexture("_reflection", _reflectionHistory[1], 0);
        _compositeMaterial.SetRenderTexture("_reflectionMetadata", _reflectionHistory[1], 1);
        _compositeMaterial.SetRenderTexture("_albedo", _pipeline.GBuffer, 0);
        _compositeMaterial.SetRenderTexture("_normal", _pipeline.GBuffer, 1);
        _compositeMaterial.SetRenderTexture("_mrAO", _pipeline.GBuffer, 2);
        _compositeMaterial.SetRenderTextureDepth("_gbufferDepth", _pipeline.GBuffer);
    }

    /// <summary>
    /// Attaches the node to the pipeline's render graph as a direct
    /// <see cref="IRenderGraphNode"/> registered immediately before the pipeline's
    /// final blit: creates the scene-copy (graph-relative ×1.0) and raw-trace
    /// (×<see cref="TraceResolutionScale"/>) transient resources and rebinds the
    /// materials once — the constructor-created standalone textures are released.
    /// The temporal reflection history stays persistent (cross-frame feedback never
    /// enters the graph). After attachment the graph drives execution and resize.
    /// </summary>
    /// <exception cref="InvalidOperationException">The node is already attached.</exception>
    public void Attach()
    {
        if (_graphAttached)
        {
            throw new InvalidOperationException("The SSR renderer is already attached to a pipeline (call Detach first).");
        }
        RenderGraph graph = _pipeline.Graph;
        _graph = graph;
        _graphAttached = true;
        _sceneCopy.Dispose();
        _reflectionRaw.Dispose();
        _sceneCopyResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, name: "ssr_scene_copy"));
        _rawResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, resolutionScale: _traceResolutionScale, name: "ssr_raw"));
        _sceneCopy = _sceneCopyResource.Texture;
        _reflectionRaw = _rawResource.Texture;
        _traceMaterial.SetRenderTexture("_sceneColor", _sceneCopy);
        _compositeMaterial.SetRenderTexture("_sceneColor", _sceneCopy);
        _resolveMaterial.SetRenderTexture("_reflectionRaw", _reflectionRaw);
        graph.InsertBefore(_pipeline.FinalBlit, this);
    }

    /// <summary>
    /// Detaches the node from the pipeline: unregisters it from the graph and
    /// destroys its private transient resources. A later <see cref="Attach"/>
    /// recreates and re-registers them.
    /// </summary>
    public void Detach()
    {
        if (!_graphAttached || _graph == null)
        {
            return;
        }
        _graph.Remove(this);
        if (_sceneCopyResource != null)
        {
            _graph.DestroyTransient(_sceneCopyResource);
            _sceneCopyResource = null;
        }
        if (_rawResource != null)
        {
            _graph.DestroyTransient(_rawResource);
            _rawResource = null;
        }
        _graphAttached = false;
    }

    private void EnsureTextures(uint width, uint height)
    {
        if (_graphAttached)
        {
            // Attached: the graph owns and resizes the scene copy and the raw trace
            // target; only the persistent history (sized off the trace facade) is
            // maintained here.
            EnsureHistoryTextures();
            return;
        }

        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        uint traceWidth = TraceDimension(width);
        uint traceHeight = TraceDimension(height);
        if (_sceneCopy.Width == width && _sceneCopy.Height == height
            && _reflectionRaw.Width == traceWidth && _reflectionRaw.Height == traceHeight)
        {
            return;
        }

        _sceneCopy.Dispose();
        _reflectionRaw.Dispose();
        _reflectionHistory[0].Dispose();
        _reflectionHistory[1].Dispose();
        _sceneCopy = CreateTexture(width, height, "ssr_scene_copy");
        _reflectionRaw = CreateTexture(traceWidth, traceHeight, "ssr_raw");
        _reflectionHistory[0] = CreateHistoryTexture(traceWidth, traceHeight, "ssr_history_a");
        _reflectionHistory[1] = CreateHistoryTexture(traceWidth, traceHeight, "ssr_history_b");
        _historyReadIndex = 0;
        _historyValid = false;
        BindPersistentResources();
    }

    // Recreates the persistent temporal history when the trace resolution changed
    // (graph viewport resize or a TraceResolutionScale change). The per-frame
    // history bindings in Render pick up the new textures before use.
    private void EnsureHistoryTextures()
    {
        uint traceWidth = _reflectionRaw.Width;
        uint traceHeight = _reflectionRaw.Height;
        if (_reflectionHistory[0].Width == traceWidth && _reflectionHistory[0].Height == traceHeight)
        {
            return;
        }
        _reflectionHistory[0].Dispose();
        _reflectionHistory[1].Dispose();
        _reflectionHistory[0] = CreateHistoryTexture(traceWidth, traceHeight, "ssr_history_a");
        _reflectionHistory[1] = CreateHistoryTexture(traceWidth, traceHeight, "ssr_history_b");
        _historyReadIndex = 0;
        _historyValid = false;
    }

    /// Renders the pass into <paramref name="target"/>: preserves the completed
    /// scene color, ray-traces reflections, resolves temporal history, and
    /// composites the result in place.
    private void Render(GPUFrameBuffer target)
    {
        RenderTexture scene = _pipeline.ForwardRenderTexture;
        EnsureTextures(scene.Width, scene.Height);

        bool ssrOnly = _voxelGi.SsrOnly;
        if (ssrOnly != _lastSsrOnly)
        {
            _historyValid = false;
            _lastSsrOnly = ssrOnly;
        }

        Matrix4x4 viewProjection = _camera.Data.ViewProjectionMatrix;
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 invViewProjection))
        {
            invViewProjection = Matrix4x4.Identity;
        }

        SsrData data = new()
        {
            InvViewProjection = invViewProjection,
            ViewProjection = viewProjection,
            PreviousViewProjection = _historyValid ? _previousViewProjection : viewProjection,
            CameraPosition = new Vector4(_camera.Transform.Position, 1.0f),
            PreviousCameraPosition = new Vector4(
                _historyValid ? _previousCameraPosition : _camera.Transform.Position,
                1.0f),
            RenderSize = new Vector4(scene.Width, scene.Height,
                _reflectionRaw.Width, _reflectionRaw.Height),
            Params = new Vector4(_frameIndex, _historyValid ? 1.0f : 0.0f,
                (int)_voxelGi.DebugView, _pipeline.GiSpecularStrength),
            RayParams = new Vector4(MaxTraceDistance, RoughnessCutoff, 0.0f, 0.0f),
        };
        _dataBuffer.UpdateBuffer(data);

        // Preserve the completed HDR scene before overwriting the pipeline target.
        _copyMaterial.SetRenderTexture(ShaderResourceId.Texture, scene);
        _renderContext.Begin(_sceneCopy.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _copyMaterial);
        _renderContext.End();

        _renderContext.Begin(_reflectionRaw.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _traceMaterial);
        _renderContext.End();

        int historyWriteIndex = 1 - _historyReadIndex;
        _resolveMaterial.SetRenderTexture(
            "_reflectionHistory", _reflectionHistory[_historyReadIndex], 0);
        _resolveMaterial.SetRenderTexture(
            "_historyMetadata", _reflectionHistory[_historyReadIndex], 1);
        _renderContext.Begin(_reflectionHistory[historyWriteIndex].FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _resolveMaterial);
        _renderContext.End();

        _compositeMaterial.SetRenderTexture(
            "_reflection", _reflectionHistory[historyWriteIndex], 0);
        _compositeMaterial.SetRenderTexture(
            "_reflectionMetadata", _reflectionHistory[historyWriteIndex], 1);
        _renderContext.Begin(target);
        _renderContext.Draw(_fullScreenMesh, _compositeMaterial);
        _renderContext.End();

        _historyReadIndex = historyWriteIndex;
        _historyValid = true;
        _previousViewProjection = viewProjection;
        _previousCameraPosition = _camera.Transform.Position;
        _frameIndex++;
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        EnsureTextures(width, height);
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        if (!_graphAttached || _sceneCopyResource == null || _rawResource == null)
        {
            return;
        }
        _input = _pipeline.PostChain.Current!;
        builder.Read(_pipeline.GBufferResource);
        if (ReferenceEquals(_input, _pipeline.SceneColorResource))
        {
            // The usual case: the copy source and the composite target are both the
            // scene color target.
            builder.ReadWrite(_input);
        }
        else
        {
            builder.Read(_pipeline.SceneColorResource);
            builder.ReadWrite(_input);
        }
        builder.Write(_sceneCopyResource);
        builder.Write(_rawResource);
        if (!_graph!.HasDestinationThisFrame)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        if (!_graphAttached)
        {
            throw new InvalidOperationException("RGNode_SSR is not attached to a pipeline (call Attach first).");
        }
        Render(_input!.Texture.FrameBuffer);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _copyMaterial.Dispose();
            _traceMaterial.Dispose();
            _resolveMaterial.Dispose();
            _compositeMaterial.Dispose();
            _dataBuffer.Dispose();
            if (!_graphAttached)
            {
                // Attached textures are graph-owned facades, disposed with the graph.
                _sceneCopy.Dispose();
                _reflectionRaw.Dispose();
            }
            _reflectionHistory[0].Dispose();
            _reflectionHistory[1].Dispose();
            _historyLayout.Dispose();
            _renderContext.Dispose();
        }
    }
}
