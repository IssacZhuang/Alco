#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Multi-bounce light propagation for the voxel GI clipmap: sparse dispatch
// over a brick list (resident bricks only — freed bricks are handled by the
// inject pass). Each occupied voxel traces a set of cones through the radiance
// volume to gather incoming indirect light, multiplies by the surface albedo
// and writes the sum of existing direct radiance plus the new bounce back into
// a temporary texture. A follow-up copy pass (VoxelBounceApply.hlsl) transfers
// the result into the radiance Texture3D mip 0, after which the mip chain is
// rebuilt.
//
// On the first bounce (bounceIndex == 0), unoccluded cones fall back to the
// sky gradient. This is the primary path by which sky light enters the voxel
// volume — matching CE5 SVOGI where bAllowSkyLight = (nPassId == 0) in
// ComputePropagateLighting. The hemisphere of cones provides a proper
// integration of sky irradiance with natural occlusion from nearby geometry,
// which is far more accurate than the single-direction sky sample the inject
// pass uses. Subsequent bounces exclude sky to avoid double-counting.

struct VoxelPropagateConstants
{
    float4 params; // x=levelIndex, y=bounceStrength, z=bounceIndex, w=unused
};

DEFINE_TEX3D_SAMPLE(1, _radiance);
DEFINE_STORAGE(2, uint4, _attrStatic);
DEFINE_STORAGE(3, uint4, _attrDynamic);
DEFINE_TEX3D_STORAGE(4, _propagateOut, float4, "rgba16f");
// Combined page table: x=static, y=dynamic. Saves a descriptor set for the
// brick list.
DEFINE_STORAGE(5, uint2, _pageTable);
DEFINE_TEX3D_SAMPLE(7, _opacity);
DEFINE_STORAGE(6, uint4, _brickList);

PUSH_CONSTANT VoxelPropagateConstants constants;

// --- Propagation cone set ---------------------------------------------------
// Nine wide cones cover the hemisphere for cached propagation: 1 cone at
// θ=0°, 4 at θ=45° and 4 at θ=75°. The final screen-space gather uses the
// separate rotation-balanced narrow-cone kernel from VoxelTrace.hlsl.
static const uint PROP_CONE_COUNT = 9u;
static const float PROP_CONE_APERTURE = 0.57735; // tan(30°)

static const float3 PROP_CONE_DIRECTIONS[9] = {
    float3( 0.00000,  0.00000,  1.00000), // θ=0°
    float3( 0.70711,  0.00000,  0.70711), // θ=45°, φ=0°
    float3( 0.00000,  0.70711,  0.70711), // θ=45°, φ=90°
    float3(-0.70711,  0.00000,  0.70711), // θ=45°, φ=180°
    float3( 0.00000, -0.70711,  0.70711), // θ=45°, φ=270°
    float3( 0.68301,  0.68301,  0.25882), // θ=75°, φ=45°
    float3(-0.68301,  0.68301,  0.25882), // θ=75°, φ=135°
    float3(-0.68301, -0.68301,  0.25882), // θ=75°, φ=225°
    float3( 0.68301, -0.68301,  0.25882), // θ=75°, φ=315°
};

static const float PROP_CONE_WEIGHTS[9] = {
    1.00000,
    0.70711, 0.70711, 0.70711, 0.70711,
    0.25882, 0.25882, 0.25882, 0.25882,
};

float3x3 GetTangentBasis(float3 normal)
{
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    return float3x3(tangent, bitangent, normal);
}

