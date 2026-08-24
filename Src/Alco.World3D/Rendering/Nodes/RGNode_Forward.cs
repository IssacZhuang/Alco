using System.Numerics;
using Alco.Graphics;

using Alco;

using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// A renderable object that the <see cref="RGNode_Forward"/> draws in the
/// forward transparency pass (after deferred lighting). Glass objects use this
/// to blend semi-transparently onto the lit HDR scene.
/// </summary>
public interface IForwardRenderable
{
    /// <summary>Whether this object is static and should be baked into a render bundle.</summary>
    bool IsStatic { get; }

    /// <summary>The mesh to draw.</summary>
    Mesh Mesh { get; }

    /// <summary>The forward glass material (compiled via the <see cref="RGNode_Forward"/>
    /// "glass" material pass).</summary>
    GraphicsMaterial Material { get; }

    /// <summary>The world transform of the object.</summary>
    Matrix4x4 WorldMatrix { get; }

    /// <summary>Linear base color (rgb), alpha multiplies the albedo texture alpha.</summary>
    Vector4 BaseColor { get; }

    /// <summary>x=metallic y=roughness z=ambient occlusion.</summary>
    Vector4 MetallicRoughnessAO { get; }

    /// <summary>Linear emissive color.</summary>
    Vector3 EmissiveFactor { get; }

    /// <summary>
    /// Transmission factor in [0, 1]: 0 = opaque, 1 = fully transparent.
    /// Higher values reduce the output alpha so more of the background shows through.
    /// </summary>
    float TransmissionFactor { get; }
}

/// <summary>
/// A scene content node drawing the transparency pass of the deferred PBR
/// pipeline. Registers the "glass" material pass on the <see cref="MaterialCompiler"/>
/// (the <c>glass.slang</c> template composed per material asset, participating
/// only for blend materials) and holds a registry of <see cref="IForwardRenderable"/>
/// objects. Static objects are baked into an internal render bundle; dynamic objects
/// are drawn immediately each frame. The pass scope comes from the graph's
/// frame-shared render context — the node no longer owns a render context of its own.
/// </summary>
public sealed unsafe class RGNode_Forward : RGNode_SceneContent, IMaterialPass<PbrMaterialAsset>
{
    /// <summary>The material-pass identifier this node registers ("glass").</summary>
    public const string PassId = "glass";
    /// <summary>
    /// Push constant payload for a forward glass draw. Layout must match the
    /// <c>Constants</c> struct in ForwardGlass.slang exactly.
    /// </summary>
    public struct DrawConstants
    {
        /// <summary>The world transform of the object.</summary>
        public Matrix4x4 Model;
        /// <summary>Linear base color (rgb), alpha multiplies the albedo texture alpha.</summary>
        public Vector4 BaseColor;
        /// <summary>x=metallic y=roughness z=ambient occlusion, w is unused.</summary>
        public Vector4 MetallicRoughnessAO;
        /// <summary>x=transmission factor (0=opaque, 1=fully transparent), yzw unused.</summary>
        public Vector4 Params;
        /// <summary>Linear emissive color (rgb), w is unused.</summary>
        public Vector4 Emissive;

        /// <summary>Create draw constants for a glass surface.</summary>
        public DrawConstants(in Matrix4x4 model, in Vector3 baseColor, float metallic, float roughness,
            float ambientOcclusion, float transmission, in Vector3 emissive)
        {
            Model = model;
            BaseColor = new Vector4(baseColor, 1.0f);
            MetallicRoughnessAO = new Vector4(metallic, roughness, ambientOcclusion, 1.0f);
            Params = new Vector4(transmission, 0.0f, 0.0f, 0.0f);
            Emissive = new Vector4(emissive, 1.0f);
        }
    }

    private readonly RenderingSystem _rendering;
    private CameraPerspectiveBuffer? _camera;

    // Pipeline resources bound to every glass material (shared with the deferred pipeline).
    private readonly GraphicsBuffer _lightingDataBuffer;
    private readonly GraphicsBuffer _pointLightBuffer;
    private readonly RenderTexture _shadowRT;

    // Registered renderables split by static / dynamic.
    private readonly UnorderedList<IForwardRenderable> _staticItems = new();
    private readonly UnorderedList<IForwardRenderable> _dynamicItems = new();

    // Static render bundle — re-recorded only when dirty.
    private readonly SubRenderContext _staticBundle;
    private bool _staticBundleDirty = true;
    // Dynamic render bundle — re-recorded every frame.
    private readonly SubRenderContext _dynamicBundle;
    private GPUAttachmentLayout? _bundleLayout;

