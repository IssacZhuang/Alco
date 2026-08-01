#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Voxel cone tracing for the voxel GI clipmap: one dispatch at the half-res
// trace resolution. Reconstructs the world position and normal from the
// G-buffer, gathers diffuse irradiance from camera-relative DDGI cascades and
// traces one rough specular cone through the radiance clipmap. The result is
// written into the output atlas
// (twice the trace width: diffuse in the left half, specular in the right).
// Cones that leave every clipmap region fall back to the sky gradient.

DEFINE_TEX3D_SAMPLE(1, _radiance);
DEFINE_TEX2D_DEPTH(2, _gbufferDepth);
DEFINE_TEX2D_READ(3, _normal);
DEFINE_TEX2D_READ(4, _mrAO);
DEFINE_TEX2D_STORAGE(5, _indirectGI, float4, "rgba16f");
DEFINE_TEX3D_SAMPLE(6, _ddgiIrradiance);
DEFINE_TEX2D_READ(7, _albedo);

static const uint DDGI_COEFFICIENT_COUNT = 4u;

float4 DdgiOrigin(int cascade)
{
    return ddgiOrigins[cascade];
}

float4 SampleDdgiCoefficient(float3 localProbe, int cascade, uint coefficient)
{
    float3 probeResolution = ddgiParams.xyz;
    float totalDepth = probeResolution.z * DDGI_COEFFICIENT_COUNT * ddgiParams2.w;
    float slabOffset = probeResolution.z * (coefficient + DDGI_COEFFICIENT_COUNT * cascade);
    float3 uvw = float3(
        (localProbe.x + 0.5) / probeResolution.x,
        (localProbe.y + 0.5) / probeResolution.y,
        (slabOffset + localProbe.z + 0.5) / totalDepth);
    return SAMPLE_TEX3D_LEVEL(_ddgiIrradiance, uvw, 0.0);
}

float3 GatherDdgiDiffuse(float3 worldPosition, float3 normal)
{
    int selectedCascade = -1;
    float3 localProbe = 0.0;
    for (int cascade = 0; cascade < (int)ddgiParams2.w; cascade++)
    {
        float4 origin = DdgiOrigin(cascade);
        float3 candidate = (worldPosition - origin.xyz) / origin.w;
        if (all(candidate >= 0.0) && all(candidate <= ddgiParams.xyz - 1.0))
        {
            selectedCascade = cascade;
            localProbe = candidate;
            break;
        }
    }

    if (selectedCascade < 0)
    {
        return VoxelSkyColor(normal);
    }

    float4 coefficient0 = SampleDdgiCoefficient(localProbe, selectedCascade, 0u);
    float4 coefficient1 = SampleDdgiCoefficient(localProbe, selectedCascade, 1u);
    float4 coefficient2 = SampleDdgiCoefficient(localProbe, selectedCascade, 2u);
    float4 coefficient3 = SampleDdgiCoefficient(localProbe, selectedCascade, 3u);
    if (coefficient2.a < 0.05)
    {
        return VoxelSkyColor(normal);
    }

    // Cosine-convolved first-order SH, divided by PI because deferred lighting
    // applies albedo directly to this irradiance-like radiance value.
    float3 diffuse = coefficient0.rgb * 0.886227
        + coefficient1.rgb * (1.023327 * normal.y)
        + coefficient2.rgb * (1.023327 * normal.z)
        + coefficient3.rgb * (1.023327 * normal.x);

    // Omnidirectional first/second hit-distance moments provide a conservative
    // leak guard. The screen-space near-field term refines local contact later.
    float spacing = DdgiOrigin(selectedCascade).w;
    float3 fractionalProbe = frac(localProbe);
    float probeDistance = length(min(fractionalProbe, 1.0 - fractionalProbe) * spacing);
    float meanDistance = coefficient0.a;
    float variance = max(coefficient1.a - meanDistance * meanDistance, spacing * spacing * 0.01);
    float delta = max(probeDistance - meanDistance, 0.0);
    float visibility = delta > 0.0 ? variance / (variance + delta * delta) : 1.0;
    return max(diffuse * (visibility / PI), 0.0);
}

