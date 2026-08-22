using System.Numerics;
using Alco.Graphics;

using Alco;

using Alco.Rendering;

namespace Alco.World3D;

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

    // Instanced draw batches: per-instance data uploaded to the _instances
    // storage buffer, grouped into (material, mesh) draw segments.
    private readonly PbrInstanceBatch _staticBatch = new();
    private readonly PbrInstanceBatch _dynamicBatch = new();

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

        // Static batch: rebuild and re-record the bundle only when dirty. The
        // rebuild also handles an emptied registry, leaving empty segments that
        // suppress the replay below.
        if (_staticBundleDirty)
        {
            RebuildBatch(_staticBatch, _staticItems, "pbr_gbuffer_static_instances");
            if (!_staticBatch.Segments.IsEmpty)
            {
                using RenderPassScope bundle = _staticBundle.BeginPass(layout);
                RecordBatch(bundle, _staticBatch);
            }
            _staticBundleDirty = false;
        }

        if (!_staticBatch.Segments.IsEmpty)
        {
            context.ExecuteSubContext(_staticBundle);
        }

        // Dynamic batch: rebuilt and re-recorded every frame so recording errors
        // stay isolated from the main render context.
        if (_dynamicItems.Count > 0)
        {
            RebuildBatch(_dynamicBatch, _dynamicItems, "pbr_gbuffer_dynamic_instances");
            if (!_dynamicBatch.Segments.IsEmpty)
            {
                using (RenderPassScope bundle = _dynamicBundle.BeginPass(layout))
                {
                    RecordBatch(bundle, _dynamicBatch);
                }
                context.ExecuteSubContext(_dynamicBundle);
            }
        }
    }

    /// <summary>
    /// Fill a batch with the instance data of the given items and upload it.
    /// </summary>
    /// <param name="batch">The batch to rebuild.</param>
    /// <param name="items">The items to append (read live: the world transform).</param>
    /// <param name="bufferName">The name of the (re)created instance buffer.</param>
    private void RebuildBatch(PbrInstanceBatch batch, UnorderedList<IGBufferRenderable> items, string bufferName)
    {
        batch.BeginBatch();
        for (int i = 0; i < items.Count; i++)
        {
            IGBufferRenderable item = items[i];
            batch.AddInstance(new PbrInstanceData
            {
                Model = item.WorldMatrix,
                BaseColor = item.BaseColor,
                MetallicRoughnessAO = item.MetallicRoughnessAO,
                Params = new Vector4(item.AlphaCutoff, 0.0f, 0.0f, 0.0f),
                Emissive = new Vector4(item.EmissiveFactor, 1.0f),
            }, item.Material, item.Mesh);
        }
        batch.Flush(_rendering, bufferName);
    }

    /// <summary>
    /// Record the draws of a batch's segments into the given context (immediate
    /// or bundle): one instanced draw per (material, mesh) segment, with the
    /// batch's instance buffer bound to the shared <c>_instances</c> slot.
    /// </summary>
    /// <param name="target">The context to record into.</param>
    /// <param name="batch">The batch holding the uploaded instance buffer and segments.</param>
    private static void RecordBatch(IRenderContext target, PbrInstanceBatch batch)
    {
        GraphicsBuffer buffer = batch.Buffer!;
        ReadOnlySpan<PbrInstanceSegment> segments = batch.Segments;
        for (int i = 0; i < segments.Length; i++)
        {
            PbrInstanceSegment segment = segments[i];
            segment.Material.SetBuffer(ShaderResourceId.Instances, buffer);
            target.DrawInstanced(segment.Mesh, segment.Material, segment.Count, segment.Start);
        }
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
        GraphicsMaterial material = CreateMaterial(_shader, doubleSided, name);
        SetMaterialTextures(material, albedoTexture, normalTexture, metallicRoughnessTexture, emissiveTexture);
        return material;
    }

    /// <summary>
    /// Create a G-buffer material from a pass-template shader already composed with its
    /// surface (see <see cref="MaterialCompiler"/>): applies the pass-mandated state —
    /// reversed-infinite-depth write, cull mode from double-sidedness and the camera
    /// binding — and leaves every texture slot to the caller. The caller owns the
    /// material and must dispose it.
    /// </summary>
    /// <param name="shader">The composed G-buffer template shader.</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    public GraphicsMaterial CreateMaterial(Shader shader, bool doubleSided = false, string name = "pbr_gbuffer_material")
    {
        var material = _rendering.CreateMaterial(shader, name);
        // Reversed infinite camera depth (near = 1, far = 0): GreaterEqual keeps
        // the nearest surface, matching the 0.0 depth clear of the G-buffer pass.
        material.DepthStencilState = DepthStencilState.WriteReverseZ;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        if (_camera != null)
        {
            material.SetBuffer(ShaderResourceId.Camera, _camera);
        }
        return material;
    }

    /// <summary>
    /// (Re)bind the texture slots of a G-buffer material created by
    /// <see cref="CreateMaterial(Texture2D?, Texture2D?, Texture2D?, Texture2D?, bool, string)"/>,
    /// applying the same fallback textures.
    /// Use when textures stream in asynchronously after the material was created.
    /// </summary>
    public void SetMaterialTextures(
        GraphicsMaterial material, Texture2D? albedoTexture, Texture2D? normalTexture,
        Texture2D? metallicRoughnessTexture, Texture2D? emissiveTexture)
    {
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        material.SetTexture("_normalTexture", normalTexture ?? GetOrCreateFlatNormalTexture());
        material.SetTexture("_metallicRoughnessTexture", metallicRoughnessTexture ?? _rendering.TextureWhite);
        material.SetTexture("_emissiveTexture", emissiveTexture ?? _rendering.TextureBlack);
    }

    /// <summary>
    /// The 1x1 flat-normal fallback texture of the standard surface's normal slot
    /// (see <see cref="StandardSurfaceSlotsUtility.Normal"/>).
    /// </summary>
    public Texture2D FlatNormalTexture => GetOrCreateFlatNormalTexture();

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
            _staticBatch.Dispose();
            _dynamicBatch.Dispose();
            _flatNormalTexture?.Dispose();
        }
    }
}
