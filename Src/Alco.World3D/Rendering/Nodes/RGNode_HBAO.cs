using System.Numerics;
using Alco.Graphics;

using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// HBAO+ (horizon-based ambient occlusion) renderer for deferred PBR compositions.
/// <br/>Reads the G-buffer depth and world-normal attachments, marches screen-space
/// horizon rays in a compute pass (HBAO.slang) and filters the noisy result with a
/// depth/normal-aware bilateral blur (HBAOBlur.slang). The blur pass writes the
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
    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly ComputeMaterial _hbaoMaterial;
    private readonly ComputeMaterial _blurMaterial;
    private readonly UniformGraphicsBuffer _dataBuffer;

    // GPU timestamp ring buffer for per-stage timing (slot 0 = pass begin,
    // slot 1 = after AO before Blur, slot 2 = pass end).
    private const int TimestampSlotCount = 3;
    private readonly GpuTimestampSampler? _gpuTimestamps;
    private double _aoGpuMilliseconds;
    private double _blurGpuMilliseconds;

    // Facades of the graph-owned transients below; null until Attach creates them.
    // They are not disposed here and are rematerialized by the graph on resize.
    private RenderTexture? _rawAO;
    private RenderTexture? _aoResult;
    private RenderTexture? _boundGBuffer;

    // Graph-owned transient resources.
    private RenderGraph? _graph;
    private RGNode_DeferredLighting? _lighting;
    private RenderGraphTexture? _gbufferResource;
    private PBRSceneEnvironment? _environment;
    private RenderGraphTexture? _rawAOResource;
    private RenderGraphTexture? _aoResource;

    // Profiler counter handles — lazily registered on first Execute call.
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
    /// <exception cref="InvalidOperationException">The renderer is not attached to a graph.</exception>
    public RenderTexture AOResult => _aoResult
        ?? throw new InvalidOperationException("The HBAO renderer is not attached to a graph (call Attach first).");

    /// <summary>
    /// The node's construction data: the raw AO shader and the bilateral blur
    /// shader. Service-type dependencies (the rendering system) are explicit
    /// constructor parameters instead — a descriptor is pure data. No GPU
    /// textures are allocated at construction — the AO textures are graph
    /// transients created by <see cref="Attach"/>.
    /// </summary>
    public readonly struct Descriptor
    {
        /// <summary>The raw AO shader (HBAO.slang).</summary>
        public required Shader HbaoShader { get; init; }
        /// <summary>The bilateral blur shader (HBAOBlur.slang).</summary>
        public required Shader BlurShader { get; init; }
    }

    /// <summary>
    /// Create the HBAO+ renderer from its descriptor's compute shaders.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="descriptor">The node's construction data.</param>
    public RGNode_HBAO(RenderingSystem rendering, in Descriptor descriptor)
    {
        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _hbaoMaterial = rendering.CreateComputeMaterial(descriptor.HbaoShader);
        _blurMaterial = rendering.CreateComputeMaterial(descriptor.BlurShader);
        // Reflection-driven uniform buffer over the shared _data block — no CPU
        // twin of HbaoData; members land by name at their reflected offsets.
        _dataBuffer = rendering.CreateUniformGraphicsBuffer(
            descriptor.HbaoShader.GetShaderModules().ReflectionInfo.UniformBlocks.First(block => block.Name == "data"),
            "hbao_data");
        _hbaoMaterial.SetBuffer("data", _dataBuffer);
        _blurMaterial.SetBuffer("data", _dataBuffer);

        if (_device.IsFeatureSupported(GPUFeatures.TimestampQuery))
        {
            _gpuTimestamps = new GpuTimestampSampler(_device, TimestampSlotCount, "hbao");
        }
    }

    /// <summary>
    /// Attaches the renderer to a deferred composition as a direct
    /// <see cref="IRenderGraphNode"/> in the graph: creates the transient AO result
    /// and raw-AO intermediate, registers itself immediately before the lighting
    /// node, and wires the result to <see cref="RGNode_DeferredLighting.AoInput"/> and
    /// the lighting material's _aoTexture slot. After attachment the graph drives
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
        // RGBA16Float (light map layout): proven as both a compute storage target and
        // a filterable sampled texture.
        _aoResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, name: "hbao_ao"));
        _rawAOResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, name: "hbao_raw"));
        _rawAO = _rawAOResource.Texture;
        _aoResult = _aoResource.Texture;
        _hbaoMaterial.SetRenderTexture("aoOutput", _rawAO);
        _blurMaterial.SetRenderTexture("aoInput", _rawAO);
        _blurMaterial.SetRenderTexture("aoResult", _aoResult);
        graph.InsertBefore(lighting, this);
        lighting.AoInput = _aoResource;
        lighting.Material.SetRenderTexture("aoTexture", _aoResult);
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
        _rawAO = null;
        _aoResult = null;
        if (_lighting != null)
        {
            _lighting.AoInput = null;
            _lighting.Material.SetTexture("aoTexture", _rendering.TextureWhite);
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
        ExecuteCore(camera.Data.ProjectionMatrix, invViewProjection, camera.Transform, gbuffer, context.RenderContext.CommandBuffer, _graph?.Profiler);
    }

    // Shared body of the execute path: assembles the per-frame constants and records
    // the AO and blur dispatches into the frame-shared command buffer.
    private void ExecuteCore(
        in Matrix4x4 projectionMatrix,
        in Matrix4x4 invViewProjection,
        Transform3D cameraTransform,
        RenderTexture gbuffer,
        GPUCommandBuffer commandBuffer,
        RenderProfiler? profiler)
    {
        // ── Assemble the GPU constant buffer internally ──
        // Camera basis axes are derived from the camera rotation quaternion.
        // The engine camera convention is +X forward, +Y right, +Z up.
        Quaternion rot = cameraTransform.Rotation;
        float r2 = MathF.Max(Radius * Radius, 1e-6f);
        float projectionScale = 0.5f * gbuffer.Height * projectionMatrix.M22;
        _dataBuffer.SetValue("invViewProjection", invViewProjection);
        _dataBuffer.SetValue("cameraPosition", new Vector4(cameraTransform.Position, 0.0f));
        _dataBuffer.SetValue("cameraRight", new Vector4(Vector3.Transform(Vector3.UnitY, rot), 0.0f));
        _dataBuffer.SetValue("cameraUp", new Vector4(Vector3.Transform(Vector3.UnitZ, rot), 0.0f));
        _dataBuffer.SetValue("cameraForward", new Vector4(Vector3.Transform(Vector3.UnitX, rot), 0.0f));
        _dataBuffer.SetValue("radius", Radius);
        _dataBuffer.SetValue("intensity", Intensity);
        _dataBuffer.SetValue("bias", Bias);
        _dataBuffer.SetValue("invRadius2", 1.0f / r2);
        _dataBuffer.SetValue("projScale", projectionScale);
        _dataBuffer.SetValue("viewportWidth", gbuffer.Width);
        _dataBuffer.SetValue("viewportHeight", gbuffer.Height);
        _dataBuffer.SetValue("maxStepPixels", MaxStepPixels);
        _dataBuffer.SetValue("strength", Strength);
        _dataBuffer.Flush();

        // The G-buffer render texture is recreated on resize; avoid rebinding every frame.
        if (!ReferenceEquals(_boundGBuffer, gbuffer))
        {
            _hbaoMaterial.SetRenderTextureDepth("gbufferDepth", gbuffer);
            _hbaoMaterial.SetRenderTexture("normal", gbuffer, 1);
            _blurMaterial.SetRenderTextureDepth("gbufferDepth", gbuffer);
            _blurMaterial.SetRenderTexture("normal", gbuffer, 1);
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

        // Record into the graph's frame-shared command buffer; the graph submits
        // it once at the end of the frame.
        using (GPUCommandBuffer.ComputePass computePass = measureGpu
            ? commandBuffer.BeginCompute(_gpuTimestamps!.QuerySet, 0, 2)
            : commandBuffer.BeginCompute())
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
            commandBuffer.ResolveTimestamps(_gpuTimestamps!.QuerySet, 0, TimestampSlotCount, _gpuTimestamps.ResolveBuffer);
            _gpuTimestamps.EndSample();
        }

        // Lazily register profiler counters on the first Execute call.
        if (profiler != null)
        {
            if (!_profilerCounterRegistered)
            {
                _aoCounter = profiler.RegisterCounter("HBAO+", "AO");
                _blurCounter = profiler.RegisterCounter("HBAO+", "Blur");
                _profilerCounterRegistered = true;
            }

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
            // Compute materials hold no native resources of their own (bind groups
            // are cache-retained by the parameter set) and are not disposable.
            _dataBuffer.Dispose();
            _gpuTimestamps?.Dispose();
        }
    }
}