    // Per-pass GPU timing (throttled sampler, one slot pair wrapping the final
    // pass) and the profiler counter lazily registered on the first render. The
    // cached GPU duration is re-pushed every frame (BeginFrame clears buffers).
    private readonly GpuTimestampSampler? _gpuTimestamps;
    private RenderProfileCounterId _gpuCounter;
    private bool _profilerCountersRegistered;
    private double _gpuMilliseconds;

    /// <summary>
    /// Create the forward renderer, registering the "glass" material pass on the
    /// compiler (blend materials only), with the shared pipeline resources.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="graph">The render graph the node is registered in.</param>
    /// <param name="chain">The pipeline's content chain (the node draws into its current target).</param>
    /// <param name="compiler">The material compiler the "glass" pass registers on.</param>
    /// <param name="lightingDataBuffer">The deferred lighting data buffer (shared with the pipeline).</param>
    /// <param name="pointLightBuffer">The point light buffer (shared with the pipeline).</param>
    /// <param name="shadowRT">The shadow map render texture (for shadow comparison sampling).</param>
    public RGNode_Forward(
        RenderingSystem rendering,
        RenderGraph graph,
        RenderChain chain,
        MaterialCompiler compiler,
        GraphicsBuffer lightingDataBuffer,
        GraphicsBuffer pointLightBuffer,
        RenderTexture shadowRT)
        : base(graph, chain)
    {
        _rendering = rendering;
        _lightingDataBuffer = lightingDataBuffer;
        _pointLightBuffer = pointLightBuffer;
        _shadowRT = shadowRT;
        _staticBundle = rendering.CreateSubRenderContext("pbr_forward_static");
        _dynamicBundle = rendering.CreateSubRenderContext("pbr_forward_dynamic");

        if (rendering.GraphicsDevice.TimestampQuerySupported)
        {
            _gpuTimestamps = new GpuTimestampSampler(rendering.GraphicsDevice, 2, "forward_pass");
        }

        compiler.RegisterPass(this);
    }

    /// <summary>
    /// Set the camera used for glass material binding.
    /// </summary>
    public void SetCamera(CameraPerspectiveBuffer camera)
    {
        _camera = camera;
    }

    // ── Renderable registry ──

    /// <summary>
    /// Register a renderable. Static items are baked into the internal render bundle;
    /// dynamic items are drawn immediately each frame.
    /// </summary>
    public void Add(IForwardRenderable item)
    {
        if (item.IsStatic)
        {
            _staticItems.Add(item);
        }
        else
        {
            _dynamicItems.Add(item);
        }
        _staticBundleDirty = true;
    }

    /// <summary>
    /// Unregister a renderable.
    /// </summary>
    public void Remove(IForwardRenderable item)
    {
        _staticItems.Remove(item);
        _dynamicItems.Remove(item);
        _staticBundleDirty = true;
    }

    /// <summary>
    /// Mark the static render bundle as dirty so it is re-recorded on the next
    /// <see cref="OnRender"/>. Call after changing a static item's mesh,
    /// material or other bundle-recorded property.
    /// </summary>
    public void MarkStaticBundleDirty()
    {
        _staticBundleDirty = true;
    }

    // ── Pipeline callback ──

    /// <summary>Whether any renderable is registered (static or dynamic).</summary>
    public bool HasContent => _staticItems.Count > 0 || _dynamicItems.Count > 0;

