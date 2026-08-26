using NUnit.Framework;
using Alco.Graphics;
using Alco.World3D;
using Alco.Rendering;
using Alco.ShaderCompiler;

namespace Alco.World3D.Test;

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
    // Minimal deferred lighting module declaring every resource the pipeline binds
    // by name (the cbuffer layout itself is irrelevant to the NoGPU backend).
    // One cbuffer block per set — the slang module convention — with depth
    // textures as native DepthTexture2D.
    // The data block mirrors AlcoWorld3D_PBRCommon's PbrData: the environment
    // writes every member by name through the reflection-driven uniform buffer,
    // so the fixture must spell the same members (types included).
    private const string LightingShaderSource = """
        module test_lighting;

        // Marks the environment's lighting-data block for PBRSceneEnvironment's
        // discovery; declared inline, module-local like the engine's own PBR common.
        [__AttributeUsage(_AttributeTargets.Var)]
        public struct SceneEnvironmentParams {};

        [SceneEnvironmentParams]
        cbuffer data : register(b0, space0)
        {
            float4x4 invViewProjection;
            float4x4 sunViewProjection[4];
            float4 cameraPosition;
            float4 sunDirection;
            float4 sunColorAndIntensity;
            float4 skyParams;
            float4 skyParams2;
            float4 skyHorizonColor;
            float4 skyZenithColor;
            bool shadowEnabled;
            uint numPointLights;
            float shadowMapSize;
            bool sunDiscEnabled;
            float4 cascadeSplits;
            float4 cascadeTexelSizes;
            float shadowTightness;
            uint viewportWidth;
            uint viewportHeight;
            bool giEnabled;
            float giDiffuseStrength;
            float giSpecularStrength;
            float sunDiscSize;
            float sunDiscBrightness;
            bool volumetricLightEnabled;
            float volumetricLightIntensity;
            float fogDensity;
            float heightScaleHeight;
            float phaseG;
            float cloudShadowStrength;
            float cloudShadowPlaneAltitude;
            float cloudShadowExtent;
            bool cloudShadowEnabled;
        };

        struct PointLightData { float4 positionRange; float4 colorIntensity; };

        cbuffer lighting : register(b0, space1)
        {
            Texture2D albedo;
            Texture2D normal;
            Texture2D mrAO;
            DepthTexture2D gbufferDepth;
            Texture2D emissive;
            Texture2D giDiffuse;
            Texture2D giSpecular;
            Texture2D aoTexture;
            Texture2D cloudShadow;
            DepthTexture2D shadowMap;
            SamplerState linearClamp;
            RWStructuredBuffer<PointLightData> pointLights;
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

        // Mirrors the real deferred-lighting module's DebugView axis: the preset
        // constructs its lighting material with the Off specialization.
        [shader("pixel")]
        float4 MainPS<let DebugView : int>(V2F input) : SV_TARGET
        {
            return albedo.Sample(linearClamp, input.uv);
        }
        """;

    private const string BlitShaderSource = """
        module test_blit;

        cbuffer pass : register(b0, space0)
        {
            Texture2D texture;
            SamplerState linearClamp;
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
            return texture.Sample(linearClamp, input.uv);
        }
        """;

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
    private ShaderSystem _shaderSystem;
    private GPUDevice _device;
    private Shader _blitShader;
    private Shader _lightingShader;
    private GPUAttachmentLayout _destinationLayout;

    [SetUp]
    public void SetUp()
    {
        // The preset's scene environment reflects the real PBR common module
        // (reflection-driven uniform buffer), so the resolver serves both shader
        // trees (Alco.Rendering's core libs + Alco.World3D's pipelines) from the
        // repository, applying the engine resolver's underscore→dashed filename
        // convention (module 'alco_world3d_pbr_common' lives in
        // 'alco-world3d-pbr-common.slang'); the two test modules stay virtual
        // sources that bypass the resolver.
        Dictionary<string, string> shaderFiles = [];
        foreach (string module in new[] { "Alco.Rendering", "Alco.World3D" })
        {
            string shaderRoot = Path.Combine(RepoRoot(), "Src", module, "Assets", "Shaders");
            foreach (IGrouping<string, string> group in Directory
                .EnumerateFiles(shaderRoot, "*.slang", SearchOption.AllDirectories)
                .GroupBy(file => Path.GetFileName(file).Replace('_', '-'), StringComparer.OrdinalIgnoreCase))
            {
                shaderFiles.TryAdd(group.Key, group.First());
            }
        }
        string? ResolveShader(string path)
            => shaderFiles.TryGetValue(Path.GetFileName(path).Replace('_', '-'), out string? file)
                ? File.ReadAllText(file)
                : null;
        // Installed at construction so the rendering system's own shader system
        // (used by PBRSceneEnvironment's reflection lookup) shares the same
        // file-serving resolver as the test's isolated one.
        _host = Utility.CreateRenderingSystem(ResolveShader);
        _rendering = _host.RenderingSystem;
        _device = _rendering.GraphicsDevice;
        _shaderSystem = new ShaderSystem(
            _rendering,
            new SlangCompilerOptions
            {
                Resolver = ResolveShader,
            },
            cacheDirectory: null);
        _blitShader = _shaderSystem.GetShaderFromModule("test_blit", "test_blit.slang", BlitShaderSource);
        _lightingShader = _shaderSystem.GetShaderFromModule("test_lighting", "test_lighting.slang", LightingShaderSource);
        _destinationLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "test_destination"));
    }

    [TearDown]
    public void TearDown()
    {
        _shaderSystem.Dispose();
        _blitShader.Dispose();
        _lightingShader.Dispose();
        _destinationLayout.Dispose();
        _host.Dispose();
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Alco.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private PBRDeferredPreset CreatePreset(uint width = 64, uint height = 64)
    {
        return RenderPipelines.CreatePBRDeferred(
            _rendering,
            _lightingShader,
            _shaderSystem.GetLibrary("test_lighting"),
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
