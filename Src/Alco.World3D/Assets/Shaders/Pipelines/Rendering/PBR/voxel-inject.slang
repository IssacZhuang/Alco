// file "voxel-inject.slang" - module "voxel_inject"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VoxelInject.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module voxel_inject;

import alco_rendering_core;
import alco_world3d_voxel_common;

// Direct lighting injection for the voxel GI clipmap: sparse dispatch over a
// brick list (one entry per resident or recently-freed 8³ brick). Reads the
// voxelized attribute buffers (dynamic wins over static), evaluates sun (CSM
// shadowed), an upward sky-visibility march, dynamic point lights (from a
// StructuredBuffer) and emissive, and writes HDR radiance + occupancy into mip 0 of the level's slab
// of the radiance Texture3D. Freed bricks have page-table entry 0, so the
// occupancy check naturally writes zeros — clearing stale radiance without a
// separate full-resolution clear pass.

struct VoxelInjectConstants
{
    float4 params; // x=levelIndex, yzw=unused
};

public struct PointLightData
{
    float4 positionRange;    // xyz = world-space position, w = cutoff radius
    float4 colorIntensity;   // rgb = linear color, a = intensity (0 disables)
};
// alco_world3d_voxel_common declares _data; this pass's ParameterBlock follows it
// in declaration order.
public struct PassParams
{
    public RWStructuredBuffer<uint4> _attrStatic;
    public RWStructuredBuffer<uint4> _attrDynamic;
    public DepthTexture2D _shadowMap;
// Combined page table: x=static page entry, y=dynamic page entry. Merging the
// two pools into one buffer frees a binding for the brick list.
    public RWStructuredBuffer<uint2> _pageTable;
    public RWStructuredBuffer<uint4> _brickList;

// Point lights shared with the deferred lighting pass (same StructuredBuffer).
    public RWStructuredBuffer<PointLightData> _pointLights;
    public [[vk::image_format("rgba16f")]] RWTexture3D<float4> _radianceOut;
    public [[vk::image_format("rgba16f")]] RWTexture3D<float4> _opacityOut;
};

public ParameterBlock<PassParams> _pass;


[[vk::push_constant]] VoxelInjectConstants constants;

// Read the occupancy flag of one voxel from the merged attribute buffers.
bool IsVoxelOccupied(uint3 logicalCoord, uint resolution, int level)
{
    uint pageSlot = VoxelPageTableSlot(logicalCoord, resolution, level);
    uint2 pages = _pass._pageTable[pageSlot];
    uint4 attr = uint4(0u, 0u, 0u, 0u);
    if (pages.y != 0u)
    {
        attr = _pass._attrDynamic[VoxelAttributeIndex(pages.y, logicalCoord)];
    }
    if (!VoxelAttrOccupied(attr))
    {
        if (pages.x != 0u)
        {
            attr = _pass._attrStatic[VoxelAttributeIndex(pages.x, logicalCoord)];
        }
    }
    return VoxelAttrOccupied(attr);
}

