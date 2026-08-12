using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

/// <summary>
/// End-to-end tests of <see cref="PBRDeferredPipeline"/> driven by the NoGPU backend:
/// node dispatch order and counts, feature gating (shadow / headless destination),
/// facade identity and version stability, resize and the no-camera guard.
/// </summary>
[TestFixture]
public sealed class TestPBRDeferredPipeline
{
    // Minimal deferred lighting shader declaring every resource the pipeline binds
    // by name (the cbuffer layout itself is irrelevant to the NoGPU backend). The
    // depth textures must go through the DEFINE_TEX2D_DEPTH* macro patterns so the
    // reflection marks them as depth textures (see ShaderUtility / SpirvDepthTexturePatcher).
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

    private sealed class FakeShadowNode : IShadowRenderNode
    {
        public bool IsEnabled { get; set; } = true;
        public List<string> Log = new();
        public void OnRenderShadow(RenderContext context, int cascadeIndex)
        {
            Log.Add("shadow");
        }
    }

    private sealed class FakeGBufferNode : IGBufferRenderNode
    {
        public bool IsEnabled { get; set; } = true;
        public List<string> Log = new();
        public void OnRenderGBuffer(RenderContext context, GPUAttachmentLayout layout)
        {
            Log.Add("gbuffer");
        }
    }

    private sealed class FakeForwardNode : IForwardRenderNode
    {
        public bool IsEnabled { get; set; } = true;
        public int ResizeCount;
        public List<string> Log = new();
        public void OnRenderForward(GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            Log.Add("forward");
        }
        public void Resize(uint width, uint height)
        {
            ResizeCount++;
        }
    }

    private sealed class FakeProcessorNode : IContentProcessorNode
    {
        public bool IsEnabled { get; set; } = true;
        public int ResizeCount;
        public List<string> Log = new();
        public void OnRenderForward(RenderTexture input, RenderTexture target)
        {
            Log.Add("processor");
        }
        public void Resize(uint width, uint height)
        {
            ResizeCount++;
        }
    }

    private DummyRenderingSystemHost _host;
    private RenderingSystem _rendering;
    private GPUDevice _device;
    private Shader _blitShader;
    private GPUAttachmentLayout _destinationLayout;

