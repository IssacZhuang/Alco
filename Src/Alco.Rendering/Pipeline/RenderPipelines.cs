using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Preset factories composing common render pipelines from the public building
/// blocks (<see cref="RenderGraph"/>, the <c>RGNode_*</c> nodes,
/// <see cref="PBRSceneEnvironment"/> and <see cref="RenderPipeline"/>).
/// <br/>A preset is not a special pipeline type — it is a one-time assembly of the
/// same nodes and resources a user can compose by hand. Everything a preset does is
/// public API: study a factory's body as the reference composition, then modify the
/// result through <see cref="PBRDeferredPreset.Graph"/> (insert custom nodes between
/// any two stages, remove a stock stage and replace it with a custom implementation),
/// or re-implement the whole frame without the factory.
/// </summary>
public static class RenderPipelines
{
    // GPU timestamp query-slot bases of the preset's instrumented stages
    // (2 slots per stage: begin + end).
    private const int GBufferQueryBase = 2;
    private const int LightingQueryBase = 4;
    private const int VolumetricLightQueryBase = 6;
    private const int TimestampSlotCount = 8;

    /// <summary>
    /// Creates the deferred PBR pipeline preset: shadow cascades → G-buffer →
    /// <see cref="PBRDeferredPreset.AfterGBuffer"/> hook → effect plugins → deferred
    /// lighting into the scene color target → volumetric light (optional) → forward
    /// content nodes (transparency, hardware depth-tested against the scene depth) →
    /// post-process chain → final blit into the destination.
    /// <br/>The composition owns three transient targets — a G-buffer (albedo / normal /
    /// metallic-roughness-ao / emissive + depth), a depth-only shadow map holding
    /// <see cref="PBRSceneEnvironment.ShadowCascadeCount"/> cascades in a 2x2 atlas and
    /// the scene color target (HDR color + depth) — plus the deferred lighting material
    /// and the shared scene environment. Effect plugins (HBAO, VoxelGI, SSR) are not
    /// part of the preset; attach them afterwards through their public
    /// <c>Attach</c> methods, wiring their outputs to
    /// <see cref="RGNode_DeferredLighting.AoInput"/> /
    /// <see cref="RGNode_DeferredLighting.GiDiffuseInput"/>.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="lightingShader">The deferred lighting shader (DeferredLighting.hlsl). Caller-owned, like every shader the composition takes. It must declare its depth textures with the <c>DEFINE_TEX2D_DEPTH*</c> macros so the reflection carries the depth sample type and comparison sampler.</param>
    /// <param name="blitShader">The shader the final blit uses for plain copies.</param>
    /// <param name="shadowMapSize">The per-cascade shadow map resolution in texels; the shadow map is a 2x2 atlas of <see cref="PBRSceneEnvironment.ShadowCascadeCount"/> cascades, so the actual texture is twice this size along each axis.</param>
    /// <param name="width">The initial G-buffer width in pixels.</param>
    /// <param name="height">The initial G-buffer height in pixels.</param>
    /// <param name="volumetricLightShader">Optional volumetric light (god rays) shader.
    /// When non-null the composition creates an additive blend pass that runs after
    /// deferred lighting. Pass null to skip volumetric light entirely.</param>
    public static PBRDeferredPreset CreatePBRDeferred(
        RenderingSystem rendering,
        Shader lightingShader,
        Shader blitShader,
        uint shadowMapSize = 2048,
        uint width = 1280,
        uint height = 720,
        Shader? volumetricLightShader = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        GPUDevice device = rendering.GraphicsDevice;

        var environment = new PBRSceneEnvironment(rendering, shadowMapSize);

        var gbufferLayout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
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

        var shadowLayout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_shadow_pass"));

        // Scene color: HDR color + Depth32Float shared from the G-buffer (the depth
        // formats must match for the graph's depth sharing).
        var forwardLayout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(rendering.PreferredHDRFormat)],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_forward_pass"));

