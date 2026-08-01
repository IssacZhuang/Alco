// Shared data and helpers for the voxel global illumination passes
// (VoxelClear.hlsl, Voxelize.hlsl, VoxelInject.hlsl, VoxelMip.hlsl,
// VoxelTrace.hlsl). Include after Shaders/Libs/Core.hlsli. The cbuffer
// layout must match VoxelGiRenderer.VoxelGiData on the C# side exactly.
//
// The scene is stored in a clipmap of up to 4 voxel levels, each a cube of
// resolution^3 voxels at twice the voxel size of the previous level, centered
// on the camera. Attribute voxels are packed into uint2:
//   x = albedo rgb888 + occupancy (a; 0 = empty)
//   y = normal (rgb888, *0.5+0.5 encoded) + emissive intensity (a, 0..1)
// Radiance voxels (with a mip chain) are packed into uint2:
//   x = RGB9E5 shared-exponent HDR radiance
//   y = occupancy fraction (low byte, 0..255)

#define VOXEL_MAX_LEVELS 4

DEFINE_UNIFORM(0, _data)
{
    float4x4 invViewProjection;
    float4x4 sunViewProjection[4];
    float4 levelOrigins[4];        // xyz = min corner in world space, w = voxel size
    float4 cameraPosition;         // xyz = world-space camera position
    float4 sunDirection;           // normalized direction the sun light travels
    float4 sunColorAndIntensity;   // rgb + intensity
    float4 skyTopColor;
    float4 skyBottomColor;
    float4 pointLight0Position;
    float4 pointLight0Color;       // rgb + intensity
    float4 pointLight1Position;
    float4 pointLight1Color;
    float4 pointLight2Position;
    float4 pointLight2Color;
    float4 pointLight3Position;
    float4 pointLight3Color;
    float4 cascadeSplits;          // radial end distance of each shadow cascade
    float4 cascadeTexelSizes;      // world units per shadow texel of each cascade
    float4 clipmapParams;          // x=resolution y=levelCount z=mipCount w=unused
    float4 lightingParams;         // x=shadowEnabled y=pointLightEnabled z=shadowMapSize w=unused
    float4 giParams;               // x=emissiveScale y=traceMaxDistance z=traceWidth w=traceHeight
    float4 giParams2;              // x=debugView y=gbufferWidth z=gbufferHeight w=unused
};

// ---------------------------------------------------------------- packing ---

uint PackBytes4(uint r, uint g, uint b, uint a)
{
    return (r & 255u) | ((g & 255u) << 8) | ((b & 255u) << 16) | ((a & 255u) << 24);
}

uint2 PackVoxelAttr(float3 albedo, float3 normal, float emissiveQ)
{
    uint2 packed;
    packed.x = PackBytes4(
        (uint)(saturate(albedo.r) * 255.0 + 0.5),
        (uint)(saturate(albedo.g) * 255.0 + 0.5),
        (uint)(saturate(albedo.b) * 255.0 + 0.5), 255u);
    packed.y = PackBytes4(
        (uint)(saturate(normal.x * 0.5 + 0.5) * 255.0 + 0.5),
        (uint)(saturate(normal.y * 0.5 + 0.5) * 255.0 + 0.5),
        (uint)(saturate(normal.z * 0.5 + 0.5) * 255.0 + 0.5),
        (uint)(saturate(emissiveQ) * 255.0 + 0.5));
    return packed;
}

bool VoxelAttrOccupied(uint2 attr)
{
    return (attr.x >> 24) != 0u;
}

void UnpackVoxelAttr(uint2 attr, out float3 albedo, out float3 normal, out float emissiveQ)
{
    albedo = float3((float)(attr.x & 255u), (float)((attr.x >> 8) & 255u), (float)((attr.x >> 16) & 255u)) / 255.0;
    normal = float3((float)(attr.y & 255u), (float)((attr.y >> 8) & 255u), (float)((attr.y >> 16) & 255u)) / 255.0 * 2.0 - 1.0;
    emissiveQ = (float)(attr.y >> 24) / 255.0;
}

// RGB9E5 shared-exponent HDR packing (3 x 9-bit mantissa, 5-bit biased exponent).
uint PackRGB9E5(float3 rgb)
{
    rgb = clamp(rgb, 0.0, 65000.0);
    float maxComponent = max(rgb.r, max(rgb.g, rgb.b));
    int exponent = (int)ceil(log2(max(maxComponent, 0.000061)));
    exponent = clamp(exponent, -15, 15);
    float scale = exp2((float)(9 - exponent));
    uint r = (uint)floor(rgb.r * scale + 0.5);
    uint g = (uint)floor(rgb.g * scale + 0.5);
    uint b = (uint)floor(rgb.b * scale + 0.5);
    return ((uint)(exponent + 15) << 27) | (min(b, 511u) << 18) | (min(g, 511u) << 9) | min(r, 511u);
}

float3 UnpackRGB9E5(uint v)
{
    int exponent = (int)(v >> 27) - 15 - 9;
    float scale = exp2((float)exponent);
    return float3((float)(v & 511u), (float)((v >> 9) & 511u), (float)((v >> 18) & 511u)) * scale;
}

uint2 PackVoxelRadiance(float3 radiance, float occupancy)
{
    uint2 packed;
    packed.x = PackRGB9E5(radiance);
    packed.y = (uint)(saturate(occupancy) * 255.0 + 0.5);
    return packed;
}

float4 UnpackVoxelRadiance(uint2 packed)
{
    return float4(UnpackRGB9E5(packed.x), (float)(packed.y & 255u) / 255.0);
}

// ------------------------------------------------------------- addressing ---

uint VoxelResolution()
{
    return (uint)clipmapParams.x;
}

uint VoxelIndex(uint3 coord, uint resolution)
{
    return coord.x + coord.y * resolution + coord.z * resolution * resolution;
}

// Mip levels halve the resolution each step down to 1; the chain is stored
// back to back in the same buffer as mip 0.
uint VoxelMipOffset(uint resolution, uint mip)
{
    uint offset = 0;
    for (uint k = 0; k < mip; k++)
    {
        uint size = max(resolution >> k, 1u);
        offset += size * size * size;
    }
    return offset;
}

// All clipmap levels share one radiance buffer: each level's mip chain occupies
// an equal stride (the total mip-chain voxel count of one level).
uint VoxelRadianceLevelStride(uint resolution, uint mipCount)
{
    return VoxelMipOffset(resolution, mipCount);
}

// ----------------------------------------------------------------- levels ---

// Whether a world position is inside the region covered by the given level.
bool VoxelLevelContains(float3 position, int level)
{
    float4 originAndSize = levelOrigins[level];
    float3 relative = position - originAndSize.xyz;
    float extent = originAndSize.w * clipmapParams.x;
    return all(relative >= 0.0) && all(relative < extent);
}

// The finest (smallest voxel) level covering the position; -1 when outside all.
int VoxelFindLevel(float3 position)
{
    int levelCount = (int)clipmapParams.y;
    for (int i = 0; i < levelCount; i++)
    {
        if (VoxelLevelContains(position, i))
        {
            return i;
        }
    }
    return -1;
}

// Procedural gradient sky (no sun disc), matching the deferred lighting sky.
float3 VoxelSkyColor(float3 direction)
{
    float t = pow(saturate(direction.z * 0.5 + 0.5), 0.6);
    return lerp(skyBottomColor.rgb, skyTopColor.rgb, t);
}
