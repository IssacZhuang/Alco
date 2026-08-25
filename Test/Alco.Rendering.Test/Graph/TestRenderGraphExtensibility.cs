using System.Numerics;
using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;
using Alco.ShaderCompiler;
using Alco.World3D;

namespace Alco.Rendering.Test;

/// <summary>
/// The render graph extensibility contract, exercised strictly through the public API
/// surface (no internal access, no engine modification):
/// <list type="bullet">
/// <item>A user can compose a complete rendering pipeline from scratch — graph,
/// transient targets, clear / content / transform / blit nodes.</item>
/// <item>A user can replace a pipeline stage with their own implementation
/// (here: the deferred lighting pass of a preset composed by
/// <see cref="RenderPipelines"/>).</item>
/// <item>A user can insert custom post-process effects into an existing pipeline,
/// reorder and toggle them, and remove them again at runtime.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class TestRenderGraphExtensibility
{
    // Same minimal deferred lighting module as TestPBRDeferredPreset: declares every
    // resource the pipeline binds by name (the cbuffer layout itself is irrelevant to
    // the NoGPU backend). One cbuffer block per set — the slang module convention —
    // with depth textures as native DepthTexture2D.
    private const string LightingShaderSource = """
        module test_render_graph_lighting;

        cbuffer _data : register(b0, space0) { float4 dummy0; float4 dummy1; };

        struct PointLightData { float4 positionRange; float4 colorIntensity; };

        cbuffer _lighting : register(b0, space1)
        {
            Texture2D _albedo;
            SamplerState _albedoSampler;
            Texture2D _normal;
            SamplerState _normalSampler;
            Texture2D _mrAO;
            SamplerState _mrAOSampler;
            DepthTexture2D _gbufferDepth;
            Texture2D _emissive;
            SamplerState _emissiveSampler;
            Texture2D _giDiffuse;
            SamplerState _giDiffuseSampler;
            Texture2D _giSpecular;
            SamplerState _giSpecularSampler;
            Texture2D _aoTexture;
            SamplerState _aoTextureSampler;
            Texture2D _cloudShadow;
            SamplerState _cloudShadowSampler;
            DepthTexture2D _shadowMap;
            SamplerComparisonState _shadowMapSampler;
            RWStructuredBuffer<PointLightData> _pointLights;
        };

        struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
        struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        V2F MainVS(Vertex input)
        {
            V2F o;
            o.position = float4(input.position, 1.0f);
            o.uv = input.uv;
            return o;
        }

        [shader("pixel")]
        // Mirrors the real deferred-lighting module's DebugView axis: the
        // pipeline constructs its lighting material with the Off specialization.
        float4 MainPS<let DebugView : int>(V2F input) : SV_TARGET
        {
            return _albedo.Sample(_albedoSampler, input.uv);
        }
        """;

    private const string BlitShaderSource = """
        module test_render_graph_blit;

        cbuffer _pass : register(b0, space0)
        {
            Texture2D _texture;
            SamplerState _textureSampler;
        };

        struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
        struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        V2F MainVS(Vertex input)
        {
            V2F o;
            o.position = float4(input.position, 1.0f);
            o.uv = input.uv;
            return o;
        }

        [shader("pixel")]
        float4 MainPS(V2F input) : SV_TARGET
        {
            return _texture.Sample(_textureSampler, input.uv);
        }
        """;

    /// <summary>A user content node drawing into the chain's current target.</summary>
    private sealed class CustomSceneNode : RGNode_SceneContent
    {
        public List<string> Log = new();

        public CustomSceneNode(RenderGraph graph, RenderChain chain)
            : base(graph, chain)
        {
        }

        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            Log.Add("content");
        }
    }

    /// <summary>A user post-process effect transforming the chain content.</summary>
    private sealed class CustomEffectNode : RGNode_ChainTransform
    {
        private readonly string _tag;
        public List<string> Log = new();

        public CustomEffectNode(RenderGraph graph, RenderChain chain, GPUAttachmentLayout outputLayout, string tag)
            : base(graph, chain, outputLayout, name: tag)
        {
            _tag = tag;
        }

        protected override void OnProcess(RenderTexture input, RenderTexture output, in RenderGraphContext context)
        {
            Log.Add(_tag);
        }
    }

    /// <summary>
    /// A user replacement for the deferred lighting pass: clears the scene color
    /// target with a constant color instead of running the engine's lighting shader.
    /// </summary>
    private sealed class CustomLightingNode : AutoDisposable, IRenderGraphNode
    {
        private readonly RenderGraphTexture _sceneColor;
        public List<string> Log = new();

        public CustomLightingNode(RenderGraphTexture sceneColor)
        {
            _sceneColor = sceneColor;
        }

        public bool IsEnabled { get; set; } = true;

        public void Setup(RenderGraphBuilder builder)
        {
            builder.Write(_sceneColor);
        }

        public void Execute(in RenderGraphContext context)
        {
            Log.Add("custom_lighting");
            using (context.RenderContext.BeginPass(_sceneColor.Texture.FrameBuffer,
                new[] { new ClearColorData(0, Vector4.UnitW) }, 1.0f))
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
        }
    }

    private DummyRenderingSystemHost _host;
    private RenderingSystem _rendering;
    private ShaderSystem _shaderSystem;
    private GPUDevice _device;
    private Shader _blitShader;
    private Shader _lightingShader;
    private GPUAttachmentLayout _destinationLayout;
    private GPUAttachmentLayout _postProcessLayout;

    [SetUp]
    public void SetUp()
    {
        _host = Utility.CreateRenderingSystem();
        _rendering = _host.RenderingSystem;
        _device = _rendering.GraphicsDevice;
        _shaderSystem = new ShaderSystem(
            _rendering, new SlangCompilerOptions { Resolver = _ => null }, cacheDirectory: null);
        _blitShader = _shaderSystem.GetShaderFromModule(
            "test_render_graph_blit", "test_render_graph_blit.slang", BlitShaderSource);
        _lightingShader = _shaderSystem.GetShaderFromModule(
            "test_render_graph_lighting", "test_render_graph_lighting.slang", LightingShaderSource);
        _destinationLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "test_destination"));
        _postProcessLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(_rendering.PreferredHDRFormat)], null, "test_post_process"));
    }

    [TearDown]
    public void TearDown()
    {
        _shaderSystem.Dispose();
        _destinationLayout.Dispose();
        _postProcessLayout.Dispose();
        _host.Dispose();
    }

    private PBRDeferredPreset CreatePreset()
    {
        return RenderPipelines.CreatePBRDeferred(
            _rendering,
            _lightingShader,
            _blitShader,
            shadowMapSize: 64,
            width: 64,
            height: 64);
    }

    [Test(Description = "A complete pipeline — clear, content, post-process, blit — composed from scratch with public API only")]
    public void UserCanComposeACompletePipelineFromScratch()
    {
        using var graph = new RenderGraph(_rendering, 64, 64, "user_pipeline");
        var chain = new RenderChain();
        RenderGraphTexture scene = graph.CreateTransient(
            new RenderGraphTextureDescriptor(_rendering.PreferredHDRPass, name: "user_scene"));

        var content = new CustomSceneNode(graph, chain);
        var effect = new CustomEffectNode(graph, chain, _postProcessLayout, "effect");
        var blit = new RGNode_Blit(_rendering, graph, chain, new RGNode_Blit.Descriptor { BlitShader = _blitShader });
        graph.Use(new RGNode_Clear(scene, [new ClearColorData(0, Vector4.Zero)], 1.0f));
        graph.Use(content);
        graph.Use(effect);
        graph.Use(blit);
        using RenderTexture destination = _rendering.CreateRenderTexture(_destinationLayout, 64, 64, "test_destination_rt");

        chain.Reset(scene);
        graph.Execute(destination.FrameBuffer);

        Assert.That(content.Log, Is.EqualTo(new[] { "content" }));
        Assert.That(effect.Log, Is.EqualTo(new[] { "effect" }));

        // Headless frame: the transform is culled (its output has no consumer),
        // the content node still runs.
        chain.Reset(scene);
        graph.Execute(null);

        Assert.That(content.Log, Is.EqualTo(new[] { "content", "content" }));
        Assert.That(effect.Log, Is.EqualTo(new[] { "effect" }));
    }

    [Test(Description = "The engine's deferred lighting node can be swapped for a user implementation")]
    public void UserCanReplaceTheDeferredLightingStage()
    {
        using PBRDeferredPreset preset = CreatePreset();
        using RenderTexture destination = _rendering.CreateRenderTexture(_destinationLayout, 64, 64, "test_destination_rt");
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f);

        var replacement = new CustomLightingNode(preset.SceneColorResource);
        Assert.That(preset.Graph.Remove(preset.Lighting), Is.True);
        preset.Graph.InsertAfter(preset.GBufferPass, replacement);

        Assert.DoesNotThrow(() => preset.Pipeline.Render(destination.FrameBuffer));
        Assert.That(replacement.Log, Is.EqualTo(new[] { "custom_lighting" }));
        Assert.That(preset.Graph.Nodes, Does.Not.Contain(preset.Lighting));
    }

    [Test(Description = "Custom post-process effects insert into the pipeline's chain, run in order, and cull when disabled")]
    public void UserCanInsertOrderAndToggleCustomPostEffects()
    {
        using PBRDeferredPreset preset = CreatePreset();
        using RenderTexture destination = _rendering.CreateRenderTexture(_destinationLayout, 64, 64, "test_destination_rt");
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f);

        var log = new List<string>(4);
        var effectA = new CustomEffectNode(preset.Graph, preset.PostChain, preset.PostProcessLayout, "a") { Log = log };
        var effectB = new CustomEffectNode(preset.Graph, preset.PostChain, preset.PostProcessLayout, "b") { Log = log };
        preset.Pipeline.Use(effectA);
        preset.Pipeline.Use(effectB);

        preset.Pipeline.Render(destination.FrameBuffer);
        Assert.That(log, Is.EqualTo(new[] { "a", "b" }));

        // Disabling a transform rewires the chain automatically: the next effect
        // reads the previous enabled node's output.
        effectA.IsEnabled = false;
        preset.Pipeline.Render(destination.FrameBuffer);
        Assert.That(log, Is.EqualTo(new[] { "a", "b", "b" }));
    }

    [Test(Description = "A node can be removed at runtime and its transient destroyed with it")]
    public void UserCanRemoveANodeAndDestroyItsTransient()
    {
        using PBRDeferredPreset preset = CreatePreset();
        using RenderTexture destination = _rendering.CreateRenderTexture(_destinationLayout, 64, 64, "test_destination_rt");
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f);

        var effect = new CustomEffectNode(preset.Graph, preset.PostChain, preset.PostProcessLayout, "fx");
        preset.Pipeline.Use(effect);
        preset.Pipeline.Render(destination.FrameBuffer);
        Assert.That(effect.Log, Has.Count.EqualTo(1));

        Assert.That(preset.Pipeline.Remove(effect), Is.True);
        // Disposing tears down the node's private transient symmetrically.
        effect.Dispose();

        Assert.DoesNotThrow(() => preset.Pipeline.Render(destination.FrameBuffer));
        Assert.That(effect.Log, Has.Count.EqualTo(1));
    }
}
