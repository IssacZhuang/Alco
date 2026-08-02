#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Multi-bounce light propagation for the voxel GI clipmap: one dispatch per
// clipmap level at (resolution, resolution, resolution). Each occupied voxel
// traces a small set of cones through the radiance volume to gather incoming
// indirect light, multiplies by the surface albedo and writes the sum of
// existing direct radiance plus the new bounce back into a temporary texture.
// A follow-up copy pass (VoxelBounceApply.hlsl) transfers the result into the
// radiance Texture3D mip 0, after which the mip chain is rebuilt.

struct VoxelPropagateConstants
{
    float4 params; // x=levelIndex, y=bounceStrength, zw=unused
};

DEFINE_TEX3D_SAMPLE(1, _radiance);
DEFINE_STORAGE(2, uint2, _attrStatic);
DEFINE_STORAGE(3, uint2, _attrDynamic);
DEFINE_TEX3D_STORAGE(4, _propagateOut, float4, "rgba16f");
DEFINE_STORAGE(5, uint, _pageTableStatic);
DEFINE_STORAGE(6, uint, _pageTableDynamic);
DEFINE_TEX3D_SAMPLE(7, _opacity);

PUSH_CONSTANT VoxelPropagateConstants constants;

// --- Propagation cone set ---------------------------------------------------
// 4 cones: 1 along the normal + 3 at ~55° spreading across the hemisphere.
// Coarser than the 9-cone screen-space set because this runs per-voxel (up to
// 128^3 * 4 levels dispatch).  The wider half-angle compensates for the lower
// cone count by covering more solid angle per cone.
static const uint PROP_CONE_COUNT = 4u;
static const float PROP_CONE_APERTURE = 0.86603; // tan(40.9°) — wide cones

static const float3 PROP_CONE_DIRECTIONS[4] = {
    float3(0.00000, 0.00000, 1.00000), // θ=0°  (along normal)
    float3(0.57358, 0.00000, 0.81915), // θ=55°, φ=0°
    float3(-0.28679, 0.49607, 0.81915), // θ=55°, φ=120°
    float3(-0.28679, -0.49607, 0.81915), // θ=55°, φ=240°
};

static const float PROP_CONE_WEIGHTS[4] = {
    1.00000,
    0.81915, 0.81915, 0.81915,
};

float3x3 GetTangentBasis(float3 normal)
{
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    return float3x3(tangent, bitangent, normal);
}

// Truncated cone trace for propagation: fewer steps and shorter range than the
// screen-space version. Returns gathered radiance (without sky fallback — bounce
// should not add sky light that the voxel already receives from injection).
// Uses anisotropic directional opacity projected onto |rayDir|.
float3 TracePropagationCone(float3 startPosition, float3 direction, float apertureTan, float maxDistance)
{
    float mipCount = clipmapParams.z;
    float fineVoxelSize = levelOrigins[0].w;
    float3 color = 0.0;
    float alpha = 0.0;
    float t = fineVoxelSize;
    float3 absDir = abs(direction);

    [loop]
    for (int step = 0; step < 16 && t <= maxDistance && alpha < 0.95; step++)
    {
        float3 position = startPosition + direction * t;
        int level = VoxelFindLevel(position);
        if (level < 0)
        {
            break;
        }

        float voxelSize = levelOrigins[level].w;
        float diameter = max(2.0 * t * apertureTan, voxelSize);
        float mip = clamp(log2(diameter / voxelSize), 0.0, mipCount - 1.0);
        float3 uvw = VoxelWorldToUVW(position, level, mip);
        float4 radSample = SAMPLE_TEX3D_LEVEL(_radiance, uvw, mip);
        float4 opaSample = SAMPLE_TEX3D_LEVEL(_opacity, uvw, mip);
        float voxelAlpha = dot(opaSample.xyz, absDir);
        voxelAlpha = max(voxelAlpha, radSample.a * 0.3);

        color += (1.0 - alpha) * voxelAlpha * radSample.rgb;
        alpha += (1.0 - alpha) * voxelAlpha;
        t += max(voxelSize, diameter * 0.5);
    }

    return color;
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
    float bounceStrength = constants.params.y;
    float4 originAndSize = levelOrigins[level];
    float voxelSize = originAndSize.w;

    uint3 storeCoord = uint3(dispatchId.x, dispatchId.y, (uint)level * resolution + dispatchId.z);

    // Read attributes (dynamic wins over static), same as the injection pass.
    uint pageSlot = VoxelPageTableSlot(dispatchId, resolution, level);
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

    // Preserve the current radiance (direct lighting from injection) so the
    // copy-back step replaces mip 0 with direct + bounce.
    float3 currentRadiance = SAMPLE_TEX3D_LEVEL(
        _radiance,
        VoxelWorldToUVW(originAndSize.xyz + (float3(dispatchId) + 0.5) * voxelSize, level, 0.0),
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
    normal = normalize(normal);

    float3 worldPosition = originAndSize.xyz + (float3(dispatchId) + 0.5) * voxelSize;
    float3 startPosition = worldPosition + normal * voxelSize * 1.5;

    // Trace a small hemisphere of cones to gather incoming indirect light.
    float3x3 tbn = GetTangentBasis(normal);
    float3 gathered = 0.0;
    float totalWeight = 0.0;
    float maxDistance = voxelSize * resolution * 0.5;
    [unroll]
    for (uint i = 0u; i < PROP_CONE_COUNT; i++)
    {
        float3 worldDir = mul(PROP_CONE_DIRECTIONS[i], tbn);
        gathered += TracePropagationCone(startPosition, worldDir, PROP_CONE_APERTURE, maxDistance)
            * PROP_CONE_WEIGHTS[i];
        totalWeight += PROP_CONE_WEIGHTS[i];
    }
    gathered /= max(totalWeight, 0.0001);

    // Bounce = surface reflectance × incoming indirect, modulated by strength.
    float3 bounce = albedo * gathered * bounceStrength;

    _propagateOut[storeCoord] = float4(currentRadiance + bounce, 1.0);
}