    [SetUp]
    public void SetUp()
    {
        _host = Utility.CreateRenderingSystem();
        _rendering = _host.RenderingSystem;
        _device = _rendering.GraphicsDevice;
        _blitShader = _rendering.CreateShader(BlitShaderText, "test_blit");
        _destinationLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "test_destination"));
    }

    [TearDown]
    public void TearDown()
    {
        _blitShader.Dispose();
        _destinationLayout.Dispose();
        _host.Dispose();
    }

    private PBRDeferredPipeline CreatePipeline(uint width = 64, uint height = 64)
    {
        return new PBRDeferredPipeline(
            _rendering,
            LightingShaderText,
            "test_lighting",
            _blitShader,
            shadowMapSize: 64,
            width: width,
            height: height);
    }

    private RenderTexture CreateDestination(uint width = 64, uint height = 64)
    {
        return _rendering.CreateRenderTexture(_destinationLayout, width, height, "test_destination_rt");
    }

    [Test(Description = "All registered nodes are invoked in registration order with the expected per-pass counts")]
    public void RenderInvokesAllNodesInOrder()
    {
        using PBRDeferredPipeline pipeline = CreatePipeline();
        using RenderTexture destination = CreateDestination();
        var log = new List<string>(8);
        var shadow = new FakeShadowNode { Log = log };
        var gbuffer = new FakeGBufferNode { Log = log };
        var forward = new FakeForwardNode { Log = log };
        var processor = new FakeProcessorNode { Log = log };
        pipeline.Use(shadow);
        pipeline.Use(gbuffer);
        pipeline.Use(forward);
        pipeline.Use(processor);
        pipeline.SetCamera(_rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f));

        pipeline.Render(destination.FrameBuffer);

        Assert.That(log, Is.EqualTo(new[]
        {
            "shadow", "shadow", "shadow", "shadow",
            "gbuffer",
            "forward",
            "processor",
        }));
    }

    [Test(Description = "ShadowEnabled=false culls the whole shadow pass; every other node still runs")]
    public void ShadowDisabledSkipsShadowPass()
    {
        using PBRDeferredPipeline pipeline = CreatePipeline();
        using RenderTexture destination = CreateDestination();
        pipeline.ShadowEnabled = false;
        var log = new List<string>(8);
        var shadow = new FakeShadowNode { Log = log };
        var gbuffer = new FakeGBufferNode { Log = log };
        var forward = new FakeForwardNode { Log = log };
        var processor = new FakeProcessorNode { Log = log };
        pipeline.Use(shadow);
        pipeline.Use(gbuffer);
        pipeline.Use(forward);
        pipeline.Use(processor);
        pipeline.SetCamera(_rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f));

        pipeline.Render(destination.FrameBuffer);

        Assert.That(log, Is.EqualTo(new[] { "gbuffer", "forward", "processor" }));
    }

    [Test(Description = "A null destination (headless view) skips processors but still runs forward content nodes")]
    public void NullDestinationSkipsProcessorsButKeepsContent()
    {
        using PBRDeferredPipeline pipeline = CreatePipeline();
        var log = new List<string>(8);
        var shadow = new FakeShadowNode { Log = log };
        var gbuffer = new FakeGBufferNode { Log = log };
        var forward = new FakeForwardNode { Log = log };
        var processor = new FakeProcessorNode { Log = log };
        pipeline.Use(shadow);
        pipeline.Use(gbuffer);
        pipeline.Use(forward);
        pipeline.Use(processor);
        pipeline.SetCamera(_rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f));

        pipeline.Render(null);

        Assert.That(log, Is.EqualTo(new[]
        {
            "shadow", "shadow", "shadow", "shadow",
            "gbuffer",
            "forward",
        }));
    }

    [Test(Description = "The GBuffer and ForwardRenderTexture facades keep identity and version across steady-state frames")]
    public void FacadesKeepIdentityAndVersionAcrossFrames()
    {
        using PBRDeferredPipeline pipeline = CreatePipeline();
        using RenderTexture destination = CreateDestination();
        pipeline.SetCamera(_rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f));

        RenderTexture gbuffer = pipeline.GBuffer;
        RenderTexture forward = pipeline.ForwardRenderTexture;

        pipeline.Render(destination.FrameBuffer);
        pipeline.Render(destination.FrameBuffer);

        uint gbufferVersion = pipeline.GBuffer.Version;
        uint forwardVersion = pipeline.ForwardRenderTexture.Version;

        pipeline.Render(destination.FrameBuffer);

        Assert.That(ReferenceEquals(pipeline.GBuffer, gbuffer), Is.True);
        Assert.That(ReferenceEquals(pipeline.ForwardRenderTexture, forward), Is.True);
        Assert.That(pipeline.GBuffer.Version, Is.EqualTo(gbufferVersion));
        Assert.That(pipeline.ForwardRenderTexture.Version, Is.EqualTo(forwardVersion));
    }

    [Test(Description = "Resize keeps facade identity, updates sizes and notifies chain nodes")]
    public void ResizeUpdatesFacadesAndNotifiesNodes()
    {
        using PBRDeferredPipeline pipeline = CreatePipeline();
        using RenderTexture destination = CreateDestination();
        var forward = new FakeForwardNode();
        var processor = new FakeProcessorNode();
        pipeline.Use(forward);
        pipeline.Use(processor);
        pipeline.SetCamera(_rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f));

        RenderTexture gbuffer = pipeline.GBuffer;
        RenderTexture forwardRT = pipeline.ForwardRenderTexture;

        pipeline.Resize(128, 96);

        Assert.That(ReferenceEquals(pipeline.GBuffer, gbuffer), Is.True);
        Assert.That(ReferenceEquals(pipeline.ForwardRenderTexture, forwardRT), Is.True);
        Assert.That(pipeline.GBuffer.Width, Is.EqualTo(128));
        Assert.That(pipeline.GBuffer.Height, Is.EqualTo(96));
        Assert.That(pipeline.ForwardRenderTexture.Width, Is.EqualTo(128));
        Assert.That(pipeline.ForwardRenderTexture.Height, Is.EqualTo(96));
        Assert.That(forward.ResizeCount, Is.EqualTo(1));
        Assert.That(processor.ResizeCount, Is.EqualTo(1));

        // The pipeline still renders after the resize.
        Assert.DoesNotThrow(() => pipeline.Render(destination.FrameBuffer));
    }

    [Test(Description = "Render without a camera throws InvalidOperationException")]
    public void RenderWithoutCameraThrows()
    {
        using PBRDeferredPipeline pipeline = CreatePipeline();
        using RenderTexture destination = CreateDestination();

        Assert.Throws<InvalidOperationException>(() => pipeline.Render(destination.FrameBuffer));
    }
}
