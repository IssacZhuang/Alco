// Shared data and helpers for the voxel global illumination passes
// (VoxelClear.hlsl, Voxelize.hlsl, VoxelInject.hlsl, VoxelMip.hlsl,
// VoxelTrace.hlsl). Include after Shaders/Libs/Core.hlsli. The cbuffer
// layout must match VoxelGiRenderer.VoxelGiData on the C# side exactly.
// The scene is stored in a clipmap of up to 4 voxel levels, each a cube of
// resolution^3 voxels at twice the voxel size of the previous level, centered
// on the camera. Attribute voxels are packed into uint4 in storage buffers as
// pairs of 16-bit fixed-point sums. Voxelization atomically accumulates up to
// 255 triangle samples, making the result independent of thread order:
//   x = sample count | emissive sum
//   y = albedo r sum | albedo g sum
//   z = albedo b sum | encoded normal x sum
//   w = encoded normal y sum | encoded normal z sum
// Radiance (HDR, half float) lives in one RGBA16Float Texture3D with a full
// mip chain: all levels are stacked along the w axis, each level's mip cube
// occupying 1/VOXEL_MAX_LEVELS of the texture depth at every mip; alpha holds
// the occupancy fraction.
// Diffuse GI is computed by tracing 9 cones covering the hemisphere through
// the radiance volume ( CryEngine SVOGI style ); specular GI uses one cone
// along the reflection vector.

#define VOXEL_MAX_LEVELS 4

DEFINE_UNIFORM(0, _data)
{
    float4x4 invViewProjection;
    float4x4 viewProjectionPrev; // previous frame's view-projection (for temporal reprojection)
    float4x4 viewProjection;
    float4x4 sunViewProjection[4];
    float4 levelOrigins[4];        // xyz = min corner in world space, w = voxel size
    float4 levelRingOffsets[4];    // xyz = toroidal storage offset in voxels
    float4 cameraPosition;         // xyz = world-space camera position
    float4 sunDirection;           // normalized direction the sun light travels
    float4 sunColorAndIntensity;   // rgb + intensity
    float4 skyHorizonColor;        // low-frequency physical sky for voxel GI
    float4 skyZenithColor;
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
    float4 giParams2;              // x=debugView y=gbufferWidth z=gbufferHeight w=giSkyIntensity
    float4 giFrameParams;          // x=frameIndex y=giDiffuseBias z=historyValid w=unused
};

// ---------------------------------------------------------------- packing ---

uint PackWords2(uint low, uint high)
{
    return (low & 65535u) | ((high & 65535u) << 16);
}

uint QuantizeVoxelAttribute(float value)
{
    return (uint)(saturate(value) * 255.0 + 0.5);
}

uint4 PackVoxelAttr(float3 albedo, float3 normal, float emissiveQ)
{
    float3 encodedNormal = normal * 0.5 + 0.5;
    uint4 packed;
    packed.x = PackWords2(1u, QuantizeVoxelAttribute(emissiveQ));
    packed.y = PackWords2(QuantizeVoxelAttribute(albedo.r), QuantizeVoxelAttribute(albedo.g));
    packed.z = PackWords2(QuantizeVoxelAttribute(albedo.b), QuantizeVoxelAttribute(encodedNormal.x));
    packed.w = PackWords2(QuantizeVoxelAttribute(encodedNormal.y), QuantizeVoxelAttribute(encodedNormal.z));
    return packed;
}

bool VoxelAttrOccupied(uint4 attr)
{
    return (attr.x & 65535u) != 0u;
}

void UnpackVoxelAttr(uint4 attr, out float3 albedo, out float3 normal, out float emissiveQ)
{
    float sampleCount = min((float)(attr.x & 65535u), 255.0);
    float inverseScale = rcp(max(sampleCount, 1.0) * 255.0);
    emissiveQ = (float)(attr.x >> 16) * inverseScale;
    albedo = float3(
        (float)(attr.y & 65535u),
        (float)(attr.y >> 16),
        (float)(attr.z & 65535u)) * inverseScale;
    normal = float3(
        (float)(attr.z >> 16),
        (float)(attr.w & 65535u),
        (float)(attr.w >> 16)) * inverseScale * 2.0 - 1.0;
}

// ------------------------------------------------------------- addressing ---

uint VoxelResolution()
{
    return (uint)clipmapParams.x;
}

static const uint VOXEL_BRICK_SIZE = 8u;
static const uint VOXEL_BRICK_VOXEL_COUNT = VOXEL_BRICK_SIZE * VOXEL_BRICK_SIZE * VOXEL_BRICK_SIZE;

// Maps a logical clipmap voxel to its toroidal page-table slot. Page-table
// values are one-based physical page indices; zero means the brick is absent.
uint VoxelPageTableSlot(uint3 coord, uint resolution, int level)
{
    uint bricksPerAxis = resolution / VOXEL_BRICK_SIZE;
    uint3 ringBrickOffset = (uint3)levelRingOffsets[level].xyz / VOXEL_BRICK_SIZE;
    uint3 physicalBrick = (coord / VOXEL_BRICK_SIZE + ringBrickOffset) % bricksPerAxis;
    return physicalBrick.x
        + physicalBrick.y * bricksPerAxis
        + physicalBrick.z * bricksPerAxis * bricksPerAxis;
}

uint VoxelBrickLocalIndex(uint3 coord)
{
    uint3 local = coord % VOXEL_BRICK_SIZE;
    return local.x
        + local.y * VOXEL_BRICK_SIZE
        + local.z * VOXEL_BRICK_SIZE * VOXEL_BRICK_SIZE;
}

uint VoxelAttributeIndex(uint pageEntry, uint3 coord)
{
    return (pageEntry - 1u) * VOXEL_BRICK_VOXEL_COUNT + VoxelBrickLocalIndex(coord);
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

// Smooth transition weight from a clipmap level to its next coarser level.
// The outer 20 percent of the cube is a blend band; returning a smooth cubic
// weight keeps both sampled data and ray-march scale continuous at boundaries.
float VoxelLevelTransitionWeight(float3 position, int level)
{
    float4 originAndSize = levelOrigins[level];
    float extent = originAndSize.w * clipmapParams.x;
    float3 relative = (position - originAndSize.xyz) / extent;
    float3 distanceFromCenter = abs(relative - 0.5);
    float maximumDistance = max(distanceFromCenter.x, max(distanceFromCenter.y, distanceFromCenter.z));
    float linearWeight = saturate((maximumDistance - 0.4) / 0.1);
    return linearWeight * linearWeight * (3.0 - 2.0 * linearWeight);
}

float VoxelEffectiveVoxelSize(float3 position, int level)
{
    int levelCount = (int)clipmapParams.y;
    float voxelSize = levelOrigins[level].w;
    if (level + 1 >= levelCount)
    {
        return voxelSize;
    }
    return lerp(voxelSize, levelOrigins[level + 1].w, VoxelLevelTransitionWeight(position, level));
}

// The physical atmosphere contains more angular detail than six voxel cones
// can sample without aliasing against normal maps. The CPU evaluates and
// azimuthally prefilters the current atmosphere into this smooth two-color
// representation; the visible sky and direct sun retain their full detail.
float3 VoxelSkyColor(float3 direction)
{
    float zenithWeight = pow(saturate(direction.z), 0.6);
    return lerp(skyHorizonColor.rgb, skyZenithColor.rgb, zenithWeight) * giParams2.w;
}