        // The render graph and its transient targets. The 2x2 cascade atlas uses an
        // absolute size; the G-buffer and scene color follow the graph viewport.
        var graph = new RenderGraph(rendering, width, height, "pbr_deferred");
        var profiler = new RenderProfiler();
        graph.Profiler = profiler;
        RenderGraphTexture shadowMapResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            shadowLayout, shadowMapSize * 2, shadowMapSize * 2, name: "pbr_shadow_map"));
        RenderGraphTexture gbufferResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            gbufferLayout, name: "pbr_gbuffer"));
        RenderGraphTexture sceneColorResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            forwardLayout, name: "pbr_scene_color"));

        // IMPORTANT: DepthStencilState.None means depthCompare=Never — with a depth
        // attachment present (the engine's HDR main target), every fragment would be
        // rejected. Default (Always) disables the depth test without rejecting pixels.
        var lightingMaterial = rendering.CreateMaterial(lightingShader);
        lightingMaterial.DepthStencilState = DepthStencilState.Default;
        lightingMaterial.RasterizerState = RasterizerState.CullNone;
        lightingMaterial.SetBuffer(ShaderResourceId.Data, environment.LightingDataBuffer);
        lightingMaterial.SetBuffer(ShaderResourceId.PointLights, environment.PointLightBuffer);
        BindLightingTargets(rendering, lightingMaterial, gbufferResource, shadowMapResource);

        // Register pipeline stage counters once; the returned handles are used for
        // zero-allocation PushValue calls on the per-frame hot path.
        RenderProfileCounterId shadowCounter = profiler.RegisterCounter("Pipeline", "Shadow");
        RenderProfileCounterId gbufferCounter = profiler.RegisterCounter("Pipeline", "GBuffer");
        RenderProfileCounterId lightingCounter = profiler.RegisterCounter("Pipeline", "Lighting");
        RenderProfileCounterId volumetricLightCounter = profiler.RegisterCounter("Pipeline", "VolumetricLight");

        // GPU timestamp ring buffer for per-stage GPU timing, when the device
        // supports it. The frame-start readback and the sample end run as graph
        // callback nodes (see below).
        GpuTimestampSampler? gpuTimestamps = device.TimestampQuerySupported
            ? new GpuTimestampSampler(device, TimestampSlotCount, "pbr_pipeline")
            : null;

        // The composed nodes, in execution order.
        var shadowNode = new RGNode_ShadowPass(rendering, shadowMapResource, environment.ShadowDataBufferTyped,
            environment.CascadeViewProjections, shadowMapSize, "pbr_shadow_pass")
        {
            Instrumentation = new PassInstrumentation { Profiler = profiler, CpuCounter = shadowCounter },
        };
        var gbufferNode = new RGNode_GeometryPass(rendering, gbufferResource,
            [
                new ClearColorData(0, System.Numerics.Vector4.Zero),
                new ClearColorData(1, new System.Numerics.Vector4(0.5f, 0.5f, 1.0f, 1.0f)),
                new ClearColorData(2, System.Numerics.Vector4.Zero),
                new ClearColorData(3, System.Numerics.Vector4.Zero),
            ],
            clearDepth: 1.0f, name: "pbr_gbuffer_pass")
        {
            Instrumentation = new PassInstrumentation
            {
                Profiler = profiler, CpuCounter = gbufferCounter,
                GpuTimestamps = gpuTimestamps, GpuQueryBase = GBufferQueryBase,
            },
        };
        var afterGBufferNode = new RGNode_Callback();
        var lightingNode = new RGNode_DeferredLighting(rendering, graph, lightingMaterial,
            gbufferResource, sceneColorResource, "pbr_lighting_pass")
        {
            ShadowMap = shadowMapResource,
            PrepareData = node =>
            {
                // Uniform uploads must precede the pass recording (see
                // RGNode_DeferredLighting.PrepareData).
                var camera = environment.Camera
                    ?? throw new InvalidOperationException("RenderLighting requires a camera (set Environment.Camera first).");
                System.Numerics.Matrix4x4.Invert(camera.Data.ViewProjectionMatrix, out System.Numerics.Matrix4x4 invViewProjection);
                environment.AssembleLightingData(invViewProjection, gbufferResource.Texture, node.GiDiffuseInput != null);
                environment.UploadLightingData();
            },
            Instrumentation = new PassInstrumentation
            {
                Profiler = profiler, CpuCounter = lightingCounter,
                GpuTimestamps = gpuTimestamps, GpuQueryBase = LightingQueryBase,
            },
        };
        var chain = new RenderChain();
        var blitNode = new RGNode_Blit(rendering, graph, chain, blitShader);

        // Volumetric light pass (optional). Created eagerly so no runtime
        // recompilation is needed; controlled at runtime via
        // PBRSceneEnvironment.VolumetricLightEnabled.
        GraphicsMaterial? volumetricLightMaterial = null;
        RGNode_FullscreenOverlay? volumetricLightNode = null;
        if (volumetricLightShader != null)
        {
            volumetricLightMaterial = rendering.CreateMaterial(volumetricLightShader);
            volumetricLightMaterial.DepthStencilState = DepthStencilState.Default;
            volumetricLightMaterial.RasterizerState = RasterizerState.CullNone;
            volumetricLightMaterial.BlendState = BlendState.Additive;
            volumetricLightMaterial.SetBuffer(ShaderResourceId.Data, environment.LightingDataBuffer);
            volumetricLightMaterial.SetBuffer(ShaderResourceId.PointLights, environment.PointLightBuffer);
            volumetricLightMaterial.SetRenderTextureDepth("_gbufferDepth", gbufferResource.Texture);
            volumetricLightMaterial.SetRenderTextureDepth("_shadowMap", shadowMapResource.Texture);
            volumetricLightNode = new RGNode_FullscreenOverlay(rendering, graph, chain,
                volumetricLightMaterial, "pbr_volumetric_light_pass")
            {
                IsEnabled = environment.VolumetricLightEnabled,
                Instrumentation = new PassInstrumentation
                {
                    Profiler = profiler, CpuCounter = volumetricLightCounter,
                    GpuTimestamps = gpuTimestamps, GpuQueryBase = VolumetricLightQueryBase,
                },
            };
        }

        // Environment → node synchronization (the environment itself knows no nodes).
        environment.ShadowEnabledChanged += enabled =>
        {
            shadowNode.IsEnabled = enabled;
            lightingNode.ShadowMapEnabled = enabled;
        };
        environment.VolumetricLightEnabledChanged += enabled =>
        {
            if (volumetricLightNode != null)
            {
                volumetricLightNode.IsEnabled = enabled;
            }
        };

        // Register the nodes in execution order.
        if (gpuTimestamps != null)
        {
            // Frame start: read back the previous GPU timestamp sample (guaranteed
            // GPU-complete via the sampler's throttled interval) and push the values
            // to the profiler. Gating on ShouldRecord first flips the frame's sample
            // flag, which both enables the readback and lets the instrumented nodes
            // record their timestamps this frame.
            GpuTimestampSampler sampler = gpuTimestamps;
            graph.Use(new RGNode_Callback
            {
                Callback = _ =>
                {
                    if (!sampler.ShouldRecord)
                    {
                        return;
                    }
                    ulong[]? timestamps = sampler.TryReadback();
                    if (timestamps == null)
                    {
                        return;
                    }
                    profiler.PushValue(gbufferCounter,
                        sampler.DeltaMilliseconds(timestamps, GBufferQueryBase, GBufferQueryBase + 1));
                    profiler.PushValue(lightingCounter,
                        sampler.DeltaMilliseconds(timestamps, LightingQueryBase, LightingQueryBase + 1));
                    profiler.PushValue(volumetricLightCounter,
                        sampler.DeltaMilliseconds(timestamps, VolumetricLightQueryBase, VolumetricLightQueryBase + 1));
                },
            });
        }
        graph.Use(shadowNode);
        graph.Use(gbufferNode);
        graph.Use(afterGBufferNode);
        graph.Use(lightingNode);
        if (volumetricLightNode != null)
        {
            graph.Use(volumetricLightNode);
        }
        if (gpuTimestamps != null)
        {
            // After all instrumented stages have recorded: finalize the sample.
            GpuTimestampSampler sampler = gpuTimestamps;
            graph.Use(new RGNode_Callback { Callback = _ => sampler.EndSample() });
        }
        graph.Use(blitNode);

        var pipeline = new RenderPipeline(rendering, graph, sceneColorResource, chain, blitNode);
        return new PBRDeferredPreset(
            pipeline, environment, profiler,
            gbufferResource, shadowMapResource,
            shadowNode, gbufferNode, afterGBufferNode, lightingNode, volumetricLightNode,
            gbufferLayout, shadowLayout, forwardLayout,
            lightingMaterial, volumetricLightMaterial, gpuTimestamps);
    }

    /// <summary>Binds the G-buffer, shadow map and neutral plugin fallbacks to the
    /// lighting material. Plugin output textures default to white/black until a
    /// plugin sets them.</summary>
    private static void BindLightingTargets(RenderingSystem rendering, GraphicsMaterial lightingMaterial,
        RenderGraphTexture gbufferResource, RenderGraphTexture shadowMapResource)
    {
        RenderTexture gbuffer = gbufferResource.Texture;
        lightingMaterial.SetRenderTexture("_albedo",   gbuffer, 0);
        lightingMaterial.SetRenderTexture("_normal",   gbuffer, 1);
        lightingMaterial.SetRenderTexture("_mrAO",     gbuffer, 2);
        lightingMaterial.SetRenderTexture("_emissive", gbuffer, 3);
        lightingMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
        lightingMaterial.SetRenderTextureDepth("_shadowMap", shadowMapResource.Texture);
        // Plugin output textures default to white/black until a plugin sets them.
        lightingMaterial.SetTexture("_aoTexture", rendering.TextureWhite);
        lightingMaterial.SetTexture("_giDiffuse", rendering.TextureBlack);
        lightingMaterial.SetTexture("_giSpecular", rendering.TextureBlack);
    }
}
