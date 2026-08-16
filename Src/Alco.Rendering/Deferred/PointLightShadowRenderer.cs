using System.Numerics;
using Alco.Graphics;

using Alco;

namespace Alco.Rendering;

/// <summary>
/// A caster content provider for <see cref="RGNode_PointLightShadow"/>: holds the
/// point light shadow depth shaders, the material factory methods and a registry
/// of <see cref="IShadowRenderable"/> objects (shared with the sun
/// <see cref="ShadowRenderer"/> registry — register the same objects with both).
/// Every caster is drawn immediately into the face's pass; the atlas only
/// re-renders faces whose light slot changed (or when
/// <see cref="HasDynamicCasters"/> is true), so immediate draws stay cheap in
/// the static steady state.
/// <br/>The renderer does <b>not</b> own the atlas render texture, attachment
/// layout or face matrix buffer — those are owned by the node.
/// </summary>
public sealed unsafe class PointLightShadowRenderer : AutoDisposable, IPointLightShadowContent
{
    /// <summary>
    /// Push constant payload for a point light shadow atlas draw. Layout must
    /// match the <c>Constants</c> struct in PointLightShadowDepth.hlsl exactly.
    /// The folded face matrices are read from the <c>_data</c> uniform buffer
    /// instead, so the constants stay static for static geometry.
    /// <para>For cutout variants, <see cref="Params"/>.y carries the alpha cutoff
    /// and <see cref="Params"/>.z the base-color alpha multiplier; both are
    /// ignored by the opaque shaders.</para>
    /// </summary>
    public struct DrawConstants
    {
        /// <summary>The world transform of the mesh.</summary>
        public Matrix4x4 Model;
        /// <summary>x=matrix index (slot*6+face), y=alphaCutoff (cutout only), z=baseColorAlpha (cutout only), w unused.</summary>
        public Vector4 Params;
    }

    private readonly RenderingSystem _rendering;
    private readonly Shader _shader;
    private readonly GPUAttachmentLayout _atlasLayout;
    private readonly GraphicsBuffer _matrixBuffer;

    // Registered renderables split by static / dynamic (dynamic casters force
    // per-face re-rendering every frame via HasDynamicCasters).
    private readonly UnorderedList<IShadowRenderable> _staticItems = new();
    private readonly UnorderedList<IShadowRenderable> _dynamicItems = new();

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether any registered caster is dynamic (per-frame face re-render).</summary>
    public bool HasDynamicCasters => _dynamicItems.Count > 0;

    /// <summary>
    /// Create the point light shadow renderer.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="shadowShader">The shadow depth shader (PointLightShadowDepth.hlsl).</param>
    /// <param name="atlasLayout">The atlas attachment layout (owned by the node, see <see cref="RGNode_PointLightShadow.AtlasLayout"/>).</param>
    /// <param name="matrixBuffer">The folded face matrix buffer (owned by the node, see <see cref="RGNode_PointLightShadow.MatrixBuffer"/>).</param>
    public PointLightShadowRenderer(
        RenderingSystem rendering,
        Shader shadowShader,
        GPUAttachmentLayout atlasLayout,
        GraphicsBuffer matrixBuffer)
    {
        _rendering = rendering;
        _shader = shadowShader;
        _atlasLayout = atlasLayout;
        _matrixBuffer = matrixBuffer;
    }

    // ── Renderable registry ──

    /// <summary>
    /// Register a caster. Objects shared with the sun shadow renderer can be
    /// registered with both — the interface is read-only.
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
    }

    /// <summary>Unregister a caster.</summary>
    public void Remove(IShadowRenderable item)
    {
        _staticItems.Remove(item);
        _dynamicItems.Remove(item);
    }

    // ── Node callback ──

    /// <summary>
    /// Draw all enabled casters into one atlas face. Called by
    /// <see cref="RGNode_PointLightShadow"/> inside the open atlas pass with the
    /// face's scissor applied.
    /// </summary>
    /// <param name="context">The live pass scope.</param>
    /// <param name="matrixIndex">The folded face matrix index (slot*6+face).</param>
    public void OnRenderPointLightShadow(RenderPassScope context, int matrixIndex)
    {
        for (int i = 0; i < _staticItems.Count; i++)
        {
            DrawItem(_staticItems[i], context, matrixIndex);
        }
        for (int i = 0; i < _dynamicItems.Count; i++)
        {
            DrawItem(_dynamicItems[i], context, matrixIndex);
        }
    }

    private static void DrawItem(IShadowRenderable item, RenderPassScope target, int matrixIndex)
    {
        if (!item.CastsShadow)
        {
            return;
        }
        target.DrawWithConstant(item.Mesh, item.Material, new DrawConstants
        {
            Model = item.WorldMatrix,
            Params = new Vector4(matrixIndex, item.AlphaCutoff, item.BaseColorAlpha, 0.0f),
        });
    }

    // ── Material factory ──

    /// <summary>
    /// Create an opaque point light shadow depth material (PointLightShadowDepth.hlsl).
    /// The caller owns the material and must dispose it.
    /// </summary>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    public GraphicsMaterial CreateShadowMaterial(bool doubleSided = false, string name = "pls_shadow_material")
    {
        var material = _rendering.CreateMaterial(_shader, name);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        material.SetBuffer(ShaderResourceId.Data, _matrixBuffer);
        return material;
    }

    /// <summary>
    /// Create a caller-owned cutout point light shadow depth material — the depth
    /// shader compiled with the <c>SHADOW_CUTOUT</c> define so the pixel shader
    /// samples _albedoTexture and discards transparent fragments.
    /// </summary>
    /// <param name="albedoTexture">The albedo texture whose alpha channel drives the cutout; null binds the shared white texture (opaque).</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    public GraphicsMaterial CreateShadowCutoutMaterial(Texture2D? albedoTexture, bool doubleSided = false,
        string name = "pls_shadow_cutout_material")
    {
        var material = _rendering.CreateMaterial(_shader, name);
        material.SetDefines("SHADOW_CUTOUT");
        // Force the SHADOW_CUTOUT variant to compile and update the reflection so
        // the _albedoTexture binding is visible before SetTexture is called.
        material.GetPipelineContext(_atlasLayout);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        material.SetBuffer(ShaderResourceId.Data, _matrixBuffer);
        return material;
    }

    /// <summary>
    /// (Re)bind the albedo texture slot of a cutout material created by
    /// <see cref="CreateShadowCutoutMaterial"/> (streamed-in textures).
    /// </summary>
    public void SetCutoutMaterialTextures(GraphicsMaterial material, Texture2D? albedoTexture)
    {
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
    }
}
