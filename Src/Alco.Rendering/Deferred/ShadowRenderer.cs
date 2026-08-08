using System.Numerics;
using Alco.Graphics;

using Alco;

namespace Alco.Rendering;

/// <summary>
/// A renderable object that the <see cref="ShadowRenderer"/> draws into the shadow map.
/// The renderer reads these properties each frame (for dynamic items) or at
/// bundle-record time (for static items).
/// </summary>
public interface IShadowRenderable
{
    /// <summary>Whether this object is static and should be baked into a render bundle.</summary>
    bool IsStatic { get; }

    /// <summary>Whether this object casts shadows. Non-casters are skipped entirely.</summary>
    bool CastsShadow { get; }

    /// <summary>The mesh to draw.</summary>
    Mesh Mesh { get; }

    /// <summary>The shadow material (created via <see cref="ShadowRenderer.CreateShadowMaterial"/> /
    /// <see cref="ShadowRenderer.CreateShadowCutoutMaterial"/>).</summary>
    GraphicsMaterial Material { get; }

    /// <summary>The world transform of the object (read live each frame for dynamic items).</summary>
    Matrix4x4 WorldMatrix { get; }

    /// <summary>Alpha test threshold; 0 disables alpha testing (opaque).</summary>
    float AlphaCutoff { get; }

    /// <summary>Base-color alpha multiplier used in cutout alpha testing.</summary>
    float BaseColorAlpha { get; }
}

/// <summary>
/// A shadow render node of the deferred PBR pipeline. Holds the shadow depth
/// shaders, material factory methods and a registry of <see cref="IShadowRenderable"/>
/// objects. Static objects are baked into internal per-cascade render bundles; dynamic
/// objects are drawn immediately each frame. The pipeline calls <see cref="OnRenderShadow"/>
/// automatically between <c>BeginShadowPass</c> and <c>EndShadowPass</c>.
/// <br/>The renderer does <b>not</b> own the shadow render texture, attachment layout,
/// render context or cascade VP data buffer — those are owned by
/// <see cref="PBRDeferredPipeline"/>.
/// </summary>
public sealed unsafe class ShadowRenderer : AutoDisposable, IShadowRenderNode
{
    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;
    /// <summary>
    /// Push constant payload for a shadow map draw. Layout must match the
    /// <c>Constants</c> struct in ShadowDepth.hlsl exactly. The per-cascade light
    /// view-projection matrices are read from the <c>_data</c> uniform buffer instead,
    /// so the constants stay static for static geometry (render-bundle friendly).
    /// <para>For cutout variants, <see cref="Params"/>.y carries the alpha cutoff and
    /// <see cref="Params"/>.z carries the base-color alpha multiplier; both are ignored
    /// by the opaque shaders.</para>
    /// </summary>
    public struct DrawConstants
    {
        /// <summary>The world transform of the mesh.</summary>
        public Matrix4x4 Model;
        /// <summary>x=cascade index, y=alphaCutoff (cutout only), z=baseColorAlpha (cutout only), w unused.</summary>
        public Vector4 Params;
    }

    private readonly RenderingSystem _rendering;
    private readonly Shader _shader;
    private readonly GPUAttachmentLayout _shadowLayout;
    private readonly GraphicsBuffer _shadowDataBuffer;

    // Registered renderables split by static / dynamic.
    private readonly UnorderedList<IShadowRenderable> _staticItems = new();
    private readonly UnorderedList<IShadowRenderable> _dynamicItems = new();

    // Per-cascade static render bundles — re-recorded only when dirty.
    private readonly SubRenderContext[] _staticBundles;
    private bool _staticBundleDirty = true;
    // Dynamic render bundle — re-recorded every frame per cascade so errors in
    // recording cannot corrupt the main render context.
    private readonly SubRenderContext _dynamicBundle;

    /// <summary>
    /// Create the shadow renderer.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="shadowShader">The shadow depth shader (ShadowDepth.hlsl).</param>
    /// <param name="shadowLayout">The shadow pass attachment layout (owned by the pipeline, exposed via <see cref="PBRDeferredPipeline.ShadowLayout"/>).</param>
    /// <param name="shadowDataBuffer">The cascade VP data buffer (owned by the pipeline, exposed via <see cref="PBRDeferredPipeline.ShadowDataBuffer"/>).</param>
    public ShadowRenderer(
        RenderingSystem rendering,
        Shader shadowShader,
        GPUAttachmentLayout shadowLayout,
        GraphicsBuffer shadowDataBuffer)
    {
        _rendering = rendering;
        _shader = shadowShader;
        _shadowLayout = shadowLayout;
        _shadowDataBuffer = shadowDataBuffer;

        int cascadeCount = PBRDeferredPipeline.ShadowCascadeCount;
        _staticBundles = new SubRenderContext[cascadeCount];
        for (int i = 0; i < cascadeCount; i++)
        {
            _staticBundles[i] = rendering.CreateSubRenderContext($"pbr_shadow_static_{i}");
        }
        _dynamicBundle = rendering.CreateSubRenderContext("pbr_shadow_dynamic");
    }

    // ── Renderable registry ──

    /// <summary>
    /// Register a renderable. Static items are baked into the internal per-cascade
    /// render bundles; dynamic items are drawn immediately each frame.
    /// </summary>
    public void Add(IShadowRenderable item)
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
    public void Remove(IShadowRenderable item)
    {
        _staticItems.Remove(item);
        _dynamicItems.Remove(item);
        _staticBundleDirty = true;
    }

