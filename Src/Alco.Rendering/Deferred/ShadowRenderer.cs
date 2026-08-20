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

    /// <summary>
    /// Optional RSM material (created via <see cref="ShadowRenderer.CreateRsmMaterial"/>).
    /// Null skips this object in the RSM pass; the object then contributes no
    /// sun-bounce radiance to the voxel GI.
    /// </summary>
    GraphicsMaterial? RsmMaterial { get; }

    /// <summary>
    /// Linear base color (rgb tints the RSM albedo, w multiplies its alpha) written
    /// into the RSM pass push constants.
    /// </summary>
    Vector4 RsmBaseColor { get; }
}

/// <summary>
/// A shadow content provider of the deferred PBR pipeline. Holds the shadow depth
/// shaders, material factory methods and a registry of <see cref="IShadowRenderable"/>
/// objects. Static objects are baked into internal per-cascade render bundles; dynamic
/// objects are drawn immediately each frame. The owning <see cref="RGNode_ShadowPass"/>
/// calls <see cref="OnRenderShadow"/> automatically inside each cascade's pass
/// (register via <see cref="RGNode_ShadowPass.Content"/>).
/// <br/>The renderer does <b>not</b> own the shadow render texture, attachment layout,
/// render context or cascade VP data buffer — those are owned by the pass node and
/// the pipeline.
/// </summary>
public sealed unsafe class ShadowRenderer : AutoDisposable, IShadowPassContent, IRsmPassContent
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

    // RSM pass state (reflective shadow map for the voxel GI sun bounce).
    // Null until EnableRsm is called; the RSM bundles are separate from the
    // shadow bundles because the pass records at a different resolution into
    // different attachments, and a single bundle can only target one layout.
    private Shader? _rsmShader;
    private GPUAttachmentLayout? _rsmLayout;
    private SubRenderContext? _rsmStaticBundle;
    private SubRenderContext? _rsmDynamicBundle;
    private int _rsmRecordedCascade = -1;

    /// <summary>
    /// Create the shadow renderer.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="shadowShader">The shadow depth shader (ShadowDepth.hlsl).</param>
    /// <param name="shadowLayout">The shadow pass attachment layout (owned by the composition, e.g. <see cref="PBRDeferredPreset.ShadowLayout"/>).</param>
    /// <param name="shadowDataBuffer">The cascade VP data buffer (owned by the scene environment, see <see cref="PBRSceneEnvironment.ShadowDataBuffer"/>).</param>
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

        int cascadeCount = RGNode_ShadowPass.CascadeCount;
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
    /// Called by the owning <see cref="RGNode_ShadowPass"/> inside the cascade's open
    /// pass, once per cascade. When the static bundle is dirty all
    /// cascade bundles are re-recorded (on cascade 0); otherwise they are replayed.
    /// Dynamic items are re-recorded and replayed every cascade every frame.
    /// </summary>
    /// <param name="context">The live shadow pass scope.</param>
    /// <param name="cascadeIndex">The cascade being rendered (0 = nearest).</param>
    public void OnRenderShadow(RenderPassScope context, int cascadeIndex)
    {
        // Static bundles: re-record all cascades when dirty (only on cascade 0
        // to avoid redundant work).
        if (cascadeIndex == 0 && _staticItems.Count > 0 && _staticBundleDirty)
        {
            int cascadeCount = RGNode_ShadowPass.CascadeCount;
            for (int c = 0; c < cascadeCount; c++)
            {
                using (RenderPassScope bundle = _staticBundles[c].BeginPass(_shadowLayout))
                {
                    for (int i = 0; i < _staticItems.Count; i++)
                    {
                        DrawItem(_staticItems[i], bundle, c);
                    }
                }
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
            using (RenderPassScope bundle = _dynamicBundle.BeginPass(_shadowLayout))
            {
                for (int i = 0; i < _dynamicItems.Count; i++)
                {
                    DrawItem(_dynamicItems[i], bundle, cascadeIndex);
                }
            }
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

    // ── RSM pass (reflective shadow map for the voxel GI sun bounce) ──

    /// <summary>
    /// Push constant payload for one RSM draw. Layout must match the
    /// <c>Constants</c> struct in Rsm.hlsl exactly.
    /// </summary>
    public struct RsmDrawConstants
    {
        /// <summary>The world transform of the mesh.</summary>
        public Matrix4x4 Model;
        /// <summary>Linear base color (rgb tints the albedo, w multiplies its alpha).</summary>
        public Vector4 BaseColor;
        /// <summary>x=cascade index, y=alphaCutoff (0 disables the test), zw unused.</summary>
        public Vector4 Params;
    }

    /// <summary>
    /// Enable the RSM pass support: stores the RSM shader and attachment layout and
    /// creates the RSM render bundles. After this call the renderer can be
    /// registered as an <see cref="IRsmPassContent"/> on an <see cref="RGNode_RsmPass"/>
    /// and RSM materials can be created via <see cref="CreateRsmMaterial"/>.
    /// </summary>
    /// <param name="rsmShader">The RSM pass shader (Rsm.hlsl).</param>
    /// <param name="rsmLayout">The RSM pass attachment layout (two RGBA8 colors +
    /// depth; the layout of the RSM render texture the pass draws into).</param>
    public void EnableRsm(Shader rsmShader, GPUAttachmentLayout rsmLayout)
    {
        ArgumentNullException.ThrowIfNull(rsmShader);
        ArgumentNullException.ThrowIfNull(rsmLayout);
        _rsmShader = rsmShader;
        _rsmLayout = rsmLayout;
        _rsmStaticBundle ??= _rendering.CreateSubRenderContext("pbr_rsm_static");
        _rsmDynamicBundle ??= _rendering.CreateSubRenderContext("pbr_rsm_dynamic");
        _rsmRecordedCascade = -1;
    }

    /// <summary>
    /// Draw every registered renderable that carries an
    /// <see cref="IShadowRenderable.RsmMaterial"/> into the reflective shadow map.
    /// Called by the owning <see cref="RGNode_RsmPass"/> inside its open pass.
    /// Static bundles share the shadow path's dirty flag; a change of the
    /// RSM cascade forces a re-record because the cascade index is baked into the
    /// recorded push constants.
    /// </summary>
    /// <param name="context">The live RSM pass scope.</param>
    /// <param name="cascadeIndex">The CSM cascade whose sun view defines the RSM.</param>
    public void OnRenderRsm(RenderPassScope context, int cascadeIndex)
    {
        SubRenderContext staticBundle = _rsmStaticBundle!;
        SubRenderContext dynamicBundle = _rsmDynamicBundle!;

        if (_staticItems.Count > 0)
        {
            if (_staticBundleDirty || _rsmRecordedCascade != cascadeIndex)
            {
                using (RenderPassScope bundle = staticBundle.BeginPass(_rsmLayout!))
                {
                    for (int i = 0; i < _staticItems.Count; i++)
                    {
                        DrawRsmItem(_staticItems[i], bundle, cascadeIndex);
                    }
                }
                _rsmRecordedCascade = cascadeIndex;
            }

            context.ExecuteSubContext(staticBundle);
        }

        if (_dynamicItems.Count > 0)
        {
            using (RenderPassScope bundle = dynamicBundle.BeginPass(_rsmLayout!))
            {
                for (int i = 0; i < _dynamicItems.Count; i++)
                {
                    DrawRsmItem(_dynamicItems[i], bundle, cascadeIndex);
                }
            }
            context.ExecuteSubContext(dynamicBundle);
        }
    }

    /// <summary>
    /// Draw a single renderable into the RSM (immediate or bundle context). Items
    /// without an RSM material are skipped.
    /// </summary>
    private static void DrawRsmItem(IShadowRenderable item, IRenderContext target, int cascadeIndex)
    {
        GraphicsMaterial? material = item.RsmMaterial;
        if (material == null)
        {
            return;
        }
        target.DrawWithConstant(item.Mesh, material,
            new RsmDrawConstants
            {
                Model = item.WorldMatrix,
                BaseColor = item.RsmBaseColor,
                Params = new Vector4(cascadeIndex, item.AlphaCutoff, 0.0f, 0.0f),
            });
    }

    /// <summary>
    /// Create a caller-owned RSM material — the RSM pass shader (Rsm.hlsl)
    /// sampling the albedo texture and writing sRGB albedo + world normal.
    /// Requires <see cref="EnableRsm"/> first; the material binds the shared
    /// shadow cascade data buffer internally (the RSM vertex shader unfolds the
    /// selected cascade's atlas quadrant), so recorded bundles stay valid while
    /// the cascades move.
    /// </summary>
    /// <param name="albedoTexture">The albedo texture; null binds the shared white texture.</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    public GraphicsMaterial CreateRsmMaterial(Texture2D? albedoTexture, bool doubleSided = false, string name = "pbr_rsm_material")
    {
        if (_rsmShader == null || _rsmLayout == null)
        {
            throw new InvalidOperationException("Call EnableRsm before creating RSM materials.");
        }
        var material = _rendering.CreateMaterial(_rsmShader, name);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        material.SetBuffer(ShaderResourceId.Data, _shadowDataBuffer);
        return material;
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
            _rsmStaticBundle?.Dispose();
            _rsmDynamicBundle?.Dispose();
        }
    }
}
