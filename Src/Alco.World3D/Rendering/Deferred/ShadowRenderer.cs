using System.Numerics;
using Alco.Graphics;

using Alco;

using Alco.Rendering;

namespace Alco.World3D;

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
    /// into the RSM pass instance data.
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
    /// Push constant payload for the instanced shadow map / RSM draws. Layout
    /// must match the <c>ShadowConstants</c> struct in ShadowDepth.hlsl and the
    /// <c>RsmConstants</c> struct in Rsm.hlsl exactly. All other per-item data
    /// comes from the <c>_instances</c> storage buffer, so this constant stays
    /// static per cascade (render-bundle friendly).
    /// </summary>
    public struct CascadeConstants
    {
        /// <summary>x = cascade index, yzw unused.</summary>
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

    // Instanced draw batches: per-instance data uploaded to the _instances
    // storage buffer, grouped into (material, mesh) draw segments. The shadow
    // batches draw every item with its shadow material; the RSM batches draw
    // only items carrying an RsmMaterial (their instance data carries the RSM
    // base color instead of the cutout scalars).
    private readonly PbrInstanceBatch _shadowStaticBatch = new();
    private readonly PbrInstanceBatch _shadowDynamicBatch = new();
    private readonly PbrInstanceBatch _rsmStaticBatch = new();
    private readonly PbrInstanceBatch _rsmDynamicBatch = new();

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
    /// pass, once per cascade. Static items live in instance batches recorded once
    /// into the per-cascade bundles (re-recorded only when dirty on cascade 0);
    /// dynamic items are batched once per frame (cascade 0) and their bundle is
    /// re-recorded per cascade with the cascade constant.
    /// </summary>
    /// <param name="context">The live shadow pass scope.</param>
    /// <param name="cascadeIndex">The cascade being rendered (0 = nearest).</param>
    public void OnRenderShadow(RenderPassScope context, int cascadeIndex)
    {
        // Static batch: rebuild and re-record all cascade bundles when dirty
        // (only on cascade 0 to avoid redundant work). The rebuild also handles
        // an emptied registry, leaving empty segments that suppress the replay.
        if (cascadeIndex == 0 && _staticBundleDirty)
        {
            RebuildShadowBatch(_shadowStaticBatch, _staticItems, "pbr_shadow_static_instances");
            if (!_shadowStaticBatch.Segments.IsEmpty)
            {
                int cascadeCount = RGNode_ShadowPass.CascadeCount;
                for (int c = 0; c < cascadeCount; c++)
                {
                    using RenderPassScope bundle = _staticBundles[c].BeginPass(_shadowLayout);
                    RecordInstancedPass(bundle, _shadowStaticBatch, c);
                }
            }
            _staticBundleDirty = false;
        }

        // Replay the static bundle for this cascade.
        if (!_shadowStaticBatch.Segments.IsEmpty)
        {
            context.ExecuteSubContext(_staticBundles[cascadeIndex]);
        }

        // Dynamic batch: rebuilt once per frame (cascade 0); each cascade
        // re-records the bundle with its own cascade constant so recording
        // errors stay isolated from the main render context.
        if (cascadeIndex == 0)
        {
            RebuildShadowBatch(_shadowDynamicBatch, _dynamicItems, "pbr_shadow_dynamic_instances");
        }

        if (!_shadowDynamicBatch.Segments.IsEmpty)
        {
            using RenderPassScope bundle = _dynamicBundle.BeginPass(_shadowLayout);
            RecordInstancedPass(bundle, _shadowDynamicBatch, cascadeIndex);
            context.ExecuteSubContext(_dynamicBundle);
        }
    }

    // ── Material factory ──

    /// <summary>
    /// Create an opaque shadow depth material (ShadowDepth.hlsl with its default
    /// surface). The renderer applies the pass-mandated state (depth write, rasterizer,
    /// data buffer binding); the caller owns the material and must dispose it.
    /// </summary>
    public GraphicsMaterial CreateShadowMaterial(bool doubleSided = false, string name = "pbr_shadow_material")
    {
        return CreateShadowMaterial(_shader, [], doubleSided, name);
    }

    /// <summary>
    /// Create a caller-owned cutout shadow material — the shadow depth shader
    /// (ShadowDepth.hlsl) compiled with the <c>SHADOW_CUTOUT</c> define so the
    /// pixel shader evaluates the surface's base color alpha
    /// (its only consumed surface function) and discards transparent fragments.
    /// Alpha-tested meshes (foliage, fences, etc.) cast correctly shaped shadows.
    /// The material binds the shadow data buffer internally.
    /// </summary>
    /// <param name="albedoTexture">The albedo texture whose alpha channel drives the cutout; null binds the shared white texture (opaque).</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    /// <returns>The caller-owned cutout shadow material.</returns>
    public GraphicsMaterial CreateShadowCutoutMaterial(Texture2D? albedoTexture, bool doubleSided = false, string name = "pbr_shadow_cutout_material")
    {
        GraphicsMaterial material = CreateShadowMaterial(_shader, ["SHADOW_CUTOUT"], doubleSided, name);
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        return material;
    }

    /// <summary>
    /// Create a shadow depth material from a pass-template shader already composed with
    /// its surface (see <see cref="MaterialCompiler"/>): applies the pass-mandated state
    /// (depth write, rasterizer, data buffer binding) and the given shader defines —
    /// <c>SHADOW_CUTOUT</c> selects the alpha-tested permutation. The define permutation
    /// is compiled eagerly so its texture bindings are visible before SetTexture is
    /// called. The caller owns the material and must dispose it.
    /// </summary>
    /// <param name="shader">The composed shadow depth template shader.</param>
    /// <param name="defines">Shader defines of the material (may be empty).</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    public GraphicsMaterial CreateShadowMaterial(Shader shader, string[] defines, bool doubleSided = false, string name = "pbr_shadow_material")
    {
        var material = _rendering.CreateMaterial(shader, name);
        if (defines.Length > 0)
        {
            material.SetDefines(defines);
            // Force the permutation to compile and update the reflection so the
            // texture bindings are visible before SetTexture is called.
            material.GetPipelineContext(_shadowLayout);
        }
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
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

    /// <summary>Whether the RSM pass support is enabled (see <see cref="EnableRsm"/>).</summary>
    public bool IsRsmEnabled => _rsmShader != null;

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
    /// Static items live in an instance batch recorded once into the RSM bundle;
    /// a change of the RSM cascade forces a re-record because the cascade index
    /// is baked into the recorded push constant.
    /// </summary>
    /// <param name="context">The live RSM pass scope.</param>
    /// <param name="cascadeIndex">The CSM cascade whose sun view defines the RSM.</param>
    public void OnRenderRsm(RenderPassScope context, int cascadeIndex)
    {
        SubRenderContext staticBundle = _rsmStaticBundle!;
        SubRenderContext dynamicBundle = _rsmDynamicBundle!;

        // Static batch: rebuild when dirty (also handles an emptied registry,
        // leaving empty segments that suppress the replay) and re-record when
        // the RSM cascade changed (the cascade index is baked into the bundle's
        // push constants).
        if (_staticBundleDirty || _rsmRecordedCascade != cascadeIndex)
        {
            RebuildRsmBatch(_rsmStaticBatch, _staticItems, "pbr_rsm_static_instances");
            if (!_rsmStaticBatch.Segments.IsEmpty)
            {
                using RenderPassScope bundle = staticBundle.BeginPass(_rsmLayout!);
                RecordInstancedPass(bundle, _rsmStaticBatch, cascadeIndex);
            }
            _rsmRecordedCascade = cascadeIndex;
        }

        if (!_rsmStaticBatch.Segments.IsEmpty)
        {
            context.ExecuteSubContext(staticBundle);
        }

        // Dynamic batch: rebuilt and re-recorded every frame so recording
        // errors stay isolated from the main render context.
        RebuildRsmBatch(_rsmDynamicBatch, _dynamicItems, "pbr_rsm_dynamic_instances");
        if (!_rsmDynamicBatch.Segments.IsEmpty)
        {
            using RenderPassScope bundle = dynamicBundle.BeginPass(_rsmLayout!);
            RecordInstancedPass(bundle, _rsmDynamicBatch, cascadeIndex);
            context.ExecuteSubContext(dynamicBundle);
        }
    }

    /// <summary>
    /// Fill a shadow batch with the instance data of the given items and upload
    /// it. The cutout scalars ride in the shared instance fields: alphaCutoff in
    /// <see cref="PbrInstanceData.Params"/>.x and the base-color alpha multiplier
    /// in <see cref="PbrInstanceData.BaseColor"/>.w.
    /// </summary>
    /// <param name="batch">The batch to rebuild.</param>
    /// <param name="items">The items to append (read live: the world transform).</param>
    /// <param name="bufferName">The name of the (re)created instance buffer.</param>
    private void RebuildShadowBatch(PbrInstanceBatch batch, UnorderedList<IShadowRenderable> items, string bufferName)
    {
        batch.BeginBatch();
        for (int i = 0; i < items.Count; i++)
        {
            IShadowRenderable item = items[i];
            batch.AddInstance(new PbrInstanceData
            {
                Model = item.WorldMatrix,
                BaseColor = new Vector4(1.0f, 1.0f, 1.0f, item.BaseColorAlpha),
                Params = new Vector4(item.AlphaCutoff, 0.0f, 0.0f, 0.0f),
            }, item.Material, item.Mesh);
        }
        batch.Flush(_rendering, bufferName);
    }

    /// <summary>
    /// Fill an RSM batch with the instance data of the given items and upload
    /// it. Items without an RSM material are skipped entirely.
    /// </summary>
    /// <param name="batch">The batch to rebuild.</param>
    /// <param name="items">The items to append (read live: the world transform).</param>
    /// <param name="bufferName">The name of the (re)created instance buffer.</param>
    private void RebuildRsmBatch(PbrInstanceBatch batch, UnorderedList<IShadowRenderable> items, string bufferName)
    {
        batch.BeginBatch();
        for (int i = 0; i < items.Count; i++)
        {
            IShadowRenderable item = items[i];
            GraphicsMaterial? material = item.RsmMaterial;
            if (material == null)
            {
                continue;
            }
            batch.AddInstance(new PbrInstanceData
            {
                Model = item.WorldMatrix,
                BaseColor = item.RsmBaseColor,
                Params = new Vector4(item.AlphaCutoff, 0.0f, 0.0f, 0.0f),
            }, material, item.Mesh);
        }
        batch.Flush(_rendering, bufferName);
    }

    /// <summary>
    /// Record the draws of a batch's segments into the given context (immediate
    /// or bundle): one instanced draw per (material, mesh) segment with the
    /// cascade index pushed as the pass constant and the batch's instance
    /// buffer bound to the shared <c>_instances</c> slot.
    /// </summary>
    /// <param name="target">The context to record into.</param>
    /// <param name="batch">The batch holding the uploaded instance buffer and segments.</param>
    /// <param name="cascadeIndex">The cascade index baked into the push constant.</param>
    private static void RecordInstancedPass(IRenderContext target, PbrInstanceBatch batch, int cascadeIndex)
    {
        GraphicsBuffer buffer = batch.Buffer!;
        ReadOnlySpan<PbrInstanceSegment> segments = batch.Segments;
        CascadeConstants constants = new CascadeConstants
        {
            Params = new Vector4(cascadeIndex, 0.0f, 0.0f, 0.0f),
        };
        for (int i = 0; i < segments.Length; i++)
        {
            PbrInstanceSegment segment = segments[i];
            segment.Material.SetBuffer(ShaderResourceId.Instances, buffer);
            target.DrawInstancedWithConstant(segment.Mesh, segment.Material, segment.Count, segment.Start, constants);
        }
    }

    /// <summary>
    /// Create a caller-owned RSM material — the RSM pass shader (Rsm.hlsl with its
    /// default surface) sampling the albedo texture and writing sRGB albedo + world
    /// normal. Requires <see cref="EnableRsm"/> first; the material binds the shared
    /// shadow cascade data buffer internally (the RSM vertex shader unfolds the
    /// selected cascade's atlas quadrant), so recorded bundles stay valid while
    /// the cascades move.
    /// </summary>
    /// <param name="albedoTexture">The albedo texture; null binds the shared white texture.</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    public GraphicsMaterial CreateRsmMaterial(Texture2D? albedoTexture, bool doubleSided = false, string name = "pbr_rsm_material")
    {
        if (_rsmShader == null)
        {
            throw new InvalidOperationException("Call EnableRsm before creating RSM materials.");
        }
        GraphicsMaterial material = CreateRsmMaterial(_rsmShader, doubleSided, name);
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        return material;
    }

    /// <summary>
    /// Create an RSM material from a pass-template shader already composed with its
    /// surface (see <see cref="MaterialCompiler"/>): applies the pass-mandated state and
    /// the shared cascade data buffer, leaving every texture slot to the caller.
    /// Requires <see cref="EnableRsm"/> first; the caller owns the material.
    /// </summary>
    /// <param name="shader">The composed RSM template shader.</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    public GraphicsMaterial CreateRsmMaterial(Shader shader, bool doubleSided = false, string name = "pbr_rsm_material")
    {
        if (_rsmShader == null || _rsmLayout == null)
        {
            throw new InvalidOperationException("Call EnableRsm before creating RSM materials.");
        }
        var material = _rendering.CreateMaterial(shader, name);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
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
            _shadowStaticBatch.Dispose();
            _shadowDynamicBatch.Dispose();
            _rsmStaticBatch.Dispose();
            _rsmDynamicBatch.Dispose();
        }
    }
}
