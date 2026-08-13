using System.Numerics;
using Alco.Graphics;

using Alco;

namespace Alco.Rendering;

/// <summary>
/// A renderable object that the <see cref="GBufferRenderer"/> draws into the G-buffer.
/// The renderer reads these properties each frame (for dynamic items) or at
/// bundle-record time (for static items).
/// </summary>
public interface IGBufferRenderable
{
    /// <summary>Whether this object is static and should be baked into a render bundle.</summary>
    bool IsStatic { get; }

    /// <summary>The mesh to draw.</summary>
    Mesh Mesh { get; }

    /// <summary>The G-buffer material (created via <see cref="GBufferRenderer.CreateMaterial"/>).</summary>
    GraphicsMaterial Material { get; }

    /// <summary>The world transform of the object (read live each frame for dynamic items).</summary>
    Matrix4x4 WorldMatrix { get; }

    /// <summary>Linear base color (rgb), alpha multiplies the albedo texture alpha.</summary>
    Vector4 BaseColor { get; }

    /// <summary>x=metallic y=roughness z=ambient occlusion.</summary>
    Vector4 MetallicRoughnessAO { get; }

    /// <summary>Linear emissive color.</summary>
    Vector3 EmissiveFactor { get; }

    /// <summary>Alpha test threshold; 0 disables alpha testing.</summary>
    float AlphaCutoff { get; }
}

/// <summary>
/// A G-buffer content provider of the deferred PBR pipeline. Holds the G-buffer
/// shaders, material factory methods and a registry of <see cref="IGBufferRenderable"/>
/// objects. Static objects are baked into an internal render bundle; dynamic objects
/// are drawn immediately each frame. The owning <see cref="RGNode_GeometryPass"/> calls
/// <see cref="OnRender"/> automatically inside its open G-buffer pass (register via
/// <see cref="RGNode_GeometryPass.Content"/>).
/// <br/>The renderer does <b>not</b> own the G-buffer render texture, attachment layout
/// or render context — those are owned by the pass node.
/// </summary>
public sealed unsafe class GBufferRenderer : AutoDisposable, IRenderPassContent
{
    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;
    /// <summary>
    /// Push constant payload for a G-buffer draw. Layout must match the
    /// <c>Constants</c> struct in GBuffer.hlsl exactly.
    /// </summary>
    public struct DrawConstants
    {
        /// <summary>The world transform of the object (row-vector convention, compose scale → rotation → translation).</summary>
        public Matrix4x4 Model;
        /// <summary>Linear base color (rgb), alpha multiplies the albedo texture alpha.</summary>
        public Vector4 BaseColor;
        /// <summary>x=metallic y=roughness z=ambient occlusion, w is unused.</summary>
        public Vector4 MetallicRoughnessAO;
        /// <summary>x=alpha cutoff (0 disables alpha testing), yzw are unused.</summary>
        public Vector4 Params;
        /// <summary>Linear emissive color (rgb), w is unused.</summary>
        public Vector4 Emissive;

        /// <summary>
        /// Create draw constants for a PBR surface.
        /// </summary>
        public DrawConstants(in Matrix4x4 model, in Vector3 baseColor, float metallic, float roughness, float ambientOcclusion)
        {
            Model = model;
            BaseColor = new Vector4(baseColor, 1.0f);
            MetallicRoughnessAO = new Vector4(metallic, roughness, ambientOcclusion, 1.0f);
            Params = Vector4.Zero;
            Emissive = Vector4.Zero;
        }
    }

    private readonly RenderingSystem _rendering;
    private readonly Shader _shader;
    private Texture2D? _flatNormalTexture;
    private CameraPerspectiveBuffer? _camera;

    // Registered renderables split by static / dynamic.
    private readonly UnorderedList<IGBufferRenderable> _staticItems = new();
    private readonly UnorderedList<IGBufferRenderable> _dynamicItems = new();

    // Static render bundle — re-recorded only when dirty.
    private readonly SubRenderContext _staticBundle;
    private bool _staticBundleDirty = true;
    // Dynamic render bundle — re-recorded every frame so errors in recording
    // cannot corrupt the main render context.
    private readonly SubRenderContext _dynamicBundle;
    private GPUAttachmentLayout? _bundleLayout;

    /// <summary>
    /// Create the G-buffer renderer with the given shader.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="gbufferShader">The G-buffer shader (GBuffer.hlsl).</param>
    public GBufferRenderer(
        RenderingSystem rendering,
        Shader gbufferShader)
    {
        _rendering = rendering;
        _shader = gbufferShader;
        _staticBundle = rendering.CreateSubRenderContext("pbr_gbuffer_static");
        _dynamicBundle = rendering.CreateSubRenderContext("pbr_gbuffer_dynamic");
    }

    /// <summary>
    /// Set the camera used for G-buffer material binding.
    /// The caller must keep the camera updated (e.g. <c>UpdateMatrixToGPU</c>)
    /// before drawing each frame.
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
    public void Add(IGBufferRenderable item)
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
    public void Remove(IGBufferRenderable item)
    {
        _staticItems.Remove(item);
        _dynamicItems.Remove(item);
        _staticBundleDirty = true;
    }

