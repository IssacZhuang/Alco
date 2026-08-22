// file "Voxelize.slang" - module "voxelize"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/Voxelize.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module voxelize;

import alco_rendering_core;
import alco_world3d_voxel_common;

// Compute triangle voxelization for the voxel GI clipmap: one dispatch per mesh
// per clipmap level, one thread per triangle and z-slab (y dimension = 8 slabs
// splitting the triangle's voxel-space AABB along z). Writes packed attribute
// voxels as 16-bit fixed-point sums. Contributions from overlapping triangles
// are merged with commutative atomic additions and averaged during injection.
// The voxel-space AABB is additionally clamped to the brick range dirtied this
// frame (params2.yz), so retained bricks are not re-scanned or re-accumulated.
//
// Vertex data is read as raw uints from a copy of the mesh's vertex buffer;
// the layout must be position(3) / normal(3) / uv(2) floats at the head of
// each vertex, with the stride given in push constants (32 or 48 bytes).

struct VoxelizeConstants
{
    float4x4 model;
    float4 baseColor;      // linear rgb, a multiplies the albedo texture alpha
    float4 emissive;       // rgb emissive factor (multiplies the emissive texture)
    float4 params;         // x=levelIndex, y=indexIs16Bit, z=vertexStrideUints, w=alphaCutoff
    float4 params2;        // x=triangleCount, yz=packed voxel-space dirty range lo/hi (x|y<<8|z<<16), w=unused
};

[[vk::binding(0, 1)]] RWStructuredBuffer<uint> _vertices;
[[vk::binding(0, 2)]] RWStructuredBuffer<uint> _indices;
// Use a flat uint view for atomics. The same buffer is read as uint4 by later
// passes; the byte layout is identical, while Vulkan atomics require the target
// expression to be a direct runtime-array element rather than a vector member.
[[vk::binding(0, 3)]] RWStructuredBuffer<uint> _attrOut;
[[vk::binding(0, 4)]] Texture2D _albedoTexture;
[[vk::binding(1, 4)]] SamplerState _albedoTextureSampler;
[[vk::binding(0, 5)]] Texture2D _emissiveTexture;
[[vk::binding(1, 5)]] SamplerState _emissiveTextureSampler;
[[vk::binding(0, 6)]] RWStructuredBuffer<uint> _pageTable;

[[vk::push_constant]] VoxelizeConstants constants;

uint LoadIndex(uint i)
{
    if (constants.params.y > 0.5)
    {
        uint packed = _indices[i >> 1];
        return (i & 1u) == 0u ? packed & 0xFFFFu : packed >> 16;
    }
    return _indices[i];
}

float3 LoadFloat3(uint base)
{
    return float3(asfloat(_vertices[base]), asfloat(_vertices[base + 1]), asfloat(_vertices[base + 2]));
}

void AtomicAccumulateVoxel(uint index, float3 albedo, float3 normal, float emissiveQ)
{
    uint laneIndex = index * 4u;
    uint previousCountWord;
    InterlockedAdd(_attrOut[laneIndex], 1u, previousCountWord);
    if ((previousCountWord & 65535u) >= 255u)
    {
        return;
    }

    uint4 contribution = PackVoxelAttr(albedo, normal, emissiveQ);
    InterlockedAdd(_attrOut[laneIndex], contribution.x & 0xFFFF0000u);
    InterlockedAdd(_attrOut[laneIndex + 1u], contribution.y);
    InterlockedAdd(_attrOut[laneIndex + 2u], contribution.z);
    InterlockedAdd(_attrOut[laneIndex + 3u], contribution.w);
}

// Separating-axis test between an axis-aligned box and a triangle.
bool AxisSeparates(float3 axis, float3 v0, float3 v1, float3 v2, float3 boxHalf)
{
    float p0 = dot(v0, axis);
    float p1 = dot(v1, axis);
    float p2 = dot(v2, axis);
    float minP = min(p0, min(p1, p2));
    float maxP = max(p0, max(p1, p2));
    float radius = boxHalf.x * abs(axis.x) + boxHalf.y * abs(axis.y) + boxHalf.z * abs(axis.z);
    return minP > radius || maxP < -radius;
}

