#include "Shaders/Libs/Core.hlsli"

// Forward-lit glass shader for the PBR deferred pipeline's transparency pass.
// Renders semi-transparent glass objects after deferred lighting, blending onto
// the lit HDR scene. Uses the same PBR functions as DeferredLighting (via
// PBRCommon.hlsl) but evaluates them per-fragment in forward, with:
// - Tangent-space normal mapping (same vertex layout as GBufferTangent.hlsl).
// - Hardware depth testing (DepthStencilState.Read) against the opaque scene —
//   the pipeline pre-fills the forward RT's depth from the G-buffer via a copy pass.
// - Fresnel-weighted sky reflection for grazing-angle reflectivity.
// - Alpha blending with AlphaBlendNoAccumulation (Max on alpha, no sorting).

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

DEFINE_UNIFORM(0, _data)
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
    float4 pbrParams;
    float4 cascadeSplits;
    float4 cascadeTexelSizes;
    float4 params2;
    float4 viewportSize;
    float4 params3;
    float4 params4;
};

DEFINE_TEX2D_SAMPLE(1, _albedoTexture);
DEFINE_TEX2D_SAMPLE(1, _normalTexture);
DEFINE_TEX2D_SAMPLE(1, _mrTexture);
DEFINE_TEX2D_DEPTH_SAMPLE(1, _shadowMap);

// Point light storage buffer element.
struct PointLightData
{
    float4 positionRange;
    float4 colorIntensity;
};

DEFINE_STORAGE(1, PointLightData, _pointLights);

// Shared PBR functions (BRDF, shadow sampling, sky, environment). Must come
// after all DEFINE_* declarations so the globals are visible to the functions.
#include "Shaders/Pipelines/Rendering/PBR/PBRCommon.hlsl"

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
    // Discard nearly-fully-opaque texels — they contribute nothing as glass.
    if (alpha < 0.01)
    {
        discard;
    }

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
    float NdotV = max(dot(N, V), 0.0);

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

    // Point lights (shared loop from PBRCommon.hlsl).
    Lo += EvaluatePointLights(N, V, worldPosition, albedo, metallic, roughness);

    // Ambient / environment lighting.
    float3 skyAmbient = EvaluateDiffuseSky(N);
    float upDot = saturate(N.z * 0.5 + 0.5);
    float3 skyBounce = float3(0.10, 0.12, 0.15);
    float3 groundBounce = float3(0.05, 0.045, 0.04);
    float3 ambientFloor = skyParams2.w * lerp(groundBounce, skyBounce, upDot);
    float3 diffuseIrradiance = skyAmbient + ambientFloor;
    float3 ambient = diffuseIrradiance * albedo * (1.0 - metallic) * ao;

    // Fresnel reflection: glass is more reflective at grazing angles.
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 fresnel = FresnelSchlick(F0, NdotV);
    float3 reflectDir = reflect(-V, N);
    float3 skyReflection = GetSkyColor(reflectDir) * fresnel;

    // Emissive.
    float3 emissive = constants.emissive.rgb;

    float3 color = Lo + ambient + skyReflection + emissive;

    // Output alpha: blend factor onto the lit scene.
    // Higher transmission → lower alpha → more of the background shows through.
    float outputAlpha = saturate(alpha * (1.0 - transmission));

    return float4(color, outputAlpha);
}
