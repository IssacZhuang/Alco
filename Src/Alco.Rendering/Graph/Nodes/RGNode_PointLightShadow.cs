using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The complete set of point light shadow shaders. All are caller-owned assets
/// loaded from <c>Src/Alco.Engine/Assets/Shaders/Pipelines/Rendering/PBR/</c>.
/// </summary>
public readonly struct PointLightShadowShaders
{
    /// <summary>The atlas face depth shader (PointLightShadowDepth.hlsl).</summary>
    public required Shader Depth { get; init; }
    /// <summary>The half-resolution visibility trace shader (PointLightShadowTrace.hlsl).</summary>
    public required Shader Trace { get; init; }
    /// <summary>The temporal resolve shader (PointLightShadowResolve.hlsl).</summary>
    public required Shader Resolve { get; init; }
    /// <summary>The full-resolution upsample shader (PointLightShadowUpsample.hlsl).</summary>
    public required Shader Upsample { get; init; }
}

/// <summary>
/// Point light soft shadows for the deferred PBR pipeline: a persistent
/// depth-only atlas of 6-face (cube) shadow maps for the
/// <see cref="MaxSlots"/> most important point lights, importance-selected with
/// hysteresis, plus the three-stage screen chain that turns the atlas into
/// shadowed irradiance — a half-resolution PCSS trace
/// (<c>PointLightShadowTrace.hlsl</c>), a temporal resolve
/// (<c>PointLightShadowResolve.hlsl</c>) and a depth-aware upsample
/// (<c>PointLightShadowUpsample.hlsl</c>) whose full-resolution output the
/// deferred lighting pass divides by its own unshadowed evaluation to reconstruct
/// a per-pixel visibility (keeping NdotL terminators and GGX highlights sharp).
/// <br/>Static lights render their six faces only when their slot is (re)assigned
/// or their position/range changes; faces re-render every frame only while
/// dynamic casters are registered (<see cref="IPointLightShadowContent.HasDynamicCasters"/>),
/// so a fully static scene pays no per-frame atlas cost.
/// <br/>Lights beyond the atlas budget stay unshadowed. The shadow quality is
/// bound by the atlas face resolution and PCSS, not by any voxel resolution; the
/// voxel GI renderer can additionally sample the atlas to stop injected point
/// light radiance from bleeding through walls
/// (<see cref="RGNode_VoxelGI.SetPointLightShadowAtlas"/>).
/// <br/>Attach the node to a deferred composition via <see cref="Attach"/> — it
/// registers itself before the lighting node and wires its output to
/// <see cref="RGNode_DeferredLighting.PointLightShadowInput"/>.
/// </summary>
public sealed class RGNode_PointLightShadow : AutoDisposable, IRenderGraphNode
{
    /// <summary>The maximum number of shadowed light slots in the atlas.</summary>
    public const int MaxSlots = 16;

    /// <summary>The number of slot cells per atlas row (each cell packs 6 faces in a 3x2 grid).</summary>
    private const int SlotsPerRow = 4;

    /// <summary>
    /// Hysteresis margin: already-slotted lights keep their slot while they stay
    /// within the top <c>MaxSlots + HysteresisMargin</c> importance ranks, so the
    /// slot set does not churn while scores hover around the cut-off.
    /// </summary>
    private const int HysteresisMargin = 4;

    /// <summary>The range used as the face far plane of lights with no finite range.</summary>
    private const float InfiniteRangeFallback = 25.0f;

    /// <summary>
    /// Per-frame data uploaded to the trace/resolve/upsample compute passes.
    /// Layout must match the <c>_data</c> cbuffer in PointLightShadowCommon.hlsli
    /// exactly.
    /// </summary>
    private struct PointLightShadowData
    {
        /// <summary>Inverse of the camera view-projection matrix.</summary>
        public Matrix4x4 InvViewProjection;
        /// <summary>Previous frame's view-projection (temporal reprojection).</summary>
        public Matrix4x4 ViewProjectionPrev;
        /// <summary>World-space camera position (w unused).</summary>
        public Vector4 CameraPosition;
        /// <summary>x=numPointLights y=lightRadius z=traceWidth w=traceHeight.</summary>
        public Vector4 Params;
        /// <summary>x=gbufferWidth y=gbufferHeight z=frameIndex w=historyValid.</summary>
        public Vector4 Params2;
        /// <summary>x=1/faceSize y=1/atlasWidth z=1/atlasHeight w=maxPenumbraTexels.</summary>
        public Vector4 Params3;
    }

