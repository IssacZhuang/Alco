#language slang 2025
module World3DForwardEmitArgs;

import Core;
import World3DSceneData;

[GPURoot]
public struct ForwardEmitArgsRoot
{
    public GPUBufferView Directory;
    public GPUBufferView SortKeys;
    public GPUBufferView IndirectArgs;
    public GPUBufferView DrawRefs;
    public uint CandidateCount;
    public uint Reserved0;
};

struct ForwardDrawIndirectCommand
{
    uint VertexCount;
    uint InstanceCount;
    uint FirstVertex;
    uint FirstInstance;
};

struct ForwardDrawRef
{
    uint InstanceSlot;
    uint Lod;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    ForwardEmitArgsRoot root = gpu_load_root<ForwardEmitArgsRoot>();
    uint slot = id.x;
    if (slot >= root.CandidateCount)
        return;

    uint2 entry = gpu_load<uint2>(root.SortKeys, slot);
    uint payload = entry.y;
    if (payload == 0xFFFFFFFFu)
    {
        ForwardDrawIndirectCommand emptyCmd;
        emptyCmd.VertexCount = 0u;
        emptyCmd.InstanceCount = 0u;
        emptyCmd.FirstVertex = 0u;
        emptyCmd.FirstInstance = 0u;
        gpu_store<ForwardDrawIndirectCommand>(root.IndirectArgs, slot, emptyCmd);
        return;
    }

    uint instanceSlot = payload >> 2u;
    uint lodIdx = payload & 3u;
    World3DInstanceGpu instance = world3d_load_instance(root.Directory, instanceSlot);
    World3DGeometryLodGpu lod = world3d_load_geometry_lod(root.Directory, instance.GeometrySlot, lodIdx);

    ForwardDrawIndirectCommand cmd;
    cmd.VertexCount = lod.IndexCount;
    cmd.InstanceCount = lod.IndexCount > 0u ? 1u : 0u;
    cmd.FirstVertex = 0u;
    cmd.FirstInstance = 0u;
    gpu_store<ForwardDrawIndirectCommand>(root.IndirectArgs, slot, cmd);

    ForwardDrawRef drawRef;
    drawRef.InstanceSlot = instanceSlot;
    drawRef.Lod = lodIdx;
    gpu_store<ForwardDrawRef>(root.DrawRefs, slot, drawRef);
}
