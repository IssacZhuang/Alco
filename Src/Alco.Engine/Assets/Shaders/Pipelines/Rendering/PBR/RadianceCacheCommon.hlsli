#ifndef RADIANCE_CACHE_COMMON_HLSLI
#define RADIANCE_CACHE_COMMON_HLSLI

// Shared data and helpers for the screen-seeded cascaded radiance cache.
// The constant-buffer layout must match RadianceCacheRenderer.RadianceCacheData.

#define RC_CASCADE_COUNT 3

DEFINE_UNIFORM(0, _data)
{
    float4x4 invViewProjection;
    float4x4 viewProjection;
    float4x4 viewProjectionPrev;
    float4x4 sunViewProjection[4];
    float4 cameraPosition;
    float4 previousCameraPosition;
    float4 sunDirection;
    float4 sunColorAndIntensity;
    float4 skyHorizonColor;
    float4 skyZenithColor;
    float4 cascadeSplits;
    float4 cascadeTexelSizes;
    float4 cacheOrigins[RC_CASCADE_COUNT];
    float4 previousCacheOrigins[RC_CASCADE_COUNT];
    float4 cacheParams;       // x=grid resolution, y=cascade count, z=cell count, w=frame index
    float4 viewportParams;    // xy=G-buffer size, zw=trace size
    float4 lightingParams;    // x=shadow enabled, y=point-light count, z=shadow-map size, w=emissive scale
    float4 responseParams;    // x=cache hysteresis, y=temporal hysteresis, z=bounce strength, w=sky intensity
    float4 traceParams;       // x=max trace distance, y=history valid, z=propagation strength, w=off-screen decay
};

uint CacheResolution()
{
    return (uint)cacheParams.x;
}

uint CacheCellsPerCascade()
{
    uint resolution = CacheResolution();
    return resolution * resolution * resolution;
}

uint CacheLinearIndex(uint3 cell, uint cascade)
{
    uint resolution = CacheResolution();
    return cascade * CacheCellsPerCascade()
        + cell.x + cell.y * resolution + cell.z * resolution * resolution;
}

uint3 CacheCellFromLocalIndex(uint index)
{
    uint resolution = CacheResolution();
    uint plane = resolution * resolution;
    uint z = index / plane;
    uint remainder = index - z * plane;
    return uint3(remainder % resolution, remainder / resolution, z);
}

float3 CacheCellWorldPosition(uint3 cell, uint cascade)
{
    float4 origin = cacheOrigins[cascade];
    return origin.xyz + (float3(cell) + 0.5) * origin.w;
}

bool CacheWorldToCell(float3 worldPosition, uint cascade, out int3 cell)
{
    float4 origin = cacheOrigins[cascade];
    cell = (int3)floor((worldPosition - origin.xyz) / origin.w);
    int resolution = (int)CacheResolution();
    return all(cell >= 0) && all(cell < resolution);
}

bool PreviousCacheWorldToCell(float3 worldPosition, uint cascade, out int3 cell)
{
    float4 origin = previousCacheOrigins[cascade];
    cell = (int3)floor((worldPosition - origin.xyz) / origin.w);
    int resolution = (int)CacheResolution();
    return all(cell >= 0) && all(cell < resolution);
}

float3 DecodeCacheSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow(max((color + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

float3 ReconstructCacheWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

float3 CacheSkyIrradiance(float3 normal)
{
    float3 sideResponse = skyHorizonColor.rgb * 0.218505
        + skyZenithColor.rgb * 0.281495;
    float3 upResponse = skyHorizonColor.rgb * 0.230769
        + skyZenithColor.rgb * 0.769231;
    float upFacing = saturate(normal.z);
    float downFacing = saturate(-normal.z);
    return lerp(sideResponse, upResponse, upFacing)
        * (1.0 - downFacing) * responseParams.w;
}

float CacheHash(float2 value)
{
    return frac(52.9829189 * frac(dot(value, float2(0.06711056, 0.00583715))));
}

#endif // RADIANCE_CACHE_COMMON_HLSLI
