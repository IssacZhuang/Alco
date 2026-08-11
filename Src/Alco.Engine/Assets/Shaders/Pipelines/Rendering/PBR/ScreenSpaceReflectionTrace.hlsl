#include "Shaders/Pipelines/Rendering/PBR/ScreenSpaceReflectionPostCommon.hlsli"

// This pass runs after deferred lighting and forward transparency. Unlike the
// former GI-pass SSR, _sceneColor therefore contains the actual HDR shaded
// result (direct light, shadows, GI, emissive and transparent content).
DEFINE_TEX2D_SAMPLE(1, _sceneColor);
DEFINE_TEX2D_READ(1, _albedo);
DEFINE_TEX2D_READ(1, _normal);
DEFINE_TEX2D_READ(1, _mrAO);
DEFINE_TEX2D_DEPTH(1, _gbufferDepth);

static const float2 SSR_BLUR_OFFSETS[9] = {
    float2( 0.0,  0.0), float2( 0.8,  0.2), float2(-0.7,  0.5),
    float2( 0.3, -0.9), float2(-0.2, -0.7), float2( 1.0, -0.4),
    float2(-1.0, -0.2), float2( 0.5,  0.9), float2(-0.6,  0.9),
};

float3 SsrSampleShadedScene(float2 hitUV, float roughness, float rayDistance, float dither)
{
    float blurPixels = roughness * roughness
        * lerp(1.0, 22.0, saturate(rayDistance / 45.0));
    if (blurPixels < 0.75)
    {
        return SAMPLE_TEX2D(_sceneColor, hitUV).rgb;
    }

    float angle = dither * TAU;
    float s, c;
    sincos(angle, s, c);
    float2x2 rotation = float2x2(c, -s, s, c);
    float2 inverseSize = rcp(ssrRenderSize.xy);
    float3 result = 0.0;
    float weightSum = 0.0;
    [unroll]
    for (int i = 0; i < 9; i++)
    {
        float2 offset = mul(rotation, SSR_BLUR_OFFSETS[i]) * blurPixels * inverseSize;
        float weight = i == 0 ? 1.5 : 1.0;
        result += SAMPLE_TEX2D(_sceneColor, clamp(hitUV + offset, 0.0, 1.0)).rgb * weight;
        weightSum += weight;
    }
    return result / weightSum;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET
{
    float2 fullSize = ssrRenderSize.xy;
    int2 pixel = clamp(int2(input.uv * fullSize), int2(0, 0), int2(fullSize) - 1);
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, pixel);
    if (depth >= 0.9999)
    {
        return 0.0;
    }

    float4 packedAlbedo = GET_PIXEL_TEX2D(_albedo, pixel);
    float roughness = packedAlbedo.a;
    if (roughness >= ssrRayParams.y)
    {
        return 0.0;
    }

    float3 worldPosition = SsrPostReconstructWorldPosition(input.uv, depth);
    float3 normal = normalize(GET_PIXEL_TEX2D(_normal, pixel).xyz * 2.0 - 1.0);
    float3 viewDirection = normalize(worldPosition - ssrCameraPosition.xyz);
    float viewDistance = length(worldPosition - ssrCameraPosition.xyz);
    float fresnel = saturate(1.0 + dot(normal, viewDirection));

    // Complementary-style temporally changing rough normal. It turns one
    // deterministic mirror ray into a glossy lobe that the resolve pass can
    // accumulate without permanently banding on a fixed sample pattern.
    float dither = frac(SsrPostHash(float2(pixel))
        + ssrParams.x * 0.61803398875);
    float radialDither = frac(SsrPostHash(float2(pixel.yx) + 37.17)
        + ssrParams.x * 0.75487766625);
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    float noiseAngle = dither * TAU;
    // Sample a disk rather than a fixed-radius ring. The old ring pattern kept
    // jumping between equally distant rays and produced persistent bright dots.
    float noiseRadius = sqrt(radialDither) * roughness * roughness * 0.30;
    float3 rayNormal = normalize(normal
        + (cos(noiseAngle) * tangent + sin(noiseAngle) * bitangent) * noiseRadius);
    float3 rayDirection = normalize(reflect(viewDirection, rayNormal));

    float normalBias = 0.05 + viewDistance * 0.005 * (1.0 - fresnel);
    float3 start = worldPosition + normal * normalBias;
    float initialStep = clamp(viewDistance * 0.01, 0.12, 0.5);
    float3 vector = rayDirection * initialStep;
    float3 travelVector = vector;
    float3 rayPosition = start + travelVector;

    float2 hitUV = input.uv;
    float3 hitWorldPosition = worldPosition;
    float hitError = 1e20;
    float hitDepth = 1.0;
    int refinementCount = 0;
    bool geometryHit = false;
    bool backgroundHit = false;
    float2 backgroundUV = input.uv;

    // Port of Complementary Unbound's exponentially growing screen-space ray
    // with decimal refinement after approaching a depth surface.
    [loop]
    for (int i = 0; i < 38; i++)
    {
        float3 screenPosition;
        if (!SsrPostProjectWorldPosition(rayPosition, screenPosition))
        {
            break;
        }

        hitUV = screenPosition.xy;
        int2 hitPixel = clamp(int2(hitUV * fullSize), int2(0, 0), int2(fullSize) - 1);
        hitDepth = GET_PIXEL_TEX2D(_gbufferDepth, hitPixel);

        if (hitDepth >= 0.9999)
        {
            backgroundHit = true;
            backgroundUV = hitUV;
        }
        else
        {
            hitWorldPosition = SsrPostReconstructWorldPosition(hitUV, hitDepth);
            hitError = length(rayPosition - hitWorldPosition);
            if (hitError * 0.33333 < length(vector))
            {
                refinementCount++;
                if (refinementCount >= 8)
                {
                    geometryHit = true;
                    break;
                }
                travelVector -= vector;
                vector *= 0.1;
            }
        }

        vector *= 2.0;
        travelVector += vector * (0.95 + 0.1 * dither);
        if (length(travelVector) > ssrRayParams.x)
        {
            break;
        }
        rayPosition = start + travelVector;
    }

    float confidence = 0.0;
    float rayDistance = length(travelVector);
    if (geometryHit)
    {
        float hitViewDistance = length(hitWorldPosition - ssrCameraPosition.xyz);
        float2 selfDistance = abs(hitUV - input.uv) * fullSize;
        bool advanced = any(selfDistance > 2.0);
        bool plausibleThickness = hitError * (1.0 - fresnel)
            < 1.0 + hitViewDistance * 0.2;

        if (advanced && plausibleThickness)
        {
            float2 edgeDistance = abs(hitUV - 0.5) / float2(0.525, 0.525);
            float border = saturate(1.0 - pow(max(edgeDistance.x, edgeDistance.y), 50.0));
            float foregroundFade = saturate(hitViewDistance - viewDistance + 3.0);
            confidence = border * foregroundFade;
        }
    }
    else if (backgroundHit)
    {
        // Screen-visible sky/background is a valid screen-space source too.
        hitUV = backgroundUV;
        float2 edgeDistance = abs(hitUV - 0.5) / float2(0.525, 0.525);
        confidence = saturate(1.0 - pow(max(edgeDistance.x, edgeDistance.y), 50.0)) * 0.75;
    }

    float roughnessFade = saturate((ssrRayParams.y - roughness) / 0.25);
    confidence *= roughnessFade;
    if (confidence <= 0.001)
    {
        return 0.0;
    }

    float3 reflectedColor = SsrSampleShadedScene(hitUV, roughness, rayDistance, dither);
    return float4(max(reflectedColor, 0.0), confidence);
}