bool TriBoxOverlap(float3 boxCenter, float3 boxHalf, float3 v0, float3 v1, float3 v2)
{
    v0 -= boxCenter;
    v1 -= boxCenter;
    v2 -= boxCenter;

    float3 e0 = v1 - v0;
    float3 e1 = v2 - v1;
    float3 e2 = v0 - v2;

    // 9 axes: triangle edges crossed with the box axes.
    if (AxisSeparates(cross(e0, float3(1, 0, 0)), v0, v1, v2, boxHalf)) return false;
    if (AxisSeparates(cross(e0, float3(0, 1, 0)), v0, v1, v2, boxHalf)) return false;
    if (AxisSeparates(cross(e0, float3(0, 0, 1)), v0, v1, v2, boxHalf)) return false;
    if (AxisSeparates(cross(e1, float3(1, 0, 0)), v0, v1, v2, boxHalf)) return false;
    if (AxisSeparates(cross(e1, float3(0, 1, 0)), v0, v1, v2, boxHalf)) return false;
    if (AxisSeparates(cross(e1, float3(0, 0, 1)), v0, v1, v2, boxHalf)) return false;
    if (AxisSeparates(cross(e2, float3(1, 0, 0)), v0, v1, v2, boxHalf)) return false;
    if (AxisSeparates(cross(e2, float3(0, 1, 0)), v0, v1, v2, boxHalf)) return false;
    if (AxisSeparates(cross(e2, float3(0, 0, 1)), v0, v1, v2, boxHalf)) return false;

    // Triangle AABB against the box.
    float3 boxMin = min(v0, min(v1, v2));
    float3 boxMax = max(v0, max(v1, v2));
    if (any(boxMax < -boxHalf) || any(boxMin > boxHalf)) return false;

    // Triangle plane.
    if (AxisSeparates(cross(e0, e1), v0, v1, v2, boxHalf)) return false;
    return true;
}

