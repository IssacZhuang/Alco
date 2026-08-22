// file "alco-world3d-ssr-common.slang" - module "alco_world3d_ssr_common"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/SsrCommon.hlsli (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module alco_world3d_ssr_common;

import alco_world3d_reversed_depth;
import alco_rendering_core;
import alco_world3d_voxel_common;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)


// Module-owned declarations of the include-era contract documented above:
// the bindings and callback functions the including .hlsl had to provide.
// Nothing imports this module today; these keep it loadable and give a future
// importer the documented surface (the Hi-Z pyramid, the G-buffer normal and
// depth, world reconstruction and screen-radiance approximation).
[[vk::binding(0, 1)]] public Texture2D<float4> _depthPyramid;
[[vk::binding(1, 1)]] public Texture2D _normal;
[[vk::binding(2, 1)]] [[vk::image_format("r32f")]] public Texture2D<float> _gbufferDepth;

public float3 ReconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

// TODO(phase 3): the include-era consumers approximated hit radiance from the
// shaded scene; no scene-color binding exists in this contract, so the
// placeholder returns black until a real consumer owns this function.
public float3 ApproximateScreenSurfaceRadiance(int2 pixel, float3 normal, float3 worldPosition)
{
    return float3(0.0, 0.0, 0.0);
}

// Screen-space reflection (Hi-Z hierarchical raymarch, GGX VNDF sampling,
// temporal AABB clip). Adapted from FidelityFX SSSR.
//
// Pure algorithm — no resource bindings. The including .hlsl must define:
//   - Bindings: _gbufferDepth, _normal, _ssrHistory, _depthPyramid,
//     _ssrHistoryOut
//   - Functions: ReconstructWorldPosition, ApproximateScreenSurfaceRadiance
// The shared cbuffer _data (viewProjection, giParams2, giFrameParams, …) comes
// from VoxelCommon.hlsli.





#define SSR_FLOAT_MAX 3.402823466e+38
#define SSR_DEPTH_HIERARCHY_MAX_MIP 6

// --- Screen-space projection helpers -----------------------------------------

// Transform a world-space position to screen UV [0,1]³ with NDC z in .z.
public float3 SsrProjectPosition(float3 worldPos, float4x4 mat)
{
    float4 projected = mul(mat, float4(worldPos, 1.0));
    projected.xyz /= projected.w;
    projected.xy = projected.xy * 0.5 + 0.5;
    projected.y = 1.0 - projected.y;
    return projected.xyz;
}

// Project a direction as the delta between projected (origin+dir) and projectedOrigin.
public float3 SsrProjectDirection(float3 origin, float3 direction, float3 screenOrigin, float4x4 mat)
{
    return SsrProjectPosition(origin + direction, mat) - screenOrigin;
}

// --- Hi-Z hierarchical raymarch ----------------------------------------------

// Intersect a ray with the cell boundaries in screen UV space and advance.
// Returns true if the ray skipped a tile (went coarser); false if it hit the surface.
public bool SsrAdvanceRay(
    float3 origin, float3 direction, float3 invDirection,
    float2 currentMipPosition, float2 currentMipResolutionInv,
    float2 floorOffset, float2 uvOffset, float surfaceZ,
    inout float3 position, inout float currentT)
{
    float2 xyPlane = floor(currentMipPosition) + floorOffset;
    xyPlane = xyPlane * currentMipResolutionInv + uvOffset;
    float3 boundaryPlanes = float3(xyPlane, surfaceZ);

    float3 t = boundaryPlanes * invDirection - origin * invDirection;
    // Reversed depth (near = 1, far = 0): rays marching away from the camera
    // move toward decreasing z, so only negative-z crossings can reach the
    // surface depth plane.
    t.z = direction.z < 0.0 ? t.z : SSR_FLOAT_MAX;

    float tMin = min(min(t.x, t.y), t.z);
    // Reversed depth: "above the surface" (in front of it, nearer) is the
    // larger NDC z.
    bool aboveSurface = position.z > surfaceZ;
    bool skippedTile = (asuint(tMin) != asuint(t.z)) && aboveSurface;

    currentT = aboveSurface ? tMin : currentT;
    position = origin + currentT * direction;
    return skippedTile;
}

