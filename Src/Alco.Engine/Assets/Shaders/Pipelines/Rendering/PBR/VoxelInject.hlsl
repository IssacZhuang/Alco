#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Direct lighting injection for the voxel GI clipmap: one dispatch per clipmap
// level at (resolution, resolution, resolution). Reads the voxelized attribute
// buffers (dynamic wins over static), evaluates sun (CSM shadowed), an upward
// sky-visibility march, the four point lights and emissive, and writes HDR
// radiance + occupancy into mip 0 of the level's slab of the radiance Texture3D.

struct VoxelInjectConstants
{
    float4 params; // x=levelIndex, yzw=unused
};

DEFINE_STORAGE(1, uint2, _attrStatic);
DEFINE_STORAGE(2, uint2, _attrDynamic);
DEFINE_TEX3D_STORAGE(3, _radianceOut, float4, "rgba16f");
DEFINE_TEX2D_DEPTH_SAMPLE(4, _shadowMap);
DEFINE_STORAGE(5, uint, _pageTableStatic);
DEFINE_STORAGE(6, uint, _pageTableDynamic);
DEFINE_TEX3D_STORAGE(7, _opacityOut, float4, "rgba16f");

PUSH_CONSTANT VoxelInjectConstants constants;

// Read the occupancy flag of one voxel from the merged attribute buffers.
bool IsVoxelOccupied(uint3 logicalCoord, uint resolution, int level)
{
    uint pageSlot = VoxelPageTableSlot(logicalCoord, resolution, level);
    uint2 attr = uint2(0u, 0u);
    uint dynamicPage = _pageTableDynamic[pageSlot];
    if (dynamicPage != 0u)
    {
        attr = _attrDynamic[VoxelAttributeIndex(dynamicPage, logicalCoord)];
    }
    if (!VoxelAttrOccupied(attr))
    {
        uint staticPage = _pageTableStatic[pageSlot];
        if (staticPage != 0u)
        {
            attr = _attrStatic[VoxelAttributeIndex(staticPage, logicalCoord)];
        }
    }
    return VoxelAttrOccupied(attr);
}

// Cone march along the sun direction through the voxel grid, accumulating
// occlusion. CE SVOGI traces a cone up to 63 m; here we march through the
// clipmap's attribute buffers (the opacity volume isn't built yet at inject
// time). Used for voxels beyond the CSM shadow range.
float TraceVoxelSunCone(float3 worldPosition, float3 sunDir, float voxelSize,
    float4 originAndSize, uint resolution, int level)
{
    float3 origin = originAndSize.xyz;
    float extent = originAndSize.w * resolution;
    float alpha = 0.0;
    float t = voxelSize * 2.0;
    float maxDistance = min(63.0, giParams.y);

    [loop]
    for (int step = 0; step < 24 && t <= maxDistance && alpha < 0.99; step++)
    {
        float3 position = worldPosition + sunDir * t;
        float3 relative = position - origin;
        if (any(relative < 0.0) || any(relative >= extent))
        {
            break;
        }
        uint3 coord = (uint3)floor(relative / voxelSize);
        if (IsVoxelOccupied(coord, resolution, level))
        {
            alpha += (1.0 - alpha) * 0.35;
        }
        t += voxelSize * 2.0;
    }
    return saturate(1.0 - alpha);
}

