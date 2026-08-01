// Shared data and helpers for the voxel global illumination passes
// (VoxelClear.hlsl, Voxelize.hlsl, VoxelInject.hlsl, VoxelMip.hlsl,
// VoxelTrace.hlsl). Include after Shaders/Libs/Core.hlsli. The cbuffer
// layout must match VoxelGiRenderer.VoxelGiData on the C# side exactly.
//
// The scene is stored in a clipmap of up to 4 voxel levels, each a cube of
// resolution^3 voxels at twice the voxel size of the previous level, centered
// on the camera. Attribute voxels are packed into uint2 in storage buffers:
//   x = albedo rgb888 + occupancy (a; 0 = empty)
//   y = normal (rgb888, *0.5+0.5 encoded) + emissive intensity (a, 0..1)
// Radiance (HDR, half float) lives in one RGBA16Float Texture3D with a full
// mip chain: all levels are stacked along the w axis, each level's mip cube
// occupying 1/VOXEL_MAX_LEVELS of the texture depth at every mip; alpha holds
// the occupancy fraction.

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

// ------------------------------------------------------------- addressing ---

uint VoxelResolution()
{
    return (uint)clipmapParams.x;
}

uint VoxelIndex(uint3 coord, uint resolution)
{
    return coord.x + coord.y * resolution + coord.z * resolution * resolution;
}

// Normalized texture coordinates of a world position inside a clipmap level's
// slab of the shared radiance Texture3D (all levels stacked along w, each
// occupying 1/VOXEL_MAX_LEVELS of the depth at every mip). The coordinates are
// clamped to a half-texel inset of the coarsest sampled mip so hardware
// trilinear taps never bleed across slab boundaries.
float3 VoxelWorldToUVW(float3 position, int level, float mip)
{
    uint resolution = VoxelResolution();
    float4 originAndSize = levelOrigins[level];

    // Position normalized to the level cube (mip independent).
    float extent = originAndSize.w * clipmapParams.x;
    float3 p = (position - originAndSize.xyz) / extent;

    uint sizeMip = max(resolution >> (uint)ceil(mip), 1u);
    float inset = 0.5 / (float)sizeMip;
    p = clamp(p, inset, 1.0 - inset);
    return float3(p.xy, (level + p.z) * (1.0 / VOXEL_MAX_LEVELS));
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
