#language slang 2025
module World3DVoxelVoxelize;

import Core;
import World3DSceneData;
import World3DSurface;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelVoxelizeRoot
{
    public int4 OriginAnchorCell;
    public GPUBufferView Directory;
    public GPUBufferView Cascades;
    public GPUBufferView StaticDirtyMask;
    public GPUBufferView StaticPageTable;
    public GPUBufferView StaticBrickPool;
    public GPUBufferView DynamicDesiredMask;
    public GPUBufferView DynamicPageTable;
    public GPUBufferView DynamicBrickPool;
    public GPUBufferView CandidateList;
    public GPUBufferView ChunkList;
    public GPUBufferView Counters;
    public uint BricksPerAxis;
    public uint StaticCapacity;
    public uint DynamicCapacity;
    public uint MaxChunks;
    public uint TargetSurfaceBucket;
    public uint EnableDynamic;
    public uint Reserved0;
    public uint Reserved1;
};

float3 transformNormalWS(float3 localNormal, float3x3 m)
{
    float3 r0 = m[0];
    float3 r1 = m[1];
    float3 r2 = m[2];
    float3 c0 = cross(r1, r2);
    float3 c1 = cross(r2, r0);
    float3 c2 = cross(r0, r1);
    if (dot(r0, c0) < 0.0)
    {
        c0 = -c0;
        c1 = -c1;
        c2 = -c2;
    }
    float3 worldN = c0 * localNormal.x + c1 * localNormal.y + c2 * localNormal.z;
    float lenSq = dot(worldN, worldN);
    return lenSq > 1e-12 ? worldN * rsqrt(lenSq) : float3(0.0, 0.0, 1.0);
}

float3 permuteToUVW(float3 v, uint axis)
{
    if (axis == 0u) return float3(v.y, v.z, v.x);
    if (axis == 1u) return float3(v.x, v.z, v.y);
    return v;
}

int3 permuteBackXYZ(int3 uvw, uint axis)
{
    if (axis == 0u) return int3(uvw.z, uvw.x, uvw.y);
    if (axis == 1u) return int3(uvw.x, uvw.z, uvw.y);
    return uvw;
}

float3 computeBarycentrics(float3 p, float3 a, float3 b, float3 c)
{
    float3 ab = b - a;
    float3 ac = c - a;
    float3 ap = p - a;
    float d00 = dot(ab, ab);
    float d01 = dot(ab, ac);
    float d11 = dot(ac, ac);
    float d20 = dot(ap, ab);
    float d21 = dot(ap, ac);
    float denom = d00 * d11 - d01 * d01;
    if (abs(denom) < 1e-10)
        return float3(1.0 / 3.0);
    float invD = rcp(denom);
    float v = clamp((d11 * d20 - d01 * d21) * invD, 0.0, 1.0);
    float w = clamp((d00 * d21 - d01 * d20) * invD, 0.0, 1.0);
    float u = max(0.0, 1.0 - v - w);
    float sum = u + v + w;
    return sum > 0.0 ? float3(u, v, w) * rcp(sum) : float3(1.0 / 3.0);
}

// A triangle clipped against a voxel's six planes has at most nine vertices.
static const uint MaxClippedVertices = 9u;

uint clipVoxelPolygon(float3 polygon[MaxClippedVertices], uint count, uint axis, float boundary, float side,
    out float3 clipped[MaxClippedVertices])
{
    uint outCount = 0u;
    float3 prev = polygon[count - 1u];
    float prevDist = (prev[axis] - boundary) * side;
    for (uint i = 0u; i < count; ++i)
    {
        float3 point = polygon[i];
        float dist = (point[axis] - boundary) * side;
        if ((prevDist >= 0.0) != (dist >= 0.0))
        {
            float3 intersection = lerp(prev, point, prevDist / (prevDist - dist));
            intersection[axis] = boundary;
            clipped[outCount++] = intersection;
        }
        if (dist >= 0.0)
            clipped[outCount++] = point;
        prev = point;
        prevDist = dist;
    }
    return outCount;
}

