using System.Numerics;
using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

/// <summary>
/// The render graph extensibility contract, exercised strictly through the public API
/// surface (no internal access, no engine modification):
/// <list type="bullet">
/// <item>A user can compose a complete rendering pipeline from scratch — graph,
/// transient targets, clear / content / transform / blit nodes.</item>
/// <item>A user can replace a pipeline stage with their own implementation
/// (here: the deferred lighting pass of <see cref="PBRDeferredPipeline"/>).</item>
/// <item>A user can insert custom post-process effects into an existing pipeline,
/// reorder and toggle them, and remove them again at runtime.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class TestRenderGraphExtensibility
{
    // Same minimal deferred lighting shader as TestPBRDeferredPipeline: declares every
    // resource the pipeline binds by name (the cbuffer layout itself is irrelevant to
    // the NoGPU backend).
    private const string LightingShaderText = @"
#define ALCO_PASTE_(a, b) a##b
#define ALCO_PASTE(a, b) ALCO_PASTE_(a, b)
#define ALCO_SET(set) register(ALCO_PASTE(space, set))
#define DEFINE_UNIFORM(index, name) cbuffer name : ALCO_SET(index)
#define DEFINE_STORAGE(index, type, name) RWStructuredBuffer<type> name : ALCO_SET(index)
#define DEFINE_TEX2D_SAMPLE(index, name) Texture2D name : ALCO_SET(index); SamplerState name##Sampler : ALCO_SET(index)
#define DEFINE_TEX2D_DEPTH(index, name) Texture2D<float> name : ALCO_SET(index)
#define DEFINE_TEX2D_DEPTH_SAMPLE(index, name) Texture2D<float> name : ALCO_SET(index); SamplerComparisonState name##Sampler : ALCO_SET(index)

struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

DEFINE_UNIFORM(0, _data) { float4 dummy0; float4 dummy1; };

DEFINE_TEX2D_SAMPLE(1, _albedo);
DEFINE_TEX2D_SAMPLE(1, _normal);
DEFINE_TEX2D_SAMPLE(1, _mrAO);
DEFINE_TEX2D_DEPTH(1, _gbufferDepth);
DEFINE_TEX2D_SAMPLE(1, _emissive);
DEFINE_TEX2D_SAMPLE(1, _giDiffuse);
DEFINE_TEX2D_SAMPLE(1, _giSpecular);
DEFINE_TEX2D_SAMPLE(1, _aoTexture);
DEFINE_TEX2D_DEPTH_SAMPLE(1, _shadowMap);

struct PointLightData { float4 positionRange; float4 colorIntensity; };
DEFINE_STORAGE(1, PointLightData, _pointLights);

[shader(""vertex"")]
V2F MainVS(Vertex input)
{
    V2F o;
    o.position = float4(input.position, 1.0f);
    o.uv = input.uv;
    return o;
}

[shader(""pixel"")]
float4 MainPS(V2F input) : SV_TARGET
{
    return _albedo.Sample(_albedoSampler, input.uv);
}
";

    private const string BlitShaderText = @"
Texture2D _texture : register(space0);
SamplerState _textureSampler : register(space0);

struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

[shader(""vertex"")]
V2F MainVS(Vertex input)
{
    V2F o;
    o.position = float4(input.position, 1.0f);
    o.uv = input.uv;
    return o;
}

[shader(""pixel"")]
float4 MainPS(V2F input) : SV_TARGET
{
    return _texture.Sample(_textureSampler, input.uv);
}
";

    /// <summary>A user content node drawing into the chain's current target.</summary>
    private sealed class CustomSceneNode : RGNode_SceneContent
    {
        public List<string> Log = new();

        public CustomSceneNode(RenderGraph graph, RenderChain chain)
            : base(graph, chain)
        {
        }

        protected override void OnRender(GPUFrameBuffer target, GPUAttachmentLayout layout)
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
        private readonly RenderContext _context;
        public List<string> Log = new();

        public CustomLightingNode(RenderingSystem rendering, RenderGraphTexture sceneColor)
        {
            _sceneColor = sceneColor;
            _context = rendering.CreateRenderContext("custom_lighting");
        }

        public bool IsEnabled { get; set; } = true;

        public void Setup(RenderGraphBuilder builder)
        {
            builder.Write(_sceneColor);
        }

        public void Execute(in RenderGraphContext context)
        {
            Log.Add("custom_lighting");
            _context.Begin(_sceneColor.Texture.FrameBuffer,
                new[] { new ClearColorData(0, Vector4.UnitW) }, 1.0f);
            _context.End();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _context.Dispose();
            }
        }
    }

    private DummyRenderingSystemHost _host;
    private RenderingSystem _rendering;
    private GPUDevice _device;
    private Shader _blitShader;
    private GPUAttachmentLayout _destinationLayout;
    private GPUAttachmentLayout _postProcessLayout;

    [SetUp]
    public void SetUp()
    {
        _host = Utility.CreateRenderingSystem();
        _rendering = _host.RenderingSystem;
        _device = _rendering.GraphicsDevice;
        _blitShader = _rendering.CreateShader(BlitShaderText, "test_blit");
        _destinationLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "test_destination"));
        _postProcessLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(_rendering.PreferredHDRFormat)], null, "test_post_process"));
    }

    [TearDown]
    public void TearDown()
    {
        _blitShader.Dispose();
        _destinationLayout.Dispose();
        _postProcessLayout.Dispose();
        _host.Dispose();
    }

    private PBRDeferredPipeline CreatePipeline()
    {
        return new PBRDeferredPipeline(
            _rendering,
            LightingShaderText,
            "test_lighting",
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
        var blit = new RGNode_Blit(_rendering, graph, chain, _blitShader);
        graph.Use(new RGNode_Clear(_rendering, scene, [new ClearColorData(0, Vector4.Zero)], 1.0f));
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
        using PBRDeferredPipeline pipeline = CreatePipeline();
        using RenderTexture destination = _rendering.CreateRenderTexture(_destinationLayout, 64, 64, "test_destination_rt");
        pipeline.SetCamera(_rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f));

        var replacement = new CustomLightingNode(_rendering, pipeline.SceneColorResource);
        Assert.That(pipeline.Graph.Remove(pipeline.LightingNode), Is.True);
        pipeline.Graph.InsertAfter(pipeline.GBufferPass, replacement);

        Assert.DoesNotThrow(() => pipeline.Render(destination.FrameBuffer));
        Assert.That(replacement.Log, Is.EqualTo(new[] { "custom_lighting" }));
        Assert.That(pipeline.Graph.Nodes, Does.Not.Contain(pipeline.LightingNode));
    }

    [Test(Description = "Custom post-process effects insert into the pipeline's chain, run in order, and cull when disabled")]
    public void UserCanInsertOrderAndToggleCustomPostEffects()
    {
        using PBRDeferredPipeline pipeline = CreatePipeline();
        using RenderTexture destination = _rendering.CreateRenderTexture(_destinationLayout, 64, 64, "test_destination_rt");
        pipeline.SetCamera(_rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f));

        var log = new List<string>(4);
        var effectA = new CustomEffectNode(pipeline.Graph, pipeline.PostChain, pipeline.PostProcessLayout, "a") { Log = log };
        var effectB = new CustomEffectNode(pipeline.Graph, pipeline.PostChain, pipeline.PostProcessLayout, "b") { Log = log };
        pipeline.Use(effectA);
        pipeline.Use(effectB);

        pipeline.Render(destination.FrameBuffer);
        Assert.That(log, Is.EqualTo(new[] { "a", "b" }));

        // Disabling a transform rewires the chain automatically: the next effect
        // reads the previous enabled node's output.
        effectA.IsEnabled = false;
        pipeline.Render(destination.FrameBuffer);
        Assert.That(log, Is.EqualTo(new[] { "a", "b", "b" }));
    }

    [Test(Description = "A node can be removed at runtime and its transient destroyed with it")]
    public void UserCanRemoveANodeAndDestroyItsTransient()
    {
        using PBRDeferredPipeline pipeline = CreatePipeline();
        using RenderTexture destination = _rendering.CreateRenderTexture(_destinationLayout, 64, 64, "test_destination_rt");
        pipeline.SetCamera(_rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f));

        var effect = new CustomEffectNode(pipeline.Graph, pipeline.PostChain, pipeline.PostProcessLayout, "fx");
        pipeline.Use(effect);
        pipeline.Render(destination.FrameBuffer);
        Assert.That(effect.Log, Has.Count.EqualTo(1));

        Assert.That(pipeline.Remove(effect), Is.True);
        // Disposing tears down the node's private transient symmetrically.
        effect.Dispose();

        Assert.DoesNotThrow(() => pipeline.Render(destination.FrameBuffer));
        Assert.That(effect.Log, Has.Count.EqualTo(1));
    }
}