// Initial advance to get past the starting cell and avoid self-intersection.
public void SsrInitialAdvanceRay(
    float3 origin, float3 direction, float3 invDirection,
    float2 currentMipResolution, float2 currentMipResolutionInv,
    float2 floorOffset, float2 uvOffset,
    out float3 position, out float currentT)
{
    float2 currentMipPosition = currentMipResolution * origin.xy;
    float2 xyPlane = floor(currentMipPosition) + floorOffset;
    xyPlane = xyPlane * currentMipResolutionInv + uvOffset;
    float2 t = xyPlane * invDirection.xy - origin.xy * invDirection.xy;
    currentT = min(t.x, t.y);
    position = origin + currentT * direction;
}

public float2 SsrGetMipResolution(float2 screenDimensions, int mipLevel)
{
    return screenDimensions * pow(0.5, mipLevel);
}

// Hierarchical raymarch through the depth pyramid. Origin and direction must
// be in screen UV space [0,1]³. Returns the hit position; validHit reports
// whether the loop completed within the step budget.
public float3 SsrHierarchicalRaymarch(
    float3 origin, float3 direction, bool isMirror,
    float2 screenSize, int mostDetailedMip,
    uint maxTraversalIntersections,
    out bool validHit)
{
    float3 invDirection = (direction.x != 0.0 || direction.y != 0.0 || direction.z != 0.0)
        ? float3(1.0, 1.0, 1.0) / direction
        : float3(SSR_FLOAT_MAX, SSR_FLOAT_MAX, SSR_FLOAT_MAX);

    int currentMip = mostDetailedMip;
    float2 currentMipResolution = SsrGetMipResolution(screenSize, currentMip);
    float2 currentMipResolutionInv = rcp(currentMipResolution);

    float2 uvOffset = 0.005 * exp2(mostDetailedMip) / screenSize;
    uvOffset.x = direction.x < 0.0 ? -uvOffset.x : uvOffset.x;
    uvOffset.y = direction.y < 0.0 ? -uvOffset.y : uvOffset.y;

    float2 floorOffset;
    floorOffset.x = direction.x < 0.0 ? 0.0 : 1.0;
    floorOffset.y = direction.y < 0.0 ? 0.0 : 1.0;

    float currentT;
    float3 position;
    SsrInitialAdvanceRay(origin, direction, invDirection,
        currentMipResolution, currentMipResolutionInv, floorOffset, uvOffset,
        position, currentT);

    bool exitDueToLowOccupancy = false;
    uint i = 0u;
    while (i < maxTraversalIntersections && currentMip >= 0 && !exitDueToLowOccupancy)
    {
        float2 currentMipPosition = currentMipResolution * position.xy;
        int2 mipPixel = int2(floor(currentMipPosition));
        float surfaceZ = _depthPyramid.Load(int3(mipPixel.x, mipPixel.y, currentMip)).x;
        bool skippedTile = SsrAdvanceRay(origin, direction, invDirection,
            currentMipPosition, currentMipResolutionInv, floorOffset, uvOffset,
            surfaceZ, position, currentT);

        bool nextMipOutOfRange = skippedTile && (currentMip >= SSR_DEPTH_HIERARCHY_MAX_MIP);
        if (!nextMipOutOfRange)
        {
            currentMip += skippedTile ? 1 : -1;
            currentMipResolution *= skippedTile ? 0.5 : 2.0;
            currentMipResolutionInv *= skippedTile ? 2.0 : 0.5;
        }
        ++i;
    }

    validHit = i <= maxTraversalIntersections;
    return position;
}

// --- Hit validation ----------------------------------------------------------

// Validate a hit: reject offscreen, self-reflection, backface, and background.
// Returns a confidence weight combining vignette and depth-consistency.
public float SsrValidateHit(float3 hit, float2 uv, float3 worldRayDirection, float2 screenSize, float depthBufferThickness)
{
    if (hit.x < 0.0 || hit.y < 0.0 || hit.x > 1.0 || hit.y > 1.0)
        return 0.0;

    float2 manhattanDist = abs(hit.xy - uv);
    if (manhattanDist.x < (2.0 / screenSize.x) && manhattanDist.y < (2.0 / screenSize.y))
        return 0.0;

    int2 texelCoords = int2(floor(screenSize * hit.xy));
    float surfaceZ = _depthPyramid.Load(int3(texelCoords.x, texelCoords.y, 0)).x;
    if (IS_SKY_DEPTH(surfaceZ))
        return 0.0;

    float3 hitNormal = normalize(GET_PIXEL_TEX2D(_normal, texelCoords).xyz * 2.0 - 1.0);
    if (dot(hitNormal, worldRayDirection) > 0.0)
        return 0.0;

    // Thickness test in NDC depth space (not world space) — matches FidelityFX SSSR.
    float depthDiff = abs(surfaceZ - hit.z);
    float confidence = 1.0 - smoothstep(0.0, depthBufferThickness, depthDiff);
    confidence *= confidence;
    if (confidence <= 0.0)
        return 0.0;

    float2 fov = 0.05 * float2(screenSize.y / screenSize.x, 1.0);
    float2 border = smoothstep(float2(0.0, 0.0), fov, hit.xy)
                  * (1.0 - smoothstep(float2(1.0, 1.0) - fov, float2(1.0, 1.0), hit.xy));
    float vignette = border.x * border.y;

    return vignette * confidence;
}

