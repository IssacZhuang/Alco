using System.Diagnostics;
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// HBAO+ (horizon-based ambient occlusion) renderer for deferred PBR compositions.
/// <br/>Reads the G-buffer depth and world-normal attachments, marches screen-space
/// horizon rays in a compute pass (HBAO.hlsl) and filters the noisy result with a
/// depth/normal-aware bilateral blur (HBAOBlur.hlsl). The blur pass writes the
/// filtered AO to a full-resolution texture (<see cref="AOResult"/>),
/// which the deferred lighting material samples through its _aoTexture slot.
/// <br/>Attach the renderer to a deferred composition via <see cref="Attach"/>: it
/// creates its graph transient resources, registers itself as a direct
/// <see cref="IRenderGraphNode"/> before the lighting node and wires its
/// output to <see cref="RGNode_DeferredLighting.AoInput"/>. The raw intermediate is
/// pooled/aliased by the graph.
/// </summary>
public sealed class RGNode_HBAO : AutoDisposable, IRenderGraphNode
{
    /// <summary>
    /// Per-frame HBAO data uploaded to both compute passes. Layout must match the
    /// <c>_data</c> cbuffer in HBAOCommon.hlsli exactly. Assembled internally by
    /// the renderer from camera data and user-tunable properties.
    /// </summary>
    private struct HbaoData
    {
        /// <summary>Inverse of the camera view-projection matrix.</summary>
        public Matrix4x4 InvViewProjection;
        /// <summary>World-space camera position (w unused).</summary>
        public Vector4 CameraPosition;
        /// <summary>World-space camera right axis (w unused).</summary>
        public Vector4 CameraRight;
        /// <summary>World-space camera up axis (w unused).</summary>
        public Vector4 CameraUp;
        /// <summary>World-space camera forward axis (w unused).</summary>
        public Vector4 CameraForward;
        /// <summary>x=radius (world units) y=intensity exponent z=angle bias (sin space) w=1/radius^2.</summary>
        public Vector4 Params;
        /// <summary>x=projScale (0.5 * viewportHeight * projection[1][1]) yz=viewport size in pixels (filled by <see cref="Execute"/>) w=max step length in pixels.</summary>
        public Vector4 Params2;
        /// <summary>x=strength (0 disables; scales how much of the blurred AO is written to the result texture) yzw=unused.</summary>
        public Vector4 Params3;
    }

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly ComputeMaterial _hbaoMaterial;
    private readonly ComputeMaterial _blurMaterial;
    private readonly GraphicsValueBuffer<HbaoData> _dataBuffer;

    // GPU timestamp ring buffer for per-stage timing (slot 0 = pass begin,
    // slot 1 = after AO before Blur, slot 2 = pass end).
    private const int TimestampSlotCount = 3;
    private readonly GpuTimestampSampler? _gpuTimestamps;
    private double _aoGpuMilliseconds;
    private double _blurGpuMilliseconds;

    private RenderTexture _rawAO;
    private RenderTexture _aoResult;
    private RenderTexture? _boundGBuffer;

    // Graph-owned transient resources. _rawAO/_aoResult are facades of the
    // transients below; they are not disposed here and are rematerialized by
    // the graph on resize.
    private RenderGraph? _graph;
    private RGNode_DeferredLighting? _lighting;
    private RenderGraphTexture? _gbufferResource;
    private PBRSceneEnvironment? _environment;
    private RenderGraphTexture? _rawAOResource;
    private RenderGraphTexture? _aoResource;

    // Profiler counter handles — lazily registered on first Execute call.
    private RenderProfileCounterId _hbaoCounter;
    private RenderProfileCounterId _aoCounter;
    private RenderProfileCounterId _blurCounter;
    private bool _profilerCounterRegistered;

