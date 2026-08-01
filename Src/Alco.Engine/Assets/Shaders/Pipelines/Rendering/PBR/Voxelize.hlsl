#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Compute triangle voxelization for the voxel GI clipmap: one dispatch per mesh
// per clipmap level, one thread per triangle and z-slab (y dimension = 8 slabs
// splitting the triangle's voxel-space AABB along z). Writes packed attribute
// voxels (albedo + occupancy, normal + emissive) with last-writer-wins stores.
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
    float4 params2;        // x=triangleCount, yzw=unused
};

DEFINE_STORAGE(1, uint, _vertices);
DEFINE_STORAGE(2, uint, _indices);
DEFINE_STORAGE(3, uint2, _attrOut);
DEFINE_TEX2D_SAMPLE(4, _albedoTexture);
DEFINE_TEX2D_SAMPLE(5, _emissiveTexture);
DEFINE_STORAGE(6, uint, _pageTable);

PUSH_CONSTANT VoxelizeConstants constants;

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

    // Albedo and emissive at the triangle centroid (high mip = averaged color).
    float2 uv0 = float2(asfloat(_vertices[i0 * strideUints + 6]), asfloat(_vertices[i0 * strideUints + 7]));
    float2 uv1 = float2(asfloat(_vertices[i1 * strideUints + 6]), asfloat(_vertices[i1 * strideUints + 7]));
    float2 uv2 = float2(asfloat(_vertices[i2 * strideUints + 6]), asfloat(_vertices[i2 * strideUints + 7]));
    float2 uvCentroid = (uv0 + uv1 + uv2) / 3.0;

    float4 albedoSample = _albedoTexture.SampleLevel(_albedoTextureSampler, uvCentroid, 5.0);
    float alphaCutoff = constants.params.w;
    if (alphaCutoff > 0.0 && albedoSample.a * constants.baseColor.a < alphaCutoff)
    {
        return;
    }

    float3 albedo = albedoSample.rgb * constants.baseColor.rgb;
    float3 emissiveSample = _emissiveTexture.SampleLevel(_emissiveTextureSampler, uvCentroid, 5.0).rgb;
    float emissiveQ = saturate(dot(constants.emissive.rgb * emissiveSample, float3(0.2126, 0.7152, 0.0722)) / 8.0);

    float3 normal = cross(w1 - w0, w2 - w0);
    float normalLength = length(normal);
    if (normalLength < 1e-8)
    {
        return;
    }
    normal /= normalLength;

    uint2 attr = PackVoxelAttr(albedo, normal, emissiveQ);

    // Voxel-space AABB clamped to the grid, z-split into 8 slabs across threads.
    float3 gridMin = (worldMin - originAndSize.xyz) / voxelSize;
    float3 gridMax = (worldMax - originAndSize.xyz) / voxelSize;
    int3 lo = clamp((int3)floor(gridMin), 0, (int)resolution - 1);
    int3 hi = clamp((int3)floor(gridMax), 0, (int)resolution - 1);

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
                    uint3 logicalCoord = uint3(x, y, z);
                    uint pageEntry = _pageTable[VoxelPageTableSlot(logicalCoord, resolution, level)];
                    if (pageEntry != 0u)
                    {
                        _attrOut[VoxelAttributeIndex(pageEntry, logicalCoord)] = attr;
                    }
                }
            }
        }
    }
}
