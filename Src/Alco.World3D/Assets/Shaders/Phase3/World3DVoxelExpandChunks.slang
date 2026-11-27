#language slang 2025
module World3DVoxelExpandChunks;

import Core;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelExpandChunksRoot
{
    public GPUBufferView CandidateList;
    public GPUBufferView ChunkList;
    public GPUBufferView Counters;
    public uint MaxCandidates;
    public uint MaxChunks;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void csMain(uint3 groupId : SV_GroupID, uint3 localId : SV_GroupThreadID)
{
    VoxelExpandChunksRoot root = gpu_load_root<VoxelExpandChunksRoot>();
    uint cands = min(gpu_load<uint>(root.Counters, VOXEL_COUNTER_CANDIDATE_INSTANCES), root.MaxCandidates);
    if (groupId.x >= cands)
        return;

    World3DVoxelCandidateGpu cand = gpu_load<World3DVoxelCandidateGpu>(root.CandidateList, groupId.x);
    uint chunkCount = (cand.TriangleCount + 63u) >> 6;
    for (uint baseChunk = 0u; baseChunk < chunkCount; baseChunk += 64u)
    {
        uint chunkIdx = baseChunk + localId.x;
        if (chunkIdx < chunkCount)
        {
            uint dst = gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, VOXEL_COUNTER_DISPATCH_CHUNKS * 4u), 1u);
            if (dst < root.MaxChunks)
            {
                World3DVoxelChunkGpu chunk;
                chunk.CandidateIndex = groupId.x;
                chunk.StartTriangle = chunkIdx << 6;
                gpu_store<World3DVoxelChunkGpu>(root.ChunkList, dst, chunk);
            }
        }
    }
}
