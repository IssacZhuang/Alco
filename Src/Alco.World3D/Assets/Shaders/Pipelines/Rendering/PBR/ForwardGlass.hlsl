#include "Shaders/Libs/Core.hlsli"

// Forward-lit glass shader for the PBR deferred pipeline's transparency pass.
// Renders semi-transparent glass objects after deferred lighting, blending onto
// the lit HDR scene. Uses the same PBR functions as DeferredLighting (via
// PBRCommon.hlsli) but evaluates them per-fragment in forward, with:
// - Tangent-space normal mapping (same vertex layout as GBuffer.hlsl).
// - Hardware depth testing (DepthStencilState.Read) against the opaque scene —
//   the pipeline pre-fills the forward RT's depth from the G-buffer via a copy pass.
// - Alpha blending with AlphaBlendNoAccumulation (Max on alpha, no sorting).
//   Opacity is driven by the transmission factor (texture alpha only raises
//   it further), so glass stays visible even when the albedo texture's alpha
//   channel is zero.

struct Vertex
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 tangent : TANGENT;
};

struct V2F
{
    float4 position : SV_POSITION;
    float3 normal : TEXCOORD0;
    float2 uv : TEXCOORD1;
    float4 tangent : TEXCOORD2;  // xyz = world tangent, w = bitangent sign
    float3 worldPosition : TEXCOORD3;
};

struct Constants
{
    float4x4 model;
    float4 baseColor;
    float4 metallicRoughnessAO; // x=metallic y=roughness z=ambientOcclusion
    float4 params_;             // x=transmissionFactor (0=opaque, 1=fully transparent)
    float4 emissive;            // rgb = emissive factor
};

DEFINE_UNIFORM(0, _camera)
{
    float4x4 viewProjection;
};

// Pass-specific textures (set 1). The shared _data cbuffer, _pointLights
// buffer and _shadowMap texture live in PBRCommon.hlsli.
DEFINE_TEX2D_SAMPLE(1, _albedoTexture);
DEFINE_TEX2D_SAMPLE(1, _normalTexture);
DEFINE_TEX2D_SAMPLE(1, _mrTexture);

#include "Shaders/Pipelines/Rendering/PBR/PBRCommon.hlsli"

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    float4 worldPosition = mul(constants.model, float4(input.position, 1.0f));
    output.position = mul(viewProjection, worldPosition);
    output.worldPosition = worldPosition.xyz;
    output.normal = mul((float3x3)constants.model, input.normal);
    output.tangent = float4(mul((float3x3)constants.model, input.tangent.xyz), input.tangent.w);
    output.uv = input.uv;
    return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET
{
    float4 albedoTex = SAMPLE_TEX2D(_albedoTexture, input.uv);
    float3 albedo = DecodeSRGB(albedoTex.rgb) * constants.baseColor.rgb;
    float alpha = albedoTex.a * constants.baseColor.a;

    float transmission = constants.params_.x;

    float4 mrTex = SAMPLE_TEX2D(_mrTexture, input.uv);
    float metallic = constants.metallicRoughnessAO.x * mrTex.b;
    float roughness = constants.metallicRoughnessAO.y * mrTex.g;
    float ao = constants.metallicRoughnessAO.z;

    // TBN frame: re-orthogonalize the interpolated tangent against the normal.
    float3 n = normalize(input.normal);
    float3 t = input.tangent.xyz - n * dot(n, input.tangent.xyz);
    t = normalize(t);
    float3 b = cross(n, t) * input.tangent.w;

    float2 normalXY = SAMPLE_TEX2D(_normalTexture, input.uv).rg * 2.0 - 1.0;
    float3 normalTex = float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    float3 N = normalize(t * normalTex.x + b * normalTex.y + n * normalTex.z);

    float3 worldPosition = input.worldPosition;
    float3 V = normalize(cameraPosition.xyz - worldPosition);

    float3 Lo = 0.0;

    // Directional sun light with cascaded shadows.
    {
        float3 L = normalize(-sunDirection.xyz);
        float sunNdotL = dot(N, L);
        float sunShadow = 1.0;
        float viewDistance = length(worldPosition - cameraPosition.xyz);
        int cascade = SelectCascade(viewDistance);
        if (pbrParams.x > 0.5 && sunNdotL > 0.0)
        {
            sunShadow = SampleSunShadow(worldPosition, N, L, input.position.xy, viewDistance, cascade);
        }

        Lo += EvaluatePBR(N, V, L, albedo, metallic, roughness)
            * sunColorAndIntensity.rgb
            * sunColorAndIntensity.w
            * sunShadow;
    }

    // Point lights (shared loop from PBRCommon.hlsli).
    Lo += EvaluatePointLights(N, V, worldPosition, albedo, metallic, roughness);

    // Ambient / environment lighting.
    float3 skyAmbient = EvaluateDiffuseSky(N);
    float upDot = saturate(N.z * 0.5 + 0.5);
    float3 skyBounce = float3(0.10, 0.12, 0.15);
    float3 groundBounce = float3(0.05, 0.045, 0.04);
    float3 ambientFloor = skyParams2.w * lerp(groundBounce, skyBounce, upDot);
    float3 diffuseIrradiance = skyAmbient + ambientFloor;
    float3 ambient = diffuseIrradiance * albedo * (1.0 - metallic) * ao;

    // Emissive.
    float3 emissive = constants.emissive.rgb;

    float3 color = Lo + ambient + emissive;

    // Output alpha: blend factor onto the lit scene. Opacity comes from the
    // transmission factor and the texture alpha (whichever is larger — the
    // Bistro glass textures carry a zero alpha channel, so transmission must
    // not rely on it).
    float outputAlpha = saturate(max(alpha, 1.0 - transmission));

    return float4(color, outputAlpha);
}