    /// <summary>
    /// Gets or sets the world-space AO sampling radius. Larger radii catch broader
    /// occlusion but are more expensive and prone to streaking.
    /// </summary>
    public float Radius { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the AO intensity (power exponent applied to the raw occlusion).
    /// </summary>
    public float Intensity { get; set; } = 1.2f;

    /// <summary>
    /// Gets or sets the angle bias in sine space. Higher values reject thin
    /// geometry features that cause false occlusion.
    /// </summary>
    public float Bias { get; set; } = 0.02f;

    /// <summary>
    /// Gets or sets the overall AO strength multiplier. Zero disables the effect
    /// (the output texture is all white). Values above 1 amplify occlusion.
    /// </summary>
    public float Strength { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the maximum march distance in pixels per direction.
    /// </summary>
    public float MaxStepPixels { get; set; } = 64.0f;

    /// <summary>The full-resolution AO result texture (r = occlusion [0,1], white = unoccluded).</summary>
    public RenderTexture AOResult => _aoResult;

    /// <summary>
    /// Create the HBAO+ renderer with the given compute shaders.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="hbaoShader">The raw AO shader (HBAO.hlsl).</param>
    /// <param name="blurShader">The bilateral blur shader (HBAOBlur.hlsl).</param>
    /// <param name="width">The initial AO texture width in pixels (match the G-buffer).</param>
    /// <param name="height">The initial AO texture height in pixels (match the G-buffer).</param>
    public RGNode_HBAO(RenderingSystem rendering, Shader hbaoShader, Shader blurShader, uint width, uint height)
    {
        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _commandBuffer = _device.CreateCommandBuffer("hbao");
        _hbaoMaterial = rendering.CreateComputeMaterial(hbaoShader);
        _blurMaterial = rendering.CreateComputeMaterial(blurShader);
        _dataBuffer = rendering.CreateGraphicsValueBuffer<HbaoData>("hbao_data");
        _hbaoMaterial.SetBuffer("_data", _dataBuffer);
        _blurMaterial.SetBuffer("_data", _dataBuffer);

        // RGBA16Float (light map layout): proven as both a compute storage target and
        // a filterable sampled texture.
        _rawAO = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, width, height, "hbao_raw");
        _aoResult = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, width, height, "hbao_result");

        _hbaoMaterial.SetRenderTexture("_aoOutput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoInput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoResult", _aoResult);

        if (_device.TimestampQuerySupported)
        {
            _gpuTimestamps = new GpuTimestampSampler(_device, TimestampSlotCount, "hbao");
        }
    }

