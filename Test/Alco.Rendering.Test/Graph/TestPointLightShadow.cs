using System.Numerics;
using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

/// <summary>
/// Tests of the point light shadow atlas: the CPU↔GPU sampling contract of
/// <see cref="PointLightShadowMath"/> (folded matrices vs analytic face
/// projection), the atlas tile layout, and the graph integration of
/// <see cref="RGNode_PointLightShadow"/> — slot assignment (importance ranking,
/// hysteresis retention, eviction), static face caching and the shadow-info /
/// matrix buffer contents — driven end-to-end by the NoGPU backend.
/// </summary>
[TestFixture]
public sealed class TestPointLightShadow
{
    // ── CPU-side math contract ─────────────────────────────────────────────

    [Test(Description = "Folded face matrices and the analytic projection map the same world point to the same atlas texel and depth")]
    public void FoldedMatrixMatchesAnalyticFaceProjection()
    {
        uint faceSize = 64;
        uint slotsPerRow = 4;
        uint atlasWidth = faceSize * 3u * slotsPerRow;
        uint atlasHeight = faceSize * 2u * (RGNode_PointLightShadow.MaxSlots / slotsPerRow);
        var random = new Random(20260816);

        for (int iteration = 0; iteration < 250; iteration++)
        {
            int slot = random.Next(RGNode_PointLightShadow.MaxSlots);
            int face = random.Next(PointLightShadowMath.FaceCount);
            var light = new Vector3(
                (float)(random.NextDouble() * 40.0 - 20.0),
                (float)(random.NextDouble() * 40.0 - 20.0),
                (float)(random.NextDouble() * 10.0));
            float near = 0.05f + (float)random.NextDouble() * 0.5f;
            float far = near + 2.0f + (float)random.NextDouble() * 20.0f;
            PointLightShadowMath.GetFaceBasis(face, out Vector3 forward, out Vector3 right, out Vector3 up);

            // Points strictly inside the face frustum: forward distance between
            // the planes, lateral offset within ±0.7 of the forward distance so
            // the analytic side selects this face as the dominant one.
            float z = near + (float)random.NextDouble() * (far - near) * 0.98f;
            float x = ((float)random.NextDouble() * 1.4f - 0.7f) * z;
            float y = ((float)random.NextDouble() * 1.4f - 0.7f) * z;
            Vector3 world = light + forward * z + right * x + up * y;

            Matrix4x4 folded = PointLightShadowMath.FoldToAtlas(
                PointLightShadowMath.BuildFaceViewProjection(light, near, far, face),
                slot, face, faceSize, slotsPerRow, atlasWidth, atlasHeight);
            Vector4 clip = Vector4.Transform(new Vector4(world, 1.0f), folded);
            var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
            float projectedDepth = clip.Z / clip.W;

            PointLightShadowMath.ProjectToFace(world, light, out int selectedFace,
                out Vector2 uvLocal, out float linearDepth);
            Assert.That(selectedFace, Is.EqualTo(face),
                $"the dominant face of an in-frustum point must be the built face");
            Assert.That(linearDepth, Is.EqualTo(z).Within(1e-3f), "forward distance");
            Assert.That(clip.W, Is.EqualTo(linearDepth).Within(1e-3f * MathF.Max(1.0f, linearDepth)),
                "clip.w of a LH perspective equals the forward distance");

            // Matrix side: rasterized pixel = uv-atlas. Analytic side: face-local
            // uv placed at the face tile origin — the trace/inject shaders sample
            // exactly there, so the two must agree to sub-texel precision.
            (float originX, float originY) = PointLightShadowMath.FacePixelOrigin(slot, face, faceSize, slotsPerRow);
            var expectedUv = new Vector2(
                (originX + uvLocal.X * faceSize) / atlasWidth,
                (originY + uvLocal.Y * faceSize) / atlasHeight);
            var matrixUv = new Vector2(ndc.X * 0.5f + 0.5f, 0.5f - ndc.Y * 0.5f);
            Assert.That(matrixUv.X, Is.EqualTo(expectedUv.X).Within(1e-3f), $"uv.x (slot {slot} face {face})");
            Assert.That(matrixUv.Y, Is.EqualTo(expectedUv.Y).Within(1e-3f), $"uv.y (slot {slot} face {face})");
            Assert.That(projectedDepth,
                Is.EqualTo(PointLightShadowMath.LinearToProjectedDepth(linearDepth, near, far))
                    .Within(1e-3f), "projected depth");
        }
    }