float3 DecodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow((color + 0.055) / 1.055, 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

float3 ReconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

float3 ApproximateScreenSurfaceRadiance(int2 pixel, float3 normal)
{
    float3 albedo = DecodeSRGB(GET_PIXEL_TEX2D(_albedo, pixel).rgb);
    float3 sky = VoxelSkyColor(normal);
    float3 L = normalize(-sunDirection.xyz);
    float sunAmount = max(dot(normal, L), 0.0) / PI;
    float3 sun = sunColorAndIntensity.rgb * sunColorAndIntensity.w * sunAmount;
    return albedo * (sky + sun);
}

// A compact screen-space near-field gather. Coplanar samples reject naturally;
// nearby facing surfaces contribute colored contact bounce while DDGI supplies
// stable off-screen and medium/far-field lighting.
float4 GatherScreenSpaceNearField(
    int2 centerPixel,
    float2 centerUV,
    float3 worldPosition,
    float3 normal)
{
    uint2 resolution = uint2(giParams2.y, giParams2.z);
    float rotation = frac(sin(dot(float2(centerPixel), float2(12.9898, 78.233))) * 43758.5453) * TAU;
    float3 gathered = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for (int ray = 0; ray < 4; ray++)
    {
        float angle = rotation + ray * (TAU / 4.0);
        float2 direction = float2(cos(angle), sin(angle));
        [unroll]
        for (int step = 1; step <= 6; step++)
        {
            float radiusPixels = 2.0 + step * step * 1.25;
            float2 sampleUV = centerUV + direction * radiusPixels / float2(resolution);
            if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0))
            {
                break;
            }

            int2 samplePixel = clamp((int2)(sampleUV * float2(resolution)), 0, (int2)resolution - 1);
            float sampleDepth = GET_PIXEL_TEX2D(_gbufferDepth, samplePixel);
            if (sampleDepth >= 0.9999)
            {
                continue;
            }
            float3 samplePosition = ReconstructWorldPosition(sampleUV, sampleDepth);
            float3 toSample = samplePosition - worldPosition;
            float distance_ = length(toSample);
            if (distance_ < levelOrigins[0].w * 2.0 || distance_ > ddgiOrigins[0].w * 4.0)
            {
                continue;
            }

            float3 directionToSample = toSample / distance_;
            float receiverFacing = max(dot(normal, directionToSample), 0.0);
            float3 sampleNormal = normalize(GET_PIXEL_TEX2D(_normal, samplePixel).xyz * 2.0 - 1.0);
            float sourceFacing = max(dot(sampleNormal, -directionToSample), 0.0);
            float weight = receiverFacing * sourceFacing / (1.0 + distance_ * distance_);
            if (weight < 0.01)
            {
                continue;
            }

            gathered += ApproximateScreenSurfaceRadiance(samplePixel, sampleNormal) * weight;
            totalWeight += weight;
            break;
        }
    }

    if (totalWeight <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    return float4(gathered / totalWeight, saturate(totalWeight * 0.75));
}

// Low-roughness screen-space reflection. The voxel cone remains the fallback
// for rough, hidden and off-screen reflection paths.
float4 TraceScreenSpaceReflection(
    float3 startPosition,
    float3 direction,
    float roughness)
{
    uint2 resolution = uint2(giParams2.y, giParams2.z);
    float maximumDistance = min(giParams.y, ddgiOrigins[1].w * 8.0);
    float previousDifference = -1.0;
    [loop]
    for (int step = 1; step <= 24; step++)
    {
        float progress = step / 24.0;
        float distance_ = maximumDistance * progress * progress;
        float3 rayPosition = startPosition + direction * distance_;
        float4 clip = mul(viewProjection, float4(rayPosition, 1.0));
        if (clip.w <= 0.0)
        {
            break;
        }
        float3 ndc = clip.xyz / clip.w;
        float2 sampleUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
        if (any(sampleUV <= 0.0) || any(sampleUV >= 1.0) || ndc.z <= 0.0 || ndc.z >= 1.0)
        {
            break;
        }

        int2 samplePixel = clamp((int2)(sampleUV * float2(resolution)), 0, (int2)resolution - 1);
        float sceneDepth = GET_PIXEL_TEX2D(_gbufferDepth, samplePixel);
        float difference = ndc.z - sceneDepth;
        if (sceneDepth < 0.9999 && difference >= 0.0 && previousDifference < 0.0)
        {
            float3 scenePosition = ReconstructWorldPosition(sampleUV, sceneDepth);
            float thickness = levelOrigins[0].w * 4.0 + distance_ * 0.025;
            if (length(scenePosition - rayPosition) <= thickness)
            {
                float3 sampleNormal = normalize(GET_PIXEL_TEX2D(_normal, samplePixel).xyz * 2.0 - 1.0);
                float edge = saturate(min(min(sampleUV.x, sampleUV.y), min(1.0 - sampleUV.x, 1.0 - sampleUV.y)) * 12.0);
                float confidence = edge * (1.0 - roughness);
                return float4(ApproximateScreenSurfaceRadiance(samplePixel, sampleNormal), confidence);
            }
        }
        previousDifference = difference;
    }
    return float4(0.0, 0.0, 0.0, 0.0);
}