    /// <summary>
    /// Mark the static render bundles as dirty so they are re-recorded on the next
    /// <see cref="OnRenderShadow"/>. Call after changing a static item's mesh,
    /// material or other bundle-recorded property.
    /// </summary>
    public void MarkStaticBundleDirty()
    {
        _staticBundleDirty = true;
    }

    // ── Pipeline callback ──

    /// <summary>
    /// Draw all registered casters into the shadow map for the given cascade.
    /// Called by the pipeline automatically between <c>BeginShadowPass</c> and
    /// <c>EndShadowPass</c>, once per cascade. When the static bundle is dirty all
    /// cascade bundles are re-recorded (on cascade 0); otherwise they are replayed.
    /// Dynamic items are re-recorded and replayed every cascade every frame.
    /// </summary>
    /// <param name="context">The live shadow render context.</param>
    /// <param name="cascadeIndex">The cascade being rendered (0 = nearest).</param>
    public void OnRenderShadow(RenderContext context, int cascadeIndex)
    {
        // Static bundles: re-record all cascades when dirty (only on cascade 0
        // to avoid redundant work).
        if (cascadeIndex == 0 && _staticItems.Count > 0 && _staticBundleDirty)
        {
            int cascadeCount = PBRDeferredPipeline.ShadowCascadeCount;
            for (int c = 0; c < cascadeCount; c++)
            {
                _staticBundles[c].Begin(_shadowLayout);
                for (int i = 0; i < _staticItems.Count; i++)
                {
                    DrawItem(_staticItems[i], _staticBundles[c], c);
                }
                _staticBundles[c].End();
            }
            _staticBundleDirty = false;
        }

        // Replay the static bundle for this cascade.
        if (_staticItems.Count > 0)
        {
            context.ExecuteSubContext(_staticBundles[cascadeIndex]);
        }

        // Dynamic bundle: re-recorded every frame per cascade so recording errors
        // stay isolated from the main render context.
        if (_dynamicItems.Count > 0)
        {
            _dynamicBundle.Begin(_shadowLayout);
            for (int i = 0; i < _dynamicItems.Count; i++)
            {
                DrawItem(_dynamicItems[i], _dynamicBundle, cascadeIndex);
            }
            _dynamicBundle.End();
            context.ExecuteSubContext(_dynamicBundle);
        }
    }

    /// <summary>
    /// Draw a single renderable into the given context (immediate or bundle).
    /// </summary>
    private static void DrawItem(IShadowRenderable item, IRenderContext target, int cascadeIndex)
    {
        target.DrawWithConstant(item.Mesh, item.Material,
            new DrawConstants
            {
                Model = item.WorldMatrix,
                Params = new Vector4(cascadeIndex, item.AlphaCutoff, item.BaseColorAlpha, 0.0f),
            });
    }

    // ── Material factory ──

    /// <summary>
    /// Create an opaque shadow depth material (ShadowDepth.hlsl). The renderer applies
    /// the pass-mandated state (depth write, rasterizer, data buffer binding); the caller
    /// owns the material and must dispose it.
    /// </summary>
    public GraphicsMaterial CreateShadowMaterial(bool doubleSided = false, string name = "pbr_shadow_material")
    {
        var material = _rendering.CreateMaterial(_shader, name);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        material.SetBuffer(ShaderResourceId.Data, _shadowDataBuffer);
        return material;
    }

    /// <summary>
    /// Create a caller-owned cutout shadow material — the shadow depth shader
    /// (ShadowDepth.hlsl) compiled with the <c>SHADOW_CUTOUT</c> define so the
    /// pixel shader samples _albedoTexture and discards transparent fragments.
    /// Alpha-tested meshes (foliage, fences, etc.) cast correctly shaped shadows.
    /// The material binds the shadow data buffer internally.
    /// </summary>
    /// <param name="albedoTexture">The albedo texture whose alpha channel drives the cutout; null binds the shared white texture (opaque).</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    /// <returns>The caller-owned cutout shadow material.</returns>
    public GraphicsMaterial CreateShadowCutoutMaterial(Texture2D? albedoTexture, bool doubleSided = false, string name = "pbr_shadow_cutout_material")
    {
        var material = _rendering.CreateMaterial(_shader, name);
        material.SetDefines("SHADOW_CUTOUT");
        // Force the SHADOW_CUTOUT variant to compile and update the reflection so
        // the _albedoTexture binding is visible before SetTexture is called.
        material.GetPipelineContext(_shadowLayout);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        material.SetBuffer(ShaderResourceId.Data, _shadowDataBuffer);
        return material;
    }

    /// <summary>
    /// (Re)bind the albedo texture slot of a cutout shadow material created by
    /// <see cref="CreateShadowCutoutMaterial"/>. Use when textures stream in
    /// asynchronously after the material was created (render bundles recorded with
    /// the material must be re-recorded afterwards).
    /// </summary>
    public void SetShadowCutoutMaterialTextures(GraphicsMaterial material, Texture2D? albedoTexture)
    {
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            for (int i = 0; i < _staticBundles.Length; i++)
            {
                _staticBundles[i].Dispose();
            }
            _dynamicBundle.Dispose();
        }
    }
}
