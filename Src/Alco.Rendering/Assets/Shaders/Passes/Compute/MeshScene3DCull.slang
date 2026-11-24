#language slang 2025
module MeshScene3DCull;

import Core;
import AlcoRendering_MeshScene3D;

[GPURoot]
public struct MeshScene3DCullRoot
{
    public float4 Plane0;
    public float4 Plane1;
    public float4 Plane2;
    public float4 Plane3;
    public float4 Plane4;
    public float4 Plane5;
    public GPUBufferView Pool;
    public GPUBufferView SlotBuckets;
    public GPUBufferView BucketMeta;
    public GPUBufferView DrawArgs;
    public GPUBufferView VisibleSlots;
    public uint SlotCount;
    public uint BucketCount;
    public float CullMargin;
    public uint RequiredCullMask;
    public uint PlaneCount;
    public uint Pad0;
    public float3 OriginOffset;
    public uint Pad1;
};

bool isAabbOutsidePlane(float3 center, float3 extent, float4 plane, float margin)
{
    float dist = dot(center, plane.xyz) + plane.w;
    float radius = dot(extent, abs(plane.xyz)) + margin;
    return dist + radius < 0.0;
}

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    MeshScene3DCullRoot root = gpu_load_root<MeshScene3DCullRoot>();
    uint slot = dispatchId.x;
    if (slot >= root.SlotCount)
    {
        return;
    }

    uint bucket = gpu_load<uint>(root.SlotBuckets, slot);
    if (bucket >= root.BucketCount)
    {
        return;
    }

    MeshScene3DInstance inst = gpu_load<MeshScene3DInstance>(root.Pool, slot);
    float linearMagSq = dot(inst.World0.xyz, inst.World0.xyz)
                      + dot(inst.World1.xyz, inst.World1.xyz)
                      + dot(inst.World2.xyz, inst.World2.xyz);
    if (linearMagSq <= 1e-12)
    {
        return;
    }

    if (root.RequiredCullMask != 0u && (inst.CullMask & root.RequiredCullMask) == 0u)
    {
        return;
    }

    MeshScene3DBucketMeta meta = gpu_load<MeshScene3DBucketMeta>(root.BucketMeta, bucket);
    if (root.PlaneCount > 0u)
    {
        float3 center = meshScene3DTransformPositionRelative(inst, meta.LocalCenter, root.OriginOffset);
        float3 extent = float3(
            dot(meta.LocalExtent, abs(inst.World0.xyz)),
            dot(meta.LocalExtent, abs(inst.World1.xyz)),
            dot(meta.LocalExtent, abs(inst.World2.xyz)));

        float m = root.CullMargin;
        if (isAabbOutsidePlane(center, extent, root.Plane0, m)) return;
        if (isAabbOutsidePlane(center, extent, root.Plane1, m)) return;
        if (isAabbOutsidePlane(center, extent, root.Plane2, m)) return;
        if (isAabbOutsidePlane(center, extent, root.Plane3, m)) return;
        if (isAabbOutsidePlane(center, extent, root.Plane4, m)) return;
        if (isAabbOutsidePlane(center, extent, root.Plane5, m)) return;
    }

    // Offset 4 inside DrawIndexedIndirectArgs (20 bytes) is InstanceCount.
    uint dst = gpu_atomic_add_u32(gpu_offset_bytes(root.DrawArgs, bucket * 20u + 4u), 1u);
    gpu_store<uint>(root.VisibleSlots, meta.VisibleBase + dst, slot);
}
