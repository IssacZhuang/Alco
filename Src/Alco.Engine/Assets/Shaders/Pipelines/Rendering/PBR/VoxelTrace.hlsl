#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Voxel cone tracing for the voxel GI clipmap: one dispatch at the half-res
// trace resolution. Reconstructs the world position and normal from the
// G-buffer, traces 6 diffuse cones around the normal and 1 specular cone along
// the reflection direction through the radiance clipmap (mip selected by cone
// diameter), and writes the gathered indirect radiance into the output atlas
// (twice the trace width: diffuse in the left half, specular in the right).
// Cones that leave every clipmap region fall back to the sky gradient.

DEFINE_STORAGE(1, uint2, _radiance);
DEFINE_TEX2D_DEPTH(2, _gbufferDepth);
DEFINE_TEX2D_READ(3, _normal);
DEFINE_TEX2D_READ(4, _mrAO);
DEFINE_TEX2D_STORAGE(5, _indirectGI, float4, "rgba16f");

// All clipmap levels share one radiance buffer (see VoxelRadianceLevelStride).
uint2 LoadRadiance(int level, uint index)
{
    return _radiance[(uint)level * VoxelRadianceLevelStride(VoxelResolution(), (uint)clipmapParams.z) + index];
}

// Trilinear sample of the radiance volume at a mip; rgb = radiance, a = occupancy.
float4 SampleRadianceTrilinear(float3 position, int level, uint mip)
{
    uint resolution = VoxelResolution();
    float4 originAndSize = levelOrigins[level];
    float voxelSizeMip = originAndSize.w * (float)(1u << mip);
    uint sizeMip = max(resolution >> mip, 1u);

    float3 grid = (position - originAndSize.xyz) / voxelSizeMip - 0.5;
    grid = clamp(grid, 0.0, (float)sizeMip - 1.001);
    uint3 i0 = (uint3)floor(grid);
    uint3 i1 = min(i0 + 1, sizeMip - 1);
    float3 f = grid - floor(grid);

    uint base = VoxelMipOffset(resolution, mip);
    float4 c000 = UnpackVoxelRadiance(LoadRadiance(level, base + VoxelIndex(uint3(i0.x, i0.y, i0.z), sizeMip)));
    float4 c001 = UnpackVoxelRadiance(LoadRadiance(level, base + VoxelIndex(uint3(i0.x, i0.y, i1.z), sizeMip)));
    float4 c010 = UnpackVoxelRadiance(LoadRadiance(level, base + VoxelIndex(uint3(i0.x, i1.y, i0.z), sizeMip)));
    float4 c011 = UnpackVoxelRadiance(LoadRadiance(level, base + VoxelIndex(uint3(i0.x, i1.y, i1.z), sizeMip)));
    float4 c100 = UnpackVoxelRadiance(LoadRadiance(level, base + VoxelIndex(uint3(i1.x, i0.y, i0.z), sizeMip)));
    float4 c101 = UnpackVoxelRadiance(LoadRadiance(level, base + VoxelIndex(uint3(i1.x, i0.y, i1.z), sizeMip)));
    float4 c110 = UnpackVoxelRadiance(LoadRadiance(level, base + VoxelIndex(uint3(i1.x, i1.y, i0.z), sizeMip)));
    float4 c111 = UnpackVoxelRadiance(LoadRadiance(level, base + VoxelIndex(uint3(i1.x, i1.y, i1.z), sizeMip)));

    float4 x00 = lerp(c000, c100, f.x);
    float4 x01 = lerp(c001, c101, f.x);
    float4 x10 = lerp(c010, c110, f.x);
    float4 x11 = lerp(c011, c111, f.x);
    float4 y0 = lerp(x00, x10, f.y);
    float4 y1 = lerp(x01, x11, f.y);
    return lerp(y0, y1, f.z);
}

// March one cone through the clipmap, accumulating radiance front-to-back.
// Returns rgb = gathered radiance (with sky fallback), a = accumulated occlusion.
float4 TraceCone(float3 startPosition, float3 direction, float apertureTan, float maxDistance)
{
    uint mipCount = (uint)clipmapParams.z;
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
        uint mip = clamp((uint)floor(log2(diameter / voxelSize)), 0u, mipCount - 1u);
        float4 sample_ = SampleRadianceTrilinear(position, level, mip);

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

    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    float3 worldPosition = world.xyz / world.w;
    float3 N = normalize(GET_PIXEL_TEX2D(_normal, gbufferPixel).xyz * 2.0 - 1.0);
    float roughness = GET_PIXEL_TEX2D(_mrAO, gbufferPixel).y;
    float3 V = normalize(cameraPosition.xyz - worldPosition);
    float maxDistance = giParams.y;

    // Start half a fine voxel above the surface to avoid immediate self-hits.
    float fineVoxelSize = levelOrigins[0].w;
    float3 startPosition = worldPosition + N * fineVoxelSize * 1.5;

    // Diffuse: 6 cones, one along the normal and five at 45 degrees around it.
    float3 upAxis = abs(N.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 T = normalize(cross(upAxis, N));
    float3 B = cross(N, T);

    const float apertureTan = 0.57735; // tan(30°): 60° cones
    float3 diffuse = 0.0;
    [unroll]
    for (int cone = 0; cone < 6; cone++)
    {
        float weight;
        float3 directionTS;
        if (cone == 0)
        {
            directionTS = float3(0.0, 0.0, 1.0);
            weight = 0.25;
        }
        else
        {
            float azimuth = (cone - 1) * (TAU / 5.0);
            directionTS = float3(float2(cos(azimuth), sin(azimuth)) * 0.7071, 0.7071);
            weight = 0.15;
        }
        float3 direction = T * directionTS.x + B * directionTS.y + N * directionTS.z;
        diffuse += weight * TraceCone(startPosition, direction, apertureTan, maxDistance).rgb;
    }

    // Specular: one cone along the reflection direction, aperture from roughness.
    float3 reflectDirection = reflect(-V, N);
    float specularApertureTan = max(roughness * roughness, 0.03);
    float3 specular = TraceCone(startPosition, reflectDirection, specularApertureTan, maxDistance).rgb;

    _indirectGI[tracePixel] = float4(diffuse, 1.0);
    _indirectGI[uint2(tracePixel.x + traceResolution.x, tracePixel.y)] = float4(specular, 1.0);
}
