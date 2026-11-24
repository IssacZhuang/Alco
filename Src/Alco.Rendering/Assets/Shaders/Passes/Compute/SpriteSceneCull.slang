#language slang 2025
module SpriteSceneCull;

import Core;
import AlcoRendering_SpriteScene;

[GPURoot]
public struct SpriteSceneCullRoot
{
    public float4 Viewport;
    public GPUBufferView Pool;
    public GPUBufferView SlotBuckets;
    public GPUBufferView BucketMeta;
    public GPUBufferView DrawArgs;
    public GPUBufferView VisibleSlots;
    public uint SlotCount;
    public uint BucketCount;
    public float CullMargin;
    public uint Pad0;
    public float2 OriginOffset;
};

groupshared uint s_localCount[SPRITE_SCENE_LOCAL_AGGREGATE_BUCKETS];
groupshared uint s_globalBase[SPRITE_SCENE_LOCAL_AGGREGATE_BUCKETS];

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 dispatchId : SV_DispatchThreadID, uint3 groupThreadId : SV_GroupThreadID)
{
    SpriteSceneCullRoot root = gpu_load_root<SpriteSceneCullRoot>();
    uint groupIndex = groupThreadId.x;
    uint localBucketLimit = min(root.BucketCount, SPRITE_SCENE_LOCAL_AGGREGATE_BUCKETS);

    if (groupIndex < localBucketLimit)
    {
        s_localCount[groupIndex] = 0u;
        s_globalBase[groupIndex] = 0u;
    }
    GroupMemoryBarrierWithGroupSync();

    uint slot = dispatchId.x;
    bool visible = false;
    uint bucket = 0u;
    SpriteSceneBucketMeta meta = (SpriteSceneBucketMeta)0;

    if (slot < root.SlotCount)
    {
        SpriteSceneInstance inst = gpu_load<SpriteSceneInstance>(root.Pool, slot);
        if (inst.Scale.x != 0.0 && inst.Scale.y != 0.0)
        {
            bucket = gpu_load<uint>(root.SlotBuckets, slot);
            if (bucket < root.BucketCount)
            {
                meta = gpu_load<SpriteSceneBucketMeta>(root.BucketMeta, bucket);
                float s = abs(inst.Rotation.x);
                float c = abs(inst.Rotation.y);
                float sx = abs(inst.Scale.x);
                float sy = abs(inst.Scale.y);
                float ex = meta.LocalExtentScale * (c * sx + s * sy) + root.CullMargin;
                float ey = meta.LocalExtentScale * (s * sx + c * sy) + root.CullMargin;
                float2 pos = inst.Position - root.OriginOffset;
                if (pos.x + ex >= root.Viewport.x &&
                    pos.x - ex <= root.Viewport.z &&
                    pos.y + ey >= root.Viewport.y &&
                    pos.y - ey <= root.Viewport.w)
                {
                    visible = true;
                }
            }
        }
    }

    uint localOffset = 0u;
    bool useLocalAggregation = visible && bucket < SPRITE_SCENE_LOCAL_AGGREGATE_BUCKETS;
    if (useLocalAggregation)
    {
        InterlockedAdd(s_localCount[bucket], 1u, localOffset);
    }
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex < localBucketLimit)
    {
        uint localCount = s_localCount[groupIndex];
        if (localCount > 0u)
        {
            // Offset 4 inside DrawIndirectArgs (16 bytes) is InstanceCount.
            s_globalBase[groupIndex] = gpu_atomic_add_u32(gpu_offset_bytes(root.DrawArgs, groupIndex * 16u + 4u), localCount);
        }
    }
    GroupMemoryBarrierWithGroupSync();

    if (visible)
    {
        uint dstOffset = useLocalAggregation
            ? (s_globalBase[bucket] + localOffset)
            : gpu_atomic_add_u32(gpu_offset_bytes(root.DrawArgs, bucket * 16u + 4u), 1u);
        gpu_store<uint>(root.VisibleSlots, meta.VisibleBase + dstOffset, slot);
    }
}