// --- GGX VNDF importance sampling (Eric Heitz 2018) -------------------------

public float3 SampleGGXVNDF(float3 Ve, float alphaX, float alphaY, float U1, float U2)
{
    float3 Vh = normalize(float3(alphaX * Ve.x, alphaY * Ve.y, Ve.z));
    float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
    float3 T1 = lensq > 0.0 ? float3(-Vh.y, Vh.x, 0.0) * rsqrt(lensq) : float3(1.0, 0.0, 0.0);
    float3 T2 = cross(Vh, T1);
    float r = sqrt(U1);
    float phi = 2.0 * PI * U2;
    float t1 = r * cos(phi);
    float t2 = r * sin(phi);
    float s = 0.5 * (1.0 + Vh.z);
    t2 = (1.0 - s) * sqrt(max(1.0 - t1 * t1, 0.0)) + s * t2;
    float3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * Vh;
    return normalize(float3(alphaX * Nh.x, alphaY * Nh.y, max(0.0, Nh.z)));
}

// Interleaved gradient noise for per-pixel, per-frame stochastic variation.
public float SsrIGN(uint2 pixel, uint frame)
{
    return frac(52.9829189 * frac(dot(float2(pixel),
        float2(0.06711056, 0.00583715))) + frame * 2.3992821);
}

// --- Temporal denoising: AABB clip (Playdead TRAA) --------------------------

public float3 SsrClipAABB(float3 aabbMin, float3 aabbMax, float3 prevSample)
{
    float3 center = 0.5 * (aabbMax + aabbMin);
    float3 extent = 0.5 * (aabbMax - aabbMin) + 0.001;
    float3 clipped = abs((prevSample - center) / extent);
    float maxUnit = max(max(clipped.x, clipped.y), clipped.z);
    if (maxUnit > 1.0)
        return center + (prevSample - center) / maxUnit;
    return prevSample;
}

// --- Entry point: Hi-Z screen-space reflection ------------------------------

// Raymarches the depth pyramid in screen UV space, validates the hit, and
// returns radiance + confidence. Depends on ReconstructWorldPosition and
// ApproximateScreenSurfaceRadiance from the including file.
public float4 TraceScreenSpaceReflection(
    float3 worldPosition,
    float3 reflectDirection,
    float roughness,
    float2 uv)
{
    float2 screenSize = float2(giParams2.y, giParams2.z);

    float3 screenOrigin = SsrProjectPosition(worldPosition, viewProjection);
    float3 screenDir = SsrProjectDirection(worldPosition, reflectDirection, screenOrigin, viewProjection);

    bool isMirror = roughness < 0.001;
    int mostDetailedMip = isMirror ? 0 : 2;  // start coarser to avoid self-hit

    bool validHit = false;
    float3 hit = SsrHierarchicalRaymarch(
        screenOrigin, screenDir, isMirror, screenSize,
        mostDetailedMip, 30u, validHit);

    float3 worldRay = ReconstructWorldPosition(hit.xy, hit.z) - worldPosition;
    float confidence = SsrValidateHit(hit, uv, worldRay, screenSize, 0.15);

    if (confidence > 0.0)
    {
        int2 hitPixel = clamp(int2(floor(screenSize * hit.xy)), int2(0, 0), int2(screenSize) - 1);
        float hitDepth = GetPixelDepth2D(_gbufferDepth, hitPixel);
        float3 hitWorldPos = ReconstructWorldPosition(hit.xy, hitDepth);
        float3 hitNormal = normalize(GET_PIXEL_TEX2D(_normal, hitPixel).xyz * 2.0 - 1.0);
        return float4(
            ApproximateScreenSurfaceRadiance(hitPixel, hitNormal, hitWorldPos),
            confidence * (1.0 - roughness));
    }
    return float4(0.0, 0.0, 0.0, 0.0);
}
