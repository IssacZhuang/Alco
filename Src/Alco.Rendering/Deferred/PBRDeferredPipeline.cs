using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A deferred PBR rendering pipeline built on the engine's WebGPU resources.
/// <br/>Owns a G-buffer (albedo / normal / metallic-roughness-ao / emissive + depth), a
/// depth-only shadow map holding <see cref="ShadowCascadeCount"/> cascades in a 2x2 atlas,
/// and three render contexts (shadow pass, G-buffer pass, lighting pass).
/// <br/>The caller drives the frame explicitly: per cascade
/// <c>BeginShadowPass → DrawShadow×N → EndShadowPass</c>, then
/// <c>BeginGBufferPass → DrawGBuffer×N → EndGBufferPass</c>, then
/// <c>RenderLighting(target, ref data)</c> which resolves lighting, sky and shadows
/// into the target frame buffer (typically the engine's HDR main target).
/// <br/>Cascade splits are computed by <see cref="ComputeShadowCascades"/> (PSSM,
/// camera-fitted, texel-snapped).
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
    /// <c>Constants</c> struct in ShadowDepth.hlsl exactly.
    /// </summary>
    public struct ShadowDrawConstants
    {
        /// <summary>Combined model * light view-projection matrix.</summary>
        public Matrix4x4 LightViewProjection;
    }

    /// <summary>
    /// A point light: position in world space and linear color (rgb) plus intensity (w).
    /// </summary>
    public struct PointLight
    {
        /// <summary>World-space position (w is unused).</summary>
        public Vector4 Position;
        /// <summary>Linear color (rgb) and intensity (w). Zero intensity disables the light.</summary>
        public Vector4 ColorAndIntensity;

        /// <summary>
        /// Create a point light.
        /// </summary>
        /// <param name="position">World-space position.</param>
        /// <param name="color">Linear color.</param>
        /// <param name="intensity">Light intensity; zero disables the light.</param>
        public PointLight(in Vector3 position, in Vector3 color, float intensity)
        {
            Position = new Vector4(position, 1.0f);
            ColorAndIntensity = new Vector4(color, intensity);
        }
    }

    /// <summary>The number of shadow cascades (atlas quadrants) the pipeline supports.</summary>
    public const int ShadowCascadeCount = 4;

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
        /// <summary>Sky top color (linear).</summary>
        public Vector4 SkyTopColor;
        /// <summary>Sky bottom / horizon color (linear).</summary>
        public Vector4 SkyBottomColor;
        /// <summary>Point light 0 position (w unused).</summary>
        public Vector4 PointLight0Position;
        /// <summary>Point light 0 color (rgb) and intensity (w).</summary>
        public Vector4 PointLight0Color;
        /// <summary>Point light 1 position (w unused).</summary>
        public Vector4 PointLight1Position;
        /// <summary>Point light 1 color (rgb) and intensity (w).</summary>
        public Vector4 PointLight1Color;
        /// <summary>Point light 2 position (w unused).</summary>
        public Vector4 PointLight2Position;
        /// <summary>Point light 2 color (rgb) and intensity (w).</summary>
        public Vector4 PointLight2Color;
        /// <summary>Point light 3 position (w unused).</summary>
        public Vector4 PointLight3Position;
        /// <summary>Point light 3 color (rgb) and intensity (w).</summary>
        public Vector4 PointLight3Color;
        /// <summary>x=shadowEnabled y=pointLightEnabled z=shadowMapSize w=sunDiscEnabled.</summary>
        public Vector4 Params;
        /// <summary>View-distance end boundary of each cascade; beyond w there is no shadow.</summary>
        public Vector4 CascadeSplits;
        /// <summary>World units per shadow texel of each cascade (for the normal-offset bias).</summary>
        public Vector4 CascadeTexelSizes;
        /// <summary>x=cascadeDebugTint, rest unused.</summary>
        public Vector4 Params2;
        /// <summary>xy=render target size in pixels (filled by the pipeline).</summary>
        public Vector4 ViewportSize;

        /// <summary>
        /// Copy the given point lights into the light slots (up to four lights).
        /// </summary>
        /// <param name="lights">The lights to copy; unused slots keep their previous values.</param>
        public void SetPointLights(ReadOnlySpan<PointLight> lights)
        {
            int count = Math.Min(lights.Length, 4);
            if (count > 0)
            {
                PointLight0Position = lights[0].Position;
                PointLight0Color = lights[0].ColorAndIntensity;
            }
            if (count > 1)
            {
                PointLight1Position = lights[1].Position;
                PointLight1Color = lights[1].ColorAndIntensity;
            }
            if (count > 2)
            {
                PointLight2Position = lights[2].Position;
                PointLight2Color = lights[2].ColorAndIntensity;
            }
            if (count > 3)
            {
                PointLight3Position = lights[3].Position;
                PointLight3Color = lights[3].ColorAndIntensity;
            }
        }
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
    private readonly GraphicsMaterial _gbufferMaterial;
    private readonly GraphicsMaterial _shadowMaterial;
    private readonly GraphicsMaterial? _shadowTangentMaterial;
    private readonly GraphicsMaterial _lightingMaterial;
    private readonly Dictionary<(Texture2D? Texture, bool DoubleSided), GraphicsMaterial> _gbufferMaterialCache = new();
    private readonly Dictionary<(Texture2D? Albedo, Texture2D? Normal, Texture2D? Mr, Texture2D? Emissive, bool DoubleSided), GraphicsMaterial> _gbufferTangentMaterialCache = new();
    private Texture2D? _flatNormalTexture;
    private GraphicsBuffer? _cameraBuffer;

    private readonly GraphicsValueBuffer<DeferredLightingData> _lightingDataBuffer;

    private readonly RenderContext _shadowContext;
    private readonly RenderContext _gbufferContext;
    private readonly RenderContext _lightingContext;

    private Matrix4x4 _sunViewProjection;

    /// <summary>
    /// The G-buffer render texture (albedo / normal / metallic-roughness-ao / depth).
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
    /// <param name="albedoTexture">Optional albedo texture for all G-buffer draws; defaults to a white texture.</param>
    /// <param name="gbufferTangentShader">Optional tangent-space G-buffer shader (GBufferTangent.hlsl) enabling the normal-mapped <see cref="DrawGBuffer(in Mesh, in Matrix4x4, in Vector4, in Vector4, Texture2D?, Texture2D?, Texture2D?, Texture2D?, in Vector3, bool, float)"/> overload.</param>
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
        Texture2D? albedoTexture = null,
        Shader? gbufferTangentShader = null,
        Shader? shadowTangentShader = null)
    {
        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _fullScreenMesh = rendering.MeshFullScreen;
        ShadowMapSize = shadowMapSize;

        // The lighting shader samples depth textures (G-buffer depth and shadow map),
        // so its bind group layouts must declare Depth sample type for those slots,
        // matching the engine's depth read / depth comparison bind group layouts.
        Shader lightingShader = rendering.CreateShader(lightingShaderText, lightingShaderName, null, CreateLightingBindGroupLayouts());

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

        _gbufferMaterial = rendering.CreateMaterial(gbufferShader);
        _gbufferMaterial.DepthStencilState = DepthStencilState.Write;
        _gbufferMaterial.RasterizerState = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.Clockwise);
        _gbufferMaterial.SetTexture("_albedoTexture", albedoTexture ?? rendering.TextureWhite);
        _gbufferShader = gbufferShader;
        _gbufferTangentShader = gbufferTangentShader;

        _shadowMaterial = rendering.CreateMaterial(shadowShader);
        _shadowMaterial.DepthStencilState = DepthStencilState.Write;
        _shadowMaterial.RasterizerState = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.Clockwise);

        if (shadowTangentShader != null)
        {
            _shadowTangentMaterial = rendering.CreateMaterial(shadowTangentShader);
            _shadowTangentMaterial.DepthStencilState = DepthStencilState.Write;
            _shadowTangentMaterial.RasterizerState = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.Clockwise);
        }

        // IMPORTANT: DepthStencilState.None means depthCompare=Never — with a depth
        // attachment present (the engine's HDR main target), every fragment would be
        // rejected. Default (Always) disables the depth test without rejecting pixels.
        _lightingMaterial = rendering.CreateMaterial(lightingShader);
        _lightingMaterial.DepthStencilState = DepthStencilState.Default;
        _lightingMaterial.RasterizerState = RasterizerState.CullNone;

        _lightingDataBuffer = rendering.CreateGraphicsValueBuffer<DeferredLightingData>("pbr_lighting_data");
        _lightingMaterial.SetBuffer(ShaderResourceId.Data, _lightingDataBuffer);
        RebindLightingTargets();

        _shadowContext = rendering.CreateRenderContext("pbr_shadow_pass");
        _gbufferContext = rendering.CreateRenderContext("pbr_gbuffer_pass");
        _lightingContext = rendering.CreateRenderContext("pbr_lighting_pass");
    }

    /// <summary>
    /// Bind the camera used by the G-buffer pass. The caller must keep the camera
    /// updated (e.g. <c>UpdateMatrixToGPU</c>) before drawing each frame.
    /// </summary>
    /// <param name="cameraBuffer">The camera buffer (a <c>CameraPerspectiveBuffer</c>).</param>
    public void SetCamera(GraphicsBuffer cameraBuffer)
    {
        _cameraBuffer = cameraBuffer;
        _gbufferMaterial.SetBuffer(ShaderResourceId.Camera, cameraBuffer);
        foreach (GraphicsMaterial material in _gbufferMaterialCache.Values)
        {
            material.SetBuffer(ShaderResourceId.Camera, cameraBuffer);
        }
        foreach (GraphicsMaterial material in _gbufferTangentMaterialCache.Values)
        {
            material.SetBuffer(ShaderResourceId.Camera, cameraBuffer);
        }
    }

    /// <summary>
    /// Recreate the G-buffer at a new resolution. Call when the view resizes.
    /// </summary>
    /// <param name="width">The new G-buffer width in pixels.</param>
    /// <param name="height">The new G-buffer height in pixels.</param>
    public void Resize(uint width, uint height)
    {
        _gbufferRT.Dispose();
        _gbufferRT = _rendering.CreateRenderTexture(_gbufferLayout, width, height, "pbr_gbuffer");
        RebindLightingTargets();
    }

    /// <summary>
    /// Begin the shadow map pass for one cascade. All <see cref="DrawShadow"/> calls must happen
    /// between this and <see cref="EndShadowPass"/>. Cascades render into their own quadrant of
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
        _sunViewProjection = sunViewProjection * quadrant;
        _shadowContext.Begin(_shadowRT.FrameBuffer, clearDepth: cascadeIndex == 0 ? 1.0f : null);
        _shadowContext.SetScissorRect(
            (uint)(cascadeIndex % 2) * ShadowMapSize,
            (uint)(cascadeIndex / 2) * ShadowMapSize,
            ShadowMapSize,
            ShadowMapSize);
    }

    /// <summary>
    /// Draw a mesh into the shadow map. Must be called inside the shadow pass.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="model">The world transform of the mesh.</param>
    public void DrawShadow(in Mesh mesh, in Matrix4x4 model)
    {
        _shadowContext.DrawWithConstant(mesh, _shadowMaterial,
            new ShadowDrawConstants { LightViewProjection = model * _sunViewProjection });
    }

    /// <summary>
    /// Draw a tangent-bearing mesh (<see cref="VertexPositionNormalTextureTangent"/>) into
    /// the shadow map. Must be called inside the shadow pass. Requires the tangent shadow
    /// shader (<c>shadowTangentShader</c> constructor argument): a mesh's vertex layout must
    /// match its shader exactly, so tangent meshes cannot go through <see cref="DrawShadow"/>.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="model">The world transform of the mesh.</param>
    /// <exception cref="InvalidOperationException">The pipeline was created without a tangent shadow shader.</exception>
    public void DrawShadowTangent(in Mesh mesh, in Matrix4x4 model)
    {
        if (_shadowTangentMaterial == null)
        {
            throw new InvalidOperationException(
                "DrawShadowTangent requires the pipeline to be created with a tangent shadow shader.");
        }
        _shadowContext.DrawWithConstant(mesh, _shadowTangentMaterial,
            new ShadowDrawConstants { LightViewProjection = model * _sunViewProjection });
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
    /// Begin the G-buffer pass. All <see cref="DrawGBuffer"/> calls must happen
    /// between this and <see cref="EndGBufferPass"/>.
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
    /// Draw a mesh into the G-buffer. Must be called inside the G-buffer pass.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="model">The world transform of the mesh.</param>
    /// <param name="baseColor">The linear base color.</param>
    /// <param name="metallicRoughnessAO">x=metallic y=roughness z=ambient occlusion.</param>
    public void DrawGBuffer(in Mesh mesh, in Matrix4x4 model, in Vector4 baseColor, in Vector4 metallicRoughnessAO)
    {
        _gbufferContext.DrawWithConstant(mesh, _gbufferMaterial,
            new PBRDrawConstants
            {
                Model = model,
                BaseColor = baseColor,
                MetallicRoughnessAO = metallicRoughnessAO,
            });
    }

    /// <summary>
    /// Draw a mesh into the G-buffer with a per-material albedo texture. Must be called
    /// inside the G-buffer pass. Materials are cached per (texture, doubleSided) pair.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="model">The world transform of the mesh.</param>
    /// <param name="baseColor">The linear base color, multiplied with the albedo texture.</param>
    /// <param name="metallicRoughnessAO">x=metallic y=roughness z=ambient occlusion.</param>
    /// <param name="albedoTexture">The albedo texture; null binds the shared white texture.</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="alphaCutoff">Alpha test threshold; 0 disables alpha testing.</param>
    public void DrawGBuffer(in Mesh mesh, in Matrix4x4 model, in Vector4 baseColor, in Vector4 metallicRoughnessAO,
        Texture2D? albedoTexture, bool doubleSided = false, float alphaCutoff = 0.0f)
    {
        GraphicsMaterial material = GetOrCreateGBufferMaterial(albedoTexture, doubleSided);
        _gbufferContext.DrawWithConstant(mesh, material,
            new PBRDrawConstants
            {
                Model = model,
                BaseColor = baseColor,
                MetallicRoughnessAO = metallicRoughnessAO,
                Params = new Vector4(alphaCutoff, 0.0f, 0.0f, 0.0f),
            });
    }

    /// <summary>
    /// Draw a mesh into the G-buffer with per-material albedo, normal, metallic-roughness
    /// and emissive textures. Must be called inside the G-buffer pass. Requires the tangent
    /// G-buffer shader (<c>gbufferTangentShader</c> constructor argument) and tangent-bearing
    /// meshes (<see cref="VertexPositionNormalTextureTangent"/>). Materials are cached per
    /// (albedo, normal, metallic-roughness, emissive, doubleSided) tuple.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="model">The world transform of the mesh.</param>
    /// <param name="baseColor">The linear base color, multiplied with the albedo texture.</param>
    /// <param name="metallicRoughnessAO">x=metallic y=roughness z=ambient occlusion; metallic and roughness multiply with the texture.</param>
    /// <param name="albedoTexture">The albedo texture; null binds the shared white texture.</param>
    /// <param name="normalTexture">The tangent-space normal map; null binds a flat normal texture.</param>
    /// <param name="metallicRoughnessTexture">The metallic-roughness texture (roughness in G, metallic in B); null binds the shared white texture.</param>
    /// <param name="emissiveTexture">The emissive texture; null binds the shared black texture.</param>
    /// <param name="emissiveFactor">The linear emissive color, multiplied with the emissive texture.</param>
    /// <param name="doubleSided">Whether to disable back-face culling for this material.</param>
    /// <param name="alphaCutoff">Alpha test threshold; 0 disables alpha testing.</param>
    /// <exception cref="InvalidOperationException">The pipeline was created without a tangent G-buffer shader.</exception>
    public void DrawGBuffer(in Mesh mesh, in Matrix4x4 model, in Vector4 baseColor, in Vector4 metallicRoughnessAO,
        Texture2D? albedoTexture, Texture2D? normalTexture, Texture2D? metallicRoughnessTexture,
        Texture2D? emissiveTexture, in Vector3 emissiveFactor, bool doubleSided = false, float alphaCutoff = 0.0f)
    {
        if (_gbufferTangentShader == null)
        {
            throw new InvalidOperationException(
                "The normal-mapped DrawGBuffer overload requires the pipeline to be created with a tangent G-buffer shader.");
        }
        GraphicsMaterial material = GetOrCreateGBufferTangentMaterial(albedoTexture, normalTexture, metallicRoughnessTexture, emissiveTexture, doubleSided);
        _gbufferContext.DrawWithConstant(mesh, material,
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
    /// End the G-buffer pass and submit its commands.
    /// </summary>
    public void EndGBufferPass()
    {
        _gbufferContext.End();
    }

    private GraphicsMaterial GetOrCreateGBufferMaterial(Texture2D? albedoTexture, bool doubleSided)
    {
        if (_gbufferMaterialCache.TryGetValue((albedoTexture, doubleSided), out GraphicsMaterial? cached))
        {
            return cached;
        }

        var material = _rendering.CreateMaterial(_gbufferShader);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        if (_cameraBuffer != null)
        {
            material.SetBuffer(ShaderResourceId.Camera, _cameraBuffer);
        }
        _gbufferMaterialCache[(albedoTexture, doubleSided)] = material;
        return material;
    }

    private GraphicsMaterial GetOrCreateGBufferTangentMaterial(
        Texture2D? albedoTexture, Texture2D? normalTexture, Texture2D? metallicRoughnessTexture,
        Texture2D? emissiveTexture, bool doubleSided)
    {
        if (_gbufferTangentMaterialCache.TryGetValue((albedoTexture, normalTexture, metallicRoughnessTexture, emissiveTexture, doubleSided), out GraphicsMaterial? cached))
        {
            return cached;
        }

        var material = _rendering.CreateMaterial(_gbufferTangentShader!);
        material.DepthStencilState = DepthStencilState.Write;
        material.RasterizerState = new RasterizerState(FillMode.Solid,
            doubleSided ? CullMode.None : CullMode.Back, FrontFace.Clockwise);
        material.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        material.SetTexture("_normalTexture", normalTexture ?? GetOrCreateFlatNormalTexture());
        material.SetTexture("_mrTexture", metallicRoughnessTexture ?? _rendering.TextureWhite);
        material.SetTexture("_emissiveTexture", emissiveTexture ?? _rendering.TextureBlack);
        if (_cameraBuffer != null)
        {
            material.SetBuffer(ShaderResourceId.Camera, _cameraBuffer);
        }
        _gbufferTangentMaterialCache[(albedoTexture, normalTexture, metallicRoughnessTexture, emissiveTexture, doubleSided)] = material;
        return material;
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

    /// <summary>
    /// Bind group layouts for the deferred lighting shader: uniform buffer (set 0),
    /// four filterable texture+sampler pairs (sets 1-3 and 6), the G-buffer depth texture
    /// (set 4) and the shadow map depth texture with a comparison sampler (set 5).
    /// Must stay in sync with DeferredLighting.hlsl.
    /// </summary>
    /// <returns>The custom bind group layouts.</returns>
    public static IReadOnlyList<BindGroupLayout> CreateLightingBindGroupLayouts()
    {
        BindGroupLayout CreateTextureSamplerGroup(uint group) => new BindGroupLayout
        {
            Group = group,
            Bindings =
            [
                new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D)),
                },
                new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(1, ShaderStage.Standard, BindingType.Sampler),
                },
            ],
        };

        BindGroupLayout CreateDepthReadGroup(uint group) => new BindGroupLayout
        {
            Group = group,
            Bindings =
            [
                new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, TextureBindingInfo.Depth2D),
                },
            ],
        };

        BindGroupLayout CreateDepthComparisonGroup(uint group) => new BindGroupLayout
        {
            Group = group,
            Bindings =
            [
                new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, TextureBindingInfo.Depth2D),
                },
                new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(1, ShaderStage.Standard, BindingType.SamplerComparison),
                },
            ],
        };

        return
        [
            new BindGroupLayout
            {
                Group = 0,
                Bindings =
                [
                    new BindGroupEntryInfo
                    {
                        Entry = new BindGroupEntry(0, ShaderStage.Standard, BindingType.UniformBuffer),
                    },
                ],
            },
            CreateTextureSamplerGroup(1),
            CreateTextureSamplerGroup(2),
            CreateTextureSamplerGroup(3),
            CreateDepthReadGroup(4),
            CreateDepthComparisonGroup(5),
            CreateTextureSamplerGroup(6),
        ];
    }

    private void RebindLightingTargets()
    {
        _lightingMaterial.SetRenderTexture("_albedo", _gbufferRT, 0);
        _lightingMaterial.SetRenderTexture("_normal", _gbufferRT, 1);
        _lightingMaterial.SetRenderTexture("_mrAO", _gbufferRT, 2);
        _lightingMaterial.SetRenderTexture("_emissive", _gbufferRT, 3);
        _lightingMaterial.SetRenderTextureDepth("_gbufferDepth", _gbufferRT);
        _lightingMaterial.SetRenderTextureDepth("_shadowMap", _shadowRT);
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
            _lightingMaterial.Dispose();
            foreach (GraphicsMaterial material in _gbufferTangentMaterialCache.Values)
            {
                material.Dispose();
            }
            _gbufferTangentMaterialCache.Clear();
            foreach (GraphicsMaterial material in _gbufferMaterialCache.Values)
            {
                material.Dispose();
            }
            _gbufferMaterialCache.Clear();
            _flatNormalTexture?.Dispose();
            _gbufferMaterial.Dispose();
            _shadowTangentMaterial?.Dispose();
            _shadowMaterial.Dispose();
            _gbufferRT.Dispose();
            _shadowRT.Dispose();
            _gbufferLayout.Dispose();
            _shadowLayout.Dispose();
        }
    }
}
