using System.Diagnostics;
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// HBAO+ (horizon-based ambient occlusion) renderer for the deferred PBR pipeline.
/// <br/>Reads the G-buffer depth and world-normal attachments, marches screen-space
/// horizon rays in a compute pass (HBAO.hlsl) and filters the noisy result with a
/// depth/normal-aware bilateral blur (HBAOBlur.hlsl). The blur pass writes the
/// filtered AO to a standalone full-resolution texture (<see cref="AOResult"/>),
/// which the pipeline binds to the deferred lighting material's _aoTexture slot.
/// <br/>Implements <see cref="IRenderPlugin"/> so it can be registered with
/// <see cref="PBRDeferredPipeline.RegisterPlugin"/> and executes automatically at
/// the <see cref="RenderInjectionPoint.AfterGBuffer"/> injection point.
/// </summary>
public sealed class HbaoRenderer : AutoDisposable, IRenderPlugin
{
    /// <summary>
    /// Per-frame HBAO data uploaded to both compute passes. Layout must match the
    /// <c>_data</c> cbuffer in HBAOCommon.hlsli exactly. Assembled internally by
    /// the renderer from <see cref="RenderPluginContext"/> and user-tunable
    /// properties.
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

    /// <inheritdoc />
    public string Name => "HBAO+";

    /// <inheritdoc />
    public RenderInjectionPoint InjectionPoint => RenderInjectionPoint.AfterGBuffer;

    /// <summary>
    /// Create the HBAO+ renderer with the given compute shaders.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="hbaoShader">The raw AO shader (HBAO.hlsl).</param>
    /// <param name="blurShader">The bilateral blur shader (HBAOBlur.hlsl).</param>
    /// <param name="width">The initial AO texture width in pixels (match the G-buffer).</param>
    /// <param name="height">The initial AO texture height in pixels (match the G-buffer).</param>
    public HbaoRenderer(RenderingSystem rendering, Shader hbaoShader, Shader blurShader, uint width, uint height)
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

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        // In-place resize: the materials keep referencing the same wrappers, the bind
        // groups are rebuilt automatically through the render texture version check.
        _rawAO.Resize(width, height);
        _aoResult.Resize(width, height);
        _boundGBuffer = null;
    }

    /// <inheritdoc />
    public void Execute(RenderPluginContext context)
    {
        long startTimestamp = Stopwatch.GetTimestamp();

        // ── Assemble the GPU constant buffer internally ──
        // Camera basis axes are derived from the camera rotation quaternion.
        // The engine camera convention is +X forward, +Y right, +Z up.
        Quaternion rot = context.CameraTransform.Rotation;
        float r2 = MathF.Max(Radius * Radius, 1e-6f);
        float projectionScale = 0.5f * context.Height * context.ProjectionMatrix.M22;
        HbaoData data = new()
        {
            InvViewProjection = context.InvViewProjection,
            CameraPosition = new Vector4(context.CameraTransform.Position, 0.0f),
            CameraRight = new Vector4(Vector3.Transform(Vector3.UnitY, rot), 0.0f),
            CameraUp = new Vector4(Vector3.Transform(Vector3.UnitZ, rot), 0.0f),
            CameraForward = new Vector4(Vector3.Transform(Vector3.UnitX, rot), 0.0f),
            Params = new Vector4(Radius, Intensity, Bias, 1.0f / r2),
            Params2 = new Vector4(projectionScale, context.GBuffer.Width, context.GBuffer.Height, MaxStepPixels),
            Params3 = new Vector4(Strength, 0.0f, 0.0f, 0.0f),
        };
        _dataBuffer.UpdateBuffer(data);

        RenderTexture gbuffer = context.GBuffer;

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
        _device.Submit(_commandBuffer);

        context.AOResult = _aoResult;

        // Lazily register profiler counters on the first Execute call.
        if (!_profilerCounterRegistered)
        {
            _hbaoCounter = context.Profiler.RegisterCounter("HBAO+", "Total");
            _aoCounter = context.Profiler.RegisterCounter("HBAO+", "AO");
            _blurCounter = context.Profiler.RegisterCounter("HBAO+", "Blur");
            _profilerCounterRegistered = true;
        }

        double elapsedMs = (double)(Stopwatch.GetTimestamp() - startTimestamp) / Stopwatch.Frequency * 1000.0;
        context.Profiler.PushValue(_hbaoCounter, elapsedMs);
        context.Profiler.PushValue(_aoCounter, _aoGpuMilliseconds);
        context.Profiler.PushValue(_blurCounter, _blurGpuMilliseconds);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rawAO.Dispose();
            _aoResult.Dispose();
            _dataBuffer.Dispose();
            _gpuTimestamps?.Dispose();
            _commandBuffer.Dispose();
        }
    }
}
