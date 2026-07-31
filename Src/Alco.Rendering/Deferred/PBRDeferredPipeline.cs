using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A deferred PBR rendering pipeline built on the engine's WebGPU resources.
/// <br/>Owns a G-buffer (albedo / normal / metallic-roughness-ao + depth), a depth-only
/// shadow map, and three render contexts (shadow pass, G-buffer pass, lighting pass).
/// <br/>The caller drives the frame explicitly:
/// <c>BeginShadowPass → DrawShadow×N → EndShadowPass</c>, then
/// <c>BeginGBufferPass → DrawGBuffer×N → EndGBufferPass</c>, then
/// <c>RenderLighting(target, ref data)</c> which resolves lighting, sky and shadows
/// into the target frame buffer (typically the engine's HDR main target).
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
        /// <summary>Linear base color (rgb), alpha is unused.</summary>
        public Vector4 BaseColor;
        /// <summary>x=metallic y=roughness z=ambient occlusion, w is unused.</summary>
        public Vector4 MetallicRoughnessAO;

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
        }
    }

    /// <summary>
    /// Push constant payload for a shadow map draw. Layout must match the
    /// <c>Constants</c> struct in ShadowDepth.hlsl exactly.
    /// </summary>
    public struct ShadowDrawConstants
    {
        /// <summary>Combined light view-projection * model matrix.</summary>
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

    /// <summary>
    /// Per-frame data uploaded to the lighting pass. Layout must match the
    /// <c>_data</c> cbuffer in DeferredLighting.hlsl exactly.
    /// </summary>
    public struct DeferredLightingData
    {
        /// <summary>Inverse of the camera view-projection matrix.</summary>
        public Matrix4x4 InvViewProjection;
        /// <summary>Sun light view-projection matrix (light space).</summary>
        public Matrix4x4 SunViewProjection;
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

    private readonly GraphicsMaterial _gbufferMaterial;
    private readonly GraphicsMaterial _shadowMaterial;
    private readonly GraphicsMaterial _lightingMaterial;

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
    /// The depth-only shadow map render texture.
    /// </summary>
    public RenderTexture ShadowMap => _shadowRT;

    /// <summary>
    /// The width of the shadow map in texels.
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
    /// <param name="shadowMapSize">The shadow map resolution in texels.</param>
    /// <param name="width">The initial G-buffer width in pixels.</param>
    /// <param name="height">The initial G-buffer height in pixels.</param>
    /// <param name="albedoTexture">Optional albedo texture for all G-buffer draws; defaults to a white texture.</param>
    public PBRDeferredPipeline(
        RenderingSystem rendering,
        Shader gbufferShader,
        Shader shadowShader,
        string lightingShaderText,
        string lightingShaderName,
        uint shadowMapSize = 2048,
        uint width = 1280,
        uint height = 720,
        Texture2D? albedoTexture = null)
    {
        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _fullScreenMesh = rendering.MeshFullScreen;
        ShadowMapSize = shadowMapSize;

        // The lighting shader samples depth textures (G-buffer depth and shadow map),
        // so its bind group layouts must declare UnfilterableFloat for those slots,
        // matching the engine's depth-read bind group layout.
        Shader lightingShader = rendering.CreateShader(lightingShaderText, lightingShaderName, null, CreateLightingBindGroupLayouts());

        _gbufferLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [
                // RGBA8Unorm + manual sRGB encode/decode: wgpu forbids STORAGE_BINDING
                // usage on sRGB textures, and engine framebuffer textures always carry it.
                new ColorAttachment(PixelFormat.RGBA8Unorm),
                new ColorAttachment(PixelFormat.RGBA16Float),
                new ColorAttachment(PixelFormat.RGBA8Unorm),
            ],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_gbuffer_pass"));

        _shadowLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_shadow_pass"));

        _gbufferRT = rendering.CreateRenderTexture(_gbufferLayout, width, height, "pbr_gbuffer");
        _shadowRT = rendering.CreateRenderTexture(_shadowLayout, shadowMapSize, shadowMapSize, "pbr_shadow_map");

        _gbufferMaterial = rendering.CreateMaterial(gbufferShader);
        _gbufferMaterial.DepthStencilState = DepthStencilState.Write;
        _gbufferMaterial.RasterizerState = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.Clockwise);
        _gbufferMaterial.SetTexture("_albedoTexture", albedoTexture ?? rendering.TextureWhite);

        _shadowMaterial = rendering.CreateMaterial(shadowShader);
        _shadowMaterial.DepthStencilState = DepthStencilState.Write;
        _shadowMaterial.RasterizerState = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.Clockwise);

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
        _gbufferMaterial.SetBuffer(ShaderResourceId.Camera, cameraBuffer);
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
    /// Begin the shadow map pass. All <see cref="DrawShadow"/> calls must happen
    /// between this and <see cref="EndShadowPass"/>.
    /// </summary>
    /// <param name="sunViewProjection">The light view-projection matrix (orthographic for the sun).</param>
    public void BeginShadowPass(in Matrix4x4 sunViewProjection)
    {
        _sunViewProjection = sunViewProjection;
        _shadowContext.Begin(_shadowRT.FrameBuffer, clearDepth: 1.0f);
    }

    /// <summary>
    /// Draw a mesh into the shadow map. Must be called inside the shadow pass.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="model">The world transform of the mesh.</param>
    public void DrawShadow(in Mesh mesh, in Matrix4x4 model)
    {
        _shadowContext.DrawWithConstant(mesh, _shadowMaterial,
            new ShadowDrawConstants { LightViewProjection = _sunViewProjection * model });
    }

    /// <summary>
    /// End the shadow map pass and submit its commands.
    /// </summary>
    public void EndShadowPass()
    {
        _shadowContext.End();
    }

    /// <summary>
    /// Begin the G-buffer pass. All <see cref="DrawGBuffer"/> calls must happen
    /// between this and <see cref="EndGBufferPass"/>.
    /// </summary>
    public void BeginGBufferPass()
    {
        ReadOnlySpan<ClearColorData> clearColors = stackalloc ClearColorData[3]
        {
            new(0, Vector4.Zero),
            new(1, new Vector4(0.5f, 0.5f, 1.0f, 1.0f)),
            new(2, Vector4.Zero),
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

    /// <summary>
    /// Bind group layouts for the deferred lighting shader: uniform buffer (set 0),
    /// three filterable texture+sampler pairs (sets 1-3) and two unfilterable depth
    /// textures (sets 4-5). Must stay in sync with DeferredLighting.hlsl.
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
                    Entry = new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D, TextureSampleType.UnfilterableFloat)),
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
            CreateDepthReadGroup(5),
        ];
    }

    private void RebindLightingTargets()
    {
        _lightingMaterial.SetRenderTexture("_albedo", _gbufferRT, 0);
        _lightingMaterial.SetRenderTexture("_normal", _gbufferRT, 1);
        _lightingMaterial.SetRenderTexture("_mrAO", _gbufferRT, 2);
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
            _gbufferMaterial.Dispose();
            _shadowMaterial.Dispose();
            _gbufferRT.Dispose();
            _shadowRT.Dispose();
            _gbufferLayout.Dispose();
            _shadowLayout.Dispose();
        }
    }
}
