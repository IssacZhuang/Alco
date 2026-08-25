// file "screen-space-reflection-trace.slang" - module "screen_space_reflection_trace"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/ScreenSpaceReflectionTrace.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module screen_space_reflection_trace;

import alco_world3d_ssr_post_common;
import alco_world3d_reversed_depth;
import alco_rendering_core;

// This pass runs after deferred lighting and forward transparency. Unlike the
// former GI-pass SSR, _sceneColor therefore contains the actual HDR shaded
// result (direct light, shadows, GI, emissive and transparent content).
// alco_world3d_ssr_post_common declares _ssrData; this pass's ParameterBlock follows
// it in declaration order.
public struct PassParams
{
    public Texture2D _sceneColor;
    public Texture2D _albedo;
    public Texture2D _normal;
    public Texture2D _mrAO;
    public DepthTexture2D _gbufferDepth;
    public Texture2D _blueNoise;
};

public ParameterBlock<PassParams> _pass;


// Runtime-baked Heitz blue-noise lookup (see ScreenSpaceReflectionBlueNoise.hlsl).
static const uint SSR_BLUE_NOISE_TILE = 128u;

static const float2 SSR_BLUR_OFFSETS[9] = {
    float2( 0.0,  0.0), float2( 0.8,  0.2), float2(-0.7,  0.5),
    float2( 0.3, -0.9), float2(-0.2, -0.7), float2( 1.0, -0.4),
    float2(-1.0, -0.2), float2( 0.5,  0.9), float2(-0.6,  0.9),
};

float3 SsrSampleShadedScene(
    float2 hitUV,
    float roughness,
    float rayDistance,
    float rotationSample)
{
    float blurPixels = roughness * roughness
        * lerp(1.0, 22.0, saturate(rayDistance / 45.0));
    if (blurPixels < 0.75)
    {
        return _pass._sceneColor.Sample(_samplers._linearClamp, hitUV).rgb;
    }

    float angle = rotationSample * TAU;
    float s, c;
    sincos(angle, s, c);
    float2x2 rotation = float2x2(c, -s, s, c);
    float2 inverseSize = rcp(_ssrData.ssrRenderSize.xy);
    float3 result = 0.0;
    float weightSum = 0.0;
    [unroll]
    for (int i = 0; i < 9; i++)
    {
        float2 offset = mul(rotation, SSR_BLUR_OFFSETS[i]) * blurPixels * inverseSize;
        float weight = i == 0 ? 1.5 : 1.0;
        result += _pass._sceneColor.Sample(_samplers._linearClamp, clamp(hitUV + offset, 0.0, 1.0)).rgb * weight;
        weightSum += weight;
    }
    return result / weightSum;
}

[shader("vertex")]
V2F MainVS(Vertex input)
{
    return SsrPostFullQuadVS(input);
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET
{
    float2 fullSize = _ssrData.ssrRenderSize.xy;
    int2 pixel = clamp(int2(input.uv * fullSize), int2(0, 0), int2(fullSize) - 1);
    float depth = GetPixelDepth2D(_pass._gbufferDepth, pixel);
    if (IS_SKY_DEPTH(depth))
    {
        return 0.0;
    }

    float4 packedAlbedo = _pass._albedo.Load(int3(pixel, 0));
    float roughness = packedAlbedo.a;
    if (roughness >= _ssrData.ssrRayParams.y)
    {
        return 0.0;
    }

    // input.uv is the canonical receiver location of this trace texel. Keeping
    // reconstruction on that grid makes a stationary reprojection land exactly
    // on the corresponding history texel instead of introducing a fixed
    // quarter-texel bilinear bias at half resolution.
    float3 worldPosition = SsrPostReconstructWorldPosition(input.uv, depth);
    float3 normal = normalize(_pass._normal.Load(int3(pixel, 0)).xyz * 2.0 - 1.0);
    float3 viewDirection = normalize(worldPosition - _ssrData.ssrCameraPosition.xyz);
    float viewDistance = length(worldPosition - _ssrData.ssrCameraPosition.xyz);
    float fresnel = saturate(1.0 + dot(normal, viewDirection));

    // Blue-noise sampling (Heitz's Owen-scrambled Sobol over an optimized
    // scrambling tile, baked once at startup): neighbouring trace texels draw
    // from uncorrelated sequences, so error energy concentrates in the high
    // frequencies where the half-res 2x2 footprint, the bilateral resolve and
    // temporal accumulation average it away. Each frame advances the
    // stochastic dimensions with a Cranley-Patterson rotation; the (R, G)
    // angle/radius pair uses the R2 low-discrepancy constants so every pixel's
    // 2D disk samples stay stratified over time. The blur rotation (A) always
    // reads the baked value and stays temporally stable.
    uint2 noisePixel = uint2(input.uv * _ssrData.ssrRenderSize.zw) % SSR_BLUE_NOISE_TILE;
    float4 blueNoise = _pass._blueNoise.Load(int3(int2(noisePixel), 0));
    uint noiseFrame = (uint)_ssrData.ssrParams.x % 256u;
    float angleSample = frac(blueNoise.r + noiseFrame * 0.7548776662);
    float radialSample = frac(blueNoise.g + noiseFrame * 0.5698402910);
    float stepSample = frac(blueNoise.b + noiseFrame * 0.6180339887);
    float blurRotationSample = blueNoise.a;
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    float noiseAngle = angleSample * TAU;
    // Sample a disk rather than a fixed-radius ring. The old ring pattern kept
    // jumping between equally distant rays and produced persistent bright dots.
    float noiseRadius = sqrt(radialSample) * roughness * roughness * 0.30;
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
    float hitDepth = 0.0;
    int refinementCount = 0;
    bool geometryHit = false;

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
        hitDepth = GetPixelDepth2D(_pass._gbufferDepth, hitPixel);

        // Empty depth is not a verified reflection hit. The shaded background
        // contains the procedural sun disc, but screen-space depth cannot tell
        // whether hidden geometry blocks it from the receiver. Keep marching
        // for screen-visible geometry and leave sky misses to the occlusion-aware
        // voxel specular fallback. Direct sun specular is already shadowed in the
        // deferred lighting pass.
        if (!IS_SKY_DEPTH(hitDepth))
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
        travelVector += vector * (0.95 + 0.1 * stepSample);
        if (length(travelVector) > _ssrData.ssrRayParams.x)
        {
            break;
        }
        rayPosition = start + travelVector;
    }

    float confidence = 0.0;
    float rayDistance = length(travelVector);
    if (geometryHit)
    {
        float hitViewDistance = length(hitWorldPosition - _ssrData.ssrCameraPosition.xyz);
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
    float roughnessFade = saturate((_ssrData.ssrRayParams.y - roughness) / 0.25);
    confidence *= roughnessFade;
    if (confidence <= 0.001)
    {
        return 0.0;
    }

    float3 reflectedColor = SsrSampleShadedScene(
        hitUV, roughness, rayDistance, blurRotationSample);
    return float4(max(reflectedColor, 0.0), confidence);
}
