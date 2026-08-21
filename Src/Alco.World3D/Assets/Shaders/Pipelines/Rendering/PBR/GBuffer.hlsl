#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/GeometryNormal.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/PbrInstance.hlsli"
#include "Shaders/Libs/Surface.hlsli"
#include "Shaders/Materials/PbrStandard.hlsli" // @SURFACE@ default; the material composer swaps this line for a custom surface.

// G-buffer pass template for surface materials of the deferred pipeline.
// Writes albedo, world-space normal (from the surface's tangent-space normal
// via TBN), material and emissive data. The interpolated mesh normal is
// octahedrally packed into two spare channels for stable diffuse GI. All
// material evaluation lives in the surface shader included above (contract:
// Shaders/Libs/Surface.hlsli); this template owns the entry points, the
// render-target writes and the pass-mandated bindings. The vertex layout must
// match Alco.Rendering.VertexPBR exactly.
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
    float3 worldPos : TEXCOORD4;
};

DEFINE_UNIFORM(0, _camera)
{
    float4x4 viewProjection;
};

DEFINE_STORAGE(1, PbrInstance, _instances);

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    PbrInstance inst = _instances[input.instanceId];
    // Rigid transform only (uniform scale); fine for the demo scene.
    float3 worldPos = mul(inst.model, float4(input.position, 1.0f)).xyz;
    float3 worldNormal = mul((float3x3)inst.model, input.normal);
    float3 worldTangent = mul((float3x3)inst.model, input.tangent.xyz);
    // The surface may deform the vertex; every pass applies this identically
    // so G-buffer, shadows and GI stay consistent.
    ModifyVertex(worldPos, worldNormal, input.uv);
    output.position = mul(viewProjection, float4(worldPos, 1.0f));
    output.normal = worldNormal;
    output.tangent = float4(worldTangent, input.tangent.w);
    output.uv = input.uv;
    output.instanceId = input.instanceId;
    output.worldPos = worldPos;
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

    // TBN frame: re-orthogonalize the interpolated tangent against the normal.
    float3 n = normalize(input.normal);
    float3 t = input.tangent.xyz - n * dot(n, input.tangent.xyz);
    t = normalize(t);
    float3 b = cross(n, t) * input.tangent.w;

    SurfaceInput surfaceInput;
    surfaceInput.worldPos = input.worldPos;
    surfaceInput.normalWS = n;
    surfaceInput.tangentWS = float4(t, input.tangent.w);
    surfaceInput.uv = input.uv;
    surfaceInput.baseColorFactor = inst.baseColor;
    surfaceInput.metallicRoughnessAO = inst.metallicRoughnessAO;
    surfaceInput.emissiveFactor = inst.emissive;
    surfaceInput.alphaCutoff = inst.params_.x;

    SurfaceOutput s = EvaluateSurface(surfaceInput);

    // Alpha test: discard fragments below the cutoff (0 disables the test).
    if (surfaceInput.alphaCutoff > 0.0 && s.alpha < surfaceInput.alphaCutoff)
    {
        discard;
    }

    albedoRT = float4(EncodeSRGB(s.albedo), s.roughness);

    float3 worldNormal = normalize(t * s.normalTS.x + b * s.normalTS.y + n * s.normalTS.z);
    float2 geometryNormal = EncodeGeometryNormal(n);
    normalRT = float4(worldNormal * 0.5 + 0.5, geometryNormal.x);

    mrAORT = float4(s.metallic, s.roughness, s.ao, 1.0);

    // Stored linear in the RGBA16Float target; no shading applied downstream.
    emissiveRT = float4(s.emissive, geometryNormal.y);
}
