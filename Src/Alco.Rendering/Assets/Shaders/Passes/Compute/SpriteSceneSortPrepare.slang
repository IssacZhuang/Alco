#language slang 2025
module SpriteSceneSortPrepare;

import Core;

public static const uint SPRITE_SCENE_SORT_BLOCK_SIZE = 1024u;
public static const uint SPRITE_SCENE_SORT_MAX_MERGE_LEVELS = 16u;
public static const uint SPRITE_SCENE_SORT_ARGS_STRIDE = 1u + 2u * SPRITE_SCENE_SORT_MAX_MERGE_LEVELS;

[GPURoot]
public struct SpriteSceneSortPrepareRoot
{
    public GPUBufferView DrawArgs;
    public GPUBufferView SortArgs;
    public uint BucketCount;
    public uint Pad0;
};

struct DispatchIndirectArgs
{
    uint GroupCountX;
    uint GroupCountY;
    uint GroupCountZ;
    uint Pad0;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 id : SV_DispatchThreadID)
{
    SpriteSceneSortPrepareRoot root = gpu_load_root<SpriteSceneSortPrepareRoot>();
    uint bucket = id.x;
    if (bucket >= root.BucketCount)
    {
        return;
    }

    uint count = gpu_load<uint>(gpu_offset_bytes(root.DrawArgs, bucket * 16u + 4u));
    uint argBase = bucket * SPRITE_SCENE_SORT_ARGS_STRIDE;
    uint localGroups = count > 1u ? ((count + SPRITE_SCENE_SORT_BLOCK_SIZE - 1u) / SPRITE_SCENE_SORT_BLOCK_SIZE) : 0u;

    DispatchIndirectArgs localArgs;
    localArgs.GroupCountX = localGroups;
    localArgs.GroupCountY = 1u;
    localArgs.GroupCountZ = 1u;
    localArgs.Pad0 = 0u;
    gpu_store<DispatchIndirectArgs>(root.SortArgs, argBase, localArgs);

    uint paddedPow2 = SPRITE_SCENE_SORT_BLOCK_SIZE;
    while (paddedPow2 < count && paddedPow2 < 0x80000000u)
    {
        paddedPow2 <<= 1u;
    }
    uint globalGroups = ((paddedPow2 >> 1u) + 255u) / 256u;

    for (uint level = 0u; level < SPRITE_SCENE_SORT_MAX_MERGE_LEVELS; ++level)
    {
        uint halfH = SPRITE_SCENE_SORT_BLOCK_SIZE << level;
        bool active = count > halfH;
        uint entry = argBase + 1u + level * 2u;

        DispatchIndirectArgs globalArgs;
        globalArgs.GroupCountX = active ? globalGroups : 0u;
        globalArgs.GroupCountY = 1u;
        globalArgs.GroupCountZ = 1u;
        globalArgs.Pad0 = 0u;
        gpu_store<DispatchIndirectArgs>(root.SortArgs, entry, globalArgs);

        DispatchIndirectArgs disperseArgs;
        disperseArgs.GroupCountX = active ? localGroups : 0u;
        disperseArgs.GroupCountY = 1u;
        disperseArgs.GroupCountZ = 1u;
        disperseArgs.Pad0 = 0u;
        gpu_store<DispatchIndirectArgs>(root.SortArgs, entry + 1u, disperseArgs);
    }
}