    [Test(Description = "Linear↔projected depth conversion round-trips and maps near→0, far→1 monotonically")]
    public void DepthRangeConversionsRoundTrip()
    {
        float near = 0.1f;
        float far = 18.0f;
        Assert.That(PointLightShadowMath.LinearToProjectedDepth(near, near, far), Is.EqualTo(0.0f).Within(1e-6f));
        Assert.That(PointLightShadowMath.LinearToProjectedDepth(far, near, far), Is.EqualTo(1.0f).Within(1e-6f));

        float previous = -1.0f;
        for (int i = 0; i <= 64; i++)
        {
            float z = near + (far - near) * i / 64.0f;
            float projected = PointLightShadowMath.LinearToProjectedDepth(z, near, far);
            Assert.That(projected, Is.GreaterThan(previous), "projected depth must increase monotonically");
            previous = projected;

            float roundTrip = PointLightShadowMath.ProjectedDepthToLinear(projected, near, far);
            Assert.That(roundTrip, Is.EqualTo(z).Within(1e-4f * MathF.Max(1.0f, z)));
        }
    }

    [Test(Description = "The 96 face tiles are grid-aligned, disjoint and cover the atlas exactly")]
    public void FaceTilesAreDisjointAndCoverTheAtlas()
    {
        uint faceSize = 32;
        uint slotsPerRow = 4;
        uint atlasWidth = faceSize * 3u * slotsPerRow;
        uint atlasHeight = faceSize * 2u * (RGNode_PointLightShadow.MaxSlots / slotsPerRow);
        var origins = new HashSet<long>();

        for (int slot = 0; slot < RGNode_PointLightShadow.MaxSlots; slot++)
        {
            for (int face = 0; face < PointLightShadowMath.FaceCount; face++)
            {
                (float x, float y) = PointLightShadowMath.FacePixelOrigin(slot, face, faceSize, slotsPerRow);
                Assert.That(x % faceSize, Is.EqualTo(0.0f), "x must be tile-aligned");
                Assert.That(y % faceSize, Is.EqualTo(0.0f), "y must be tile-aligned");
                Assert.That(x + faceSize, Is.LessThanOrEqualTo(atlasWidth));
                Assert.That(y + faceSize, Is.LessThanOrEqualTo(atlasHeight));
                Assert.That(origins.Add((long)x * 4096 + (long)y), Is.True,
                    $"faces must not overlap (slot {slot} face {face})");
            }
        }
        Assert.That(origins.Count, Is.EqualTo(RGNode_PointLightShadow.MaxSlots * PointLightShadowMath.FaceCount));
    }

    [Test(Description = "ProjectToFace selects the dominant-axis face and computes the face-local uv")]
    public void ProjectToFaceSelectsTheDominantFace()
    {
        Span<Vector3> offsets =
        [
            new(5, 1, 1), new(-5, 1, 1), new(1, 5, 1),
            new(1, -5, 1), new(1, 1, 5), new(1, 1, -5),
        ];
        for (int face = 0; face < PointLightShadowMath.FaceCount; face++)
        {
            PointLightShadowMath.ProjectToFace(offsets[face], Vector3.Zero,
                out int selected, out Vector2 uvLocal, out float linearDepth);
            Assert.That(selected, Is.EqualTo(face));
            Assert.That(linearDepth, Is.EqualTo(5.0f).Within(1e-5f));
            if (face == 0)
            {
                // +X face: right = +Y, up = +Z → x = 1/5, y = 1/5.
                Assert.That(uvLocal.X, Is.EqualTo(0.6f).Within(1e-5f));
                Assert.That(uvLocal.Y, Is.EqualTo(0.4f).Within(1e-5f));
            }
        }
    }

    // ── NoGPU graph integration ────────────────────────────────────────────

