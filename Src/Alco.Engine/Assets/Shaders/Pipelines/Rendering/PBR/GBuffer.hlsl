#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/GeometryNormal.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/PbrInstance.hlsli"

// G-buffer pass shader for PBR materials of the deferred pipeline.
// Writes albedo, world-space normal (from the normal map via TBN), material and
// emissive data. The interpolated mesh normal is octahedrally packed into two
// spare channels for stable diffuse GI. The vertex layout must match
// Alco.Rendering.VertexPBR exactly.
// All per-item data (model matrix, factors, alpha cutoff) lives in the
// _instances storage buffer and is fetched by SV_InstanceID; the pixel shader
// re-reads it through the instance id interpolant (SpriteInstanced pattern).

struct Vertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 tangent : TANGENT; // xyz = tangent, w = bitangent sign
    uint instanceId : SV_InstanceID;
};

struct V2F
{
    float4 position : SV_POSITION;
    float3 normal : TEXCOORD0;
    float2 uv : TEXCOORD1;
    float4 tangent : TEXCOORD2; // xyz = world tangent, w = bitangent sign
    uint instanceId : TEXCOORD3;
};

DEFINE_UNIFORM(0, _camera)
{
    float4x4 viewProjection;
};

DEFINE_TEX2D_SAMPLE(1, _albedoTexture);
DEFINE_TEX2D_SAMPLE(2, _normalTexture);
DEFINE_TEX2D_SAMPLE(3, _mrTexture);
DEFINE_TEX2D_SAMPLE(4, _emissiveTexture);

DEFINE_STORAGE(5, PbrInstance, _instances);

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    PbrInstance inst = _instances[input.instanceId];
    float4 worldPosition = mul(inst.model, float4(input.position, 1.0f));
    output.position = mul(viewProjection, worldPosition);
    // Rigid transform only (uniform scale); fine for the demo scene.
    output.normal = mul((float3x3)inst.model, input.normal);
    output.tangent = float4(mul((float3x3)inst.model, input.tangent.xyz), input.tangent.w);
    output.uv = input.uv;
    output.instanceId = input.instanceId;
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
    PbrInstance inst = _instances[input.instanceId];
    float4 albedo = SAMPLE_TEX2D(_albedoTexture, input.uv);

    // Alpha test: discard fragments below the cutoff (0 disables the test).
    float alphaCutoff = inst.params_.x;
    if (alphaCutoff > 0.0 && albedo.a * inst.baseColor.a < alphaCutoff)
    {
        discard;
    }

    float4 mrTex = SAMPLE_TEX2D(_mrTexture, input.uv);
    float roughness = inst.metallicRoughnessAO.y * mrTex.g;
    albedoRT = float4(EncodeSRGB(albedo.rgb * inst.baseColor.rgb), roughness);

    // TBN frame: re-orthogonalize the interpolated tangent against the normal.
    float3 n = normalize(input.normal);
    float3 t = input.tangent.xyz - n * dot(n, input.tangent.xyz);
    t = normalize(t);
    float3 b = cross(n, t) * input.tangent.w;

    // Two-channel tangent-space normal map (BC5); z is reconstructed.
    float2 normalXY = SAMPLE_TEX2D(_normalTexture, input.uv).rg * 2.0 - 1.0;
    float3 normalTex = float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    float3 worldNormal = normalize(t * normalTex.x + b * normalTex.y + n * normalTex.z);
    float2 geometryNormal = EncodeGeometryNormal(n);
    normalRT = float4(worldNormal * 0.5 + 0.5, geometryNormal.x);

    // glTF metallic-roughness texture: roughness in G, metallic in B, both
    // multiplied with their factors. AO stays factor-only.
    mrAORT = float4(
        inst.metallicRoughnessAO.x * mrTex.b,
        roughness,
        inst.metallicRoughnessAO.z,
        1.0);

    // Emissive texture (sRGB-decoded by the sampler) times the linear factor,
    // stored linear in the RGBA16Float target; no shading applied downstream.
    float3 emissive = SAMPLE_TEX2D(_emissiveTexture, input.uv).rgb * inst.emissive.rgb;
    emissiveRT = float4(emissive, geometryNormal.y);
}
