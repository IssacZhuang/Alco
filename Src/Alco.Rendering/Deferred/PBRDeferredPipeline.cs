using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A deferred PBR rendering pipeline built on the engine's WebGPU resources.
/// <br/>Owns a G-buffer (albedo / normal / metallic-roughness-ao / emissive + depth), a
/// depth-only shadow map holding <see cref="ShadowCascadeCount"/> cascades in a 2x2 atlas,
/// three render contexts (shadow pass, G-buffer pass, lighting pass) and the pass-private
/// materials (shadow depth, deferred lighting, HBAO). Scene materials are created and
/// owned by the caller via <see cref="CreateGBufferMaterial"/> /
/// <see cref="CreateGBufferTangentMaterial"/>.
/// <br/>The caller drives the frame explicitly: per cascade
/// <c>BeginShadowPass → draws → EndShadowPass</c>, then
/// <c>BeginGBufferPass → draws → EndGBufferPass</c>, then
/// <c>RenderLighting(target, ref data)</c> which resolves lighting, sky and shadows
/// into the target frame buffer (typically the engine's HDR main target).
/// <br/>Every draw method takes an <see cref="IRenderContext"/> target: pass
/// <see cref="ShadowContext"/> / <see cref="GBufferContext"/> for immediate (per-frame
/// dynamic) draws, or a <see cref="SubRenderContext"/> to record static geometry into a
/// reusable render bundle once and replay it every frame via
/// <see cref="ExecuteShadowSubContext"/> / <see cref="ExecuteGBufferSubContext"/>
/// (the per-cascade shadow view-projections live in a uniform buffer with reference
/// semantics, so recorded bundles stay valid while the camera-fitted cascades move).
/// <br/>Cascade splits are computed by <see cref="ComputeShadowCascades"/> (PSSM,
/// camera-fitted, texel-snapped).
/// <br/>Pluggable effects (AO, GI, etc.) implementing <see cref="IRenderPlugin"/> can be
/// registered via <see cref="RegisterPlugin"/>; they execute at their declared
/// <see cref="RenderInjectionPoint"/> and their output textures are bound to the
/// lighting material automatically.
/// </summary>
public sealed unsafe class PBRDeferredPipeline : AutoDisposable
{
    /// <summary>
    /// Push constant payload for a G-buffer draw. Layout must match the
    /// <c>Constants</c> struct in GBuffer.hlsl exactly.
    /// </summary>
    public struct PBRDrawConstants
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
        /// <param name="model">The world transform of the object.</param>
        /// <param name="baseColor">The linear base color.</param>
        /// <param name="metallic">Metallic value in [0, 1].</param>
        /// <param name="roughness">Roughness value in [0, 1].</param>
        /// <param name="ambientOcclusion">Ambient occlusion in [0, 1].</param>
        public PBRDrawConstants(in Matrix4x4 model, in Vector3 baseColor, float metallic, float roughness, float ambientOcclusion)
        {
            Model = model;
            BaseColor = new Vector4(baseColor, 1.0f);
            MetallicRoughnessAO = new Vector4(metallic, roughness, ambientOcclusion, 1.0f);
            Params = Vector4.Zero;
            Emissive = Vector4.Zero;
        }
    }

    /// <summary>
    /// Push constant payload for a shadow map draw. Layout must match the
    /// <c>Constants</c> struct in ShadowDepth.hlsl exactly. The per-cascade light
    /// view-projection matrices are read from the <c>_data</c> uniform buffer instead,
    /// so the constants stay static for static geometry (render-bundle friendly).
    /// </summary>
    public struct ShadowDrawConstants
    {
        /// <summary>The world transform of the mesh.</summary>
        public Matrix4x4 Model;
        /// <summary>x=the shadow cascade index to project into, yzw are unused.</summary>
        public Vector4 Params;
    }

    /// <summary>
    /// Per-frame shadow pass data uploaded to the <c>_data</c> uniform buffer of the
    /// shadow depth shaders: the quadrant-folded light view-projection matrix of each
    /// cascade. Layout must match the <c>_data</c> cbuffer in ShadowDepth.hlsl exactly.
    /// </summary>
    public struct ShadowCascadeData
    {
        /// <summary>Light view-projection matrix of shadow cascade 0 (nearest).</summary>
        public Matrix4x4 CascadeViewProjection0;
        /// <summary>Light view-projection matrix of shadow cascade 1.</summary>
        public Matrix4x4 CascadeViewProjection1;
        /// <summary>Light view-projection matrix of shadow cascade 2.</summary>
        public Matrix4x4 CascadeViewProjection2;
        /// <summary>Light view-projection matrix of shadow cascade 3 (farthest).</summary>
        public Matrix4x4 CascadeViewProjection3;
    }

    /// <summary>
    /// A point light: position with range in world space and linear color (rgb)
    /// plus intensity (w). Uploaded as elements of a StructuredBuffer to the GPU.
    /// </summary>
    public struct PointLight
    {
        /// <summary>World-space position (xyz) and effective range / cutoff radius (w).</summary>
        public Vector4 Position;
        /// <summary>Linear color (rgb) and intensity (w). Zero intensity disables the light.</summary>
        public Vector4 ColorAndIntensity;

        /// <summary>
        /// Create a point light with a custom range.
        /// </summary>
        /// <param name="position">World-space position.</param>
        /// <param name="color">Linear color.</param>
        /// <param name="intensity">Light intensity; zero disables the light.</param>
        /// <param name="range">Cutoff radius beyond which the light contributes nothing.</param>
        public PointLight(in Vector3 position, in Vector3 color, float intensity, float range)
        {
            Position = new Vector4(position, range);
            ColorAndIntensity = new Vector4(color, intensity);
        }
    }

    /// <summary>The number of shadow cascades (atlas quadrants) the pipeline supports.</summary>
    public const int ShadowCascadeCount = 4;

    /// <summary>The maximum number of point lights the StructuredBuffer can hold.</summary>
    public const int MaxPointLights = 256;

    /// <summary>
    /// Per-frame data uploaded to the lighting pass. Layout must match the
    /// <c>_data</c> cbuffer in DeferredLighting.hlsl exactly.
    /// </summary>
    public struct DeferredLightingData
    {
        /// <summary>Inverse of the camera view-projection matrix.</summary>
        public Matrix4x4 InvViewProjection;
        /// <summary>Sun light view-projection matrix of shadow cascade 0 (nearest).</summary>
        public Matrix4x4 SunViewProjection0;
        /// <summary>Sun light view-projection matrix of shadow cascade 1.</summary>
        public Matrix4x4 SunViewProjection1;
        /// <summary>Sun light view-projection matrix of shadow cascade 2.</summary>
        public Matrix4x4 SunViewProjection2;
        /// <summary>Sun light view-projection matrix of shadow cascade 3 (farthest).</summary>
        public Matrix4x4 SunViewProjection3;
        /// <summary>Camera position in world space (w unused).</summary>
        public Vector4 CameraPosition;
        /// <summary>Normalized direction the sun light travels (w unused).</summary>
        public Vector4 SunDirection;
        /// <summary>Sun linear color (rgb) and intensity (w).</summary>
        public Vector4 SunColorAndIntensity;
        /// <summary>Atmosphere parameters: x=rayleighScale, y=mieScale, z=miePhaseG, w=exposure (see Atmosphere.hlsli).</summary>
        public Vector4 SkyParams;
        /// <summary>Atmosphere parameters: x=starIntensity, y=nightFloor, z=sunRadianceScale, w=ambientFloor (minimum hemisphere ambient multiplier).</summary>
        public Vector4 SkyParams2;
        /// <summary>Azimuthally filtered physical-sky radiance at the horizon.</summary>
        public Vector4 SkyHorizonColor;
        /// <summary>Filtered physical-sky radiance at the zenith.</summary>
        public Vector4 SkyZenithColor;
        /// <summary>x=shadowEnabled y=numPointLights z=shadowMapSize w=sunDiscEnabled.</summary>
        public Vector4 Params;
        /// <summary>View-distance end boundary of each cascade; beyond w there is no shadow.</summary>
        public Vector4 CascadeSplits;
        /// <summary>World units per shadow texel of each cascade (for the normal-offset bias).</summary>
        public Vector4 CascadeTexelSizes;
        /// <summary>x=cascadeDebugTint, y=shadowFactorView, z=unused, w=aoDebugView.</summary>
        public Vector4 Params2;
        /// <summary>xy=render target size in pixels (filled by the pipeline).</summary>
        public Vector4 ViewportSize;
        /// <summary>x=giEnabled, y=giDiffuseStrength, z=giSpecularStrength, w=giDebugView (0=off 1=diffuse 2=specular 3=visibility).</summary>
        public Vector4 Params3;
        /// <summary>x=sunDiscSize (cosine angular threshold, higher = smaller disc), y=sunDiscBrightness (HDR visual brightness independent of lighting intensity), z=1/GI trace width, w=1/GI trace height (filled by the pipeline, 0 when GI is off).</summary>
        public Vector4 Params4;

    }

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly Mesh _fullScreenMesh;

    private readonly GPUAttachmentLayout _gbufferLayout;
    private readonly GPUAttachmentLayout _shadowLayout;
    private RenderTexture _gbufferRT;
    private readonly RenderTexture _shadowRT;

    private readonly Shader _gbufferShader;
    private readonly Shader? _gbufferTangentShader;
    private readonly GraphicsMaterial _shadowMaterial;
    private readonly GraphicsMaterial? _shadowTangentMaterial;
    private readonly GraphicsMaterial _lightingMaterial;
    private Texture2D? _flatNormalTexture;
    private GraphicsBuffer? _cameraBuffer;

    private readonly GraphicsValueBuffer<DeferredLightingData> _lightingDataBuffer;
    private readonly GraphicsValueBuffer<ShadowCascadeData> _shadowDataBuffer;
    private readonly GraphicsArrayBuffer<PointLight> _pointLightBuffer;

    // Pluggable render effects (AO, GI, etc.) executed between the G-buffer
    // and lighting passes. The pipeline binds their output textures to the
    // lighting material automatically after execution.
    private readonly List<IRenderPlugin> _plugins = new();

    private readonly RenderContext _shadowContext;
    private readonly RenderContext _gbufferContext;
    private readonly RenderContext _lightingContext;

    /// <summary>
    /// The G-buffer render texture (albedo+packed-roughness /
    /// detail+packed-geometric normal / metallic-roughness-ao /
    /// emissive+packed-geometric normal / depth).
    /// </summary>
    public RenderTexture GBuffer => _gbufferRT;

    /// <summary>
    /// The depth-only shadow map render texture (a 2x2 cascade atlas).
    /// </summary>
    public RenderTexture ShadowMap => _shadowRT;

    /// <summary>
    /// The width of one shadow cascade (atlas quadrant) in texels.
    /// </summary>
    public uint ShadowMapSize { get; }

    /// <summary>
    /// The attachment layout of the G-buffer pass, used to record render bundles
    /// (see <see cref="SubRenderContext.Begin(GPUAttachmentLayout)"/>).
    /// </summary>
    public GPUAttachmentLayout GBufferLayout => _gbufferLayout;

    /// <summary>
    /// The attachment layout of the shadow pass, used to record render bundles
    /// (see <see cref="SubRenderContext.Begin(GPUAttachmentLayout)"/>).
    /// </summary>
    public GPUAttachmentLayout ShadowLayout => _shadowLayout;

    /// <summary>
    /// The live G-buffer render context for immediate (per-frame dynamic) draws.
    /// Only valid between <see cref="BeginGBufferPass"/> and <see cref="EndGBufferPass"/>.
    /// </summary>
    public IRenderContext GBufferContext => _gbufferContext;

    /// <summary>
    /// The live shadow render context for immediate (per-frame dynamic) draws.
    /// Only valid between <see cref="BeginShadowPass"/> and <see cref="EndShadowPass"/>.
    /// </summary>
    public IRenderContext ShadowContext => _shadowContext;

    /// <summary>
    /// Register a pluggable render effect. The pipeline executes the plugin at
    /// its declared <see cref="RenderInjectionPoint"/> and binds the output
    /// textures to the lighting material automatically. The caller owns the
    /// plugin's lifetime (dispose it after disposing the pipeline or
    /// unregistering it).
    /// </summary>
    /// <param name="plugin">The render plugin to register.</param>
    public void RegisterPlugin(IRenderPlugin plugin)
    {
        _plugins.Add(plugin);
    }

    /// <summary>
    /// Unregister a previously registered render plugin.
    /// </summary>
    public void UnregisterPlugin(IRenderPlugin plugin)
    {
        _plugins.Remove(plugin);
    }

    /// <summary>
    /// Get the first registered plugin of the specified type, or null.
    /// </summary>
    public T? GetPlugin<T>() where T : class, IRenderPlugin
    {
        for (int i = 0; i < _plugins.Count; i++)
        {
            if (_plugins[i] is T typed)
            {
                return typed;
            }
        }
        return null;
    }

    /// <summary>
    /// Execute all plugins registered at the given injection point and bind
    /// their output textures to the lighting material. Called by the caller
    /// between <see cref="EndGBufferPass"/> and <see cref="RenderLighting"/>.
    /// </summary>
    public void ExecutePlugins(RenderInjectionPoint point, RenderPluginContext context)
    {
        for (int i = 0; i < _plugins.Count; i++)
        {
            IRenderPlugin plugin = _plugins[i];
            if (plugin.InjectionPoint == point)
            {
                plugin.Execute(context);
            }
        }
        RebindPluginOutputs(context);
    }

    private void RebindPluginOutputs(RenderPluginContext context)
    {
        if (context.AOResult != null)
        {
            _lightingMaterial.SetRenderTexture("_aoTexture", context.AOResult);
        }
        else
        {
            _lightingMaterial.SetTexture("_aoTexture", _rendering.TextureWhite);
        }

        if (context.GIDiffuse != null)
        {
            _lightingMaterial.SetRenderTexture("_giDiffuse", context.GIDiffuse);
            _lightingMaterial.SetRenderTexture("_giSpecular", context.GISpecular!);
        }
        else
        {
            _lightingMaterial.SetTexture("_giDiffuse", _rendering.TextureBlack);
            _lightingMaterial.SetTexture("_giSpecular", _rendering.TextureBlack);
        }
    }

    /// <summary>
    /// Create the deferred PBR pipeline with the given shaders.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="gbufferShader">The G-buffer shader (GBuffer.hlsl).</param>
    /// <param name="shadowShader">The shadow map depth shader (ShadowDepth.hlsl).</param>
    /// <param name="lightingShaderText">The source text of the deferred lighting shader (DeferredLighting.hlsl).</param>
    /// <param name="lightingShaderName">The name of the deferred lighting shader.</param>
    /// <param name="shadowMapSize">The per-cascade shadow map resolution in texels; the shadow map is a 2x2 atlas of <see cref="ShadowCascadeCount"/> cascades, so the actual texture is twice this size along each axis.</param>
    /// <param name="width">The initial G-buffer width in pixels.</param>
    /// <param name="height">The initial G-buffer height in pixels.</param>
    /// <param name="gbufferTangentShader">Optional tangent-space G-buffer shader (GBufferTangent.hlsl) enabling <see cref="CreateGBufferTangentMaterial"/> for normal-mapped materials.</param>
    /// <param name="shadowTangentShader">Optional tangent-layout shadow depth shader (ShadowDepthTangent.hlsl) enabling <see cref="DrawShadowTangent"/> for tangent-bearing meshes.</param>
    public PBRDeferredPipeline(
        RenderingSystem rendering,
        Shader gbufferShader,
        Shader shadowShader,
        string lightingShaderText,
        string lightingShaderName,
        uint shadowMapSize = 2048,
        uint width = 1280,
        uint height = 720,
        Shader? gbufferTangentShader = null,
        Shader? shadowTangentShader = null)
    {
        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _fullScreenMesh = rendering.MeshFullScreen;
        ShadowMapSize = shadowMapSize;

        // The lighting shader declares its depth textures with the DEFINE_TEX2D_DEPTH*
        // macros, so the reflection already carries the Depth sample type and the
        // comparison sampler; the pipeline layout is built from the reflection.
        Shader lightingShader = rendering.CreateShader(lightingShaderText, lightingShaderName);

        _gbufferLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [
                // RGBA8Unorm + manual sRGB encode/decode: wgpu forbids STORAGE_BINDING
                // usage on sRGB textures, and engine framebuffer textures always carry it.
                new ColorAttachment(PixelFormat.RGBA8Unorm),
                new ColorAttachment(PixelFormat.RGBA16Float),
                new ColorAttachment(PixelFormat.RGBA8Unorm),
                // Linear emissive, HDR-capable.
                new ColorAttachment(PixelFormat.RGBA16Float),
            ],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_gbuffer_pass"));

        _shadowLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_shadow_pass"));

        _gbufferRT = rendering.CreateRenderTexture(_gbufferLayout, width, height, "pbr_gbuffer");
        // 2x2 cascade atlas: each cascade renders into one quadrant.
        _shadowRT = rendering.CreateRenderTexture(_shadowLayout, shadowMapSize * 2, shadowMapSize * 2, "pbr_shadow_map");

        _gbufferShader = gbufferShader;
        _gbufferTangentShader = gbufferTangentShader;

        _shadowDataBuffer = rendering.CreateGraphicsValueBuffer<ShadowCascadeData>("pbr_shadow_data");

        _shadowMaterial = rendering.CreateMaterial(shadowShader);
        _shadowMaterial.DepthStencilState = DepthStencilState.Write;
        _shadowMaterial.RasterizerState = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.Clockwise);
        _shadowMaterial.SetBuffer(ShaderResourceId.Data, _shadowDataBuffer);

        if (shadowTangentShader != null)
        {
            _shadowTangentMaterial = rendering.CreateMaterial(shadowTangentShader);
            _shadowTangentMaterial.DepthStencilState = DepthStencilState.Write;
            _shadowTangentMaterial.RasterizerState = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.Clockwise);
            _shadowTangentMaterial.SetBuffer(ShaderResourceId.Data, _shadowDataBuffer);
        }

        // IMPORTANT: DepthStencilState.None means depthCompare=Never — with a depth
        // attachment present (the engine's HDR main target), every fragment would be
        // rejected. Default (Always) disables the depth test without rejecting pixels.
        _lightingMaterial = rendering.CreateMaterial(lightingShader);
        _lightingMaterial.DepthStencilState = DepthStencilState.Default;
        _lightingMaterial.RasterizerState = RasterizerState.CullNone;

        _lightingDataBuffer = rendering.CreateGraphicsValueBuffer<DeferredLightingData>("pbr_lighting_data");
        _lightingMaterial.SetBuffer(ShaderResourceId.Data, _lightingDataBuffer);

        // Point lights are uploaded as a StructuredBuffer (not cbuffer) so the
        // count is bounded only by GPU memory, not by cbuffer size limits.
        _pointLightBuffer = rendering.CreateGraphicsArrayBuffer<PointLight>(MaxPointLights, "pbr_point_lights");
        _lightingMaterial.SetBuffer(ShaderResourceId.PointLights, _pointLightBuffer);

        RebindLightingTargets();

        _shadowContext = rendering.CreateRenderContext("pbr_shadow_pass");
        _gbufferContext = rendering.CreateRenderContext("pbr_gbuffer_pass");
        _lightingContext = rendering.CreateRenderContext("pbr_lighting_pass");
    }

    /// <summary>
    /// Set the camera bound by <see cref="CreateGBufferMaterial"/> and
    /// <see cref="CreateGBufferTangentMaterial"/> when they create a material
    /// (materials created earlier are not updated). The caller must keep the camera
    /// updated (e.g. <c>UpdateMatrixToGPU</c>) before drawing each frame.
    /// </summary>
    /// <param name="cameraBuffer">The camera buffer (a <c>CameraPerspectiveBuffer</c>).</param>
    public void SetCamera(GraphicsBuffer cameraBuffer)
    {
        _cameraBuffer = cameraBuffer;
    }

    /// <summary>
    /// Recreate the G-buffer at a new resolution. Call when the view resizes.
    /// <br/>Render bundles recorded against <see cref="GBufferLayout"/> stay valid:
    /// the layout (attachment formats) does not change, only the textures do.
    /// </summary>
    /// <param name="width">The new G-buffer width in pixels.</param>
    /// <param name="height">The new G-buffer height in pixels.</param>
    public void Resize(uint width, uint height)
    {
        _gbufferRT.Dispose();
        _gbufferRT = _rendering.CreateRenderTexture(_gbufferLayout, width, height, "pbr_gbuffer");
        for (int i = 0; i < _plugins.Count; i++)
        {
            _plugins[i].Resize(width, height);
        }
        RebindLightingTargets();
    }

    /// <summary>
    /// Create a G-buffer material for the non-tangent shader (GBuffer.hlsl). The
    /// pipeline applies the pass-mandated state (depth write, rasterizer, texture
    /// slots, camera binding); the caller owns the material and must dispose it.
    /// </summary>
    /// <param name="albedoTexture">The albedo texture; null binds the shared white texture.</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    /// <returns>The caller-owned G-buffer material.</returns>
    public GraphicsMaterial CreateGBufferMaterial(Texture2D? albedoTexture, bool doubleSided = false, string name = "pbr_gbuffer_material")
    {
        var material = _rendering.CreateMaterial(_gbufferShader, name);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        if (_cameraBuffer != null)
        {
            material.SetBuffer(ShaderResourceId.Camera, _cameraBuffer);
        }
        return material;
    }

    /// <summary>
    /// Create a G-buffer material for the tangent shader (GBufferTangent.hlsl) with
    /// per-material albedo, normal, metallic-roughness and emissive textures. The
    /// pipeline applies the pass-mandated state (depth write, rasterizer, texture
    /// slots, camera binding); the caller owns the material and must dispose it.
    /// </summary>
    /// <param name="albedoTexture">The albedo texture; null binds the shared white texture.</param>
    /// <param name="normalTexture">The tangent-space normal map; null binds a flat normal texture.</param>
    /// <param name="metallicRoughnessTexture">The metallic-roughness texture (roughness in G, metallic in B); null binds the shared white texture.</param>
    /// <param name="emissiveTexture">The emissive texture; null binds the shared black texture.</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="name">The material name for debugging.</param>
    /// <returns>The caller-owned G-buffer material.</returns>
    /// <exception cref="InvalidOperationException">The pipeline was created without a tangent G-buffer shader.</exception>
    public GraphicsMaterial CreateGBufferTangentMaterial(
        Texture2D? albedoTexture, Texture2D? normalTexture, Texture2D? metallicRoughnessTexture,
        Texture2D? emissiveTexture, bool doubleSided = false, string name = "pbr_gbuffer_tangent_material")
    {
        if (_gbufferTangentShader == null)
        {
            throw new InvalidOperationException(
                "CreateGBufferTangentMaterial requires the pipeline to be created with a tangent G-buffer shader.");
        }
        var material = _rendering.CreateMaterial(_gbufferTangentShader, name);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        SetGBufferTangentMaterialTextures(material, albedoTexture, normalTexture, metallicRoughnessTexture, emissiveTexture);
        if (_cameraBuffer != null)
        {
            material.SetBuffer(ShaderResourceId.Camera, _cameraBuffer);
        }
        return material;
    }

    /// <summary>
    /// (Re)bind the texture slots of a tangent G-buffer material created by
    /// <see cref="CreateGBufferTangentMaterial"/>, applying the same fallback textures.
    /// Use when textures stream in asynchronously after the material was created
    /// (render bundles recorded with the material must be re-recorded afterwards).
    /// </summary>
    /// <param name="material">The material to update.</param>
    /// <param name="albedoTexture">The albedo texture; null binds the shared white texture.</param>
    /// <param name="normalTexture">The tangent-space normal map; null binds a flat normal texture.</param>
    /// <param name="metallicRoughnessTexture">The metallic-roughness texture; null binds the shared white texture.</param>
    /// <param name="emissiveTexture">The emissive texture; null binds the shared black texture.</param>
    public void SetGBufferTangentMaterialTextures(
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
    /// identity tangent-space normal. Only the .rg channels are sampled, so the RGBA8
    /// texture is a valid stand-in for the BC5 normal maps.
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

    /// <summary>
    /// The GPU buffer holding the point light array. Bind this to any pass that
    /// needs point light data (e.g. <see cref="VoxelGiRenderer.SetPointLightBuffer"/>).
    /// </summary>
    public GraphicsBuffer PointLightBuffer => _pointLightBuffer;

    /// <summary>
    /// Upload point lights to the GPU StructuredBuffer and record the active
    /// count in <paramref name="data"/>. Call once per frame before
    /// <see cref="RenderLighting"/>; the caller passes the same
    /// <paramref name="data"/> to <c>RenderLighting</c>.
    /// </summary>
    /// <param name="lights">Active point lights; excess lights beyond
    /// <see cref="MaxPointLights"/> are silently dropped.</param>
    /// <param name="data">The per-frame lighting data whose
    /// <see cref="DeferredLightingData.Params"/>.Y (numPointLights) is updated.</param>
    public void UpdatePointLights(ReadOnlySpan<PointLight> lights, ref DeferredLightingData data)
    {
        int count = Math.Min(lights.Length, MaxPointLights);
        var span = _pointLightBuffer.AsSpan();
        for (int i = 0; i < count; i++)
        {
            span[i] = lights[i];
        }
        _pointLightBuffer.UpdateBufferRanged(0, (uint)count);
        data.Params = new Vector4(data.Params.X, count, data.Params.Z, data.Params.W);
    }

    /// <summary>
    /// Begin the shadow map pass for one cascade. All shadow draws must happen
    /// between this and <see cref="EndShadowPass"/>: bundle replays via
    /// <see cref="ExecuteShadowSubContext"/> and/or immediate draws via
    /// <see cref="ShadowContext"/>. Cascades render into their own quadrant of
    /// the 2x2 atlas; only the first cascade's pass clears the atlas.
    /// </summary>
    /// <param name="cascadeIndex">The cascade to render (0 = nearest .. <see cref="ShadowCascadeCount"/>-1).</param>
    /// <param name="sunViewProjection">The light view-projection matrix of this cascade (orthographic for the sun).</param>
    public void BeginShadowPass(int cascadeIndex, in Matrix4x4 sunViewProjection)
    {
        // Fold the atlas quadrant into the projection. The scissor is essential:
        // geometry outside this cascade's orthographic box can otherwise transform
        // into another atlas quadrant and corrupt that cascade's depth values.
        float offsetX = (cascadeIndex % 2) - 0.5f;
        float offsetY = 0.5f - (cascadeIndex / 2);
        Matrix4x4 quadrant = Matrix4x4.CreateScale(0.5f, 0.5f, 1.0f) * Matrix4x4.CreateTranslation(offsetX, offsetY, 0.0f);
        SetCascadeViewProjection(cascadeIndex, sunViewProjection * quadrant);
        _shadowContext.Begin(_shadowRT.FrameBuffer, clearDepth: cascadeIndex == 0 ? 1.0f : null);
        _shadowContext.SetScissorRect(
            (uint)(cascadeIndex % 2) * ShadowMapSize,
            (uint)(cascadeIndex / 2) * ShadowMapSize,
            ShadowMapSize,
            ShadowMapSize);
    }

    private void SetCascadeViewProjection(int cascadeIndex, in Matrix4x4 viewProjection)
    {
        // All four cascade passes record before their command buffers are submitted,
        // so every slot holds this frame's value when the passes execute on the GPU.
        switch (cascadeIndex)
        {
            case 0: _shadowDataBuffer.Value.CascadeViewProjection0 = viewProjection; break;
            case 1: _shadowDataBuffer.Value.CascadeViewProjection1 = viewProjection; break;
            case 2: _shadowDataBuffer.Value.CascadeViewProjection2 = viewProjection; break;
            default: _shadowDataBuffer.Value.CascadeViewProjection3 = viewProjection; break;
        }
        _shadowDataBuffer.UpdateBuffer();
    }

    /// <summary>
    /// Draw a mesh into the shadow map. Must be recorded into a shadow render bundle
    /// or called on <see cref="ShadowContext"/> inside the shadow pass.
    /// </summary>
    /// <param name="target">The render context to record into or draw with.</param>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="model">The world transform of the mesh.</param>
    /// <param name="cascadeIndex">The cascade whose light view-projection is used.</param>
    public void DrawShadow(IRenderContext target, in Mesh mesh, in Matrix4x4 model, int cascadeIndex)
    {
        target.DrawWithConstant(mesh, _shadowMaterial,
            new ShadowDrawConstants { Model = model, Params = new Vector4(cascadeIndex, 0.0f, 0.0f, 0.0f) });
    }

    /// <summary>
    /// Draw a tangent-bearing mesh (<see cref="VertexPositionNormalTextureTangent"/>) into
    /// the shadow map. Must be recorded into a shadow render bundle or called on
    /// <see cref="ShadowContext"/> inside the shadow pass. Requires the tangent shadow
    /// shader (<c>shadowTangentShader</c> constructor argument): a mesh's vertex layout must
    /// match its shader exactly, so tangent meshes cannot go through <see cref="DrawShadow"/>.
    /// </summary>
    /// <param name="target">The render context to record into or draw with.</param>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="model">The world transform of the mesh.</param>
    /// <param name="cascadeIndex">The cascade whose light view-projection is used.</param>
    /// <exception cref="InvalidOperationException">The pipeline was created without a tangent shadow shader.</exception>
    public void DrawShadowTangent(IRenderContext target, in Mesh mesh, in Matrix4x4 model, int cascadeIndex)
    {
        if (_shadowTangentMaterial == null)
        {
            throw new InvalidOperationException(
                "DrawShadowTangent requires the pipeline to be created with a tangent shadow shader.");
        }
        target.DrawWithConstant(mesh, _shadowTangentMaterial,
            new ShadowDrawConstants { Model = model, Params = new Vector4(cascadeIndex, 0.0f, 0.0f, 0.0f) });
    }

    /// <summary>
    /// Replay a recorded shadow render bundle. Must be called inside the shadow pass
    /// (the pass applies its scissor rect, which bundles cannot set themselves).
    /// </summary>
    /// <param name="subContext">The recorded sub render context.</param>
    public void ExecuteShadowSubContext(SubRenderContext subContext)
    {
        _shadowContext.ExecuteSubContext(subContext);
    }

    /// <summary>
    /// End the shadow map pass and submit its commands.
    /// </summary>
    public void EndShadowPass()
    {
        _shadowContext.End();
    }

    /// <summary>
    /// Compute cascaded shadow map data for a directional sun: per-cascade light
    /// view-projection matrices, split boundaries and world texel sizes.
    /// <br/>Splits follow the practical split scheme (log/uniform blend controlled by
    /// <paramref name="splitLambda"/>) on radial camera distance. The light space is a
    /// pure rotation (camera-independent) and each cascade fits a fixed-radius bounding
    /// sphere of its frustum slice, snapped to texel increments, so the shadow map stays
    /// stable when the camera moves or rotates.
    /// </summary>
    /// <param name="invCameraViewProjection">Inverse of the camera view-projection matrix (for frustum edge rays).</param>
    /// <param name="cameraPosition">World-space camera position.</param>
    /// <param name="shadowNear">Near boundary of cascade 0, typically the camera near plane distance.</param>
    /// <param name="shadowDistance">Distance beyond which shadows are not rendered.</param>
    /// <param name="sunDirection">Normalized direction the sun light travels.</param>
    /// <param name="casterExtension">How far the light-space depth range extends back toward the sun to include off-screen casters.</param>
    /// <param name="splitLambda">PSSM blend factor: 1 = fully logarithmic, 0 = fully uniform.</param>
    /// <param name="shadowMapSize">The per-cascade shadow map resolution in texels.</param>
    /// <param name="cascadeViewProjections">Output light view-projection matrices, one per cascade (<see cref="ShadowCascadeCount"/>).</param>
    /// <param name="cascadeSplits">Output radial end distance of each cascade.</param>
    /// <param name="cascadeTexelSizes">Output world units per shadow texel of each cascade.</param>
    /// <exception cref="ArgumentException">An output span does not hold <see cref="ShadowCascadeCount"/> entries.</exception>
    public static void ComputeShadowCascades(
        in Matrix4x4 invCameraViewProjection,
        in Vector3 cameraPosition,
        float shadowNear,
        float shadowDistance,
        in Vector3 sunDirection,
        float casterExtension,
        float splitLambda,
        uint shadowMapSize,
        Span<Matrix4x4> cascadeViewProjections,
        Span<float> cascadeSplits,
        Span<float> cascadeTexelSizes)
    {
        if (cascadeViewProjections.Length < ShadowCascadeCount ||
            cascadeSplits.Length < ShadowCascadeCount ||
            cascadeTexelSizes.Length < ShadowCascadeCount)
        {
            throw new ArgumentException($"Output spans must hold {ShadowCascadeCount} entries.");
        }

        // Frustum edge rays: the four far-plane corners in world space.
        Span<Vector3> edgeRays = stackalloc Vector3[4];
        int rayIndex = 0;
        for (int y = -1; y <= 1; y += 2)
        {
            for (int x = -1; x <= 1; x += 2)
            {
                Vector4 corner = Vector4.Transform(new Vector4(x, y, 1.0f, 1.0f), invCameraViewProjection);
                Vector3 farCorner = new Vector3(corner.X, corner.Y, corner.Z) / corner.W;
                edgeRays[rayIndex++] = Vector3.Normalize(farCorner - cameraPosition);
            }
        }

        // Camera-independent light space: a pure rotation around the world origin, so
        // world geometry stays still in light space while the camera moves.
        Vector3 up = Math.Abs(Vector3.Dot(sunDirection, Vector3.UnitZ)) > 0.95f ? Vector3.UnitY : Vector3.UnitZ;
        Matrix4x4 lightView = Matrix4x4.CreateLookAtLeftHanded(Vector3.Zero, sunDirection, up);

        float sliceNear = shadowNear;
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int c = 0; c < ShadowCascadeCount; c++)
        {
            float p = (c + 1) / (float)ShadowCascadeCount;
            float logarithmic = shadowNear * MathF.Pow(shadowDistance / shadowNear, p);
            float uniform = shadowNear + (shadowDistance - shadowNear) * p;
            float sliceFar = splitLambda * logarithmic + (1.0f - splitLambda) * uniform;
            cascadeSplits[c] = sliceFar;

            // Frustum slice corners on the edge rays.
            Vector3 center = Vector3.Zero;
            for (int r = 0; r < 4; r++)
            {
                corners[r] = cameraPosition + edgeRays[r] * sliceNear;
                corners[r + 4] = cameraPosition + edgeRays[r] * sliceFar;
                center += corners[r] + corners[r + 4];
            }
            center /= 8.0f;

            // Fit a bounding sphere: its radius is invariant to camera rotation and
            // translation, so the texel grid has a constant world size.
            float radius = 0.0f;
            for (int r = 0; r < 8; r++)
            {
                radius = Math.Max(radius, Vector3.Distance(corners[r], center));
            }

            // Grow by one texel per side so the sphere stays inside the snapped box
            // (snapping shifts the box by up to ~0.71 texels diagonally).
            float texel = radius * 2.0f / shadowMapSize;
            radius += texel;
            texel = radius * 2.0f / shadowMapSize;

            // Snap the box center to whole texels so it steps discretely instead of
            // sliding continuously under camera movement.
            Vector3 centerLight = Vector3.Transform(center, lightView);
            centerLight.X = MathF.Floor(centerLight.X / texel) * texel;
            centerLight.Y = MathF.Floor(centerLight.Y / texel) * texel;

            // Depth range: the sphere plus an extension toward the sun for off-screen
            // casters, quantized so the depth grid is stable too. Negative near values
            // are legal for orthographic projections.
            float zMin = centerLight.Z - radius - casterExtension;
            float zMax = centerLight.Z + radius;
            float texelZ = (zMax - zMin) / shadowMapSize;
            zMin = MathF.Floor(zMin / texelZ) * texelZ;
            zMax = zMin + texelZ * shadowMapSize;

            Matrix4x4 ortho = Matrix4x4.CreateOrthographicOffCenterLeftHanded(
                centerLight.X - radius, centerLight.X + radius,
                centerLight.Y - radius, centerLight.Y + radius,
                zMin, zMax);
            cascadeViewProjections[c] = lightView * ortho;
            cascadeTexelSizes[c] = texel;

            sliceNear = sliceFar;
        }
    }

    /// <summary>
    /// Begin the G-buffer pass. All G-buffer draws must happen between this and
    /// <see cref="EndGBufferPass"/>: bundle replays via <see cref="ExecuteGBufferSubContext"/>
    /// and/or immediate draws via <see cref="GBufferContext"/>.
    /// </summary>
    public void BeginGBufferPass()
    {
        ReadOnlySpan<ClearColorData> clearColors = stackalloc ClearColorData[4]
        {
            new(0, Vector4.Zero),
            new(1, new Vector4(0.5f, 0.5f, 1.0f, 1.0f)),
            new(2, Vector4.Zero),
            new(3, Vector4.Zero),
        };
        _gbufferContext.Begin(_gbufferRT.FrameBuffer, clearColors, 1.0f);
    }

    /// <summary>
    /// Draw a mesh into the G-buffer. Must be recorded into a G-buffer render bundle
    /// or called on <see cref="GBufferContext"/> inside the G-buffer pass.
    /// </summary>
    /// <param name="target">The render context to record into or draw with.</param>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The G-buffer material (created by <see cref="CreateGBufferMaterial"/> or <see cref="CreateGBufferTangentMaterial"/>, owned by the caller).</param>
    /// <param name="model">The world transform of the mesh.</param>
    /// <param name="baseColor">The linear base color, multiplied with the albedo texture.</param>
    /// <param name="metallicRoughnessAO">x=metallic y=roughness z=ambient occlusion; metallic and roughness multiply with the metallic-roughness texture when bound.</param>
    /// <param name="alphaCutoff">Alpha test threshold; 0 disables alpha testing.</param>
    public void DrawGBuffer(IRenderContext target, in Mesh mesh, GraphicsMaterial material, in Matrix4x4 model,
        in Vector4 baseColor, in Vector4 metallicRoughnessAO, float alphaCutoff = 0.0f)
    {
        DrawGBuffer(target, mesh, material, model, baseColor, metallicRoughnessAO, Vector3.Zero, alphaCutoff);
    }

    /// <summary>
    /// Draw a mesh into the G-buffer with an emissive factor. Must be recorded into a
    /// G-buffer render bundle or called on <see cref="GBufferContext"/> inside the
    /// G-buffer pass.
    /// </summary>
    /// <param name="target">The render context to record into or draw with.</param>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The G-buffer material (created by <see cref="CreateGBufferMaterial"/> or <see cref="CreateGBufferTangentMaterial"/>, owned by the caller).</param>
    /// <param name="model">The world transform of the mesh.</param>
    /// <param name="baseColor">The linear base color, multiplied with the albedo texture.</param>
    /// <param name="metallicRoughnessAO">x=metallic y=roughness z=ambient occlusion; metallic and roughness multiply with the metallic-roughness texture when bound.</param>
    /// <param name="emissiveFactor">The linear emissive color, multiplied with the emissive texture when bound.</param>
    /// <param name="alphaCutoff">Alpha test threshold; 0 disables alpha testing.</param>
    public void DrawGBuffer(IRenderContext target, in Mesh mesh, GraphicsMaterial material, in Matrix4x4 model,
        in Vector4 baseColor, in Vector4 metallicRoughnessAO, in Vector3 emissiveFactor, float alphaCutoff = 0.0f)
    {
        target.DrawWithConstant(mesh, material,
            new PBRDrawConstants
            {
                Model = model,
                BaseColor = baseColor,
                MetallicRoughnessAO = metallicRoughnessAO,
                Params = new Vector4(alphaCutoff, 0.0f, 0.0f, 0.0f),
                Emissive = new Vector4(emissiveFactor, 1.0f),
            });
    }

    /// <summary>
    /// Replay a recorded G-buffer render bundle. Must be called inside the G-buffer pass.
    /// </summary>
    /// <param name="subContext">The recorded sub render context.</param>
    public void ExecuteGBufferSubContext(SubRenderContext subContext)
    {
        _gbufferContext.ExecuteSubContext(subContext);
    }

    /// <summary>
    /// End the G-buffer pass and submit its commands.
    /// </summary>
    public void EndGBufferPass()
    {
        _gbufferContext.End();
    }

    /// <summary>
    /// Resolve lighting, shadows and the sky into the target frame buffer
    /// (typically the engine's HDR main target).
    /// </summary>
    /// <param name="target">The frame buffer to render the lighting result into.</param>
    /// <param name="data">Per-frame lighting data; <see cref="DeferredLightingData.ViewportSize"/> is filled by the pipeline.</param>
    public void RenderLighting(GPUFrameBuffer target, ref DeferredLightingData data)
    {
        data.ViewportSize = new Vector4(_gbufferRT.Width, _gbufferRT.Height, 0, 0);
        _lightingDataBuffer.UpdateBuffer(data);
        _lightingContext.Begin(target);
        _lightingContext.Draw(_fullScreenMesh, _lightingMaterial);
        _lightingContext.End();
    }

    private void RebindLightingTargets()
    {
        _lightingMaterial.SetRenderTexture("_albedo", _gbufferRT, 0);
        _lightingMaterial.SetRenderTexture("_normal", _gbufferRT, 1);
        _lightingMaterial.SetRenderTexture("_mrAO", _gbufferRT, 2);
        _lightingMaterial.SetRenderTexture("_emissive", _gbufferRT, 3);
        _lightingMaterial.SetRenderTextureDepth("_gbufferDepth", _gbufferRT);
        _lightingMaterial.SetRenderTextureDepth("_shadowMap", _shadowRT);
        // Plugin output textures default to white/black until a plugin sets them.
        _lightingMaterial.SetTexture("_aoTexture", _rendering.TextureWhite);
        _lightingMaterial.SetTexture("_giDiffuse", _rendering.TextureBlack);
        _lightingMaterial.SetTexture("_giSpecular", _rendering.TextureBlack);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shadowContext.Dispose();
            _gbufferContext.Dispose();
            _lightingContext.Dispose();
            _lightingDataBuffer.Dispose();
            _shadowDataBuffer.Dispose();
            _pointLightBuffer.Dispose();
            _lightingMaterial.Dispose();
            _flatNormalTexture?.Dispose();
            _shadowTangentMaterial?.Dispose();
            _shadowMaterial.Dispose();
            _gbufferRT.Dispose();
            _shadowRT.Dispose();
            _gbufferLayout.Dispose();
            _shadowLayout.Dispose();
        }
    }
}
