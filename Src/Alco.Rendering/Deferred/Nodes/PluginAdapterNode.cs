using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// The black-box graph adapter for unknown <see cref="IRenderPlugin"/> effects of the
/// <see cref="PBRDeferredPipeline"/>. Known plugin types (HBAO, VoxelGI) are registered
/// as direct <see cref="IRenderGraphNode"/>s with their own Setup/Execute; this adapter
/// only handles plugins the pipeline does not recognize, executing them with the
/// historical <see cref="RenderPluginContext"/> assembly and binding their output
/// textures to the lighting material via <see cref="PBRDeferredPipeline.RebindPluginOutputs"/>.
/// <br/>Setup declares nothing when no unknown plugin is registered for the injection
/// point, so the node is culled. Otherwise it reads the G-buffer (and the shadow map
/// when shadows are enabled) and keeps itself alive via
/// <see cref="RenderGraphBuilder.ProducesOutput"/>.
/// </summary>
internal sealed class PluginAdapterNode : IRenderGraphNode
{
    private readonly PBRDeferredPipeline _pipeline;
    private readonly RenderInjectionPoint _injectionPoint;

    internal PluginAdapterNode(PBRDeferredPipeline pipeline, RenderInjectionPoint injectionPoint)
    {
        _pipeline = pipeline;
        _injectionPoint = injectionPoint;
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        List<IRenderPlugin> plugins = _pipeline.Plugins;
        bool hasAdapterPlugin = false;
        for (int i = 0; i < plugins.Count; i++)
        {
            IRenderPlugin plugin = plugins[i];
            if (plugin.InjectionPoint != _injectionPoint)
            {
                continue;
            }
            // Known plugin types registered as direct graph nodes are handled by
            // their own nodes — skip them here.
            if (plugin is HbaoRenderer hbao && hbao.IsGraphAttached)
            {
                continue;
            }
            if (plugin is VoxelGiRenderer gi && gi.IsGraphAttached)
            {
                continue;
            }
            hasAdapterPlugin = true;
            break;
        }
        if (!hasAdapterPlugin)
        {
            return;
        }

        builder.Read(_pipeline.GBufferResource);
        if (_pipeline.ShadowEnabled)
        {
            builder.Read(_pipeline.ShadowMapResource);
        }
        // Unknown plugins have no importable outputs; keep them alive unconditionally.
        builder.ProducesOutput();
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        CameraPerspectiveBuffer? camera = _pipeline.Camera;
        if (camera == null)
        {
            throw new InvalidOperationException("ExecutePlugins requires a camera (call SetCamera first).");
        }

        Matrix4x4.Invert(camera.Data.ViewProjectionMatrix, out Matrix4x4 invViewProjection);

        // Pre-populate the lighting data for plugins that need it (GI reads sun
        // direction, cascades, sky colors from here).
        _pipeline.AssembleLightingData(invViewProjection);

        RenderTexture gbuffer = _pipeline.GBufferResource.Texture;
        RenderPluginContext pluginContext = new()
        {
            Rendering = _pipeline.Rendering,
            GBuffer = gbuffer,
            ShadowMap = _pipeline.ShadowMapResource.Texture,
            InvViewProjection = invViewProjection,
            ProjectionMatrix = camera.Data.ProjectionMatrix,
            CameraTransform = camera.Transform,
            Width = gbuffer.Width,
            Height = gbuffer.Height,
            LightingData = _pipeline.CurrentLightingData,
            PointLightBuffer = _pipeline.PointLightBuffer,
            DeltaTime = context.DeltaTime,
            Profiler = _pipeline.Profiler,
        };

        bool anyExecuted = false;
        List<IRenderPlugin> plugins = _pipeline.Plugins;
        for (int i = 0; i < plugins.Count; i++)
        {
            IRenderPlugin plugin = plugins[i];
            if (plugin.InjectionPoint != _injectionPoint)
            {
                continue;
            }
            // Skip plugins that are registered as direct graph nodes.
            if (plugin is HbaoRenderer hbao && hbao.IsGraphAttached)
            {
                continue;
            }
            if (plugin is VoxelGiRenderer gi && gi.IsGraphAttached)
            {
                continue;
            }
            plugin.Execute(pluginContext);
            anyExecuted = true;
        }
        if (anyExecuted)
        {
            _pipeline.RebindPluginOutputs(pluginContext);
        }
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        List<IRenderPlugin> plugins = _pipeline.Plugins;
        for (int i = 0; i < plugins.Count; i++)
        {
            plugins[i].Resize(width, height);
        }
    }
}
