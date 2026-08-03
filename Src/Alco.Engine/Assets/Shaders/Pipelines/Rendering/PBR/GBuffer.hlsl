#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/GeometryNormal.hlsli"

// G-buffer pass shader for the deferred PBR pipeline.
// Writes albedo, world-space normal, metallic/roughness/ambient-occlusion and
// emissive data. The geometric normal is octahedrally packed into two spare
// channels for stable diffuse GI. The vertex layout must match
// Alco.Rendering.VertexPositionNormalTexture exactly.

struct Vertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};

struct V2F
{
    float4 position : SV_POSITION;
    float3 normal : TEXCOORD0;
    float2 uv : TEXCOORD1;
};

struct Constants
{
    float4x4 model;
    float4 baseColor;
    float4 metallicRoughnessAO; // x=metallic y=roughness z=ambientOcclusion
    float4 params_;             // x=alphaCutoff (0 disables alpha testing)
    float4 emissive;            // rgb = emissive factor
};

DEFINE_UNIFORM(0, _camera)
{
    float4x4 viewProjection;
};

DEFINE_TEX2D_SAMPLE(1, _albedoTexture);

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    float4 worldPosition = mul(constants.model, float4(input.position, 1.0f));
    output.position = mul(viewProjection, worldPosition);
    // Rigid transform only (uniform scale); fine for the demo scene.
    output.normal = mul((float3x3)constants.model, input.normal);
    output.uv = input.uv;
    return output;
}

// Linear RGB to sRGB encoding (the albedo target is RGBA8Unorm).
float3 EncodeSRGB(float3 color)
{
    float3 lo = color * 12.92;
    float3 hi = 1.055 * pow(max(color, 0.0), 1.0 / 2.4) - 0.055;
    return lerp(hi, lo, step(color, float3(0.0031308, 0.0031308, 0.0031308)));
}

[shader("pixel")]
void MainPS(V2F input,
    out float4 albedoRT : SV_TARGET0,
    out float4 normalRT : SV_TARGET1,
    out float4 mrAORT : SV_TARGET2,
    out float4 emissiveRT : SV_TARGET3)
{
    float4 albedo = SAMPLE_TEX2D(_albedoTexture, input.uv);

    // Alpha test: discard fragments below the cutoff (0 disables the test).
    float alphaCutoff = constants.params_.x;
    if (alphaCutoff > 0.0 && albedo.a * constants.baseColor.a < alphaCutoff)
    {
        discard;
    }

    float3 normal = normalize(input.normal);
    float2 geometryNormal = EncodeGeometryNormal(normal);
    // The resolved albedo alpha is unused after alpha testing; preserve the
    // 8-bit material roughness there so voxel tracing can stay within the
    // backend's eight-bind-group limit while reading the half-float geometry
    // normal from the emissive target.
    albedoRT = float4(
        EncodeSRGB(albedo.rgb * constants.baseColor.rgb),
        constants.metallicRoughnessAO.y);
    normalRT = float4(normal * 0.5 + 0.5, geometryNormal.x);

    mrAORT = float4(
        constants.metallicRoughnessAO.x,
        constants.metallicRoughnessAO.y,
        constants.metallicRoughnessAO.z,
        1.0);

    // Linear emissive (RGBA16Float target), no shading applied downstream.
    emissiveRT = float4(constants.emissive.rgb, geometryNormal.y);
}