    // Minimal fake shaders declaring every resource the node and renderer bind
    // by name (mirrors the real PointLightShadow*.hlsl declarations; the NoGPU
    // backend reflects names, it never executes the bodies).
    private const string ShaderPrelude = @"
#define ALCO_PASTE_(a, b) a##b
#define ALCO_PASTE(a, b) ALCO_PASTE_(a, b)
#define ALCO_SET(set) register(ALCO_PASTE(space, set))
#define PUSH_CONSTANT [[vk::push_constant]]
#define IMAGE_FORMAT(format) [[vk::image_format(format)]]
#define SAMPLE_TEX2D(tex, uv) tex.Sample(tex##Sampler, uv)
#define GET_PIXEL_TEX2D(tex, position) tex.Load(int3(position, 0))
#define DEFINE_UNIFORM(index, name) cbuffer name : ALCO_SET(index)
#define DEFINE_STORAGE(index, type, name) RWStructuredBuffer<type> name : ALCO_SET(index)
#define DEFINE_TEX2D_SAMPLE(index, name) Texture2D name : ALCO_SET(index); SamplerState name##Sampler : ALCO_SET(index)
#define DEFINE_TEX2D_READ(index, name) Texture2D name : ALCO_SET(index)
#define DEFINE_TEX2D_STORAGE(index, name, type, format) IMAGE_FORMAT(format) RWTexture2D<type> name : ALCO_SET(index)
#define DEFINE_TEX2D_DEPTH(index, name) Texture2D<float> name : ALCO_SET(index)
#define DEFINE_TEX2D_DEPTH_SAMPLE(index, name) Texture2D<float> name : ALCO_SET(index); SamplerComparisonState name##Sampler : ALCO_SET(index)
";

    private const string DepthShaderText = ShaderPrelude + @"
struct Vertex { float3 position : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 tangent : TANGENT; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

struct Constants { float4x4 model; float4 params_; };

DEFINE_UNIFORM(0, _data) { float4x4 faceViewProjections[96]; };
DEFINE_TEX2D_SAMPLE(1, _albedoTexture);

PUSH_CONSTANT Constants constants;

[shader(""vertex"")]
V2F MainVS(Vertex input)
{
    V2F o;
    float4 world = mul(constants.model, float4(input.position, 1.0f));
    o.position = mul(faceViewProjections[0], world);
    o.uv = input.uv;
    return o;
}

[shader(""pixel"")]
void MainPS(V2F input)
{
}
";

    private const string TraceShaderText = ShaderPrelude + @"
struct PointLightData { float4 positionRange; float4 colorIntensity; };
struct PointLightShadowInfo { float4 slotNearFar; };

DEFINE_UNIFORM(0, _data) { float4 dummy0; };
DEFINE_TEX2D_DEPTH(0, _gbufferDepth);
DEFINE_TEX2D_READ(0, _normal);
DEFINE_TEX2D_DEPTH_SAMPLE(0, _plShadowAtlas);
DEFINE_TEX2D_DEPTH(0, _plShadowAtlasLoad);
DEFINE_STORAGE(0, PointLightData, _pointLights);
DEFINE_STORAGE(0, PointLightShadowInfo, _plShadowInfo);
DEFINE_TEX2D_STORAGE(1, _plRawOut, float4, ""rgba16f"");

[shader(""compute"")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    float4 info = _plShadowInfo[0].slotNearFar;
    _plRawOut[dispatchId.xy] = float4(info.xy, _pointLights[0].positionRange.zw)
        + GET_PIXEL_TEX2D(_gbufferDepth, dispatchId.xy).xxxx
        + GET_PIXEL_TEX2D(_normal, dispatchId.xy)
        + GET_PIXEL_TEX2D(_plShadowAtlasLoad, dispatchId.xy).xxxx;
}
";

    private const string ResolveShaderText = ShaderPrelude + @"
DEFINE_UNIFORM(0, _data) { float4 dummy0; };
DEFINE_TEX2D_DEPTH(0, _gbufferDepth);
DEFINE_TEX2D_READ(0, _normal);
DEFINE_TEX2D_READ(0, _plRaw);
DEFINE_TEX2D_READ(0, _plHistory);
DEFINE_TEX2D_STORAGE(1, _plOut, float4, ""rgba16f"");

[shader(""compute"")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    _plOut[dispatchId.xy] = GET_PIXEL_TEX2D(_plRaw, dispatchId.xy) + GET_PIXEL_TEX2D(_plHistory, dispatchId.xy)
        + GET_PIXEL_TEX2D(_normal, dispatchId.xy) + GET_PIXEL_TEX2D(_gbufferDepth, dispatchId.xy).xxxx;
}
";

