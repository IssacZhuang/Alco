using System.Numerics;
using Alco.Graphics;

using Alco.Rendering;

namespace Alco.World3D;

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
    // GPU timestamp slots: two per pipeline stage (begin + end).
    private const int CopyQueryBase = 0;
    private const int TraceQueryBase = 2;
    private const int ResolveQueryBase = 4;
    private const int CompositeQueryBase = 6;
    private const int TimestampSlotCount = 8;

    // Must match SSR_BLUE_NOISE_SIZE in SSRBlueNoise.slang.
    private const uint BlueNoiseTextureSize = 128;

    private readonly RenderingSystem _rendering;
    private readonly RenderGraph _graph;
    private readonly RenderChain _chain;
    private readonly RenderGraphTexture _gbuffer;
    private readonly RenderGraphTexture _sceneColor;
    private readonly PBRSceneEnvironment _environment;
    private readonly RGNode_VoxelGI _voxelGi;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly Mesh _fullScreenMesh;
    private readonly GraphicsMaterial _copyMaterial;
    private readonly GraphicsMaterial _traceMaterial;
    private readonly GraphicsMaterial _resolveMaterial;
    private readonly GraphicsMaterial _compositeMaterial;
    private readonly GraphicsMaterial _blueNoiseMaterial;
    private readonly GPUAttachmentLayout _blueNoiseLayout;
    private readonly RenderTexture _blueNoiseTexture;
    private bool _blueNoiseBaked;
    private readonly UniformGraphicsBuffer _dataBuffer;
    private readonly GPUAttachmentLayout _historyLayout;

    // Facades of the graph-owned transients below; null until Attach creates them.
    private RenderTexture? _sceneCopy;
    private RenderTexture? _reflectionRaw;
    private readonly RenderTexture[] _reflectionHistory = new RenderTexture[2];
    private int _historyReadIndex;
    private bool _historyValid;
    private bool _lastSsrOnly;
    private bool _isEnabled = true;
    private float _traceResolutionScale;
    private uint _frameIndex;
    private Matrix4x4 _previousViewProjection = Matrix4x4.Identity;
    private Vector3 _previousCameraPosition;

    // Graph attachment state (Attach). The node must be attached before it can
    // execute: _sceneCopy/_reflectionRaw are graph-owned facades of the transient
    // resources below (not disposed here and resized by the graph). The reflection
    // history stays persistent (cross-frame feedback never enters the graph).
    private RenderGraphTexture? _sceneCopyResource;
    private RenderGraphTexture? _rawResource;
    private bool _graphAttached;

    // The resource this node composites into, captured during Setup (the post chain
    // tail at this node's position — the scene color target in the usual case).
    private RenderGraphTexture? _input;

    // Per-stage GPU timing (throttled sampler, 8 slots) and the profiler counters
    // lazily registered on the first Execute. Cached GPU durations are re-pushed
    // every frame because the profiler's BeginFrame clears its buffers.
    private readonly GpuTimestampSampler? _gpuTimestamps;
    private RenderProfileCounterId _copyGpuCounter;
    private RenderProfileCounterId _traceGpuCounter;
    private RenderProfileCounterId _resolveGpuCounter;
    private RenderProfileCounterId _compositeGpuCounter;
    private bool _profilerCountersRegistered;
    private double _copyGpuMilliseconds;
    private double _traceGpuMilliseconds;
    private double _resolveGpuMilliseconds;
    private double _compositeGpuMilliseconds;

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
            if (_graphAttached && _rawResource != null)
            {
                // The raw trace target is a graph transient: recreate it at the new
                // scale and rebind (the facade object identity changes here). The
                // persistent history follows on the next frame's EnsureHistoryTextures.
                _graph.DestroyTransient(_rawResource);
                _rawResource = _graph.CreateTransient(new RenderGraphTextureDescriptor(
                    _rendering.PreferredLightMapPass, resolutionScale: value, name: "ssr_raw"));
                _reflectionRaw = _rawResource.Texture;
                _resolveMaterial.SetRenderTexture("reflectionRaw", _reflectionRaw);
            }
        }
    }

    /// <summary>Current SSR trace width in pixels.</summary>
    /// <exception cref="InvalidOperationException">The node is not attached to a graph.</exception>
    public uint TraceWidth => (_reflectionRaw
        ?? throw new InvalidOperationException("The SSR node is not attached to a graph (call Attach first).")).Width;

    /// <summary>Current SSR trace height in pixels.</summary>
    /// <exception cref="InvalidOperationException">The node is not attached to a graph.</exception>
    public uint TraceHeight => (_reflectionRaw
        ?? throw new InvalidOperationException("The SSR node is not attached to a graph (call Attach first).")).Height;

    /// <summary>
    /// Creates the post-lighting SSR node. Shader objects and the graph resources
    /// remain owned by their callers; the node owns its materials and the persistent
    /// reflection history. The scene copy and raw trace target are graph transients
    /// created by <see cref="Attach"/>; <paramref name="width"/>/<paramref name="height"/>
    /// only size the initial history.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="graph">The render graph the node will attach to.</param>
    /// <param name="chain">The content chain whose tail the node composites into.</param>
    /// <param name="gbuffer">The G-buffer resource the trace/resolve passes read.</param>
    /// <param name="sceneColor">The scene color resource the node copies and
    /// composites into.</param>
    /// <param name="voxelGi">The voxel GI renderer providing the fallback reflection
    /// and the debug view mode.</param>
    /// <param name="camera">The camera the reflection tracing runs from.</param>
    /// <summary>
    /// The node's construction data: the five shaders (trace / resolve /
    /// composite / scene-copy blit / blue-noise bake) and the trace-resolution
    /// scale. Service-type dependencies (the rendering system, graph, chain, the
    /// G-buffer and scene color resources, the voxel GI fallback node, camera
    /// and scene environment) are explicit constructor parameters or factory
    /// context services instead — a descriptor is pure data.
    /// </summary>
    public readonly struct Descriptor
    {
        /// <summary>The SSR trace shader.</summary>
        public required Shader TraceShader { get; init; }
        /// <summary>The temporal/spatial resolve shader.</summary>
        public required Shader ResolveShader { get; init; }
        /// <summary>The composite shader.</summary>
        public required Shader CompositeShader { get; init; }
        /// <summary>The plain blit shader (scene copy).</summary>
        public required Shader BlitShader { get; init; }
        /// <summary>
        /// The blue-noise bake shader filling the trace pass's stochastic-sample
        /// lookup once at runtime.
        /// </summary>
        public required Shader BlueNoiseShader { get; init; }

        /// <summary>The trace resolution relative to the viewport (0.25 - 1.0).</summary>
        public float TraceResolutionScale { get; init; } = 0.5f;

        /// <summary>Required so the property initializers run (C# struct rule).</summary>
        public Descriptor() { }
    }

    /// <summary>
    /// Creates the SSR renderer from its descriptor's shaders. The persistent
    /// temporal history is allocated up front; the scene copy and raw trace
    /// target are graph transients created by <see cref="Attach"/>.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="graph">The render graph driving the frame.</param>
    /// <param name="chain">The post chain the composition resets per frame.</param>
    /// <param name="gbuffer">The G-buffer resource read by the trace and composite.</param>
    /// <param name="sceneColor">The HDR scene color resource copied before the trace.</param>
    /// <param name="voxelGi">The voxel GI node providing the off-screen reflection fallback.</param>
    /// <param name="camera">The camera buffer providing view parameters.</param>
    /// <param name="environment">The shared scene environment (specular GI strength).</param>
    /// <param name="width">The initial viewport width in pixels.</param>
    /// <param name="height">The initial viewport height in pixels.</param>
    /// <param name="descriptor">The node's construction data.</param>
    public RGNode_SSR(
        RenderingSystem rendering,
        RenderGraph graph,
        RenderChain chain,
        RenderGraphTexture gbuffer,
        RenderGraphTexture sceneColor,
        RGNode_VoxelGI voxelGi,
        CameraPerspectiveBuffer camera,
        PBRSceneEnvironment environment,
        uint width,
        uint height,
        in Descriptor descriptor)
    {
        ValidateTraceResolutionScale(descriptor.TraceResolutionScale);
        ArgumentNullException.ThrowIfNull(descriptor.BlueNoiseShader);
        _rendering = rendering;
        _graph = graph;
        _chain = chain;
        _gbuffer = gbuffer;
        _sceneColor = sceneColor;
        _voxelGi = voxelGi;
        _camera = camera;
        _environment = environment;
        _fullScreenMesh = rendering.MeshFullScreen;
        _copyMaterial = rendering.CreateGraphicsMaterial(descriptor.BlitShader, "ssr_scene_copy");
        _traceMaterial = rendering.CreateGraphicsMaterial(descriptor.TraceShader, "ssr_trace");
        _resolveMaterial = rendering.CreateGraphicsMaterial(descriptor.ResolveShader, "ssr_resolve");
        _compositeMaterial = rendering.CreateGraphicsMaterial(descriptor.CompositeShader, "ssr_composite");
        _blueNoiseMaterial = rendering.CreateGraphicsMaterial(descriptor.BlueNoiseShader, "ssr_blue_noise_bake");
        // Reflection-driven uniform buffer over the shared _ssrData block — no
        // CPU twin of SsrData; members land by name at their reflected offsets.
        _dataBuffer = rendering.CreateUniformGraphicsBuffer(
            descriptor.TraceShader.GetShaderModules().ReflectionInfo.UniformBlocks.First(block => block.Name == "ssrData"),
            "ssr_post_data");
        _traceResolutionScale = descriptor.TraceResolutionScale;
        _historyLayout = rendering.GraphicsDevice.CreateAttachmentLayout(
            new AttachmentLayoutDescriptor(
                [
                    new ColorAttachment(PixelFormat.RGBA16Float),
                    new ColorAttachment(PixelFormat.RGBA16Float),
                ],
                null,
                "ssr_history_pass"));

        // Persistent stochastic-sample lookup: a 128x128 tile baked once on the
        // first rendered frame and reused afterwards (its size matches the
        // scrambling table embedded in the bake shader).
        _blueNoiseLayout = rendering.GraphicsDevice.CreateAttachmentLayout(
            new AttachmentLayoutDescriptor(
                [new ColorAttachment(PixelFormat.RGBA8Unorm)],
                null,
                "ssr_blue_noise_pass"));
        _blueNoiseTexture = rendering.CreateRenderTexture(
            _blueNoiseLayout, BlueNoiseTextureSize, BlueNoiseTextureSize, "ssr_blue_noise");

        // The persistent cross-frame history is created up front; the scene copy
        // and the raw trace target are graph transients created by Attach.
        uint traceWidth = TraceDimension(width);
        uint traceHeight = TraceDimension(height);
        _reflectionHistory[0] = CreateHistoryTexture(traceWidth, traceHeight, "ssr_history_a");
        _reflectionHistory[1] = CreateHistoryTexture(traceWidth, traceHeight, "ssr_history_b");

        _lastSsrOnly = voxelGi.SsrOnly;
        BindPersistentResources();

        if (rendering.GraphicsDevice.IsFeatureSupported(GPUFeatures.TimestampQuery))
        {
            _gpuTimestamps = new GpuTimestampSampler(rendering.GraphicsDevice, TimestampSlotCount, "ssr");
        }
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

    private RenderTexture CreateHistoryTexture(uint width, uint height, string name)
    {
        return _rendering.CreateRenderTexture(_historyLayout, width, height, name);
    }

    private void BindPersistentResources()
    {
        RenderTexture sceneColorTexture = _sceneColor.Texture;
        RenderTexture gbufferTexture = _gbuffer.Texture;
        _copyMaterial.SetRenderTexture(ShaderResourceId.Texture, sceneColorTexture);

        _traceMaterial.SetBuffer("ssrData", _dataBuffer);
        _traceMaterial.SetRenderTexture("albedo", gbufferTexture, 0);
        _traceMaterial.SetRenderTexture("normal", gbufferTexture, 1);
        _traceMaterial.SetRenderTexture("mrAO", gbufferTexture, 2);
        _traceMaterial.SetRenderTextureDepth("gbufferDepth", gbufferTexture);
        _traceMaterial.SetRenderTexture("blueNoise", _blueNoiseTexture);

        _resolveMaterial.SetBuffer("ssrData", _dataBuffer);
        _resolveMaterial.SetRenderTexture("reflectionHistory", _reflectionHistory[0], 0);
        _resolveMaterial.SetRenderTexture("historyMetadata", _reflectionHistory[0], 1);
        _resolveMaterial.SetRenderTexture("normal", gbufferTexture, 1);
        _resolveMaterial.SetRenderTextureDepth("gbufferDepth", gbufferTexture);

        _compositeMaterial.SetBuffer("ssrData", _dataBuffer);
        _compositeMaterial.SetRenderTexture("reflection", _reflectionHistory[1], 0);
        _compositeMaterial.SetRenderTexture("reflectionMetadata", _reflectionHistory[1], 1);
        _compositeMaterial.SetRenderTexture("albedo", gbufferTexture, 0);
        _compositeMaterial.SetRenderTexture("normal", gbufferTexture, 1);
        _compositeMaterial.SetRenderTexture("mrAO", gbufferTexture, 2);
        _compositeMaterial.SetRenderTextureDepth("gbufferDepth", gbufferTexture);
    }

    /// <summary>
    /// Attaches the node to the render graph as a direct <see cref="IRenderGraphNode"/>
    /// registered immediately before <paramref name="insertBefore"/> (usually the
    /// composition's final blit): creates the scene-copy (graph-relative ×1.0) and
    /// raw-trace (×<see cref="TraceResolutionScale"/>) transient resources and binds
    /// the materials to them. The temporal reflection history stays persistent
    /// (cross-frame feedback never enters the graph). After attachment the graph
    /// drives execution and resize.
    /// </summary>
    /// <param name="insertBefore">The registered node before which this node runs.</param>
    /// <exception cref="InvalidOperationException">The node is already attached.</exception>
    public void Attach(IRenderGraphNode insertBefore)
    {
        ArgumentNullException.ThrowIfNull(insertBefore);
        if (_graphAttached)
        {
            throw new InvalidOperationException("The SSR renderer is already attached to a graph (call Detach first).");
        }
        _graphAttached = true;
        _sceneCopyResource = _graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, name: "ssr_scene_copy"));
        _rawResource = _graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, resolutionScale: _traceResolutionScale, name: "ssr_raw"));
        _sceneCopy = _sceneCopyResource.Texture;
        _reflectionRaw = _rawResource.Texture;
        _traceMaterial.SetRenderTexture("sceneColor", _sceneCopy);
        _compositeMaterial.SetRenderTexture("sceneColor", _sceneCopy);
        _resolveMaterial.SetRenderTexture("reflectionRaw", _reflectionRaw);
        _graph.InsertBefore(insertBefore, this);
    }

    /// <summary>
    /// Detaches the node from the graph: unregisters it and destroys its private
    /// transient resources. A later <see cref="Attach"/> recreates and re-registers
    /// them.
    /// </summary>
    public void Detach()
    {
        if (!_graphAttached)
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
        _sceneCopy = null;
        _reflectionRaw = null;
        _graphAttached = false;
    }

    // Recreates the persistent temporal history when the trace resolution changed
    // (graph viewport resize or a TraceResolutionScale change). The per-frame
    // history bindings in Render pick up the new textures before use. The scene
    // copy and the raw trace target are graph transients resized by the graph.
    private void EnsureHistoryTextures()
    {
        uint traceWidth = _reflectionRaw!.Width;
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
    /// composites the result in place. Each stage's pass carries its own GPU
    /// timestamp pair when <paramref name="measureGpu"/> is set.
    private void Render(RenderContext renderContext, GPUFrameBuffer target, bool measureGpu)
    {
        RenderTexture scene = _sceneColor.Texture;
        RenderTexture sceneCopy = _sceneCopy!;
        RenderTexture reflectionRaw = _reflectionRaw!;
        EnsureHistoryTextures();

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

        _dataBuffer.SetValue("ssrInvViewProjection", invViewProjection);
        _dataBuffer.SetValue("ssrViewProjection", viewProjection);
        _dataBuffer.SetValue("ssrPreviousViewProjection", _historyValid ? _previousViewProjection : viewProjection);
        _dataBuffer.SetValue("ssrCameraPosition", new Vector4(_camera.Transform.Position, 1.0f));
        _dataBuffer.SetValue("ssrPreviousCameraPosition", new Vector4(
            _historyValid ? _previousCameraPosition : _camera.Transform.Position,
            1.0f));
        _dataBuffer.SetValue("fullWidth", scene.Width);
        _dataBuffer.SetValue("fullHeight", scene.Height);
        _dataBuffer.SetValue("traceWidth", reflectionRaw.Width);
        _dataBuffer.SetValue("traceHeight", reflectionRaw.Height);
        _dataBuffer.SetValue("frameIndex", _frameIndex);
        _dataBuffer.SetValue("historyValid", _historyValid);
        _dataBuffer.SetValue("debugMode", (int)_voxelGi.DebugView);
        _dataBuffer.SetValue("strength", _environment.GiSpecularStrength);
        _dataBuffer.SetValue("maxTraceDistance", MaxTraceDistance);
        _dataBuffer.SetValue("roughnessCutoff", RoughnessCutoff);
        _dataBuffer.Flush();

        GPUTimestampQuerySet? querySet = measureGpu ? _gpuTimestamps!.QuerySet : null;

        // Bake the blue-noise lookup once (the scrambling-table bake is a
        // one-time cost); every frame afterwards samples the persistent tile.
        // Bake the blue-noise lookup once (procedural neighborhood-rank
        // construction, see SSRBlueNoise.slang); every frame
        // afterwards samples the persistent tile.
        if (!_blueNoiseBaked)
        {
            using RenderPassScope pass = renderContext.BeginPass(_blueNoiseTexture.FrameBuffer);
            {
                pass.Draw(_fullScreenMesh, _blueNoiseMaterial);
            }
            _blueNoiseBaked = true;
        }

        // Preserve the completed HDR scene before overwriting the pipeline target.
        _copyMaterial.SetRenderTexture(ShaderResourceId.Texture, scene);
        using (RenderPassScope pass = querySet != null
            ? renderContext.BeginPass(sceneCopy.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty, querySet, CopyQueryBase, CopyQueryBase + 1)
            : renderContext.BeginPass(sceneCopy.FrameBuffer))
        {
            pass.Draw(_fullScreenMesh, _copyMaterial);
        }

        using (RenderPassScope pass = querySet != null
            ? renderContext.BeginPass(reflectionRaw.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty, querySet, TraceQueryBase, TraceQueryBase + 1)
            : renderContext.BeginPass(reflectionRaw.FrameBuffer))
        {
            pass.Draw(_fullScreenMesh, _traceMaterial);
        }

        int historyWriteIndex = 1 - _historyReadIndex;
        _resolveMaterial.SetRenderTexture(
            "reflectionHistory", _reflectionHistory[_historyReadIndex], 0);
        _resolveMaterial.SetRenderTexture(
            "historyMetadata", _reflectionHistory[_historyReadIndex], 1);
        using (RenderPassScope pass = querySet != null
            ? renderContext.BeginPass(_reflectionHistory[historyWriteIndex].FrameBuffer, ReadOnlySpan<ClearColorData>.Empty, querySet, ResolveQueryBase, ResolveQueryBase + 1)
            : renderContext.BeginPass(_reflectionHistory[historyWriteIndex].FrameBuffer))
        {
            pass.Draw(_fullScreenMesh, _resolveMaterial);
        }

        _compositeMaterial.SetRenderTexture(
            "reflection", _reflectionHistory[historyWriteIndex], 0);
        _compositeMaterial.SetRenderTexture(
            "reflectionMetadata", _reflectionHistory[historyWriteIndex], 1);
        using (RenderPassScope pass = querySet != null
            ? renderContext.BeginPass(target, ReadOnlySpan<ClearColorData>.Empty, querySet, CompositeQueryBase, CompositeQueryBase + 1)
            : renderContext.BeginPass(target))
        {
            pass.Draw(_fullScreenMesh, _compositeMaterial);
            if (querySet != null)
            {
                // Resolve the whole slot range once the final pass closes.
                pass.ResolveTimestampsOnEnd(querySet, 0, TimestampSlotCount, _gpuTimestamps!.ResolveBuffer);
            }
        }

        _historyReadIndex = historyWriteIndex;
        _historyValid = true;
        _previousViewProjection = viewProjection;
        _previousCameraPosition = _camera.Transform.Position;
        _frameIndex++;
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        // The graph owns and resizes the scene copy and the raw trace target;
        // only the persistent history (sized off the trace facade) follows here.
        if (_graphAttached)
        {
            EnsureHistoryTextures();
        }
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        if (!_graphAttached || _sceneCopyResource == null || _rawResource == null)
        {
            return;
        }
        _input = _chain.Current!;
        builder.Read(_gbuffer);
        if (ReferenceEquals(_input, _sceneColor))
        {
            // The usual case: the copy source and the composite target are both the
            // scene color target.
            builder.ReadWrite(_input);
        }
        else
        {
            builder.Read(_sceneColor);
            builder.ReadWrite(_input);
        }
        builder.Write(_sceneCopyResource);
        builder.Write(_rawResource);
        if (!_graph.HasDestinationThisFrame)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        if (!_graphAttached)
        {
            throw new InvalidOperationException("RGNode_SSR is not attached to a render graph (call Attach first).");
        }

        bool measureGpu = _gpuTimestamps != null && _gpuTimestamps.ShouldRecord;
        Render(context.RenderContext, _input!.Texture.ColorFrameBuffer, measureGpu);

        // Lazily register the GPU counters; the cached GPU durations are re-pushed
        // every frame (BeginFrame cleared the buffers). The readback below is
        // synchronous but reads the previous sample — the recorded resolves have
        // not executed yet (submission happens at frame end).
        RenderProfiler? profiler = _graph.Profiler;
        if (profiler != null && !_profilerCountersRegistered)
        {
            if (_gpuTimestamps != null)
            {
                _copyGpuCounter = profiler.RegisterCounter("SSR", "Copy (GPU)");
                _traceGpuCounter = profiler.RegisterCounter("SSR", "Trace (GPU)");
                _resolveGpuCounter = profiler.RegisterCounter("SSR", "Resolve (GPU)");
                _compositeGpuCounter = profiler.RegisterCounter("SSR", "Composite (GPU)");
            }
            _profilerCountersRegistered = true;
        }

        if (measureGpu)
        {
            ulong[]? timestamps = _gpuTimestamps!.TryReadback();
            if (timestamps != null)
            {
                _copyGpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, CopyQueryBase, CopyQueryBase + 1);
                _traceGpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, TraceQueryBase, TraceQueryBase + 1);
                _resolveGpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, ResolveQueryBase, ResolveQueryBase + 1);
                _compositeGpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, CompositeQueryBase, CompositeQueryBase + 1);
            }
            _gpuTimestamps.EndSample();
        }

        if (profiler != null && _gpuTimestamps != null)
        {
            profiler.PushValue(_copyGpuCounter, _copyGpuMilliseconds);
            profiler.PushValue(_traceGpuCounter, _traceGpuMilliseconds);
            profiler.PushValue(_resolveGpuCounter, _resolveGpuMilliseconds);
            profiler.PushValue(_compositeGpuCounter, _compositeGpuMilliseconds);
        }
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
            _blueNoiseMaterial.Dispose();
            _dataBuffer.Dispose();
            // The scene copy and raw trace target are graph-owned facades,
            // disposed with the graph.
            _reflectionHistory[0].Dispose();
            _reflectionHistory[1].Dispose();
            _historyLayout.Dispose();
            _blueNoiseTexture.Dispose();
            _blueNoiseLayout.Dispose();
            _gpuTimestamps?.Dispose();
        }
    }
}