float voxelProjectedArea(float3 p0, float3 p1, float3 p2)
{
    if (all(min(p0, min(p1, p2)) >= 0.0) && all(max(p0, max(p1, p2)) <= 1.0))
        return abs(cross(p1 - p0, p2 - p0).z) * 0.5;

    float3 polygon[MaxClippedVertices];
    float3 clipped[MaxClippedVertices];
    polygon[0] = p0;
    polygon[1] = p1;
    polygon[2] = p2;
    uint count = 3u;
    for (uint axis = 0u; axis < 3u; ++axis)
    {
        count = clipVoxelPolygon(polygon, count, axis, 0.0, 1.0, clipped);
        if (count < 3u)
            return 0.0;
        count = clipVoxelPolygon(clipped, count, axis, 1.0, -1.0, polygon);
        if (count < 3u)
            return 0.0;
    }

    float2 origin = polygon[0].xy;
    float area = 0.0;
    for (uint i = 1u; i + 1u < count; ++i)
    {
        float2 a = polygon[i].xy - origin;
        float2 b = polygon[i + 1u].xy - origin;
        area += a.x * b.y - a.y * b.x;
    }
    return abs(area) * 0.5;
}

void accumulateEmissive(GPUBufferView view, float3 radiance)
{
    uint packed = 0u;
    while (true)
    {
        uint next = voxel_rgb9e5_encode(voxel_rgb9e5_decode(packed) + radiance);
        if (next == packed)
            return;
        uint observed = gpu_atomic_cas_u32(view, packed, next);
        if (observed == packed)
            return;
        packed = observed;
    }
}

