#include "Shaders/Libs/Core.hlsli"

// Deferred lighting pass shader for the PBR pipeline.
// Samples the G-buffer, evaluates a GGX PBR BRDF with a directional sun
// (shadow mapped, manual PCF), up to four point lights, a simple sky
// ambient term and a procedural gradient skybox for empty pixels.

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

DEFINE_UNIFORM(0, _data)
{
    float4x4 invViewProjection;
    float4x4 sunViewProjection;
    float4 cameraPosition;
    float4 sunDirection;         // normalized direction the sun light travels
    float4 sunColorAndIntensity; // rgb + intensity
    float4 skyTopColor;
    float4 skyBottomColor;
    float4 pointLight0Position;
    float4 pointLight0Color;     // rgb + intensity
    float4 pointLight1Position;
    float4 pointLight1Color;     // rgb + intensity
    float4 pointLight2Position;
    float4 pointLight2Color;     // rgb + intensity
    float4 pointLight3Position;
    float4 pointLight3Color;     // rgb + intensity
    float4 pbrParams;               // x=shadowEnabled y=pointLightEnabled z=shadowMapSize w=sunDiscEnabled
    float4 viewportSize;         // xy = render target size in pixels
};

DEFINE_TEX2D_SAMPLE(1, _albedo);
DEFINE_TEX2D_SAMPLE(2, _normal);
DEFINE_TEX2D_SAMPLE(3, _mrAO);
SLOT(4, 0) Texture2D<float> _gbufferDepth;
SLOT(5, 0) Texture2D<float> _shadowMap;

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

float3 ReconstructWorldPosition(V2F input)
{
    float2 ndc = float2(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0);
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, int2(input.uv * viewportSize.xy));
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

float DistributionGGX(float NdotH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d + 1e-6);
}

float GeometrySchlickGGX(float NdotX, float roughness)
{
    float r = roughness + 1.0;
    float k = r * r / 8.0;
    return NdotX / (NdotX * (1.0 - k) + k + 1e-6);
}

float3 FresnelSchlick(float3 F0, float VdotH)
{
    return F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);
}

// Returns (diffuse + specular) * NdotL for one light.
float3 EvaluatePBR(float3 N, float3 V, float3 L, float3 albedo, float metallic, float roughness)
{
    float3 H = normalize(V + L);
    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float VdotH = max(dot(V, H), 0.0);

    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 F = FresnelSchlick(F0, VdotH);
    float D = DistributionGGX(NdotH, roughness);
    float G = GeometrySchlickGGX(NdotL, roughness) * GeometrySchlickGGX(NdotV, roughness);

    float3 specular = D * G * F / (4.0 * NdotL * NdotV + 1e-6);
    float3 diffuse = (1.0 - F) * (1.0 - metallic) * albedo / PI;

    return (diffuse + specular) * NdotL;
}

// Manual 3x3 PCF against the shadow map depth texture.
float SampleShadowMap(float3 worldPosition)
{
    float4 clip = mul(sunViewProjection, float4(worldPosition, 1.0));
    float3 ndc = clip.xyz / clip.w;

    float2 shadowUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
    if (shadowUV.x < 0.0 || shadowUV.x > 1.0 || shadowUV.y < 0.0 || shadowUV.y > 1.0)
    {
        return 1.0;
    }

    float2 texel = shadowUV * pbrParams.z;
    float bias = 0.002;
    float shadow = 0.0;
    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            float sampleDepth = GET_PIXEL_TEX2D(_shadowMap, int2(texel) + int2(dx, dy));
            shadow += (ndc.z - bias) <= sampleDepth ? 1.0 : 0.0;
        }
    }
    return shadow / 9.0;
}

// Procedural gradient sky with a sun disc.
float3 GetSkyColor(float3 direction)
{
    float t = pow(saturate(direction.z * 0.5 + 0.5), 0.6);
    float3 sky = lerp(skyBottomColor.rgb, skyTopColor.rgb, t);

    if (pbrParams.w > 0.5)
    {
        float3 sunDiscDirection = normalize(-sunDirection.xyz);
        float sunDot = saturate(dot(normalize(direction), sunDiscDirection));
        float sunDisc = smoothstep(0.9995, 0.9999, sunDot);
        sky += sunColorAndIntensity.rgb * sunColorAndIntensity.w * sunDisc * 4.0;
    }
    return sky;
}

// sRGB to linear RGB decoding (the albedo target is RGBA8Unorm, manually encoded).
float3 DecodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow(max((color + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET
{
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, int2(input.uv * viewportSize.xy));

    float3 worldPosition = ReconstructWorldPosition(input);
    float3 viewDirection = normalize(worldPosition - cameraPosition.xyz);

    if (depth >= 0.9999)
    {
        return float4(GetSkyColor(viewDirection), 1.0);
    }

    float3 albedo = DecodeSRGB(SAMPLE_TEX2D(_albedo, input.uv).rgb);
    float3 normalRT = SAMPLE_TEX2D(_normal, input.uv).xyz;
    float4 mrAO = SAMPLE_TEX2D(_mrAO, input.uv);

    float3 N = normalize(normalRT * 2.0 - 1.0);
    float metallic = mrAO.x;
    float roughness = mrAO.y;
    float ao = mrAO.z;
    float3 V = -viewDirection; // surface to camera

    float3 Lo = 0.0;

    // Directional sun light.
    {
        float3 L = normalize(-sunDirection.xyz);
        float shadow = pbrParams.x > 0.5 ? SampleShadowMap(worldPosition) : 1.0;
        Lo += EvaluatePBR(N, V, L, albedo, metallic, roughness)
            * sunColorAndIntensity.rgb
            * sunColorAndIntensity.w
            * shadow;
    }

    // Point lights.
    if (pbrParams.y > 0.5)
    {
        float4 pointLightPositions[4] = {
            pointLight0Position, pointLight1Position,
            pointLight2Position, pointLight3Position };
        float4 pointLightColors[4] = {
            pointLight0Color, pointLight1Color,
            pointLight2Color, pointLight3Color };

        for (int i = 0; i < 4; i++)
        {
            float3 lightColor = pointLightColors[i].rgb;
            float lightIntensity = pointLightColors[i].w;
            if (lightIntensity <= 0.0)
            {
                continue;
            }

            float3 toLight = pointLightPositions[i].xyz - worldPosition;
            float distanceSqr = dot(toLight, toLight);
            float attenuation = 1.0 / (distanceSqr + 1.0);

            float3 L = normalize(toLight);
            Lo += EvaluatePBR(N, V, L, albedo, metallic, roughness)
                * lightColor
                * lightIntensity
                * attenuation;
        }
    }

    // Simple sky ambient term (diffuse only).
    float3 skyDirection = lerp(skyBottomColor.rgb, skyTopColor.rgb, saturate(N.z * 0.5 + 0.5));
    float3 ambient = skyDirection * albedo * ao * (1.0 - metallic);

    return float4(Lo + ambient, 1.0);
}