    /// <summary>
    /// Draw all registered renderables onto <paramref name="target"/> (the chain's
    /// current target after deferred lighting, pre-filled with the scene depth).
    /// Called by the graph automatically.
    /// </summary>
    /// <param name="context">The frame's graph context — the forward pass scope is
    /// opened on its frame-shared <see cref="RenderGraphContext.RenderContext"/>.</param>
    protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
    {
        bool measureGpu = _gpuTimestamps != null && _gpuTimestamps.ShouldRecord;

        if (_staticItems.Count > 0 || _dynamicItems.Count > 0)
        {
            _bundleLayout = layout;

            if (_staticItems.Count > 0 && _staticBundleDirty)
            {
                using (RenderPassScope pass = _staticBundle.BeginPass(layout))
                {
                    for (int i = 0; i < _staticItems.Count; i++)
                    {
                        DrawItem(_staticItems[i], pass);
                    }
                }
                _staticBundleDirty = false;
            }

            SubRenderContext? dynamicBundle = null;
            if (_dynamicItems.Count > 0)
            {
                using (RenderPassScope pass = _dynamicBundle.BeginPass(layout))
                {
                    for (int i = 0; i < _dynamicItems.Count; i++)
                    {
                        DrawItem(_dynamicItems[i], pass);
                    }
                }
                dynamicBundle = _dynamicBundle;
            }

            using (RenderPassScope pass = measureGpu
                ? context.RenderContext.BeginPass(target, ReadOnlySpan<ClearColorData>.Empty,
                    _gpuTimestamps!.QuerySet, 0, 1)
                : context.RenderContext.BeginPass(target))
            {
                if (_staticItems.Count > 0)
                {
                    pass.ExecuteSubContext(_staticBundle);
                }
                if (dynamicBundle != null)
                {
                    pass.ExecuteSubContext(dynamicBundle);
                }
                if (measureGpu)
                {
                    pass.ResolveTimestampsOnEnd(_gpuTimestamps!.QuerySet, 0, 2, _gpuTimestamps.ResolveBuffer);
                }
            }
        }

        // Lazily register the GPU counter; the cached GPU duration is re-pushed
        // every frame (BeginFrame cleared the buffers). The readback below is
        // synchronous but reads the previous sample — the recorded resolve has
        // not executed yet (submission happens at frame end).
        RenderProfiler? profiler = context.Profiler;
        if (profiler != null && !_profilerCountersRegistered)
        {
            if (_gpuTimestamps != null)
            {
                _gpuCounter = profiler.RegisterCounter("Forward", "GPU");
            }
            _profilerCountersRegistered = true;
        }

        if (measureGpu)
        {
            ulong[]? timestamps = _gpuTimestamps!.TryReadback();
            if (timestamps != null)
            {
                _gpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, 0, 1);
            }
            _gpuTimestamps.EndSample();
        }

        if (profiler != null && _gpuTimestamps != null)
        {
            profiler.PushValue(_gpuCounter, _gpuMilliseconds);
        }
    }

    /// <summary>
    /// Draw a single renderable into the given context (immediate or bundle).
    /// </summary>
    private static void DrawItem(IForwardRenderable item, IRenderContext target)
    {
        target.DrawWithConstant(item.Mesh, item.Material,
            new DrawConstants
            {
                Model = item.WorldMatrix,
                BaseColor = item.BaseColor,
                MetallicRoughnessAO = item.MetallicRoughnessAO,
                Params = new Vector4(item.TransmissionFactor, 0.0f, 0.0f, 0.0f),
                Emissive = new Vector4(item.EmissiveFactor, 1.0f),
            });
    }

    // ── Material factory ──

    /// <summary>
    /// Create a glass material from a pass-template shader already composed with its
    /// surface (see <see cref="MaterialCompiler"/>): applies the pass-mandated state —
    /// alpha blending (no accumulation, no sorting needed), reversed-depth hardware
    /// testing against opaque geometry (the forward target shares the G-buffer's
    /// depth attachment through the graph's depth sharing — no depth copy), cull
    /// mode, the camera/lighting buffer bindings and the shadow-map depth binding.
    /// Called back by the registered pass descriptor; the compiler owns the
    /// returned material.
    /// </summary>
    /// <param name="shader">The composed glass template shader.</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    public GraphicsMaterial CreateGlassMaterial(Shader shader, bool doubleSided = false, string name = "pbr_glass_material")
    {
        var material = _rendering.CreateMaterial(shader, name);
        material.BlendState = BlendState.AlphaBlendNoAccumulation;
        material.DepthStencilState = DepthStencilState.ReadReverseZ; // hardware depth test (GreaterEqual on reversed depth, no write)
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        if (_camera != null)
        {
            material.SetBuffer(ShaderResourceId.Camera, _camera);
        }
        // Shared pipeline buffers.
        material.SetBuffer(ShaderResourceId.Data, _lightingDataBuffer);
        material.SetBuffer(ShaderResourceId.PointLights, _pointLightBuffer);
        // Shadow map for shadow comparison (the forward target shares the
        // G-buffer's depth attachment, so no G-buffer depth sampling is needed).
        material.SetRenderTextureDepth("_shadowMap", _shadowRT);
        return material;
    }

    // ── IMaterialPass<PbrMaterialAsset> ──

    string IMaterialPass.Id => PassId;

    string IMaterialPass.TemplateModule => "glass";

    GraphicsMaterial IMaterialPass<PbrMaterialAsset>.CreateMaterial(PbrMaterialAsset asset, Shader shader)
        => CreateGlassMaterial(shader, asset.DoubleSided, $"{asset.Name}_glass");

    bool IMaterialPass<PbrMaterialAsset>.Accepts(PbrMaterialAsset asset)
        => asset.AlphaMode == MeshAlphaMode.Blend;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _staticBundle.Dispose();
            _dynamicBundle.Dispose();
            _gpuTimestamps?.Dispose();
        }
    }
}