    /// <summary>An importance-ranked light candidate for slot assignment.</summary>
    private struct LightCandidate
    {
        /// <summary>The light index in the environment's point light list.</summary>
        public int LightIndex;
        /// <summary>The importance score (descending order).</summary>
        public float Score;
    }

    private readonly RenderingSystem _rendering;
    private readonly uint _faceSize;
    private readonly float _traceResolutionScale;

    // GPU resources (persistent — cross-frame state never enters the graph).
    private readonly GPUAttachmentLayout _atlasLayout;
    private readonly RenderTexture _atlas;
    private readonly GraphicsValueBuffer<PointLightShadowData> _dataBuffer;
    private readonly GraphicsArrayBuffer<Matrix4x4> _matrixBuffer;
    private readonly GraphicsArrayBuffer<Vector4> _shadowInfoBuffer;
    private readonly ComputeMaterial _traceMaterial;
    private readonly ComputeMaterial _resolveMaterial;
    private readonly ComputeMaterial _upsampleMaterial;
    private readonly GraphicsMaterial _clearMaterial;
    private readonly RenderTexture[] _history = new RenderTexture[2];
    private readonly Mesh _fullScreenMesh;

    // Facades of the graph-owned transients; null until Attach creates them.
    private RenderTexture? _rawTrace;
    private RenderTexture? _shadowedOutput;
    private RenderTexture? _boundGBuffer;

    // Graph state.
    private RenderGraph? _graph;
    private RGNode_DeferredLighting? _lighting;
    private RenderGraphTexture? _gbufferResource;
    private PBRSceneEnvironment? _environment;
    private RenderGraphTexture? _rawTraceResource;
    private RenderGraphTexture? _outputResource;

    // Slot assignment state (importance ranking with hysteresis).
    private readonly int[] _slotLightIndex = new int[MaxSlots];
    private readonly Vector3[] _slotLightPosition = new Vector3[MaxSlots];
    private readonly float[] _slotLightRange = new float[MaxSlots];
    private readonly bool[] _slotFaceDirty = new bool[MaxSlots];
    private readonly int[] _lightSlot = new int[PBRSceneEnvironment.MaxPointLights];
    private readonly LightCandidate[] _candidates = new LightCandidate[PBRSceneEnvironment.MaxPointLights];
    private readonly bool[] _keepLight = new bool[PBRSceneEnvironment.MaxPointLights];
    private readonly Vector4[] _frustumPlanes = new Vector4[6];
    private float _slotLightRadius = -1.0f;
    private bool _matricesDirty = true;
    private bool _forceAtlasDirty = true;
    private long _frameIndex;

    // Temporal state.
    private int _historyWriteIndex;
    private bool _historyValid;
    private Matrix4x4 _viewProjectionPrev = Matrix4x4.Identity;
    private bool _isEnabled = true;

    /// <summary>
    /// Create the point light shadow node. GPU resources are allocated eagerly;
    /// the screen-chain outputs are graph transients created by <see cref="Attach"/>.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="shaders">The complete shader set (PointLightShadow*.hlsl).</param>
    /// <param name="faceSize">The resolution of one cube face in texels (the atlas is 12x8 faces at this size).</param>
    /// <param name="traceResolutionScale">The visibility trace resolution relative to the G-buffer (0.25..1.0).</param>
    /// <param name="width">The initial G-buffer width in pixels.</param>
    /// <param name="height">The initial G-buffer height in pixels.</param>
    /// <exception cref="ArgumentException">The trace-resolution scale is invalid.</exception>
    public RGNode_PointLightShadow(
        RenderingSystem rendering,
        PointLightShadowShaders shaders,
        uint faceSize = 256,
        float traceResolutionScale = 0.5f,
        uint width = 1280,
        uint height = 720)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ValidateTraceResolutionScale(traceResolutionScale);
        _rendering = rendering;
        _faceSize = Math.Max(faceSize, 1);
        _traceResolutionScale = traceResolutionScale;
        _fullScreenMesh = rendering.MeshFullScreen;

