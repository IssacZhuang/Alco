using System.Numerics;
using Alco.Graphics;

using Alco;

namespace Alco.Rendering;

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

    /// <summary>The forward glass material (created via <see cref="RGNode_Forward.CreateGlassMaterial"/>).</summary>
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
/// pipeline. Holds the glass shader, material factory methods and a registry of
/// <see cref="IForwardRenderable"/> objects. Static objects are baked into an
/// internal render bundle; dynamic objects are drawn immediately each frame.
/// The pass scope comes from the graph's frame-shared render context — the node
/// no longer owns a render context of its own.
/// </summary>
public sealed unsafe class RGNode_Forward : RGNode_SceneContent
{
    /// <summary>
    /// Push constant payload for a forward glass draw. Layout must match the
    /// <c>Constants</c> struct in ForwardGlass.hlsl exactly.
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
    private readonly Shader _glassShader;
    private Texture2D? _flatNormalTexture;
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

    /// <summary>
    /// Create the forward renderer with the glass shader and shared pipeline resources.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="graph">The render graph the node is registered in.</param>
    /// <param name="chain">The pipeline's content chain (the node draws into its current target).</param>
    /// <param name="glassShader">The ForwardGlass.hlsl shader.</param>
    /// <param name="lightingDataBuffer">The deferred lighting data buffer (shared with the pipeline).</param>
    /// <param name="pointLightBuffer">The point light buffer (shared with the pipeline).</param>
    /// <param name="shadowRT">The shadow map render texture (for shadow comparison sampling).</param>
    public RGNode_Forward(
        RenderingSystem rendering,
        RenderGraph graph,
        RenderChain chain,
        Shader glassShader,
        GraphicsBuffer lightingDataBuffer,
        GraphicsBuffer pointLightBuffer,
        RenderTexture shadowRT)
        : base(graph, chain)
    {
        _rendering = rendering;
        _glassShader = glassShader;
        _lightingDataBuffer = lightingDataBuffer;
        _pointLightBuffer = pointLightBuffer;
        _shadowRT = shadowRT;
        _staticBundle = rendering.CreateSubRenderContext("pbr_forward_static");
        _dynamicBundle = rendering.CreateSubRenderContext("pbr_forward_dynamic");
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
        if (_staticItems.Count == 0 && _dynamicItems.Count == 0)
        {
            return;
        }

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

        using (RenderPassScope pass = context.RenderContext.BeginPass(target))
        {
            if (_staticItems.Count > 0)
            {
                pass.ExecuteSubContext(_staticBundle);
            }
            if (dynamicBundle != null)
            {
                pass.ExecuteSubContext(dynamicBundle);
            }
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
    /// Create a glass material for the ForwardGlass shader with per-material
    /// albedo, normal, metallic-roughness and emissive textures. The material
    /// uses alpha blending (no accumulation, no sorting needed) and hardware
    /// depth testing against opaque geometry (the forward target shares the
    /// G-buffer's depth attachment through the graph's depth sharing — no depth
    /// copy). The caller owns the material.
    /// </summary>
    public GraphicsMaterial CreateGlassMaterial(
        Texture2D? albedoTexture, Texture2D? normalTexture, Texture2D? metallicRoughnessTexture,
        Texture2D? emissiveTexture, bool doubleSided = false, string name = "pbr_glass_material")
    {
        var material = _rendering.CreateMaterial(_glassShader, name);
        material.BlendState = BlendState.AlphaBlendNoAccumulation;
        material.DepthStencilState = DepthStencilState.Read; // hardware depth test (LessEqual, no write)
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        SetGlassMaterialTextures(material, albedoTexture, normalTexture, metallicRoughnessTexture, emissiveTexture);
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

    /// <summary>
    /// (Re)bind the texture slots of a glass material created by
    /// <see cref="CreateGlassMaterial"/>, applying the same fallback textures.
    /// Use when textures stream in asynchronously after the material was created.
    /// </summary>
    public void SetGlassMaterialTextures(
        GraphicsMaterial material, Texture2D? albedoTexture, Texture2D? normalTexture,
        Texture2D? metallicRoughnessTexture, Texture2D? emissiveTexture)
    {
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        material.SetTexture("_normalTexture", normalTexture ?? GetOrCreateFlatNormalTexture());
        material.SetTexture("_mrTexture", metallicRoughnessTexture ?? _rendering.TextureWhite);
        // ForwardGlass has no emissive texture slot in the basic version;
        // when one is added, bind emissiveTexture ?? _rendering.TextureBlack here.
    }

    /// <summary>
    /// Lazily create the 1x1 flat-normal fallback texture: (128,128,255).
    /// </summary>
    private Texture2D GetOrCreateFlatNormalTexture()
    {
        if (_flatNormalTexture == null)
        {
            byte[] data = [128, 128, 255, 255];
            _flatNormalTexture = _rendering.CreateTexture2D(data, 1, 1,
                new ImageLoadOption(format: PixelFormat.RGBA8Unorm, addressMode: AddressMode.Repeat, filterMode: FilterMode.Linear, name: "pbr_flat_normal_forward"));
        }
        return _flatNormalTexture;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _staticBundle.Dispose();
            _dynamicBundle.Dispose();
            _flatNormalTexture?.Dispose();
        }
    }
}