    /// <summary>
    /// Mark the static render bundle as dirty so it is re-recorded on the next
    /// <see cref="OnRenderGBuffer"/>. Call after changing a static item's mesh,
    /// material or other bundle-recorded property.
    /// </summary>
    public void MarkStaticBundleDirty()
    {
        _staticBundleDirty = true;
    }

    // ── Pass content callback ──

    /// <summary>
    /// Draw all registered renderables into the G-buffer. Called by the owning
    /// <see cref="RGNode_GeometryPass"/> inside its open pass.
    /// Re-records the static bundle when dirty, replays it, then draws dynamic items.
    /// </summary>
    /// <param name="context">The live G-buffer pass scope.</param>
    /// <param name="layout">The G-buffer attachment layout (for bundle recording).</param>
    public void OnRender(RenderPassScope context, GPUAttachmentLayout layout)
    {
        _bundleLayout = layout;

        // Static bundle: re-record only when dirty.
        if (_staticItems.Count > 0)
        {
            if (_staticBundleDirty)
            {
                using (RenderPassScope bundle = _staticBundle.BeginPass(layout))
                {
                    for (int i = 0; i < _staticItems.Count; i++)
                    {
                        DrawItem(_staticItems[i], bundle);
                    }
                }
                _staticBundleDirty = false;
            }

            context.ExecuteSubContext(_staticBundle);
        }

        // Dynamic bundle: re-recorded every frame so recording errors stay
        // isolated from the main render context.
        if (_dynamicItems.Count > 0)
        {
            using (RenderPassScope bundle = _dynamicBundle.BeginPass(layout))
            {
                for (int i = 0; i < _dynamicItems.Count; i++)
                {
                    DrawItem(_dynamicItems[i], bundle);
                }
            }
            context.ExecuteSubContext(_dynamicBundle);
        }
    }

    /// <summary>
    /// Draw a single renderable into the given context (immediate or bundle).
    /// </summary>
    private static void DrawItem(IGBufferRenderable item, IRenderContext target)
    {
        target.DrawWithConstant(item.Mesh, item.Material,
            new DrawConstants
            {
                Model = item.WorldMatrix,
                BaseColor = item.BaseColor,
                MetallicRoughnessAO = item.MetallicRoughnessAO,
                Params = new Vector4(item.AlphaCutoff, 0.0f, 0.0f, 0.0f),
                Emissive = new Vector4(item.EmissiveFactor, 1.0f),
            });
    }

    // ── Material factory ──

    /// <summary>
    /// Create a G-buffer material with per-material albedo, normal, metallic-roughness
    /// and emissive textures. The renderer applies the pass-mandated state (depth write,
    /// rasterizer, texture slots, camera binding); the caller owns the material and must
    /// dispose it.
    /// </summary>
    public GraphicsMaterial CreateMaterial(
        Texture2D? albedoTexture, Texture2D? normalTexture, Texture2D? metallicRoughnessTexture,
        Texture2D? emissiveTexture, bool doubleSided = false, string name = "pbr_gbuffer_material")
    {
        var material = _rendering.CreateMaterial(_shader, name);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        SetMaterialTextures(material, albedoTexture, normalTexture, metallicRoughnessTexture, emissiveTexture);
        if (_camera != null)
        {
            material.SetBuffer(ShaderResourceId.Camera, _camera);
        }
        return material;
    }

    /// <summary>
    /// (Re)bind the texture slots of a G-buffer material created by
    /// <see cref="CreateMaterial"/>, applying the same fallback textures.
    /// Use when textures stream in asynchronously after the material was created.
    /// </summary>
    public void SetMaterialTextures(
        GraphicsMaterial material, Texture2D? albedoTexture, Texture2D? normalTexture,
        Texture2D? metallicRoughnessTexture, Texture2D? emissiveTexture)
    {
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        material.SetTexture("_normalTexture", normalTexture ?? GetOrCreateFlatNormalTexture());
        material.SetTexture("_mrTexture", metallicRoughnessTexture ?? _rendering.TextureWhite);
        material.SetTexture("_emissiveTexture", emissiveTexture ?? _rendering.TextureBlack);
    }

    /// <summary>
    /// Lazily create the 1x1 flat-normal fallback texture: (128,128,255) decodes to the
    /// identity tangent-space normal.
    /// </summary>
    private Texture2D GetOrCreateFlatNormalTexture()
    {
        if (_flatNormalTexture == null)
        {
            byte[] data = [128, 128, 255, 255];
            _flatNormalTexture = _rendering.CreateTexture2D(data, 1, 1,
                new ImageLoadOption(format: PixelFormat.RGBA8Unorm, addressMode: AddressMode.Repeat, filterMode: FilterMode.Linear, name: "pbr_flat_normal"));
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
