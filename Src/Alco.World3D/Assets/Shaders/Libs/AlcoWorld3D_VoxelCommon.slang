// file "AlcoWorld3D_VoxelCommon.slang" - module "AlcoWorld3D_VoxelCommon"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module AlcoWorld3D_VoxelCommon;

// Shared data and helpers for the voxel global illumination passes
// (voxel-clear, voxelize, voxel-inject, voxel-mip, voxel-trace). The cbuffer
// layout must match VoxelGiRenderer.VoxelGiData on the C# side exactly.
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set; members resolve by bare field name.
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
// Diffuse GI uses deterministic rotation-balanced narrow cones through
// the radiance volume (SVOGI style); specular GI uses one cone along
// the reflection vector.

#define VOXEL_MAX_LEVELS 4

public struct VoxelData
{
    public float4x4 invViewProjection;
    public float4x4 viewProjectionPrev; // previous frame's view-projection (for temporal reprojection)
    public float4x4 viewProjection;
    public float4x4 sunViewProjection[4];
    public float4 levelOrigins[4];        // xyz = min corner in world space, w = voxel size
    public float4 levelRingOffsets[4];    // xyz = toroidal storage offset in voxels
    public float4 cameraPosition;         // xyz = world-space camera position
    public float4 sunDirection;           // normalized direction the sun light travels
    public float4 sunColorAndIntensity;   // rgb + intensity
    public float4 skyHorizonColor;        // low-frequency physical sky for voxel GI
    public float4 skyZenithColor;
    public float4 cascadeSplits;          // radial end distance of each shadow cascade
    public float4 cascadeTexelSizes;      // world units per shadow texel of each cascade
    // Clipmap shape. Named members (not a packed float4) — the C# side writes
    // each through the reflection-driven uniform buffer by name.
    public uint   voxelResolution;        // voxels per axis of each clipmap level
    public uint   levelCount;             // number of clipmap levels
    public uint   mipCount;               // radiance volume mip levels
    public bool   voxelSpecularEnabled;   // 0 disables the specular cone trace
    // Lighting state.
    public bool   shadowEnabled;          // sun shadow map bound and sampled
    public uint   numPointLights;         // point lights in the light buffer
    public float  shadowMapSize;          // sun shadow map texels per axis
    public uint   rsmCascadeIndex;        // cascade the RSM pass renders into
    // GI tunables and frame state.
    public float  emissiveScale;          // emissive material multiplier
    public float  traceMaxDistance;       // world units a GI cone may march
    public uint   traceWidth;             // trace atlas width in pixels
    public uint   traceHeight;            // trace atlas height in pixels
    public int    debugView;              // debugView enum passthrough
    public uint   gbufferWidth;           // G-buffer width in pixels
    public uint   gbufferHeight;          // G-buffer height in pixels
    public float  giSkyIntensity;         // sky light multiplier for voxel GI
    public uint   frameIndex;             // frame counter for temporal patterns
    public float  giDiffuseBias;          // constant diffuse floor added at injection
    public bool   historyValid;           // temporal history may be reprojected
    public float  diffuseSpreading;       // dual-kernel opacity bias
    // RSM sun bounce.
    public float  rsmIntensity;           // 0 disables RSM injection
    public float  rsmMaxDistance;         // world units an RSM sample may inject
    public float  rsmDepthRange;          // world->NDC-z scale of the RSM cascade
    public float  rsmMinAlbedo;           // minimum bounce albedo
    public uint   rsmResolution;          // RSM map texels per axis
    public float  rsmTexelWorldSize;      // world size of one RSM texel
};

public ParameterBlock<VoxelData> data;


// ---------------------------------------------------------------- packing ---

public uint packWords2(uint low, uint high)
{
    return (low & 65535u) | ((high & 65535u) << 16);
}

public uint quantizeVoxelAttribute(float value)
{
    return (uint)(saturate(value) * 255.0 + 0.5);
}

public uint4 packVoxelAttr(float3 albedo, float3 normal, float emissiveQ)
{
    float3 encodedNormal = normal * 0.5 + 0.5;
    uint4 packed;
    packed.x = packWords2(1u, quantizeVoxelAttribute(emissiveQ));
    packed.y = packWords2(quantizeVoxelAttribute(albedo.r), quantizeVoxelAttribute(albedo.g));
    packed.z = packWords2(quantizeVoxelAttribute(albedo.b), quantizeVoxelAttribute(encodedNormal.x));
    packed.w = packWords2(quantizeVoxelAttribute(encodedNormal.y), quantizeVoxelAttribute(encodedNormal.z));
    return packed;
}

public bool voxelAttrOccupied(uint4 attr)
{
    return (attr.x & 65535u) != 0u;
}

public void unpackVoxelAttr(uint4 attr, out float3 albedo, out float3 normal, out float emissiveQ)
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

public uint voxelResolution()
{
    return (uint)data.voxelResolution;
}