[shader("compute")]
[numthreads(64, 1, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint triangleIndex = dispatchId.x;
    if (triangleIndex >= (uint)constants.params2.x)
    {
        return;
    }

    int level = (int)constants.params.x;
    uint strideUints = (uint)constants.params.z;
    float4 originAndSize = levelOrigins[level];
    float voxelSize = originAndSize.w;
    uint resolution = VoxelResolution();

    uint i0 = LoadIndex(triangleIndex * 3);
    uint i1 = LoadIndex(triangleIndex * 3 + 1);
    uint i2 = LoadIndex(triangleIndex * 3 + 2);

    float3 w0 = mul(constants.model, float4(LoadFloat3(i0 * strideUints), 1.0)).xyz;
    float3 w1 = mul(constants.model, float4(LoadFloat3(i1 * strideUints), 1.0)).xyz;
    float3 w2 = mul(constants.model, float4(LoadFloat3(i2 * strideUints), 1.0)).xyz;

    // Early-out when the triangle's world AABB misses the level region.
    float3 worldMin = min(w0, min(w1, w2));
    float3 worldMax = max(w0, max(w1, w2));
    float extent = voxelSize * resolution;
    if (any(worldMax < originAndSize.xyz) || any(worldMin >= originAndSize.xyz + extent))
    {
        return;
    }

    // Voxel-space AABB clamped to the grid, then to this frame's dirty range
    // (packed voxel coordinates in params2.yz). Only bricks queued for rebuild
    // are (re)voxelized: a triangle missing the dirty region skips all
    // remaining per-triangle work, and retained bricks are neither re-scanned
    // nor re-accumulated when the camera scrolls the clipmap.
    float3 gridMin = (worldMin - originAndSize.xyz) / voxelSize;
    float3 gridMax = (worldMax - originAndSize.xyz) / voxelSize;
    int3 lo = clamp((int3)floor(gridMin), 0, (int)resolution - 1);
    int3 hi = clamp((int3)floor(gridMax), 0, (int)resolution - 1);
    uint dirtyLoPacked = asuint(constants.params2.y);
    uint dirtyHiPacked = asuint(constants.params2.z);
    int3 dirtyLo = int3(dirtyLoPacked & 0xFFu, (dirtyLoPacked >> 8) & 0xFFu, (dirtyLoPacked >> 16) & 0xFFu);
    int3 dirtyHi = int3(dirtyHiPacked & 0xFFu, (dirtyHiPacked >> 8) & 0xFFu, (dirtyHiPacked >> 16) & 0xFFu);
    lo = max(lo, dirtyLo);
    hi = min(hi, dirtyHi);
    if (any(lo > hi))
    {
        return;
    }

    // Albedo and emissive textures are sampled per-voxel at the point where the
    // voxel center projects onto the triangle (barycentric UV), not at the
    // triangle centroid. This preserves texture detail across large triangles
    // that span many voxels. Alpha-test also uses the per-voxel UV.
    float2 uv0 = float2(asfloat(_vertices[i0 * strideUints + 6]), asfloat(_vertices[i0 * strideUints + 7]));
    float2 uv1 = float2(asfloat(_vertices[i1 * strideUints + 6]), asfloat(_vertices[i1 * strideUints + 7]));
    float2 uv2 = float2(asfloat(_vertices[i2 * strideUints + 6]), asfloat(_vertices[i2 * strideUints + 7]));

    // Precompute barycentric projection basis: map a world-space point to
    // barycentric coordinates (u, v, w) on the triangle plane.
    float3 edge0 = w1 - w0;
    float3 edge1 = w2 - w0;
    float3 faceNormal = cross(edge0, edge1);
    float faceArea = length(faceNormal);
    if (faceArea < 1e-8)
    {
        return;
    }
    float3 N = faceNormal / faceArea;
    float d00 = dot(edge0, edge0);
    float d01 = dot(edge0, edge1);
    float d11 = dot(edge1, edge1);
    float invDenom = 1.0 / max(d00 * d11 - d01 * d01, 1e-12);

    float alphaCutoff = constants.params.w;

    // z-split into 8 slabs across threads.
    float3 boxHalf = voxelSize * 0.5;
    for (int z = lo.z + (int)dispatchId.y; z <= hi.z; z += 8)
    {
        for (int y = lo.y; y <= hi.y; y++)
        {
            for (int x = lo.x; x <= hi.x; x++)
            {
                float3 center = originAndSize.xyz + (float3(x, y, z) + 0.5) * voxelSize;
                if (TriBoxOverlap(center, boxHalf, w0, w1, w2))
                {
                    // Project the voxel center onto the triangle plane and
                    // compute barycentric coordinates for per-voxel UV.
                    float3 vp = center - w0;
                    float d20 = dot(vp, edge0);
                    float d21 = dot(vp, edge1);
                    float v = (d11 * d20 - d01 * d21) * invDenom;
                    float w = (d00 * d21 - d01 * d20) * invDenom;
                    float u = 1.0 - v - w;
                    float2 voxelUV = uv0 * u + uv1 * v + uv2 * w;

                    float4 albedoSample = _albedoTexture.SampleLevel(_albedoTextureSampler, voxelUV, 3.0);
                    if (alphaCutoff > 0.0 && albedoSample.a * constants.baseColor.a < alphaCutoff)
                    {
                        continue;
                    }

                    float3 albedo = albedoSample.rgb * constants.baseColor.rgb;
                    float3 emissiveSample = _emissiveTexture.SampleLevel(_emissiveTextureSampler, voxelUV, 3.0).rgb;
                    float emissiveQ = saturate(dot(constants.emissive.rgb * emissiveSample, float3(0.2126, 0.7152, 0.0722)) / 8.0);

                    uint3 logicalCoord = uint3(x, y, z);
                    uint pageEntry = _pageTable[VoxelPageTableSlot(logicalCoord, resolution, level)];
                    if (pageEntry != 0u)
                    {
                        AtomicAccumulateVoxel(VoxelAttributeIndex(pageEntry, logicalCoord), albedo, N, emissiveQ);
                    }
                }
            }
        }
    }
}