float4 SamplePropagationBlended(float3 position, int level, float mip, float3 absoluteDirection, bool enableBlend)
{
    float3 uvw = VoxelWorldToUVW(position, level, mip);
    float4 radiance = SAMPLE_TEX3D_LEVEL(_radiance, uvw, mip);
    float4 opacity = SAMPLE_TEX3D_LEVEL(_opacity, uvw, mip);
    if (enableBlend)
    {
        int levelCount = (int)clipmapParams.y;
        if (level + 1 < levelCount)
        {
            float transitionWeight = VoxelLevelTransitionWeight(position, level);
            if (transitionWeight > 0.001)
            {
                float nextMip = clamp(
                    mip + log2(levelOrigins[level].w / levelOrigins[level + 1].w),
                    0.0,
                    clipmapParams.z - 1.0);
                float3 nextUVW = VoxelWorldToUVW(position, level + 1, nextMip);
                float4 nextRadiance = SAMPLE_TEX3D_LEVEL(_radiance, nextUVW, nextMip);
                float4 nextOpacity = SAMPLE_TEX3D_LEVEL(_opacity, nextUVW, nextMip);
                radiance = lerp(radiance, nextRadiance, transitionWeight);
                opacity = lerp(opacity, nextOpacity, transitionWeight);
            }
        }
    }

    // Match CE5's directional opacity projection. An isotropic occupancy floor
    // makes thin surfaces behave like solid volume and over-occludes sky light.
    float voxelAlpha = saturate(dot(opacity.xyz, absoluteDirection));
    return float4(radiance.rgb, voxelAlpha);
}

// Truncated cone trace for propagation: fewer steps and shorter range than the
// screen-space version. On the first bounce (allowSky = true), unoccluded
// cone fractions fall back to the sky gradient so that sky light is gathered
// from the full hemisphere and spread through the volume. Uses anisotropic
// directional opacity projected onto |rayDir|.
float3 TracePropagationCone(float3 startPosition, float3 direction,
    float apertureTan, float maxDistance, bool allowSky)
{
    float mipCount = clipmapParams.z;
    float fineVoxelSize = levelOrigins[0].w;
    float3 color = 0.0;
    float alpha = 0.0;
    int startLevel = VoxelFindLevel(startPosition);
    float t = startLevel >= 0
        ? VoxelEffectiveVoxelSize(startPosition, startLevel) * 0.5
        : fineVoxelSize * 0.5;
    float3 absDir = abs(direction);
    int prevLevel = -2;
    float effectiveVoxelSize = fineVoxelSize;

    [loop]
    for (int step = 0; step < 32 && t <= maxDistance && alpha < 0.95; step++)
    {
        float3 position = startPosition + direction * t;
        int level = VoxelFindLevel(position);
        if (level < 0)
        {
            break;
        }

        bool levelChanged = level != prevLevel;
        if (levelChanged)
        {
            effectiveVoxelSize = VoxelEffectiveVoxelSize(position, level);
            prevLevel = level;
        }

        float voxelSize = levelOrigins[level].w;
        float diameter = max(2.0 * t * apertureTan, voxelSize);
        // Clipmap origins move in whole 8-voxel bricks. Mips above 3 are not
        // invariant under that translation, so their downsample lattice changes
        // phase whenever the camera crosses a brick boundary.
        float mip = clamp(log2(diameter / voxelSize), 0.0,
            min(mipCount - 1.0, VOXEL_BRICK_ALIGNED_MAX_MIP));
        float4 sample = SamplePropagationBlended(position, level, mip, absDir, levelChanged);
        float marchDistance = max(effectiveVoxelSize * 0.5, diameter * 0.5);

        color += (1.0 - alpha) * sample.a * sample.rgb;
        alpha += (1.0 - alpha) * sample.a;
        t += marchDistance;
    }

    // Sky fallback on first bounce only: provides hemisphere-integrated sky
    // ambient with natural occlusion from accumulated cone alpha. A narrow
    // horizon blend replaces the discontinuous bottom-light cutoff.
    if (allowSky)
    {
        float horizonVisibility = smoothstep(-0.12, 0.12, direction.z);
        color += (1.0 - alpha) * VoxelSkyColor(direction) * horizonVisibility;
    }

    return color;
}