    private const string UpsampleShaderText = ShaderPrelude + @"
DEFINE_UNIFORM(0, _data) { float4 dummy0; };
DEFINE_TEX2D_DEPTH(0, _gbufferDepth);
DEFINE_TEX2D_SAMPLE(0, _plTrace);
DEFINE_TEX2D_STORAGE(1, _plOut, float4, ""rgba16f"");

[shader(""compute"")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    float2 uv = (float2(dispatchId.xy) + 0.5) / 64.0;
    _plOut[dispatchId.xy] = SAMPLE_TEX2D(_plTrace, uv)
        + GET_PIXEL_TEX2D(_gbufferDepth, dispatchId.xy).xxxx;
}
";

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
DEFINE_TEX2D_SAMPLE(1, _cloudShadow);
DEFINE_TEX2D_SAMPLE(1, _pointLightShadowed);
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

    private sealed class FakeCasterContent : IPointLightShadowContent
    {
        public bool IsEnabled { get; set; } = true;
        public bool HasDynamicCasters { get; set; }
        public List<int> FacesDrawn { get; } = new();

        public void OnRenderPointLightShadow(RenderPassScope context, int matrixIndex)
        {
            FacesDrawn.Add(matrixIndex);
        }
    }

    private sealed class FakeShadowRenderable : IShadowRenderable
    {
        private readonly Mesh _mesh;
        private readonly GraphicsMaterial _material;

        public FakeShadowRenderable(Mesh mesh, GraphicsMaterial material, bool isStatic)
        {
            _mesh = mesh;
            _material = material;
            IsStatic = isStatic;
        }

        public bool IsStatic { get; }
        public bool CastsShadow => true;
        Mesh IShadowRenderable.Mesh => _mesh;
        GraphicsMaterial IShadowRenderable.Material => _material;
        Matrix4x4 IShadowRenderable.WorldMatrix => Matrix4x4.Identity;
        float IShadowRenderable.AlphaCutoff => 0.5f;
        float IShadowRenderable.BaseColorAlpha => 1.0f;
    }

    private DummyRenderingSystemHost _host = null!;
    private RenderingSystem _rendering = null!;
    private Shader _blitShader = null!;
    private Shader _lightingShader = null!;
    private Shader _depthShader = null!;
    private Shader _traceShader = null!;
    private Shader _resolveShader = null!;
    private Shader _upsampleShader = null!;
    private GPUAttachmentLayout _destinationLayout = null!;