    /// <summary>
    /// Attaches the renderer to a deferred composition as a direct
    /// <see cref="IRenderGraphNode"/> in the graph: creates the transient AO result
    /// and raw-AO intermediate, registers itself immediately before the lighting
    /// node, and wires the result to <see cref="RGNode_DeferredLighting.AoInput"/> and
    /// the lighting material's _aoTexture slot. The constructor-created standalone
    /// textures are released and the materials are rebound once here — the facades
    /// keep their object identity from then on. After attachment the graph drives
    /// execution and resize.
    /// </summary>
    /// <param name="graph">The render graph driving the frame.</param>
    /// <param name="lighting">The deferred lighting node the AO output feeds.</param>
    /// <param name="gbuffer">The G-buffer resource read by the AO passes.</param>
    /// <param name="environment">The shared scene environment (camera access).</param>
    /// <exception cref="InvalidOperationException">The renderer is already attached.</exception>
    public void Attach(RenderGraph graph, RGNode_DeferredLighting lighting, RenderGraphTexture gbuffer, PBRSceneEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(lighting);
        ArgumentNullException.ThrowIfNull(gbuffer);
        ArgumentNullException.ThrowIfNull(environment);
        if (_graph != null)
        {
            throw new InvalidOperationException("The HBAO renderer is already attached to a graph (call Detach first).");
        }
        _graph = graph;
        _lighting = lighting;
        _gbufferResource = gbuffer;
        _environment = environment;
        _rawAO.Dispose();
        _aoResult.Dispose();
        _aoResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, name: "hbao_ao"));
        _rawAOResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, name: "hbao_raw"));
        _rawAO = _rawAOResource.Texture;
        _aoResult = _aoResource.Texture;
        _hbaoMaterial.SetRenderTexture("_aoOutput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoInput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoResult", _aoResult);
        graph.InsertBefore(lighting, this);
        lighting.AoInput = _aoResource;
        lighting.Material.SetRenderTexture("_aoTexture", _aoResult);
    }

    /// <summary>
    /// Detaches the renderer from the graph: unregisters it, destroys its transient
    /// resources and restores the lighting material's _aoTexture fallback. The
    /// renderer can be re-attached afterwards.
    /// </summary>
    public void Detach()
    {
        if (_graph == null)
        {
            return;
        }
        _graph.Remove(this);
        if (_rawAOResource != null)
        {
            _graph.DestroyTransient(_rawAOResource);
            _rawAOResource = null;
        }
        if (_aoResource != null)
        {
            _graph.DestroyTransient(_aoResource);
            _aoResource = null;
        }
        if (_lighting != null)
        {
            _lighting.AoInput = null;
            _lighting.Material.SetTexture("_aoTexture", _rendering.TextureWhite);
        }
        _graph = null;
        _lighting = null;
        _gbufferResource = null;
        _environment = null;
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        // The output textures are graph-owned transients, rematerialized by the
        // graph's own resize. Only the G-buffer rebind cache needs resetting.
        _boundGBuffer = null;
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Read(_gbufferResource!);
        builder.Write(_rawAOResource!);
        builder.Write(_aoResource!);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        CameraPerspectiveBuffer? camera = _environment!.Camera;
        if (camera == null)
        {
            throw new InvalidOperationException("HBAO requires a camera (set the environment's Camera first).");
        }
        Matrix4x4.Invert(camera.Data.ViewProjectionMatrix, out Matrix4x4 invViewProjection);
        RenderTexture gbuffer = _gbufferResource!.Texture;
        ExecuteCore(camera.Data.ProjectionMatrix, invViewProjection, camera.Transform, gbuffer, _graph?.Profiler);
    }

    // Shared body of the execute path: assembles the per-frame constants, records
    // the AO and blur dispatches and schedules the command buffer.
    private void ExecuteCore(
        in Matrix4x4 projectionMatrix,
        in Matrix4x4 invViewProjection,
        Transform3D cameraTransform,
        RenderTexture gbuffer,
        RenderProfiler? profiler)
    {
        long startTimestamp = Stopwatch.GetTimestamp();

        // ── Assemble the GPU constant buffer internally ──
        // Camera basis axes are derived from the camera rotation quaternion.
        // The engine camera convention is +X forward, +Y right, +Z up.
        Quaternion rot = cameraTransform.Rotation;
        float r2 = MathF.Max(Radius * Radius, 1e-6f);
        float projectionScale = 0.5f * gbuffer.Height * projectionMatrix.M22;
        HbaoData data = new()
        {
            InvViewProjection = invViewProjection,
            CameraPosition = new Vector4(cameraTransform.Position, 0.0f),
            CameraRight = new Vector4(Vector3.Transform(Vector3.UnitY, rot), 0.0f),
            CameraUp = new Vector4(Vector3.Transform(Vector3.UnitZ, rot), 0.0f),
            CameraForward = new Vector4(Vector3.Transform(Vector3.UnitX, rot), 0.0f),
            Params = new Vector4(Radius, Intensity, Bias, 1.0f / r2),
            Params2 = new Vector4(projectionScale, gbuffer.Width, gbuffer.Height, MaxStepPixels),
            Params3 = new Vector4(Strength, 0.0f, 0.0f, 0.0f),
        };
        _dataBuffer.UpdateBuffer(data);

        // The G-buffer render texture is recreated on resize; avoid rebinding every frame.
        if (!ReferenceEquals(_boundGBuffer, gbuffer))
        {
            _hbaoMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _hbaoMaterial.SetRenderTexture("_normal", gbuffer, 1);
            _blurMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _blurMaterial.SetRenderTexture("_normal", gbuffer, 1);
            _boundGBuffer = gbuffer;
        }

        bool measureGpu = _gpuTimestamps != null && _gpuTimestamps.ShouldRecord;

        // Read back GPU timestamps from the previous sample (0.5s ago — no stall).
        if (measureGpu)
        {
            ulong[]? timestamps = _gpuTimestamps!.TryReadback();
            if (timestamps != null)
            {
                _aoGpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, 0, 1);
                _blurGpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, 1, 2);
            }
        }

        _commandBuffer.Begin();
        using (GPUCommandBuffer.ComputePass computePass = measureGpu
            ? _commandBuffer.BeginCompute(_gpuTimestamps!.QuerySet, 0, 2)
            : _commandBuffer.BeginCompute())
        {
            _hbaoMaterial.DispatchBySize(computePass, gbuffer.Width, gbuffer.Height, 1);

            if (measureGpu && _gpuTimestamps!.SupportsInPassTimestamps)
            {
                computePass.WriteTimestamp(_gpuTimestamps.QuerySet, 1);
            }

            _blurMaterial.DispatchBySize(computePass, gbuffer.Width, gbuffer.Height, 1);
        }
        if (measureGpu)
        {
            _commandBuffer.ResolveTimestamps(_gpuTimestamps!.QuerySet, 0, TimestampSlotCount, _gpuTimestamps.ResolveBuffer);
            _gpuTimestamps.EndSample();
        }
        _commandBuffer.End();
        _rendering.ScheduleCommandBuffer(_commandBuffer);

        // Lazily register profiler counters on the first Execute call.
        if (profiler != null)
        {
            if (!_profilerCounterRegistered)
            {
                _hbaoCounter = profiler.RegisterCounter("HBAO+", "Total");
                _aoCounter = profiler.RegisterCounter("HBAO+", "AO");
                _blurCounter = profiler.RegisterCounter("HBAO+", "Blur");
                _profilerCounterRegistered = true;
            }

            double elapsedMs = (double)(Stopwatch.GetTimestamp() - startTimestamp) / Stopwatch.Frequency * 1000.0;
            profiler.PushValue(_hbaoCounter, elapsedMs);
            profiler.PushValue(_aoCounter, _aoGpuMilliseconds);
            profiler.PushValue(_blurCounter, _blurGpuMilliseconds);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Output textures are graph-owned facades, disposed with the graph.
            _dataBuffer.Dispose();
            _gpuTimestamps?.Dispose();
            _commandBuffer.Dispose();
        }
    }
}