// Hardware trilinear sample of the radiance volume at a (fractional) mip;
// rgb = radiance, a = occupancy. All levels share the one Texture3D, stacked
// along the w axis.
float4 SampleRadiance(float3 position, int level, float mip)
{
    return SAMPLE_TEX3D_LEVEL(_radiance, VoxelWorldToUVW(position, level, mip), mip);
}

// March one cone through the clipmap, accumulating radiance front-to-back.
// Returns rgb = gathered radiance (with sky fallback), a = accumulated occlusion.
float4 TraceCone(float3 startPosition, float3 direction, float apertureTan, float maxDistance)
{
    float mipCount = clipmapParams.z;
    float fineVoxelSize = levelOrigins[0].w;
    float3 color = 0.0;
    float alpha = 0.0;
    float t = fineVoxelSize;

    for (int step = 0; step < 24 && t <= maxDistance && alpha < 0.98; step++)
    {
        float3 position = startPosition + direction * t;
        int level = VoxelFindLevel(position);
        if (level < 0)
        {
            break;
        }

        float voxelSize = levelOrigins[level].w;
        float diameter = max(2.0 * t * apertureTan, voxelSize);
        // Fractional mip: the sampler blends the neighboring mip levels.
        float mip = clamp(log2(diameter / voxelSize), 0.0, mipCount - 1.0);
        float4 sample_ = SampleRadiance(position, level, mip);

        color += (1.0 - alpha) * sample_.a * sample_.rgb;
        alpha += (1.0 - alpha) * sample_.a;
        t += max(voxelSize, diameter * 0.5);
    }

    // Sky fallback for whatever the cones did not occlude.
    color += (1.0 - alpha) * VoxelSkyColor(direction);
    return float4(color, alpha);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 tracePixel = dispatchId.xy;
    uint2 traceResolution = uint2(giParams.z, giParams.w);
    if (any(tracePixel >= traceResolution))
    {
        return;
    }

    float2 uv = (float2(tracePixel) + 0.5) / float2(traceResolution);
    uint2 gbufferResolution = uint2(giParams2.y, giParams2.z);
    int2 gbufferPixel = int2(uv * float2(gbufferResolution));
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, gbufferPixel);
    if (depth >= 0.9999)
    {
        _indirectGI[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
        _indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float3 worldPosition = ReconstructWorldPosition(uv, depth);
    float3 N = normalize(GET_PIXEL_TEX2D(_normal, gbufferPixel).xyz * 2.0 - 1.0);
    float roughness = GET_PIXEL_TEX2D(_mrAO, gbufferPixel).y;
    float3 V = normalize(cameraPosition.xyz - worldPosition);
    float maxDistance = giParams.y;

    // Start half a fine voxel above the surface to avoid immediate self-hits.
    float fineVoxelSize = levelOrigins[0].w;
    float3 startPosition = worldPosition + N * fineVoxelSize * 1.5;

    // Diffuse: temporally updated cascaded irradiance probes.
    float3 diffuse = GatherDdgiDiffuse(startPosition, N);
    float4 nearField = GatherScreenSpaceNearField(gbufferPixel, uv, worldPosition, N);
    diffuse = lerp(diffuse, nearField.rgb, nearField.a * 0.35);

    // Specular: one cone along the reflection direction, aperture from roughness.
    float3 reflectDirection = reflect(-V, N);
    float specularApertureTan = max(roughness * roughness, 0.03);
    float3 specular = TraceCone(startPosition, reflectDirection, specularApertureTan, maxDistance).rgb;
    if (roughness < 0.65)
    {
        float4 screenReflection = TraceScreenSpaceReflection(startPosition, reflectDirection, roughness);
        specular = lerp(specular, screenReflection.rgb, screenReflection.a * (1.0 - roughness));
    }

    _indirectGI[tracePixel] = float4(diffuse, 1.0);
    _indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] = float4(specular, 1.0);
}