        // Persistent depth-only atlas: 4x4 slot cells, each a 3x2 grid of faces.
        _atlasLayout = rendering.GraphicsDevice.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [], new DepthAttachment(PixelFormat.Depth32Float), "pbr_pls_atlas_pass"));
        _atlas = rendering.CreateRenderTexture(_atlasLayout,
            _faceSize * 3u * SlotsPerRow, _faceSize * 2u * (MaxSlots / SlotsPerRow), "pls_atlas");

        _dataBuffer = rendering.CreateGraphicsValueBuffer<PointLightShadowData>("pls_data");
        _matrixBuffer = rendering.CreateGraphicsArrayBuffer<Matrix4x4>(
            MaxSlots * PointLightShadowMath.FaceCount, "pls_face_matrices");
        // Per-light shadow metadata sampled by the trace and voxel inject passes:
        // x = slot index (-1 = unshadowed), y = near plane, z = far plane, w unused.
        _shadowInfoBuffer = rendering.CreateGraphicsArrayBuffer<Vector4>(
            PBRSceneEnvironment.MaxPointLights, "pls_shadow_info");

        _traceMaterial = rendering.CreateComputeMaterial(shaders.Trace);
        _resolveMaterial = rendering.CreateComputeMaterial(shaders.Resolve);
        _upsampleMaterial = rendering.CreateComputeMaterial(shaders.Upsample);
        _traceMaterial.SetBuffer("_data", _dataBuffer);
        _resolveMaterial.SetBuffer("_data", _dataBuffer);
        _upsampleMaterial.SetBuffer("_data", _dataBuffer);
        _traceMaterial.SetBuffer("_plShadowInfo", _shadowInfoBuffer);
        _traceMaterial.SetRenderTextureDepth("_plShadowAtlas", _atlas);
        _traceMaterial.SetRenderTextureDepth("_plShadowAtlasLoad", _atlas);

        // Face clear material: the depth shader's PLS_CLEAR_FACE variant draws the
        // full-screen mesh at the far plane with an Always depth test, so scissored
        // draws reset one face rect without a whole-atlas render-pass clear.
        _clearMaterial = rendering.CreateMaterial(shaders.Depth, "pls_face_clear");
        _clearMaterial.SetDefines("PLS_CLEAR_FACE");
        _clearMaterial.GetPipelineContext(_atlasLayout);
        _clearMaterial.DepthStencilState = new DepthStencilState(true, CompareFunction.Always);
        _clearMaterial.RasterizerState = new RasterizerState(FillMode.Solid, CullMode.None, FrontFace.Clockwise);
        _clearMaterial.SetBuffer(ShaderResourceId.Data, _matrixBuffer);

        // Ping-pong temporal history at the trace resolution.
        uint traceWidth = TraceSize(width);
        uint traceHeight = TraceSize(height);
        _history[0] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth, traceHeight, "pls_history_a");
        _history[1] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth, traceHeight, "pls_history_b");

        for (int slot = 0; slot < MaxSlots; slot++)
        {
            _slotLightIndex[slot] = -1;
        }
        for (int i = 0; i < _lightSlot.Length; i++)
        {
            _lightSlot[i] = -1;
        }
    }

    /// <summary>
    /// Whether the node participates in the frame. Disabling also clears
    /// <see cref="PBRSceneEnvironment.PointLightShadowsActive"/> so the deferred
    /// lighting pass falls back to the unshadowed point light loop.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (_environment != null)
            {
                _environment.PointLightShadowsActive = value;
            }
        }
    }

    /// <summary>The caster content drawn into rendered atlas faces, in list order.
    /// The node does not take ownership of registered providers.</summary>
    public List<IPointLightShadowContent> Content { get; } = new();

    /// <summary>The physical light radius in world units — the PCSS penumbra source
    /// and the near plane (emitter housing) clamp.</summary>
    public float LightRadius { get; set; } = 0.1f;

    /// <summary>The maximum PCSS penumbra radius in face texels (bounds the filter cost).</summary>
    public float MaxPenumbraTexels { get; set; } = 24.0f;

    /// <summary>The persistent depth atlas texture (12x8 faces of <see cref="FaceSize"/> texels).</summary>
    public RenderTexture Atlas => _atlas;

    /// <summary>The atlas attachment layout (for caster material creation).</summary>
    public GPUAttachmentLayout AtlasLayout => _atlasLayout;

    /// <summary>The folded per-face view-projection matrix buffer (slot*6+face indexed).</summary>
    public GraphicsBuffer MatrixBuffer => _matrixBuffer;

    /// <summary>
    /// The per-light shadow metadata buffer consumed by the trace pass and
    /// wireable into <see cref="RGNode_VoxelGI.SetPointLightShadowAtlas"/>:
    /// x = slot index (-1 = unshadowed), y = near plane, z = far plane, w unused.
    /// </summary>
    public GraphicsBuffer ShadowInfoBuffer => _shadowInfoBuffer;

    /// <summary>The resolution of one cube face in texels.</summary>
    public uint FaceSize => _faceSize;

    /// <summary>
    /// The light index currently occupying each atlas slot (-1 = free). Internal
    /// test hook covering the slot assignment contract: importance ranking,
    /// hysteresis retention and eviction (see TestPointLightShadow).
    /// </summary>
    internal ReadOnlySpan<int> SlotLightIndices => _slotLightIndex;

    /// <summary>
    /// Forces every occupied atlas face to re-render on the next frame (e.g. after
    /// registering or moving static casters — dynamic casters re-render automatically).
    /// </summary>
    public void MarkAtlasDirty()
    {
        _forceAtlasDirty = true;
    }

    /// <summary>
    /// Attaches the node to a deferred composition: creates the transient raw-trace
    /// and full-resolution output resources, registers itself immediately before
    /// the anchor node (the lighting node by default) and wires the output to
    /// <see cref="RGNode_DeferredLighting.PointLightShadowInput"/> and the lighting
    /// material's _pointLightShadowed slot.
    /// <br/>When a voxel GI renderer wired via
    /// <see cref="RGNode_VoxelGI.SetPointLightShadowAtlas"/> samples the atlas in
    /// its inject pass, pass the GI node as <paramref name="insertAnchor"/> (or
    /// attach before it) so the atlas faces are rendered before the injection.
    /// </summary>
    /// <param name="graph">The render graph driving the frame.</param>
    /// <param name="lighting">The deferred lighting node the output feeds.</param>
    /// <param name="gbuffer">The G-buffer resource read by the trace passes.</param>
    /// <param name="environment">The shared scene environment (camera, point lights).</param>
    /// <param name="insertAnchor">The node to register before (the lighting node
    /// when null).</param>
    /// <exception cref="InvalidOperationException">The node is already attached.</exception>
    public void Attach(RenderGraph graph, RGNode_DeferredLighting lighting, RenderGraphTexture gbuffer,
        PBRSceneEnvironment environment, IRenderGraphNode? insertAnchor = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(lighting);
        ArgumentNullException.ThrowIfNull(gbuffer);
        ArgumentNullException.ThrowIfNull(environment);
        if (_graph != null)
        {
            throw new InvalidOperationException("The point light shadow node is already attached (call Detach first).");
        }
        _graph = graph;
        _lighting = lighting;
        _gbufferResource = gbuffer;
        _environment = environment;
        _rawTraceResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, resolutionScale: _traceResolutionScale, name: "pls_raw_trace"));
        _outputResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, name: "point_light_shadowed"));
        _rawTrace = _rawTraceResource.Texture;
        _shadowedOutput = _outputResource.Texture;
        _upsampleMaterial.SetRenderTexture("_plOut", _shadowedOutput);
        graph.InsertBefore(insertAnchor ?? lighting, this);
        lighting.PointLightShadowInput = _outputResource;
        lighting.Material.SetRenderTexture("_pointLightShadowed", _shadowedOutput);
        environment.PointLightShadowsActive = _isEnabled;
    }

    /// <summary>
    /// Detaches the node from the graph: unregisters it, destroys its transient
    /// resources and restores the lighting material's fallback binding.
    /// </summary>
    public void Detach()
    {
        if (_graph == null)
        {
            return;
        }
        _graph.Remove(this);
        if (_rawTraceResource != null)
        {
            _graph.DestroyTransient(_rawTraceResource);
            _rawTraceResource = null;
        }
        if (_outputResource != null)
        {
            _graph.DestroyTransient(_outputResource);
            _outputResource = null;
        }
        _rawTrace = null;
        _shadowedOutput = null;
        if (_lighting != null)
        {
            _lighting.PointLightShadowInput = null;
            _lighting.Material.SetTexture("_pointLightShadowed", _rendering.TextureBlack);
        }
        if (_environment != null)
        {
            _environment.PointLightShadowsActive = false;
        }
        _graph = null;
        _lighting = null;
        _gbufferResource = null;
        _environment = null;
    }

    /// <summary>
    /// Recreates the trace-resolution history textures at a new G-buffer size using
    /// the current trace-resolution scale. The atlas is resolution-independent.
    /// </summary>
    /// <param name="width">The new G-buffer width in pixels.</param>
    /// <param name="height">The new G-buffer height in pixels.</param>
    public void Resize(uint width, uint height)
    {
        uint traceWidth = TraceSize(width);
        uint traceHeight = TraceSize(height);
        if (traceWidth == _history[0].Width && traceHeight == _history[0].Height)
        {
            return;
        }

        RenderTexture newHistoryA = _rendering.CreateRenderTexture(
            _rendering.PreferredLightMapPass, traceWidth, traceHeight, "pls_history_a");
        RenderTexture newHistoryB;
        try
        {
            newHistoryB = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth, traceHeight, "pls_history_b");
        }
        catch
        {
            newHistoryA.Dispose();
            throw;
        }
        _history[0].Dispose();
        _history[1].Dispose();
        _history[0] = newHistoryA;
        _history[1] = newHistoryB;
        _historyWriteIndex = 0;
        _historyValid = false;
        _boundGBuffer = null;
    }

    private static void ValidateTraceResolutionScale(float scale)
    {
        if (!float.IsFinite(scale) || scale < 0.25f || scale > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "The trace-resolution scale must be between 0.25 and 1.0.");
        }
    }

    private uint TraceSize(uint size)
    {
        return Math.Max((uint)MathF.Ceiling(size * _traceResolutionScale), 1);
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Read(_gbufferResource!);
        builder.Write(_rawTraceResource!);
        builder.Write(_outputResource!);
        // The environment flag drives the lighting shader's branch; keep it in
        // lockstep with this node even after graph-driven re-registration.
        if (_environment != null)
        {
            _environment.PointLightShadowsActive = _isEnabled;
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        PBRSceneEnvironment environment = _environment!;
        CameraPerspectiveBuffer? camera = environment.Camera;
        if (camera == null)
        {
            throw new InvalidOperationException("Point light shadows require a camera (set the environment's Camera first).");
        }
        Matrix4x4 viewProjection = camera.Data.ViewProjectionMatrix;
        Matrix4x4.Invert(viewProjection, out Matrix4x4 invViewProjection);
        Vector3 cameraPosition = camera.Transform.Position;
        RenderTexture gbuffer = _gbufferResource!.Texture;
        ReadOnlySpan<PBRSceneEnvironment.PointLight> lights = environment.CurrentPointLights;
        uint traceWidth = TraceSize(gbuffer.Width);
        uint traceHeight = TraceSize(gbuffer.Height);

        UpdateSlotAssignment(lights, viewProjection, cameraPosition);
        UpdateShadowInfo(lights);
        if (_matricesDirty)
        {
            RebuildFaceMatrices();
        }

        // ── Assemble and upload the compute constant buffer ──
        PointLightShadowData data = new()
        {
            InvViewProjection = invViewProjection,
            ViewProjectionPrev = _viewProjectionPrev,
            CameraPosition = new Vector4(cameraPosition, 0.0f),
            Params = new Vector4(lights.Length, LightRadius, traceWidth, traceHeight),
            Params2 = new Vector4(gbuffer.Width, gbuffer.Height, _frameIndex % 1024, _historyValid ? 1.0f : 0.0f),
            Params3 = new Vector4(
                1.0f / _faceSize, 1.0f / _atlas.Width, 1.0f / _atlas.Height, MaxPenumbraTexels),
        };
        _dataBuffer.UpdateBuffer(data);

        // The G-buffer render texture is stable across frames (recreated on
        // resize); avoid rebinding every frame.
        if (!ReferenceEquals(_boundGBuffer, gbuffer))
        {
            _traceMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _traceMaterial.SetRenderTexture("_normal", gbuffer, 1);
            _resolveMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _resolveMaterial.SetRenderTexture("_normal", gbuffer, 1);
            _upsampleMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _boundGBuffer = gbuffer;
        }

        RenderAtlasFaces(context);

        // ── The three-stage visibility chain ──
        GPUCommandBuffer commandBuffer = context.RenderContext.CommandBuffer;
        RenderTexture historyRead = _history[_historyWriteIndex ^ 1];
        RenderTexture historyWrite = _history[_historyWriteIndex];
        _traceMaterial.SetBuffer(ShaderResourceId.PointLights, environment.PointLightBuffer);
        _traceMaterial.SetRenderTexture("_plRawOut", _rawTrace!);
        _resolveMaterial.SetRenderTexture("_plRaw", _rawTrace!);
        _resolveMaterial.SetRenderTexture("_plHistory", historyRead);
        _resolveMaterial.SetRenderTexture("_plOut", historyWrite);
        _upsampleMaterial.SetRenderTexture("_plTrace", historyWrite);
        using (GPUCommandBuffer.ComputePass computePass = commandBuffer.BeginCompute())
        {
            _traceMaterial.DispatchBySize(computePass, traceWidth, traceHeight, 1);
            _resolveMaterial.DispatchBySize(computePass, traceWidth, traceHeight, 1);
            _upsampleMaterial.DispatchBySize(computePass, gbuffer.Width, gbuffer.Height, 1);
        }

        _historyWriteIndex ^= 1;
        _historyValid = true;
        _viewProjectionPrev = viewProjection;
        _frameIndex++;
    }

    // ── Importance selection + slot assignment ──

    /// <summary>
    /// Ranks the active lights by importance (intensity over the squared distance
    /// from the camera to the light's reach, penalized when the light sphere is
    /// fully outside the camera frustum), then updates the slot assignment:
    /// slotted lights outside the hysteresis rank window are evicted and the
    /// highest-ranked unslotted lights take their places.
    /// </summary>
    private void UpdateSlotAssignment(ReadOnlySpan<PBRSceneEnvironment.PointLight> lights,
        Matrix4x4 viewProjection, Vector3 cameraPosition)
    {
        // Insertion sort by descending score, capped at the hysteresis window.
        int maxCandidates = MaxSlots + HysteresisMargin;
        int candidateCount = 0;
        ExtractFrustumPlanes(viewProjection, _frustumPlanes);
        for (int i = 0; i < lights.Length; i++)
        {
            float intensity = lights[i].ColorAndIntensity.W;
            if (intensity <= 0.0f)
            {
                continue;
            }
            Vector3 position = new(lights[i].Position.X, lights[i].Position.Y, lights[i].Position.Z);
            float range = lights[i].Position.W;
            float distance = Vector3.Distance(cameraPosition, position);
            float outside = MathF.Max(distance - MathF.Max(range, 0.01f), 0.0f);
            float score = intensity / (1.0f + outside * outside);
            if (!SphereIntersectsFrustum(position, MathF.Max(range, 0.5f)))
            {
                // Off-screen lights still light nearby visible pixels, but rank lower.
                score *= 0.1f;
            }
            if (score <= 0.0f)
            {
                continue;
            }

            int insert = Math.Min(candidateCount, maxCandidates);
            while (insert > 0 && _candidates[insert - 1].Score < score)
            {
                if (insert < maxCandidates)
                {
                    _candidates[insert] = _candidates[insert - 1];
                }
                insert--;
            }
            if (insert < maxCandidates)
            {
                _candidates[insert] = new LightCandidate { LightIndex = i, Score = score };
                candidateCount = Math.Min(candidateCount + 1, maxCandidates);
            }
        }

        // Keep flags for the hysteresis window; slots whose light fell out are freed.
        Array.Clear(_keepLight);
        for (int i = 0; i < candidateCount; i++)
        {
            _keepLight[_candidates[i].LightIndex] = true;
        }
        for (int slot = 0; slot < MaxSlots; slot++)
        {
            int light = _slotLightIndex[slot];
            if (light >= 0 && !_keepLight[light])
            {
                _lightSlot[light] = -1;
                _slotLightIndex[slot] = -1;
                _matricesDirty = true;
            }
        }

        // Assign free slots to the highest-ranked unslotted lights (top MaxSlots
        // ranks only — ranks within the hysteresis window merely keep their slot).
        int desired = Math.Min(candidateCount, MaxSlots);
        for (int i = 0; i < desired; i++)
        {
            int light = _candidates[i].LightIndex;
            if (_lightSlot[light] >= 0)
            {
                continue;
            }
            int freeSlot = -1;
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (_slotLightIndex[slot] < 0)
                {
                    freeSlot = slot;
                    break;
                }
            }
            if (freeSlot < 0)
            {
                // All slots held by lights inside the hysteresis window: the light
                // stays unshadowed until a slot frees up.
                break;
            }
            _slotLightIndex[freeSlot] = light;
            _lightSlot[light] = freeSlot;
            _slotFaceDirty[freeSlot] = true;
            _matricesDirty = true;
        }

        // Track light data changes (position/range) for face re-render and matrices.
        for (int slot = 0; slot < MaxSlots; slot++)
        {
            int light = _slotLightIndex[slot];
            if (light < 0)
            {
                continue;
            }
            Vector3 position = new(lights[light].Position.X, lights[light].Position.Y, lights[light].Position.Z);
            float range = lights[light].Position.W;
            if (position != _slotLightPosition[slot] || range != _slotLightRange[slot])
            {
                _slotLightPosition[slot] = position;
                _slotLightRange[slot] = range;
                _slotFaceDirty[slot] = true;
                _matricesDirty = true;
            }
        }

        // The near plane derives from the light radius: a radius change must
        // re-render every face and rebuild the matrices.
        if (_slotLightRadius != LightRadius)
        {
            _slotLightRadius = LightRadius;
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                _slotFaceDirty[slot] = true;
            }
            _matricesDirty = true;
        }
    }

    /// <summary>Rewrites the per-light shadow metadata buffer from the slot map.</summary>
    private void UpdateShadowInfo(ReadOnlySpan<PBRSceneEnvironment.PointLight> lights)
    {
        if (lights.Length == 0)
        {
            return;
        }
        Span<Vector4> span = _shadowInfoBuffer.AsSpan();
        span.Fill(new Vector4(-1.0f, 0.0f, 0.0f, 0.0f));
        for (int slot = 0; slot < MaxSlots; slot++)
        {
            int light = _slotLightIndex[slot];
            if (light < 0)
            {
                continue;
            }
            float range = lights[light].Position.W;
            float far = range > 0.0f ? range : InfiniteRangeFallback;
            float near = MathF.Max(0.05f, LightRadius);
            span[light] = new Vector4(slot, near, MathF.Max(far, near + 0.1f), 0.0f);
        }
        _shadowInfoBuffer.UpdateBufferRanged(0, (uint)lights.Length);
    }

    /// <summary>Rebuilds the folded per-face view-projection matrices of occupied slots.</summary>
    private void RebuildFaceMatrices()
    {
        Span<Matrix4x4> span = _matrixBuffer.AsSpan();
        for (int slot = 0; slot < MaxSlots; slot++)
        {
            int light = _slotLightIndex[slot];
            if (light < 0)
            {
                continue;
            }
            Vector3 position = _slotLightPosition[slot];
            float range = _slotLightRange[slot];
            float far = range > 0.0f ? range : InfiniteRangeFallback;
            float near = MathF.Max(0.05f, LightRadius);
            far = MathF.Max(far, near + 0.1f);
            for (int face = 0; face < PointLightShadowMath.FaceCount; face++)
            {
                Matrix4x4 vp = PointLightShadowMath.BuildFaceViewProjection(position, near, far, face);
                span[slot * PointLightShadowMath.FaceCount + face] = PointLightShadowMath.FoldToAtlas(
                    vp, slot, face, _faceSize, SlotsPerRow, _atlas.Width, _atlas.Height);
            }
        }
        _matrixBuffer.UpdateBuffer();
        _matricesDirty = false;
    }

    // ── Atlas face rendering ──

    /// <summary>
    /// Renders every dirty face in one scissored pass: each face is first reset by
    /// a full-screen far-plane draw (render-pass clears are not scissorable, and
    /// clearing the whole atlas would wipe the cached static faces), then the
    /// content providers draw their casters into it.
    /// </summary>
    private void RenderAtlasFaces(in RenderGraphContext context)
    {
        List<IPointLightShadowContent> content = Content;
        bool anyDynamic = false;
        for (int i = 0; i < content.Count; i++)
        {
            if (content[i].IsEnabled && content[i].HasDynamicCasters)
            {
                anyDynamic = true;
                break;
            }
        }

        bool anyFace = _forceAtlasDirty;
        if (!anyFace)
        {
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (_slotLightIndex[slot] >= 0 && (anyDynamic || _slotFaceDirty[slot]))
                {
                    anyFace = true;
                    break;
                }
            }
        }
        if (!anyFace)
        {
            return;
        }

        GPUFrameBuffer atlasFrameBuffer = _atlas.FrameBuffer;
        RenderPassScope pass = context.RenderContext.BeginPass(atlasFrameBuffer);
        using (pass)
        {
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (_slotLightIndex[slot] < 0)
                {
                    continue;
                }
                if (!(anyDynamic || _slotFaceDirty[slot] || _forceAtlasDirty))
                {
                    continue;
                }
                for (int face = 0; face < PointLightShadowMath.FaceCount; face++)
                {
                    (float originX, float originY) = PointLightShadowMath.FacePixelOrigin(slot, face, _faceSize, SlotsPerRow);
                    pass.SetScissorRect((uint)originX, (uint)originY, _faceSize, _faceSize);
                    // Reset the face rect (Always-pass depth write of the far plane).
                    pass.Draw(_fullScreenMesh, _clearMaterial);
                    for (int i = 0; i < content.Count; i++)
                    {
                        if (content[i].IsEnabled)
                        {
                            content[i].OnRenderPointLightShadow(pass, slot * PointLightShadowMath.FaceCount + face);
                        }
                    }
                }
                _slotFaceDirty[slot] = false;
            }
            _forceAtlasDirty = false;
        }
    }

    // ── Frustum culling helpers ──

    /// <summary>
    /// Extracts the six camera frustum planes from a view-projection matrix
    /// (Gribb-Hartmann, row-vector convention, 0..1 depth), normalized.
    /// </summary>
    private static void ExtractFrustumPlanes(Matrix4x4 m, Span<Vector4> planes)
    {
        Span<Vector4> rows =
        [
            new(m.M11, m.M12, m.M13, m.M14),
            new(m.M21, m.M22, m.M23, m.M24),
            new(m.M31, m.M32, m.M33, m.M34),
            new(m.M41, m.M42, m.M43, m.M44),
        ];
        // left, right, bottom, top, near (0..1 z), far.
        Span<Vector4> planeRows =
        [
            rows[3] + rows[0], rows[3] - rows[0],
            rows[3] + rows[1], rows[3] - rows[1],
            rows[2], rows[3] - rows[2],
        ];
        for (int i = 0; i < 6; i++)
        {
            Vector4 p = planeRows[i];
            float length = MathF.Max(MathF.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z), 1e-6f);
            planes[i] = p / length;
        }
    }

    private bool SphereIntersectsFrustum(Vector3 center, float radius)
    {
        Span<Vector4> planes = _frustumPlanes;
        for (int i = 0; i < planes.Length; i++)
        {
            float distance = planes[i].X * center.X + planes[i].Y * center.Y + planes[i].Z * center.Z + planes[i].W;
            if (distance < -radius)
            {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _atlas.Dispose();
            _atlasLayout.Dispose();
            _dataBuffer.Dispose();
            _matrixBuffer.Dispose();
            _shadowInfoBuffer.Dispose();
            _history[0].Dispose();
            _history[1].Dispose();
        }
    }
}