    [SetUp]
    public void SetUp()
    {
        _host = Utility.CreateRenderingSystem();
        _rendering = _host.RenderingSystem;
        _blitShader = _rendering.CreateShader(BlitShaderText, "test_blit");
        _lightingShader = _rendering.CreateShader(LightingShaderText, "test_lighting");
        _depthShader = _rendering.CreateShader(DepthShaderText, "test_pls_depth");
        _traceShader = _rendering.CreateShader(TraceShaderText, "test_pls_trace");
        _resolveShader = _rendering.CreateShader(ResolveShaderText, "test_pls_resolve");
        _upsampleShader = _rendering.CreateShader(UpsampleShaderText, "test_pls_upsample");
        _destinationLayout = _rendering.GraphicsDevice.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "test_destination"));
    }

    [TearDown]
    public void TearDown()
    {
        _blitShader.Dispose();
        _lightingShader.Dispose();
        _depthShader.Dispose();
        _traceShader.Dispose();
        _resolveShader.Dispose();
        _upsampleShader.Dispose();
        _destinationLayout.Dispose();
        _host.Dispose();
    }

    private RGNode_PointLightShadow CreateNode(uint width = 64, uint height = 64, uint faceSize = 32)
    {
        return new RGNode_PointLightShadow(
            _rendering,
            new PointLightShadowShaders
            {
                Depth = _depthShader,
                Trace = _traceShader,
                Resolve = _resolveShader,
                Upsample = _upsampleShader,
            },
            faceSize: faceSize,
            traceResolutionScale: 0.5f,
            width: width,
            height: height);
    }

    private RenderTexture CreateDestination(uint width = 64, uint height = 64)
    {
        return _rendering.CreateRenderTexture(_destinationLayout, width, height, "test_destination_rt");
    }

    private static PBRSceneEnvironment.PointLight Light(int index, float intensity, float range = 6.0f)
    {
        // All lights on the camera axis (+X forward) at the same distance so the
        // importance score ordering degenerates to the intensity ordering.
        return new PBRSceneEnvironment.PointLight(
            new Vector3(8.0f, 0.0f, 1.0f), Vector3.One, intensity, range);
    }

    private static int IndexOfNode(IReadOnlyList<IRenderGraphNode> nodes, IRenderGraphNode node)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], node))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool MatrixEquals(in Matrix4x4 a, in Matrix4x4 b, float epsilon)
    {
        return MathF.Abs(a.M11 - b.M11) <= epsilon && MathF.Abs(a.M12 - b.M12) <= epsilon
            && MathF.Abs(a.M13 - b.M13) <= epsilon && MathF.Abs(a.M14 - b.M14) <= epsilon
            && MathF.Abs(a.M21 - b.M21) <= epsilon && MathF.Abs(a.M22 - b.M22) <= epsilon
            && MathF.Abs(a.M23 - b.M23) <= epsilon && MathF.Abs(a.M24 - b.M24) <= epsilon
            && MathF.Abs(a.M31 - b.M31) <= epsilon && MathF.Abs(a.M32 - b.M32) <= epsilon
            && MathF.Abs(a.M33 - b.M33) <= epsilon && MathF.Abs(a.M34 - b.M34) <= epsilon
            && MathF.Abs(a.M41 - b.M41) <= epsilon && MathF.Abs(a.M42 - b.M42) <= epsilon
            && MathF.Abs(a.M43 - b.M43) <= epsilon && MathF.Abs(a.M44 - b.M44) <= epsilon;
    }

    private (PBRDeferredPreset Preset, RenderTexture Destination) CreateFrame()
    {
        PBRDeferredPreset preset = RenderPipelines.CreatePBRDeferred(
            _rendering, _lightingShader, _blitShader, shadowMapSize: 64, width: 64, height: 64);
        preset.Environment.Camera = _rendering.CreateCameraPerspective(0.83f, 1.0f, 0.1f, 100.0f);
        RenderTexture destination = CreateDestination();
        return (preset, destination);
    }

    [Test(Description = "Attach registers the node before the lighting node, wires its output and the environment flag; Detach undoes all three")]
    public void AttachWiresOutputAndRegistersBeforeLighting()
    {
        (PBRDeferredPreset preset, RenderTexture destination) = CreateFrame();
        using (preset)
        using (destination)
        using (RGNode_PointLightShadow node = CreateNode())
        {
            node.Attach(preset.Graph, preset.Lighting, preset.GBufferResource, preset.Environment);

            Assert.That(preset.Graph.Nodes, Does.Contain(node));
            int nodeIndex = IndexOfNode(preset.Graph.Nodes, node);
            int lightingIndex = IndexOfNode(preset.Graph.Nodes, preset.Lighting);
            Assert.That(nodeIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(lightingIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nodeIndex, Is.LessThan(lightingIndex),
                "the atlas must be rendered before the lighting pass consumes it");
            Assert.That(preset.Lighting.PointLightShadowInput, Is.Not.Null);
            Assert.That(preset.Environment.PointLightShadowsActive, Is.True);

            node.IsEnabled = false;
            Assert.That(preset.Environment.PointLightShadowsActive, Is.False,
                "IsEnabled=false flips the lighting shader back to the unshadowed loop");
            node.IsEnabled = true;
            Assert.That(preset.Environment.PointLightShadowsActive, Is.True);

            node.Detach();
            Assert.That(preset.Graph.Nodes, Does.Not.Contain(node));
            Assert.That(preset.Lighting.PointLightShadowInput, Is.Null);
            Assert.That(preset.Environment.PointLightShadowsActive, Is.False);
        }
    }

    [Test(Description = "One frame ranks the lights by importance, fills the top slots and uploads shadow-info + folded matrices")]
    public void ExecuteSelectsTopLightsAndWritesShadowInfo()
    {
        (PBRDeferredPreset preset, RenderTexture destination) = CreateFrame();
        using (preset)
        using (destination)
        using (RGNode_PointLightShadow node = CreateNode())
        {
            node.Attach(preset.Graph, preset.Lighting, preset.GBufferResource, preset.Environment);
            var lights = new[]
            {
                Light(0, intensity: 3.0f, range: 6.0f),
                Light(1, intensity: 1.0f, range: 5.0f),
                Light(2, intensity: 2.0f, range: 4.0f),
            };
            preset.Environment.UpdatePointLights(lights);
            preset.Pipeline.Render(destination.FrameBuffer);

            ReadOnlySpan<int> slots = node.SlotLightIndices;
            Assert.That(slots[0], Is.EqualTo(0), "highest score takes slot 0");
            Assert.That(slots[1], Is.EqualTo(2), "second-highest takes slot 1");
            Assert.That(slots[2], Is.EqualTo(1), "third light takes slot 2");
            Assert.That(slots.ToArray()[3..], Is.All.EqualTo(-1), "unused slots stay free");

            Span<Vector4> info = ((GraphicsArrayBuffer<Vector4>)node.ShadowInfoBuffer).AsSpan();
            Assert.That(info[0], Is.EqualTo(new Vector4(0.0f, 0.1f, 6.0f, 0.0f)), "slot, near, far of light 0");
            Assert.That(info[1], Is.EqualTo(new Vector4(2.0f, 0.1f, 5.0f, 0.0f)));
            Assert.That(info[2], Is.EqualTo(new Vector4(1.0f, 0.1f, 4.0f, 0.0f)));

            Span<Matrix4x4> matrices = ((GraphicsArrayBuffer<Matrix4x4>)node.MatrixBuffer).AsSpan();
            Matrix4x4 expectedFirstFace = PointLightShadowMath.FoldToAtlas(
                PointLightShadowMath.BuildFaceViewProjection(new Vector3(8.0f, 0.0f, 1.0f), 0.1f, 6.0f, 0),
                0, 0, node.FaceSize, 4, node.Atlas.Width, node.Atlas.Height);
            Assert.That(MatrixEquals(matrices[0], expectedFirstFace, 1e-4f), Is.True,
                "the uploaded slot-0/face-0 matrix must be the folded face view-projection");
            Matrix4x4 expectedSecondSlotFace = PointLightShadowMath.FoldToAtlas(
                PointLightShadowMath.BuildFaceViewProjection(new Vector3(8.0f, 0.0f, 1.0f), 0.1f, 4.0f, 5),
                1, 5, node.FaceSize, 4, node.Atlas.Width, node.Atlas.Height);
            Assert.That(MatrixEquals(matrices[PointLightShadowMath.FaceCount + 5], expectedSecondSlotFace, 1e-4f), Is.True,
                "slot 1 (light 2) face 5 must be folded with that light's near/far");
        }
    }

    [Test(Description = "Hysteresis: a slotted light inside the rank window keeps its slot beyond the cut-off; outside the window it is evicted and replaced")]
    public void HysteresisKeepsBorderlineSlotsUntilRankWindowExceeded()
    {
        (PBRDeferredPreset preset, RenderTexture destination) = CreateFrame();
        using (preset)
        using (destination)
        using (RGNode_PointLightShadow node = CreateNode())
        {
            node.Attach(preset.Graph, preset.Lighting, preset.GBufferResource, preset.Environment);

            // Frame 1: 22 candidates, intensities strictly descending — the top
            // 16 (lights 0..15) take slots 0..15.
            var lights = new PBRSceneEnvironment.PointLight[22];
            for (int i = 0; i < lights.Length; i++)
            {
                lights[i] = Light(i, intensity: 100.0f - i);
            }
            preset.Environment.UpdatePointLights(lights);
            preset.Pipeline.Render(destination.FrameBuffer);
            ReadOnlySpan<int> slots = node.SlotLightIndices;
            for (int slot = 0; slot < RGNode_PointLightShadow.MaxSlots; slot++)
            {
                Assert.That(slots[slot], Is.EqualTo(slot), $"frame 1 slot {slot}");
            }

            // Frame 2: light 15 drops to rank 17 — outside the top 16 but inside
            // the hysteresis window (top 20): it keeps its slot, and the
            // higher-ranked unslotted light 16 cannot steal one.
            lights[15] = Light(15, intensity: 780.0f);
            for (int i = 0; i <= 14; i++)
            {
                lights[i] = Light(i, intensity: 1000.0f - 10.0f * i);
            }
            lights[16] = Light(16, intensity: 800.0f);
            lights[17] = Light(17, intensity: 790.0f);
            preset.Environment.UpdatePointLights(lights);
            preset.Pipeline.Render(destination.FrameBuffer);
            slots = node.SlotLightIndices;
            Assert.That(slots[15], Is.EqualTo(15),
                "rank 17 is inside the hysteresis window: the slot is retained");
            Assert.That(slots.ToArray(), Does.Not.Contain(16),
                "no free slot exists: light 16 stays unshadowed despite rank 15");

            // Frame 3: light 15 falls outside the window entirely (rank 21) —
            // evicted, and light 16 takes the freed slot.
            lights[15] = Light(15, intensity: 1.0f);
            preset.Environment.UpdatePointLights(lights);
            preset.Pipeline.Render(destination.FrameBuffer);
            slots = node.SlotLightIndices;
            Assert.That(slots[15], Is.EqualTo(16), "the evicted slot passes to the next-ranked light");
            Assert.That(slots.ToArray(), Does.Not.Contain(15), "the evicted light is unshadowed");
        }
    }

    [Test(Description = "Static faces render once and re-render only on invalidation, light moves or dynamic casters")]
    public void StaticFacesRenderOnceAndOnlyReRenderOnInvalidation()
    {
        (PBRDeferredPreset preset, RenderTexture destination) = CreateFrame();
        using (preset)
        using (destination)
        using (RGNode_PointLightShadow node = CreateNode())
        using (PointLightShadowRenderer renderer = new(
            _rendering, _depthShader, node.AtlasLayout, node.MatrixBuffer))
        {
            node.Attach(preset.Graph, preset.Lighting, preset.GBufferResource, preset.Environment);
            var content = new FakeCasterContent();
            node.Content.Add(content);
            node.Content.Add(renderer);
            GraphicsMaterial casterMaterial = renderer.CreateShadowMaterial(name: "test_pls_caster");
            renderer.Add(new FakeShadowRenderable(_rendering.MeshFullScreen, casterMaterial, isStatic: true));

            var lights = new[] { Light(0, 3.0f), Light(1, 2.0f) };
            preset.Environment.UpdatePointLights(lights);

            preset.Pipeline.Render(destination.FrameBuffer);
            Assert.That(content.FacesDrawn.Count, Is.EqualTo(12),
                "two slotted lights x six faces on the first (dirty) frame");
            CollectionAssert.AreEqual(
                Enumerable.Range(0, 6).Concat(Enumerable.Range(6, 6)), content.FacesDrawn);

            preset.Pipeline.Render(destination.FrameBuffer);
            Assert.That(content.FacesDrawn.Count, Is.EqualTo(12),
                "a fully static frame re-renders nothing");

            node.MarkAtlasDirty();
            preset.Pipeline.Render(destination.FrameBuffer);
            Assert.That(content.FacesDrawn.Count, Is.EqualTo(24),
                "MarkAtlasDirty forces every occupied face back into the pass");

            // Moving a slotted light re-renders only its slot's six faces.
            lights[1] = Light(1, 2.0f, range: 5.0f);
            preset.Environment.UpdatePointLights(lights);
            preset.Pipeline.Render(destination.FrameBuffer);
            Assert.That(content.FacesDrawn.Count, Is.EqualTo(30));
            CollectionAssert.AreEqual(Enumerable.Range(6, 6), content.FacesDrawn.GetRange(24, 6),
                "only the moved light's slot re-renders");

            // A dynamic caster forces every occupied face each frame.
            renderer.Add(new FakeShadowRenderable(_rendering.MeshFullScreen, casterMaterial, isStatic: false));
            Assert.That(renderer.HasDynamicCasters, Is.True);
            preset.Pipeline.Render(destination.FrameBuffer);
            Assert.That(content.FacesDrawn.Count, Is.EqualTo(42));
            preset.Pipeline.Render(destination.FrameBuffer);
            Assert.That(content.FacesDrawn.Count, Is.EqualTo(54),
                "dynamic casters re-render all faces every frame");
        }
    }
}
