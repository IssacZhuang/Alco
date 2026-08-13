using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

/// <summary>
/// End-to-end tests of the <see cref="RenderPipelines.CreatePBRDeferred"/> preset driven by the NoGPU backend:
/// node dispatch order and counts, feature gating (shadow / headless destination),
/// facade identity and version stability, resize and the no-camera guard.
/// The fake nodes use only the public composition API: pass content lists
/// (<see cref="RGNode_GeometryPass.Content"/> / <see cref="RGNode_ShadowPass.Content"/>) and
/// chain nodes (<see cref="RGNode_SceneContent"/> / <see cref="RGNode_ChainTransform"/>).
/// </summary>
[TestFixture]
public sealed class TestPBRDeferredPreset
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

    private sealed class FakeShadowContent : IShadowPassContent
    {
        public bool IsEnabled { get; set; } = true;
        public List<string> Log = new();
        public void OnRenderShadow(RenderPassScope context, int cascadeIndex)
        {
            Log.Add("shadow");
        }
    }

    private sealed class FakeGBufferContent : IRenderPassContent
    {
        public bool IsEnabled { get; set; } = true;
        public List<string> Log = new();
        public void OnRender(RenderPassScope context, GPUAttachmentLayout layout)
        {
            Log.Add("gbuffer");
        }
    }

    private sealed class FakeForwardNode : RGNode_SceneContent
    {
        public int ResizeCount;
        public List<string> Log = new();

        public FakeForwardNode(PBRDeferredPreset preset)
            : base(preset.Graph, preset.PostChain)
        {
        }

        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            Log.Add("forward");
        }

        public override void Resize(uint width, uint height)
        {
            ResizeCount++;
        }
    }

    private sealed class FakeProcessorNode : RGNode_ChainTransform
    {
        public int ResizeCount;
        public List<string> Log = new();

        public FakeProcessorNode(PBRDeferredPreset preset)
            : base(preset.Graph, preset.PostChain, preset.PostProcessLayout, name: "fake_processor")
        {
        }

        protected override void OnProcess(RenderTexture input, RenderTexture output, in RenderGraphContext context)
        {
            Log.Add("processor");
        }

        public override void Resize(uint width, uint height)
        {
            ResizeCount++;
        }
    }

    private DummyRenderingSystemHost _host;
    private RenderingSystem _rendering;
    private GPUDevice _device;
    private Shader _blitShader;
    private Shader _lightingShader;
    private GPUAttachmentLayout _destinationLayout;

    [SetUp]
    public void SetUp()
    {
        _host = Utility.CreateRenderingSystem();
        _rendering = _host.RenderingSystem;
        _device = _rendering.GraphicsDevice;
        _blitShader = _rendering.CreateShader(BlitShaderText, "test_blit");
        _lightingShader = _rendering.CreateShader(LightingShaderText, "test_lighting");
        _destinationLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "test_destination"));
    }

    [TearDown]
    public void TearDown()
    {
        _blitShader.Dispose();
        _lightingShader.Dispose();
        _destinationLayout.Dispose();
        _host.Dispose();
    }

    private PBRDeferredPreset CreatePreset(uint width = 64, uint height = 64)
    {
        return RenderPipelines.CreatePBRDeferred(
            _rendering,
            _lightingShader,
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
        using PBRDeferredPreset preset = CreatePreset();
        using RenderTexture destination = CreateDestination();
        var log = new List<string>(8);
        var shadow = new FakeShadowContent { Log = log };
        var gbuffer = new FakeGBufferContent { Log = log };
        var forward = new FakeForwardNode(preset) { Log = log };
        var processor = new FakeProcessorNode(preset) { Log = log };
        preset.ShadowPass.Content.Add(shadow);
        preset.GBufferPass.Content.Add(gbuffer);
        preset.Pipeline.Use(forward);
        preset.Pipeline.Use(processor);
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f);

        preset.Pipeline.Render(destination.FrameBuffer);

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
        using PBRDeferredPreset preset = CreatePreset();
        using RenderTexture destination = CreateDestination();
        preset.Environment.ShadowEnabled = false;
        var log = new List<string>(8);
        var shadow = new FakeShadowContent { Log = log };
        var gbuffer = new FakeGBufferContent { Log = log };
        var forward = new FakeForwardNode(preset) { Log = log };
        var processor = new FakeProcessorNode(preset) { Log = log };
        preset.ShadowPass.Content.Add(shadow);
        preset.GBufferPass.Content.Add(gbuffer);
        preset.Pipeline.Use(forward);
        preset.Pipeline.Use(processor);
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f);

        preset.Pipeline.Render(destination.FrameBuffer);

        Assert.That(log, Is.EqualTo(new[] { "gbuffer", "forward", "processor" }));
    }

    [Test(Description = "A null destination (headless view) skips chain transforms but still runs content nodes")]
    public void NullDestinationSkipsProcessorsButKeepsContent()
    {
        using PBRDeferredPreset preset = CreatePreset();
        var log = new List<string>(8);
        var shadow = new FakeShadowContent { Log = log };
        var gbuffer = new FakeGBufferContent { Log = log };
        var forward = new FakeForwardNode(preset) { Log = log };
        var processor = new FakeProcessorNode(preset) { Log = log };
        preset.ShadowPass.Content.Add(shadow);
        preset.GBufferPass.Content.Add(gbuffer);
        preset.Pipeline.Use(forward);
        preset.Pipeline.Use(processor);
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f);

        preset.Pipeline.Render(null);

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
        using PBRDeferredPreset preset = CreatePreset();
        using RenderTexture destination = CreateDestination();
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f);

        RenderTexture gbuffer = preset.GBuffer;
        RenderTexture forward = preset.ForwardRenderTexture;

        preset.Pipeline.Render(destination.FrameBuffer);
        preset.Pipeline.Render(destination.FrameBuffer);

        uint gbufferVersion = preset.GBuffer.Version;
        uint forwardVersion = preset.ForwardRenderTexture.Version;

        preset.Pipeline.Render(destination.FrameBuffer);

        Assert.That(ReferenceEquals(preset.GBuffer, gbuffer), Is.True);
        Assert.That(ReferenceEquals(preset.ForwardRenderTexture, forward), Is.True);
        Assert.That(preset.GBuffer.Version, Is.EqualTo(gbufferVersion));
        Assert.That(preset.ForwardRenderTexture.Version, Is.EqualTo(forwardVersion));
    }

    [Test(Description = "The scene color target shares the G-buffer's depth attachment (render graph depthSource) instead of owning a separate depth texture")]
    public void SceneColorSharesGBufferDepth()
    {
        using PBRDeferredPreset preset = CreatePreset();
        using RenderTexture destination = CreateDestination();
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f);

        preset.Pipeline.Render(destination.FrameBuffer);

        Assert.That(ReferenceEquals(
            preset.SceneColorResource.Texture.FrameBuffer.DepthStencil,
            preset.GBuffer.FrameBuffer.DepthStencil), Is.True);
    }

    [Test(Description = "The forward layout declares its depth read-only: the lighting/forward passes sample or test the shared G-buffer depth while it is attached")]
    public void ForwardLayoutDepthIsReadOnly()
    {
        using PBRDeferredPreset preset = CreatePreset();

        Assert.That(preset.ForwardLayout.Depth.HasValue, Is.True);
        Assert.That(preset.ForwardLayout.Depth.Value.ReadOnly, Is.True);
    }

    [Test(Description = "Resize keeps facade identity, updates sizes and notifies chain nodes")]
    public void ResizeUpdatesFacadesAndNotifiesNodes()
    {
        using PBRDeferredPreset preset = CreatePreset();
        using RenderTexture destination = CreateDestination();
        var forward = new FakeForwardNode(preset);
        var processor = new FakeProcessorNode(preset);
        preset.Pipeline.Use(forward);
        preset.Pipeline.Use(processor);
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 100f);

        RenderTexture gbuffer = preset.GBuffer;
        RenderTexture forwardRT = preset.ForwardRenderTexture;

        preset.Pipeline.Resize(128, 96);

        Assert.That(ReferenceEquals(preset.GBuffer, gbuffer), Is.True);
        Assert.That(ReferenceEquals(preset.ForwardRenderTexture, forwardRT), Is.True);
        Assert.That(preset.GBuffer.Width, Is.EqualTo(128));
        Assert.That(preset.GBuffer.Height, Is.EqualTo(96));
        Assert.That(preset.ForwardRenderTexture.Width, Is.EqualTo(128));
        Assert.That(preset.ForwardRenderTexture.Height, Is.EqualTo(96));
        Assert.That(forward.ResizeCount, Is.EqualTo(1));
        Assert.That(processor.ResizeCount, Is.EqualTo(1));

        // The pipeline still renders after the resize.
        Assert.DoesNotThrow(() => preset.Pipeline.Render(destination.FrameBuffer));
    }

    [Test(Description = "Render without a camera throws InvalidOperationException")]
    public void RenderWithoutCameraThrows()
    {
        using PBRDeferredPreset preset = CreatePreset();
        using RenderTexture destination = CreateDestination();

        Assert.Throws<InvalidOperationException>(() => preset.Pipeline.Render(destination.FrameBuffer));
    }
}