public static const uint VOXEL_BRICK_SIZE = 8u;
public static const uint VOXEL_BRICK_VOXEL_COUNT = VOXEL_BRICK_SIZE * VOXEL_BRICK_SIZE * VOXEL_BRICK_SIZE;
// The clipmap scroll quantum is one brick, so mip 3 is the coarsest mip whose
// texel grid remains world-aligned after every scroll (log2(8) = 3).
public static const float VOXEL_BRICK_ALIGNED_MAX_MIP = 3.0;

// Maps a logical clipmap voxel to its toroidal page-table slot. Page-table
// values are one-based physical page indices; zero means the brick is absent.
public uint voxelPageTableSlot(uint3 coord, uint resolution, int level)
{
    uint bricksPerAxis = resolution / VOXEL_BRICK_SIZE;
    uint3 ringBrickOffset = (uint3)data.levelRingOffsets[level].xyz / VOXEL_BRICK_SIZE;
    uint3 physicalBrick = (coord / VOXEL_BRICK_SIZE + ringBrickOffset) % bricksPerAxis;
    return physicalBrick.x
        + physicalBrick.y * bricksPerAxis
        + physicalBrick.z * bricksPerAxis * bricksPerAxis;
}

public uint voxelBrickLocalIndex(uint3 coord)
{
    uint3 local = coord % VOXEL_BRICK_SIZE;
    return local.x
        + local.y * VOXEL_BRICK_SIZE
        + local.z * VOXEL_BRICK_SIZE * VOXEL_BRICK_SIZE;
}

public uint voxelAttributeIndex(uint pageEntry, uint3 coord)
{
    return (pageEntry - 1u) * VOXEL_BRICK_VOXEL_COUNT + voxelBrickLocalIndex(coord);
}

// Normalized texture coordinates of a world position inside a clipmap level's
// slab of the shared radiance Texture3D (all levels stacked along w, each
// occupying 1/VOXEL_MAX_LEVELS of the depth at every mip). The coordinates are
// clamped to a half-texel inset of the coarsest sampled mip so hardware
// trilinear taps never bleed across slab boundaries.
public float3 voxelWorldToUVW(float3 position, int level, float mip)
{
    uint resolution = voxelResolution();
    float4 originAndSize = data.levelOrigins[level];

    // Position normalized to the level cube (mip independent).
    float extent = originAndSize.w * data.voxelResolution;
    float3 p = (position - originAndSize.xyz) / extent;

    uint sizeMip = max(resolution >> (uint)ceil(mip), 1u);
    float inset = 0.5 / (float)sizeMip;
    p = clamp(p, inset, 1.0 - inset);
    return float3(p.xy, (level + p.z) * (1.0 / VOXEL_MAX_LEVELS));
}

// ----------------------------------------------------------------- levels ---

// Whether a world position is inside the region covered by the given level.
public bool voxelLevelContains(float3 position, int level)
{
    float4 originAndSize = data.levelOrigins[level];
    float3 relative = position - originAndSize.xyz;
    float extent = originAndSize.w * data.voxelResolution;
    return all(relative >= 0.0) && all(relative < extent);
}

// The finest (smallest voxel) level covering the position; -1 when outside all.
public int voxelFindLevel(float3 position)
{
    int levelCount = (int)data.levelCount;
    for (int i = 0; i < levelCount; i++)
    {
        if (voxelLevelContains(position, i))
        {
            return i;
        }
    }
    return -1;
}

// Smooth transition weight from a clipmap level to its next coarser level.
// The outer 20 percent of the cube is a blend band; returning a smooth cubic
// weight keeps both sampled data and ray-march scale continuous at boundaries.
public float voxelLevelTransitionWeight(float3 position, int level)
{
    float4 originAndSize = data.levelOrigins[level];
    float extent = originAndSize.w * data.voxelResolution;
    float3 relative = (position - originAndSize.xyz) / extent;
    float3 distanceFromCenter = abs(relative - 0.5);
    float maximumDistance = max(distanceFromCenter.x, max(distanceFromCenter.y, distanceFromCenter.z));
    float linearWeight = saturate((maximumDistance - 0.4) / 0.1);
    return linearWeight * linearWeight * (3.0 - 2.0 * linearWeight);
}

public float voxelEffectiveVoxelSize(float3 position, int level)
{
    int levelCount = (int)data.levelCount;
    float voxelSize = data.levelOrigins[level].w;
    if (level + 1 >= levelCount)
    {
        return voxelSize;
    }
    return lerp(voxelSize, data.levelOrigins[level + 1].w, voxelLevelTransitionWeight(position, level));
}

// The physical atmosphere contains more angular detail than six voxel cones
// can sample without aliasing against normal maps. The CPU evaluates and
// azimuthally prefilters the current atmosphere into this smooth two-color
// representation; the visible sky and direct sun retain their full detail.
public float3 voxelSkyColor(float3 direction)
{
    float zenithWeight = pow(saturate(direction.z), 0.6);
    return lerp(data.skyHorizonColor.rgb, data.skyZenithColor.rgb, zenithWeight) * data.giSkyIntensity;
}