// Sun shadow for a voxel: CSM when inside the cascade range; cone march
// through the voxel grid when beyond it (CE SVOGI style).
float SampleSunShadowVoxel(float3 worldPosition, float3 N, float voxelSize,
    float4 originAndSize, uint resolution, int level)
{
    float viewDistance = length(worldPosition - cameraPosition.xyz);
    int cascade = -1;
    if (viewDistance < cascadeSplits.x) cascade = 0;
    else if (viewDistance < cascadeSplits.y) cascade = 1;
    else if (viewDistance < cascadeSplits.z) cascade = 2;
    else if (viewDistance < cascadeSplits.w) cascade = 3;
    if (cascade < 0)
    {
        // Beyond CSM range: cone march along the sun direction through the
        // voxel grid (CE SVOGI cone-traced sun shadow).
        float3 sunDir = normalize(-sunDirection.xyz);
        return TraceVoxelSunCone(worldPosition, sunDir, voxelSize, originAndSize, resolution, level);
    }

    float3 biasedWorld = worldPosition + N * (cascadeTexelSizes[cascade] + voxelSize * 0.5);
    float4 clip = mul(sunViewProjection[cascade], float4(biasedWorld, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    float2 quadrantOffset = float2((cascade % 2) * 0.5, (cascade / 2) * 0.5);
    float2 shadowUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * 0.5 + quadrantOffset;
    float2 quadrantMin = quadrantOffset + 0.25 / lightingParams.z;
    float2 quadrantMax = quadrantOffset + 0.5 - 0.25 / lightingParams.z;
    shadowUV = clamp(shadowUV, quadrantMin, quadrantMax);

    float NdotL = saturate(dot(N, normalize(-sunDirection.xyz)));
    float bias = 0.0003 + 0.0015 * (1.0 - NdotL);
    return SAMPLE_TEX2D_DEPTH_CMP(_shadowMap, shadowUV, ndc.z - bias);
}

// Upward visibility through the voxel grid: open sky above means the voxel is
// sky-lit; each occupied voxel overhead dims it. Gives interiors a dark sky
// term without an extra data structure.
float SampleSkyVisibility(float3 worldPosition, float4 originAndSize, uint resolution, int level)
{
    float voxelSize = originAndSize.w;
    float visibility = 1.0;
    float3 position = worldPosition;
    [unroll]
    for (int step = 0; step < 4; step++)
    {
        position += float3(0.0, 0.0, 1.0) * voxelSize * 2.0;
        int3 coord = (int3)floor((position - originAndSize.xyz) / voxelSize);
        if (any(coord < 0) || any(coord >= (int)resolution))
        {
            break;
        }
        if (IsVoxelOccupied((uint3)coord, resolution, level))
        {
            visibility *= 0.25;
        }
    }
    return visibility;
}

[shader("compute")]
[numthreads(4, 4, 4)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = VoxelResolution();
    if (any(dispatchId >= resolution))
    {
        return;
    }

    int level = (int)constants.params.x;
    float4 originAndSize = levelOrigins[level];
    float voxelSize = originAndSize.w;
    uint pageSlot = VoxelPageTableSlot(dispatchId, resolution, level);
    // All levels share one radiance Texture3D; this level's slab starts at its
    // depth slice (mip 0 view bound, full resolution).
    uint3 storeCoord = uint3(dispatchId.x, dispatchId.y, (uint)level * resolution + dispatchId.z);

    uint2 attr = uint2(0u, 0u);
    uint dynamicPage = _pageTableDynamic[pageSlot];
    if (dynamicPage != 0u)
    {
        attr = _attrDynamic[VoxelAttributeIndex(dynamicPage, dispatchId)];
    }
    if (!VoxelAttrOccupied(attr))
    {
        uint staticPage = _pageTableStatic[pageSlot];
        if (staticPage != 0u)
        {
            attr = _attrStatic[VoxelAttributeIndex(staticPage, dispatchId)];
        }
    }
    if (!VoxelAttrOccupied(attr))
    {
        _radianceOut[storeCoord] = float4(0.0, 0.0, 0.0, 0.0);
        _opacityOut[storeCoord] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float3 albedo;
    float3 normal;
    float emissiveQ;
    UnpackVoxelAttr(attr, albedo, normal, emissiveQ);
    normal = normalize(normal);

    float3 worldPosition = originAndSize.xyz + (float3(dispatchId) + 0.5) * voxelSize;

    // Sky ambient, occluded by the upward visibility march.
    float3 direct = VoxelSkyColor(normal) * SampleSkyVisibility(worldPosition, originAndSize, resolution, level);

    // Sun (with CSM shadow). The 1/PI matches the direct pass's diffuse BRDF.
    float3 L = normalize(-sunDirection.xyz);
    float NdotL = max(dot(normal, L), 0.0);
    float shadow = lightingParams.x > 0.5 ? SampleSunShadowVoxel(worldPosition, normal, voxelSize, originAndSize, resolution, level) : 1.0;
    direct += sunColorAndIntensity.rgb * sunColorAndIntensity.w * (NdotL / PI) * shadow;

    // Point lights (unshadowed).
    if (lightingParams.y > 0.5)
    {
        float4 pointLightPositions[4] = {
            pointLight0Position, pointLight1Position,
            pointLight2Position, pointLight3Position };
        float4 pointLightColors[4] = {
            pointLight0Color, pointLight1Color,
            pointLight2Color, pointLight3Color };
        for (int i = 0; i < 4; i++)
        {
            float intensity = pointLightColors[i].w;
            if (intensity <= 0.0)
            {
                continue;
            }
            float3 toLight = pointLightPositions[i].xyz - worldPosition;
            float distanceSqr = dot(toLight, toLight);
            float attenuation = 1.0 / (distanceSqr + 1.0);
            float pointNdotL = max(dot(normal, normalize(toLight)), 0.0);
            direct += pointLightColors[i].rgb * intensity * attenuation * (pointNdotL / PI);
        }
    }

    // Emissive: albedo-tinted, intensity recovered from the quantized value.
    float3 emissive = albedo * emissiveQ * 8.0 * giParams.x;

    float3 radiance = albedo * direct + emissive;
    _radianceOut[storeCoord] = float4(radiance, 1.0);

    // Directional opacity: the surface is opaque along the normal axis and
    // transparent along the other two. Stored as xyz = |normal components|
    // so a cone ray projects it via dot(opacity.xyz, abs(rayDir)).
    _opacityOut[storeCoord] = float4(abs(normal), 1.0);
}