[shader("compute")]
[numthreads(64, 1, 1)]
public void csMain<T : IVoxelFeedSurface>(
    uint3 groupId : SV_GroupID,
    uint3 localId : SV_GroupThreadID)
{
    VoxelVoxelizeRoot root = gpu_load_root<VoxelVoxelizeRoot>();
    uint maxChunks = min(gpu_load<uint>(root.Counters, VOXEL_COUNTER_DISPATCH_CHUNKS), root.MaxChunks);
    if (groupId.x >= maxChunks)
        return;

    World3DVoxelChunkGpu chunk = gpu_load<World3DVoxelChunkGpu>(root.ChunkList, groupId.x);
    World3DVoxelCandidateGpu cand = gpu_load<World3DVoxelCandidateGpu>(root.CandidateList, chunk.CandidateIndex);
    uint triIdx = chunk.StartTriangle + localId.x;
    if (triIdx >= cand.TriangleCount)
        return;

    World3DInstanceGpu inst = world3d_load_instance(root.Directory, cand.InstanceSlot);
    World3DMaterialGpu mat = world3d_load_material(root.Directory, inst.MaterialSlot);
    uint bucket = (mat.Flags >> WORLD3D_MATERIAL_SURFACE_BUCKET_SHIFT) & WORLD3D_MATERIAL_SURFACE_BUCKET_MASK;
    if (bucket != root.TargetSurfaceBucket)
        return;

    bool isDynamic = (inst.Flags & WORLD3D_INSTANCE_DYNAMIC) != 0u;
    if (isDynamic && root.EnableDynamic == 0u)
        return;

    World3DGeometryLodGpu geomLod = world3d_load_geometry_lod(root.Directory, inst.GeometrySlot, cand.Lod);
    GPUBufferView vertices;
    GPUBufferView indices;
    if (!world3d_try_get_geometry_views(root.Directory, geomLod, vertices, indices))
        return;

    uint i0 = world3d_load_index(indices, triIdx * 3u + 0u);
    uint i1 = world3d_load_index(indices, triIdx * 3u + 1u);
    uint i2 = world3d_load_index(indices, triIdx * 3u + 2u);

    World3DVertexGpu v0 = world3d_load_vertex(vertices, i0);
    World3DVertexGpu v1 = world3d_load_vertex(vertices, i1);
    World3DVertexGpu v2 = world3d_load_vertex(vertices, i2);

    float3x3 linearMat = (float3x3)inst.Spatial.ObjectToCell;
    float3 instPivot = float3(inst.Spatial.Cell.xyz - root.OriginAnchorCell.xyz) * WORLD3D_CELL_SIZE
        + inst.Spatial.ObjectToCell[3].xyz;
    float3 a0 = mul(v0.Position, linearMat) + instPivot;
    float3 a1 = mul(v1.Position, linearMat) + instPivot;
    float3 a2 = mul(v2.Position, linearMat) + instPivot;

    float3 n0 = transformNormalWS(v0.Normal, linearMat);
    float3 n1 = transformNormalWS(v1.Normal, linearMat);
    float3 n2 = transformNormalWS(v2.Normal, linearMat);

    World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(root.Cascades, cand.CascadeIndex);
    float voxelSize = cGpu.OriginVoxelSize.w;
    float invVoxel = rcp(voxelSize);

    float3 p0 = (a0 - cGpu.OriginVoxelSize.xyz) * invVoxel;
    float3 p1 = (a1 - cGpu.OriginVoxelSize.xyz) * invVoxel;
    float3 p2 = (a2 - cGpu.OriginVoxelSize.xyz) * invVoxel;

    float3 triN = cross(p1 - p0, p2 - p0);
    float3 absN = abs(triN);
    if (max(absN.x, max(absN.y, absN.z)) < 1e-8)
        return;

    int res = int(root.BricksPerAxis * WORLD3D_VOXEL_BRICK_SIZE);
    int3 minV = max(int3(0), int3(floor(min(p0, min(p1, p2)))));
    int3 maxV = min(int3(res - 1), int3(floor(max(p0, max(p1, p2)))));
    if (any(minV > maxV))
        return;

    uint axis = 2u;
    if (absN.x >= absN.y && absN.x >= absN.z) axis = 0u;
    else if (absN.y >= absN.z) axis = 1u;

    float3 q0 = permuteToUVW(p0, axis);
    float3 q1 = permuteToUVW(p1, axis);
    float3 q2 = permuteToUVW(p2, axis);
    float3 nUVW = permuteToUVW(triN, axis);

    float det2 = (q1.x - q0.x) * (q2.y - q0.y) - (q1.y - q0.y) * (q2.x - q0.x);
    if (abs(det2) < 1e-8)
        return;
    float sgn = det2 >= 0.0 ? 1.0 : -1.0;

    float2 e0 = q1.xy - q0.xy;
    float2 e1 = q2.xy - q1.xy;
    float2 e2 = q0.xy - q2.xy;
    float m0 = 0.5 * (abs(e0.x) + abs(e0.y)) + 1e-3;
    float m1 = 0.5 * (abs(e1.x) + abs(e1.y)) + 1e-3;
    float m2 = 0.5 * (abs(e2.x) + abs(e2.y)) + 1e-3;

    float invNw = rcp(nUVW.z);
    float planeD = dot(nUVW, q0);
    float wHalf = 0.5 + 0.5 * (abs(nUVW.x) + abs(nUVW.y)) * abs(invNw);

    int3 minUVW = int3(permuteToUVW(float3(minV), axis));
    int3 maxUVW = int3(permuteToUVW(float3(maxV), axis));

    GPUBufferView maskView = isDynamic ? root.DynamicDesiredMask : root.StaticDirtyMask;
    GPUBufferView pageTable = isDynamic ? root.DynamicPageTable : root.StaticPageTable;
    GPUBufferView poolView = isDynamic ? root.DynamicBrickPool : root.StaticBrickPool;
    uint poolCapacity = isDynamic ? root.DynamicCapacity : root.StaticCapacity;
    uint alphaMode = mat.Flags & WORLD3D_MATERIAL_ALPHA_MODE_MASK;

    T surface = T();
    for (int iv = minUVW.y; iv <= maxUVW.y; ++iv)
    {
        float fv = float(iv) + 0.5;
        for (int iu = minUVW.x; iu <= maxUVW.x; ++iu)
        {
            float2 pt = float2(float(iu) + 0.5, fv);
            float d0 = sgn * (e0.x * (pt.y - q0.y) - e0.y * (pt.x - q0.x));
            float d1 = sgn * (e1.x * (pt.y - q1.y) - e1.y * (pt.x - q1.x));
            float d2 = sgn * (e2.x * (pt.y - q2.y) - e2.y * (pt.x - q2.x));
            if (d0 < -m0 || d1 < -m1 || d2 < -m2)
                continue;

            float wCenter = (planeD - nUVW.x * pt.x - nUVW.y * pt.y) * invNw;
            int iwMin = max(minUVW.z, int(floor(wCenter - wHalf)));
            int iwMax = min(maxUVW.z, int(floor(wCenter + wHalf)));

            for (int iw = iwMin; iw <= iwMax; ++iw)
            {
                int3 vCoord = permuteBackXYZ(int3(iu, iv, iw), axis);
                uint3 uCoord = uint3(vCoord);
                uint3 localBrick = uCoord >> 3;

                uint wordIdx;
                uint bitMask;
                voxel_mask_address(cand.CascadeIndex, localBrick, root.BricksPerAxis, wordIdx, bitMask);
                if ((gpu_load<uint>(maskView, wordIdx) & bitMask) == 0u)
                    continue;

                uint3 toroidalBrick = voxel_local_to_toroidal(localBrick, cGpu);
                uint pageSlot = voxel_linear_slot(cand.CascadeIndex, toroidalBrick, root.BricksPerAxis);
                uint poolIdx = gpu_load<uint>(pageTable, pageSlot);
                if (poolIdx >= poolCapacity)
                    continue;

                float3 localCenter = float3(uCoord) + 0.5;
                float3 bary = computeBarycentrics(localCenter, p0, p1, p2);
                float3 normalWS = normalize(n0 * bary.x + n1 * bary.y + n2 * bary.z);

                VoxelSurfaceInput sInput;
                sInput.AnchorRelativePosition = cGpu.OriginVoxelSize.xyz + localCenter * voxelSize;
                sInput.NormalWS = normalWS;
                sInput.UV = v0.UV * bary.x + v1.UV * bary.y + v2.UV * bary.z;
                sInput.TextureLod = float(cand.CascadeIndex) + 1.0;
                sInput.Material = mat;

                float4 baseColor = surface.getVoxelBaseColor(sInput);
                float alpha = baseColor.a * inst.Fade;
                if (alphaMode == 1u && alpha < mat.EmissiveAlphaCutoff.w)
                    continue;

                float finalOpacity = (alphaMode == 0u || alphaMode == 1u) ? 1.0 : saturate(alpha);
                if (finalOpacity <= 0.001)
                    continue;

                float3 emissive = surface.getVoxelEmissive(sInput);

                uint3 subVoxel = uCoord & 7u;
                uint localVoxel = subVoxel.x + (subVoxel.y << 3) + (subVoxel.z << 6);
                uint voxelBaseByte = (poolIdx * WORLD3D_VOXEL_BRICK_UINT4_STRIDE + localVoxel) * 16u;

                GPUBufferView w0View = gpu_offset_bytes(poolView, voxelBaseByte + 0u);
                GPUBufferView w1View = gpu_offset_bytes(poolView, voxelBaseByte + 4u);
                GPUBufferView w2View = gpu_offset_bytes(poolView, voxelBaseByte + 8u);
                GPUBufferView w3View = gpu_offset_bytes(poolView, voxelBaseByte + 12u);

                if (any(emissive > 0.0))
                {
                    float3 voxelOrigin = float3(iu, iv, iw);
                    float coverage = voxelProjectedArea(q0 - voxelOrigin, q1 - voxelOrigin, q2 - voxelOrigin);
                    if (coverage > 0.0)
                        accumulateEmissive(w2View, max(emissive, float3(0.0)) * (coverage * finalOpacity));
                }

                uint curW3 = gpu_load<uint>(w3View);
                if ((curW3 & 0xFFFFu) >= 31u)
                    continue;
                uint prevW3 = gpu_atomic_add_u32(w3View, 1u);
                if ((prevW3 & 0xFFFFu) >= 31u)
                    continue;

                uint opQ = uint(clamp(round(finalOpacity * 15.0), 1.0, 15.0));
                gpu_atomic_add_u32(w3View, opQ << 16);

                uint3 aq = uint3(clamp(round(saturate(baseColor.rgb) * 31.0), 0.0, 31.0));
                gpu_atomic_add_u32(w0View, aq.x | (aq.y << 10) | (aq.z << 20));

                uint3 nq = uint3(clamp(round((normalWS * 0.5 + 0.5) * 31.0), 0.0, 31.0));
                gpu_atomic_add_u32(w1View, nq.x | (nq.y << 10) | (nq.z << 20));
            }
        }
    }
}