[shader("compute")]
[numthreads(4, 4, 4)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = VoxelResolution();
    int level = (int)constants.params.x;
    uint brickIndex = dispatchId.z / VOXEL_BRICK_SIZE;
    uint localZ = dispatchId.z % VOXEL_BRICK_SIZE;
    uint3 logicalCoord = _brickList[brickIndex].xyz * VOXEL_BRICK_SIZE
        + uint3(dispatchId.x, dispatchId.y, localZ);
    if (any(logicalCoord >= resolution))
    {
        return;
    }

    float bounceStrength = constants.params.y;
    uint bounceIndex = (uint)constants.params.z;
    bool allowSky = bounceIndex == 0u;
    float4 originAndSize = levelOrigins[level];
    float voxelSize = originAndSize.w;

    uint3 storeCoord = uint3(logicalCoord.x, logicalCoord.y, (uint)level * resolution + logicalCoord.z);

    // Read attributes (dynamic wins over static), same as the injection pass.
    uint pageSlot = VoxelPageTableSlot(logicalCoord, resolution, level);
    uint2 pages = _pageTable[pageSlot];
    uint4 attr = uint4(0u, 0u, 0u, 0u);
    if (pages.y != 0u)
    {
        attr = _attrDynamic[VoxelAttributeIndex(pages.y, logicalCoord)];
    }
    if (!VoxelAttrOccupied(attr))
    {
        if (pages.x != 0u)
        {
            attr = _attrStatic[VoxelAttributeIndex(pages.x, logicalCoord)];
        }
    }

    // Preserve the current radiance (direct lighting from injection) so the
    // copy-back step replaces mip 0 with direct + bounce.
    float3 currentRadiance = SAMPLE_TEX3D_LEVEL(
        _radiance,
        VoxelWorldToUVW(originAndSize.xyz + (float3(logicalCoord) + 0.5) * voxelSize, level, 0.0),
        0.0).rgb;

    if (!VoxelAttrOccupied(attr))
    {
        _propagateOut[storeCoord] = float4(currentRadiance, 0.0);
        return;
    }

    float3 albedo;
    float3 normal;
    float emissiveQ;
    UnpackVoxelAttr(attr, albedo, normal, emissiveQ);
    normal = dot(normal, normal) > 1e-6 ? normalize(normal) : float3(0.0, 0.0, 1.0);

    float3 worldPosition = originAndSize.xyz + (float3(logicalCoord) + 0.5) * voxelSize;
    float receiverBias = max(levelOrigins[0].w * 2.0, voxelSize * 0.5);
    float3 startPosition = worldPosition + normal * receiverBias;

    // Trace a hemisphere of cones to gather incoming indirect light.
    float3x3 tbn = GetTangentBasis(normal);
    float3 gathered = 0.0;
    float totalWeight = 0.0;
    // CE5 uses ConeMaxLength=12m as a global fixed distance. Using the full
    // level extent (instead of half) lets coarser levels propagate much
    // further, which is essential for sky light to reach into shadowed areas.
    float maxDistance = voxelSize * resolution;
    [unroll]
    for (uint i = 0u; i < PROP_CONE_COUNT; i++)
    {
        float3 worldDir = mul(PROP_CONE_DIRECTIONS[i], tbn);
        gathered += TracePropagationCone(startPosition, worldDir, PROP_CONE_APERTURE, maxDistance, allowSky)
            * PROP_CONE_WEIGHTS[i];
        totalWeight += PROP_CONE_WEIGHTS[i];
    }
    gathered /= max(totalWeight, 0.0001);

    // CE5 PropagationBooster (default 1.5): pow(collected, 1/booster) brightens
    // midtones so that dim bounce light propagates further instead of collapsing
    // to near-zero after one bounce. 0.05 → 0.136, 0.1 → 0.215, 0.2 → 0.342.
    gathered = pow(max(gathered, 0.0), 1.0 / 1.5);

    // Bounce = Lambert BRDF × incoming indirect irradiance, modulated by
    // strength. Clamp dark albedos to a minimum reflectance so that very dark
    // surfaces still contribute meaningful bounce light — matching CE5
    // SVOGI's e_svoTI_MinReflectance (default 0.2).
    float3 bounceAlbedo = albedo + saturate(0.2 - dot(albedo, 0.333));
    float3 bounce = bounceAlbedo * gathered * bounceStrength;

    _propagateOut[storeCoord] = float4(currentRadiance + bounce, 1.0);
}
