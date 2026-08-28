using Alco.Graphics;

using Alco.Rendering;

namespace Alco.World3D;

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
    private const int ShadowQueryBase = 0;
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
    /// <param name="lightingShader">The deferred lighting shader (DeferredLighting.slang). Caller-owned, like every shader the composition takes. Scene and shadow depth are bound as native Slang depth textures.</param>
    /// <param name="lightingDataLibrary">The library owning the [<c>SceneEnvironmentParams</c>]-marked
    /// uniform block the shared lighting buffer mirrors (the PBR common module of the lighting stack).</param>
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
        ShaderLibrary lightingDataLibrary,
        Shader blitShader,
        uint shadowMapSize = 2048,
        uint width = 1280,
        uint height = 720,
        Shader? volumetricLightShader = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        GPUDevice device = rendering.GraphicsDevice;

        var environment = new PBRSceneEnvironment(rendering, lightingDataLibrary, shadowMapSize);

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
            // Reversed infinite camera depth (near = 1, far at infinity = 0):
            // cleared to 0, GreaterEqual depth tests, Depth32Float for uniform
            // relative precision (see CameraDataPerspective.ReverseInfiniteDepth).
            new DepthAttachment(PixelFormat.Depth32Float, clearDepth: 0.0f),
            "pbr_gbuffer_pass"));

        var shadowLayout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_shadow_pass"));

        // Scene color: HDR color + Depth32Float shared from the G-buffer (the depth
        // formats must match for the graph's depth sharing). The depth is attached
        // read-only: the deferred lighting / volumetric / SSR composite passes sample
        // the G-buffer depth in the same pass, and the forward pass only depth-tests
        // against it (nothing ever writes the shared depth outside the G-buffer pass).
        var forwardLayout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(rendering.PreferredHDRFormat)],
            new DepthAttachment(PixelFormat.Depth32Float) { ReadOnly = true },
            "pbr_forward_pass"));

        // The render graph and its transient targets. The 2x2 cascade atlas uses an
        // absolute size; the G-buffer and scene color follow the graph viewport. The
        // scene color shares the G-buffer's depth attachment, so the depth filled by
        // the geometry pass is available to the lighting/forward passes with no copy.
        var graph = new RenderGraph(rendering, width, height, "pbr_deferred");
        var profiler = new RenderProfiler();
        graph.Profiler = profiler;
        RenderGraphTexture shadowMapResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            shadowLayout, shadowMapSize * 2, shadowMapSize * 2, name: "pbr_shadow_map"));
        RenderGraphTexture gbufferResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            gbufferLayout, name: "pbr_gbuffer"));
        RenderGraphTexture sceneColorResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            forwardLayout, depthSource: gbufferResource, name: "pbr_scene_color"));

        // IMPORTANT: DepthStencilState.None means depthCompare=Never — with a depth
        // attachment present (the engine's HDR main target), every fragment would be
        // rejected. Default (Always) disables the depth test without rejecting pixels.
        // The material starts pinned to the Off (normal rendering) specialization of
        // the lighting shader's MainPS<let DebugView> axis; the lighting node caches
        // one material per view and swaps on PBRSceneEnvironment.LightingDebugView.
        var lightingMaterial = rendering.CreateGraphicsMaterial(lightingShader, "deferred_lighting",
            (int)LightingDebugView.Off);
        lightingMaterial.DepthStencilState = DepthStencilState.Default;
        lightingMaterial.RasterizerState = RasterizerState.CullNone;
        lightingMaterial.SetBuffer(ShaderResourceId.Data, environment.LightingDataBuffer);
        lightingMaterial.SetBuffer(ShaderResourceId.PointLights, environment.PointLightBuffer);
        BindLightingTargets(rendering, lightingMaterial, gbufferResource, shadowMapResource);

        // GPU timestamp ring buffer for per-stage GPU timing, when the device
        // supports it. The frame-start readback and the sample end run as graph
        // callback nodes (see below). Padded-pair layout: every stage resolves
        // its own slot pair right after it is written, so disabled stages simply
        // keep their previous sample and no resolve ever touches unwritten slots.
        // Per-node CPU timing needs no wiring: the graph measures every node
        // automatically (see RenderGraph.Profiler).
        GpuTimestampSampler? gpuTimestamps = device.IsFeatureSupported(GPUFeatures.TimestampQuery)
            ? new GpuTimestampSampler(device, TimestampSlotCount, "pbr_pipeline", GpuTimestampSampler.PairStrideBytes)
            : null;

        // GPU counters stay unregistered (and the callback pushes below no-op)
        // on devices without timestamp query support.
        RenderProfileCounterId shadowGpuCounter = gpuTimestamps != null ? profiler.RegisterCounter("Pipeline", "Shadow (GPU)") : default;
        RenderProfileCounterId gbufferGpuCounter = gpuTimestamps != null ? profiler.RegisterCounter("Pipeline", "GBuffer (GPU)") : default;
        RenderProfileCounterId lightingGpuCounter = gpuTimestamps != null ? profiler.RegisterCounter("Pipeline", "Lighting (GPU)") : default;
        RenderProfileCounterId volumetricLightGpuCounter = gpuTimestamps != null ? profiler.RegisterCounter("Pipeline", "VolumetricLight (GPU)") : default;

        // The composed nodes, in execution order.
        var shadowNode = new RGNode_ShadowPass(shadowMapResource, environment.ShadowDataBufferTyped,
            environment.CascadeViewProjections, shadowMapSize)
        {
            Instrumentation = new PassInstrumentation
            {
                GpuTimestamps = gpuTimestamps, GpuQueryBase = ShadowQueryBase,
            },
        };
        var gbufferNode = new RGNode_GeometryPass(gbufferResource,
            [
                new ClearColorData(0, System.Numerics.Vector4.Zero),
                new ClearColorData(1, new System.Numerics.Vector4(0.5f, 0.5f, 1.0f, 1.0f)),
                new ClearColorData(2, System.Numerics.Vector4.Zero),
                new ClearColorData(3, System.Numerics.Vector4.Zero),
            ],
            clearDepth: 0.0f)
        {
            Instrumentation = new PassInstrumentation
            {
                GpuTimestamps = gpuTimestamps, GpuQueryBase = GBufferQueryBase,
            },
        };
        var afterGBufferNode = new RGNode_Callback();
        var lightingNode = new RGNode_DeferredLighting(rendering, graph, lightingMaterial,
            gbufferResource, sceneColorResource)
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
                GpuTimestamps = gpuTimestamps, GpuQueryBase = LightingQueryBase,
            },
        };
        var chain = new RenderChain();
        var blitNode = new RGNode_Blit(rendering, graph, chain, new RGNode_Blit.Descriptor { BlitShader = blitShader });

        // Volumetric light pass (optional). Created eagerly so no runtime
        // recompilation is needed; controlled at runtime via
        // PBRSceneEnvironment.VolumetricLightEnabled.
        GraphicsMaterial? volumetricLightMaterial = null;
        RGNode_FullscreenOverlay? volumetricLightNode = null;
        if (volumetricLightShader != null)
        {
            volumetricLightMaterial = rendering.CreateGraphicsMaterial(volumetricLightShader);
            volumetricLightMaterial.DepthStencilState = DepthStencilState.Default;
            volumetricLightMaterial.RasterizerState = RasterizerState.CullNone;
            volumetricLightMaterial.BlendState = BlendState.Additive;
            volumetricLightMaterial.SetBuffer(ShaderResourceId.Data, environment.LightingDataBuffer);
            volumetricLightMaterial.SetBuffer(ShaderResourceId.PointLights, environment.PointLightBuffer);
            volumetricLightMaterial.SetRenderTextureDepth("gbufferDepth", gbufferResource.Texture);
            volumetricLightMaterial.SetRenderTextureDepth("shadowMap", shadowMapResource.Texture);
            volumetricLightNode = new RGNode_FullscreenOverlay(rendering, graph, chain,
                volumetricLightMaterial)
            {
                IsEnabled = environment.VolumetricLightEnabled,
                Instrumentation = new PassInstrumentation
                {
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
        environment.LightingDebugViewChanged += view => lightingNode.LightingDebugView = view;
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
            // Frame start: republish the last-read GPU stage timings (the
            // profiler's BeginFrame cleared them) and, on the sampler's throttled
            // sample frames, read back the previous sample (guaranteed
            // GPU-complete via the interval). Gating on ShouldRecord first flips
            // the frame's sample flag, which both enables the readback and lets
            // the instrumented nodes record their timestamps this frame.
            GpuTimestampSampler sampler = gpuTimestamps;
            double shadowGpuMs = 0.0, gbufferGpuMs = 0.0, lightingGpuMs = 0.0, volumetricLightGpuMs = 0.0;
            graph.Use(new RGNode_Callback
            {
                Callback = _ =>
                {
                    profiler.PushValue(shadowGpuCounter, shadowGpuMs);
                    profiler.PushValue(gbufferGpuCounter, gbufferGpuMs);
                    profiler.PushValue(lightingGpuCounter, lightingGpuMs);
                    profiler.PushValue(volumetricLightGpuCounter, volumetricLightGpuMs);
                    if (!sampler.ShouldRecord)
                    {
                        return;
                    }
                    ulong[]? timestamps = sampler.TryReadback();
                    if (timestamps == null)
                    {
                        return;
                    }
                    shadowGpuMs = sampler.DeltaMilliseconds(timestamps, ShadowQueryBase, ShadowQueryBase + 1);
                    gbufferGpuMs = sampler.DeltaMilliseconds(timestamps, GBufferQueryBase, GBufferQueryBase + 1);
                    lightingGpuMs = sampler.DeltaMilliseconds(timestamps, LightingQueryBase, LightingQueryBase + 1);
                    volumetricLightGpuMs = sampler.DeltaMilliseconds(timestamps, VolumetricLightQueryBase, VolumetricLightQueryBase + 1);
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
        lightingMaterial.SetRenderTexture("albedo",   gbuffer, 0);
        lightingMaterial.SetRenderTexture("normal",   gbuffer, 1);
        lightingMaterial.SetRenderTexture("mrAO",     gbuffer, 2);
        lightingMaterial.SetRenderTexture("emissive", gbuffer, 3);
        lightingMaterial.SetRenderTextureDepth("gbufferDepth", gbuffer);
        lightingMaterial.SetRenderTextureDepth("shadowMap", shadowMapResource.Texture);
        // Plugin output textures default to white/black until a plugin sets them.
        lightingMaterial.SetTexture("aoTexture", rendering.TextureWhite);
        lightingMaterial.SetTexture("cloudShadow", rendering.TextureWhite);
        lightingMaterial.SetTexture("giDiffuse", rendering.TextureBlack);
        lightingMaterial.SetTexture("giSpecular", rendering.TextureBlack);
    }
}