// Cone march along the sun direction through the voxel grid, accumulating
// occlusion. The cone traces up to 63 m; here we march through the
// clipmap's attribute buffers (the opacity volume isn't built yet at inject
// time). Used for voxels beyond the CSM shadow range.
float TraceVoxelSunCone(float3 worldPosition, float3 sunDir, float voxelSize,
    float4 originAndSize, uint resolution, int level)
{
    float3 origin = originAndSize.xyz;
    float extent = originAndSize.w * resolution;
    float alpha = 0.0;
    float t = voxelSize * 2.0;
    float maxDistance = min(63.0, _data.giParams.y);

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
// through the voxel grid when beyond it.
float SampleSunShadowVoxel(float3 worldPosition, float3 N, float voxelSize,
    float4 originAndSize, uint resolution, int level)
{
    float viewDistance = length(worldPosition - _data.cameraPosition.xyz);
    int cascade = -1;
    if (viewDistance < _data.cascadeSplits.x) cascade = 0;
    else if (viewDistance < _data.cascadeSplits.y) cascade = 1;
    else if (viewDistance < _data.cascadeSplits.z) cascade = 2;
    else if (viewDistance < _data.cascadeSplits.w) cascade = 3;
    if (cascade < 0)
    {
        // Beyond CSM range: cone march along the sun direction through the
        // voxel grid (cone-traced sun shadow).
        float3 sunDir = normalize(-_data.sunDirection.xyz);
        return TraceVoxelSunCone(worldPosition, sunDir, voxelSize, originAndSize, resolution, level);
    }

    float3 biasedWorld = worldPosition + N * (_data.cascadeTexelSizes[cascade] + voxelSize * 0.5);
    float4 clip = mul(_data.sunViewProjection[cascade], float4(biasedWorld, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndc.z < 0.0 || ndc.z > 1.0)
    {
        return 1.0;
    }

    float2 quadrantOffset = float2((cascade % 2) * 0.5, (cascade / 2) * 0.5);
    float2 shadowUV = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5) * 0.5 + quadrantOffset;
    float2 quadrantMin = quadrantOffset + 0.25 / _data.lightingParams.z;
    float2 quadrantMax = quadrantOffset + 0.5 - 0.25 / _data.lightingParams.z;
    shadowUV = clamp(shadowUV, quadrantMin, quadrantMax);

    float NdotL = saturate(dot(N, normalize(-_data.sunDirection.xyz)));
    float bias = 0.0003 + 0.0015 * (1.0 - NdotL);
    return _pass._shadowMap.SampleCmpLevelZero(_samplers._depthComparison, shadowUV, ndc.z - bias);
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
    int level = (int)constants.params.x;
    uint brickIndex = dispatchId.z / VOXEL_BRICK_SIZE;
    uint localZ = dispatchId.z % VOXEL_BRICK_SIZE;
    uint3 logicalCoord = _pass._brickList[brickIndex].xyz * VOXEL_BRICK_SIZE
        + uint3(dispatchId.x, dispatchId.y, localZ);
    if (any(logicalCoord >= resolution))
    {
        return;
    }

    float4 originAndSize = _data.levelOrigins[level];
    float voxelSize = originAndSize.w;
    uint pageSlot = VoxelPageTableSlot(logicalCoord, resolution, level);
    // All levels share one radiance Texture3D; this level's slab starts at its
    // depth slice (mip 0 view bound, full resolution).
    uint3 storeCoord = uint3(logicalCoord.x, logicalCoord.y, (uint)level * resolution + logicalCoord.z);

    uint2 pages = _pass._pageTable[pageSlot];
    uint4 attr = uint4(0u, 0u, 0u, 0u);
    if (pages.y != 0u)
    {
        attr = _pass._attrDynamic[VoxelAttributeIndex(pages.y, logicalCoord)];
    }
    if (!VoxelAttrOccupied(attr))
    {
        if (pages.x != 0u)
        {
            attr = _pass._attrStatic[VoxelAttributeIndex(pages.x, logicalCoord)];
        }
    }
    if (!VoxelAttrOccupied(attr))
    {
        _pass._radianceOut[storeCoord] = float4(0.0, 0.0, 0.0, 0.0);
        _pass._opacityOut[storeCoord] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float3 albedo;
    float3 normal;
    float emissiveQ;
    UnpackVoxelAttr(attr, albedo, normal, emissiveQ);
    normal = dot(normal, normal) > 1e-6 ? normalize(normal) : float3(0.0, 0.0, 1.0);

    float3 worldPosition = originAndSize.xyz + (float3(logicalCoord) + 0.5) * voxelSize;

    // Only direct lights (sun + point lights) are injected into surface voxels.
    // Sky light enters the volume through cone-traced fallback in the
    // propagation pass (first bounce, hemisphere-integrated with natural
    // occlusion) and the screen-space trace.
    //
    // A DiffuseBias is also injected into the volume here. This ensures
    // every occupied voxel has a minimum radiance floor, so the propagation
    // and trace passes always pick up some light even from voxels in deep shadow.
    float3 direct = 0.0;

    // Sun (with CSM shadow). The 1/PI matches the direct pass's diffuse BRDF.
    float3 L = normalize(-_data.sunDirection.xyz);
    float NdotL = max(dot(normal, L), 0.0);
    float shadow = _data.lightingParams.x > 0.5 ? SampleSunShadowVoxel(worldPosition, normal, voxelSize, originAndSize, resolution, level) : 1.0;
    direct += _data.sunColorAndIntensity.rgb * _data.sunColorAndIntensity.w * (NdotL / PI) * shadow;

    // Point lights (unshadowed, StructuredBuffer with per-light range).
    {
        uint lightCount = (uint)_data.lightingParams.y;
        [loop]
        for (uint i = 0; i < lightCount; i++)
        {
            float4 posRange = _pass._pointLights[i].positionRange;
            float4 colInt   = _pass._pointLights[i].colorIntensity;
            if (colInt.w <= 0.0)
            {
                continue;
            }
            float3 toLight = posRange.xyz - worldPosition;
            float dist = length(toLight);
            if (posRange.w > 0.0 && dist > posRange.w)
            {
                continue;
            }
            float attenuation = 1.0 / (dist * dist + 1.0);
            if (posRange.w > 0.0)
            {
                float fallOff = saturate(1.0 - dist / posRange.w);
                attenuation *= fallOff * fallOff;
            }
            float pointNdotL = max(dot(normal, toLight / max(dist, 1e-6)), 0.0);
            direct += colInt.rgb * colInt.w * attenuation * (pointNdotL / PI);
        }
    }

    // DiffuseBias injection: bake a minimum sky ambient into every occupied
    // voxel so shadowed surfaces still have non-zero radiance.
    // _data.giFrameParams.y = DiffuseBias (default 0.05).
    direct += _data.giFrameParams.y * _data.skyZenithColor.rgb * _data.giParams2.w;

    // Emissive: albedo-tinted, intensity recovered from the quantized value.
    float3 emissive = albedo * emissiveQ * 8.0 * _data.giParams.x;

    // Store exiting surface radiance = albedo × (direct irradiance) + emissive.
    float3 radiance = albedo * direct + emissive;
    _pass._radianceOut[storeCoord] = float4(radiance, 1.0);

    // Directional opacity: the surface is opaque along the normal axis and
    // transparent along the other two. Stored as xyz = |normal components|
    // so a cone ray projects it via dot(opacity.xyz, abs(rayDir)).
    _pass._opacityOut[storeCoord] = float4(abs(normal), 1.0);
}
